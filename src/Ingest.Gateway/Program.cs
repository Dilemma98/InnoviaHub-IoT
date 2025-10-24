using Microsoft.Extensions.Logging;
using Innovia.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using System.Text.Json;
using System.Formats.Asn1;

// SETUP WEB APPLICATION
var builder = WebApplication.CreateBuilder(args);

// Trim noisy logs from EF Core and HttpClient to keep console clean
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

// Add database context for ingest service
builder.Services.AddDbContext<IngestDbContext>(o => 
    o.UseNpgsql(builder.Configuration.GetConnectionString("Db")));

// Register IngestService as scoped dependency
builder.Services.AddScoped<IngestService>();

// Register FluentValidation validator for MeasurementBatch
builder.Services.AddScoped<IValidator<MeasurementBatch>, MeasurementBatchValidator>();

// Add OpenAPI/Swagger support
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// DEVICE REGISTRY CONFIG

// HTTP client to communicate with Device Registry service
builder.Services.AddHttpClient<DeviceRegistryClient>();

// Configure DeviceRegistryConfig singleton
builder.Services.AddSingleton<DeviceRegistryConfig>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>().GetSection("DeviceRegistry");
    return new DeviceRegistryConfig { BaseUrl = cfg?["BaseUrl"] ?? "http://localhost:5101" };
});

// SIGNALR REALTIME PUBLISHER

// Configuration for SignalR hub
builder.Services.AddSingleton(new RealtimeConfig
{
    HubUrl = "http://localhost:5103/hub/telemetry"
});

// Build HubConnection singleton
builder.Services.AddSingleton<HubConnection>(sp =>
{
    var cfg = sp.GetRequiredService<RealtimeConfig>();
    return new HubConnectionBuilder()
        .WithUrl(cfg.HubUrl)      // Hub URL
        .WithAutomaticReconnect() // Reconnect automatically if disconnected
        .Build();
});

// Register IRealtimePublisher implementation using SignalR
builder.Services.AddSingleton<IRealtimePublisher, SignalRRealtimePublisher>();

// CORS SETUP
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173") // Only allow frontend origin
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
    });
});

var app = builder.Build();

// Enable CORS
app.UseCors("AllowFrontend");

// START SIGNALR HUB CONNECTION
using (var scope = app.Services.CreateScope())
{
    var hub = scope.ServiceProvider.GetRequiredService<HubConnection>();
    await hub.StartAsync(); // Connect to SignalR hub at startup
}

// ENSURE DATABASE EXISTS
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IngestDbContext>();
    db.Database.EnsureCreated(); // Create tables if missing
}

// SWAGGER / OPENAPI
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Ingest.Gateway v1");
    c.RoutePrefix = "swagger"; // Swagger UI available at /swagger
});

// Redirect root to Swagger UI for convenience
app.MapGet("/", () => Results.Redirect("/swagger"));

// HTTP INGEST ENDPOINT

app.MapPost("/ingest/http/{tenant}", async (string tenant, MeasurementBatch payload, IValidator<MeasurementBatch> validator, IngestService ingest, ILogger<Program> log) =>
{
    // Validate incoming payload
    var result = await validator.ValidateAsync(payload);
    if (!result.IsValid)
    {
        log.LogWarning("Validation failed for ingest payload (tenant: {Tenant}, serial: {Serial}): {Errors}", tenant, payload?.DeviceId, result.Errors);
        return Results.BadRequest(result.Errors);
    }

    // Process and store measurements
    await ingest.ProcessAsync(tenant, payload);

    // Log success
    log.LogInformation("Ingested {Count} metrics for serial {Serial} in tenant {Tenant} at {Time}", payload.Metrics.Count, payload.DeviceId, tenant, payload.Timestamp);

    return Results.Accepted();
});

// DEBUG ENDPOINT FOR DEVICE
app.MapGet("/ingest/debug/device/{deviceId:guid}", async (Guid deviceId, IngestDbContext db) =>
{
    // Count measurements for this device
    var count = await db.Measurements.Where(m => m.DeviceId == deviceId).CountAsync();

    // Get latest 5 measurements
    var latest = await db.Measurements.Where(m => m.DeviceId == deviceId).OrderByDescending(m => m.Time).Take(5).ToListAsync();

    return Results.Ok(new { deviceId, count, latest });
});

// MQTT SUBSCRIBER
var mqttFactory = new MqttFactory();
var mqttClient = mqttFactory.CreateMqttClient();
var mqttOptions = new MqttClientOptionsBuilder()
    .WithTcpServer("localhost", 1883)
    .Build();

// Subscribe to topic pattern for all tenants and devices
var mqttTopic = "tenants/+/devices/+/measurements";

// Handle incoming MQTT messages
mqttClient.ApplicationMessageReceivedAsync += async e =>
{
    try
    {
        var topic = e.ApplicationMessage.Topic ?? string.Empty;
        var parts = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Expected format: tenants/{tenantSlug}/devices/{serial}/measurements
        if (parts.Length >= 5 && parts[0] == "tenants" && parts[2] == "devices")
        {
            var tenantSlug = parts[1];
            var serial = parts[3];

            // Deserialize MQTT payload into MeasurementBatch
            var payloadBytes = e.ApplicationMessage.PayloadSegment.ToArray();
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var batch = JsonSerializer.Deserialize<MeasurementBatch>(payloadBytes, jsonOptions);

            if (batch is null)
            {
                Console.WriteLine($"[MQTT] Skipping: could not deserialize payload on topic '{topic}'");
                return;
            }

            // Ensure DeviceId is set from topic if missing
            if (string.IsNullOrWhiteSpace(batch.DeviceId))
            {
                batch.DeviceId = serial;
            }

            // Process measurement batch using same IngestService
            using var scope = app.Services.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IngestService>();
            await svc.ProcessAsync(tenantSlug, batch);

            Console.WriteLine($"[MQTT] Ingested {batch.Metrics?.Count ?? 0} metrics for serial '{serial}' in tenant '{tenantSlug}' at {batch.Timestamp:o}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[MQTT] Handler error: {ex.Message}");
    }
};

// Connect and subscribe to MQTT broker
try
{
    await mqttClient.ConnectAsync(mqttOptions);
    await mqttClient.SubscribeAsync(new MqttTopicFilterBuilder()
        .WithTopic(mqttTopic)
        .WithAtLeastOnceQoS()
        .Build());
    Console.WriteLine($"[MQTT] Subscribed to '{mqttTopic}' on localhost:1883");
}
catch (Exception ex)
{
    Console.WriteLine($"[MQTT] Failed to connect/subscribe: {ex.Message}");
}

// RUN APP
app.Run();

// DATABASE CONTEXT & ENTITIES
public class IngestDbContext : DbContext
{
    public IngestDbContext(DbContextOptions<IngestDbContext> o) : base(o) {}

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MeasurementRow>().ToTable("Measurements"); // Map entity to table
        base.OnModelCreating(modelBuilder);
    }

    public DbSet<MeasurementRow> Measurements => Set<MeasurementRow>();
}

// Measurement row entity
public class MeasurementRow
{
    public long Id { get; set; }
    public DateTimeOffset Time { get; set; }
    public Guid TenantId { get; set; }
    public Guid DeviceId { get; set; }
    public string Type { get; set; } = "";
    public double Value { get; set; }
    public string Unit { get; set; } = "";
}

// FLUENT VALIDATOR FOR BATCH
public class MeasurementBatchValidator : AbstractValidator<MeasurementBatch>
{
    public MeasurementBatchValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty();   // Device ID required
        RuleFor(x => x.ApiKey).NotEmpty();     // API key required
        RuleFor(x => x.Metrics).NotEmpty();    // Metrics list cannot be empty
    }
}

// INGEST SERVICE
public class IngestService
{
    private readonly IngestDbContext _db;
    private readonly DeviceRegistryClient _registry;
    private readonly IRealtimePublisher _rt;

    public IngestService(IngestDbContext db, DeviceRegistryClient registry, IRealtimePublisher rt)
    {
        _db = db;
        _registry = registry;
        _rt = rt;
    }

    // Process a batch of measurements: resolve IDs, save to DB, publish realtime
    public async Task ProcessAsync(string tenant, MeasurementBatch payload)
    {
        // Resolve tenantId and deviceId using registry service
        var ids = await _registry.ResolveAsync(tenant, payload.DeviceId);

        // Save each metric to DB
        foreach (var m in payload.Metrics)
        {
            _db.Measurements.Add(new MeasurementRow {
                Time = payload.Timestamp,
                TenantId = ids.TenantId,
                DeviceId = ids.DeviceId,
                Type = m.Type,
                Value = m.Value,
                Unit = m.Unit
            });
        }
        await _db.SaveChangesAsync();

        // Publish each metric to SignalR for real-time updates
        foreach (var m in payload.Metrics)
        {
            await _rt.PublishAsync(tenant, ids.DeviceId, m.Type, m.Value, m.Unit, payload.Timestamp);
        }
    }
}

// DEVICE REGISTRY CLIENT
public record DeviceRegistryConfig
{
    public string BaseUrl { get; set; } = "http://localhost:5101";
}

public class DeviceRegistryClient
{
    private readonly HttpClient _http;
    private readonly DeviceRegistryConfig _cfg;
    private readonly Dictionary<string, (Guid TenantId, Guid DeviceId)> _cache = new();

    public DeviceRegistryClient(HttpClient http, DeviceRegistryConfig cfg)
    {
        _http = http;
        _cfg = cfg;
    }

    // Resolve tenant and device IDs from registry or cache
    public async Task<(Guid TenantId, Guid DeviceId)> ResolveAsync(string tenantSlug, string deviceSerial)
    {
        var cacheKey = $"{tenantSlug}:{deviceSerial}";
        if (_cache.TryGetValue(cacheKey, out var hit)) return hit;

        // Get tenant by slug
        var tenant = await _http.GetFromJsonAsync<TenantDto>($"{_cfg.BaseUrl}/api/tenants/by-slug/{tenantSlug}")
                    ?? throw new InvalidOperationException($"Tenant slug '{tenantSlug}' not found");

        // Get device by serial
        var device = await _http.GetFromJsonAsync<DeviceDto>($"{_cfg.BaseUrl}/api/tenants/{tenant.Id}/devices/by-serial/{deviceSerial}")
                    ?? throw new InvalidOperationException($"Device serial '{deviceSerial}' not found in tenant '{tenantSlug}'");

        var ids = (tenant.Id, device.Id);
        _cache[cacheKey] = ids; // Cache for future
        return ids;
    }

    private record TenantDto(Guid Id, string Name, string Slug);
    private record DeviceDto(Guid Id, Guid TenantId, Guid? RoomId, string Model, string Serial, string Status);
}

// REALTIME PUBLISHER
public class RealtimeConfig
{
    public string HubUrl { get; set; } = "http://localhost:5103/hub/telemetry";
}

public interface IRealtimePublisher
{
    Task PublishAsync(string tenantSlug, Guid deviceId, string type, double value, string unit, DateTimeOffset time);
}

// Implementation using SignalR
public class SignalRRealtimePublisher : IRealtimePublisher
{
    private readonly HubConnection _conn;
    public SignalRRealtimePublisher(HubConnection conn) => _conn = conn;

    public async Task PublishAsync(string tenantSlug, Guid deviceId, string type, double value, string unit, DateTimeOffset time)
    {
        var payload = new
        {
            TenantSlug = tenantSlug,
            DeviceId = deviceId,
            Type = type,
            Value = value,
            Unit = unit,
            Time = time
        };

        await _conn.InvokeAsync("PublishMeasurement", payload); // Send measurement to hub
    }
}
