using Microsoft.EntityFrameworkCore;
using Innovia.Shared.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<InnoviaDbContext>(o => 
    o.UseNpgsql(builder.Configuration.GetConnectionString("Db")));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add cors to allow frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
    });
});

var app = builder.Build();

// Use CORS
app.UseCors("AllowFrontend");

// Ensure database and tables exist (quick-start dev convenience)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InnoviaDbContext>();
    db.Database.EnsureCreated();
}

// Enable Swagger always (not only in Development)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "DeviceRegistry.Api v1");
    c.RoutePrefix = "swagger";
});
// Redirect root to Swagger UI for convenience
app.MapGet("/", () => Results.Redirect("/swagger"));

app.MapPost("/api/tenants", async (InnoviaDbContext db, Tenant t) => {
    db.Tenants.Add(t); await db.SaveChangesAsync(); return Results.Created($"/api/tenants/{t.Id}", t);
});

app.MapPost("/api/tenants/{tenantId:guid}/devices", async (Guid tenantId, InnoviaDbContext db, Device d) => {
    d.TenantId = tenantId;
    db.Devices.Add(d); await db.SaveChangesAsync();
    return Results.Created($"/api/tenants/{tenantId}/devices/{d.Id}", d);
});


app.MapGet("/api/tenants/{tenantId:guid}/devices/{deviceId:guid}", async (Guid tenantId, Guid deviceId, InnoviaDbContext db) => {
    var d = await db.Devices.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == deviceId);
    return d is null ? Results.NotFound() : Results.Ok(d);
});

// List all devices for a tenant
app.MapGet("/api/tenants/{tenantId:guid}/devices",
    async (Guid tenantId, InnoviaDbContext db) =>
{
    var list = await db.Devices
        .Where(d => d.TenantId == tenantId)
        .ToListAsync();
    return Results.Ok(list);
});

// Lookup tenant by slug (for cross-service resolution)
app.MapGet("/api/tenants/by-slug/{slug}",
    async (string slug, InnoviaDbContext db) =>
{
    var t = await db.Tenants.FirstOrDefaultAsync(x => x.Slug == slug);
    return t is null ? Results.NotFound() : Results.Ok(t);
});

// Lookup device by serial within a tenant (for cross-service resolution)
app.MapGet("/api/tenants/{tenantId:guid}/devices/by-serial/{serial}",
    async (Guid tenantId, string serial, InnoviaDbContext db) =>
{
    var d = await db.Devices.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Serial == serial);
    return d is null ? Results.NotFound() : Results.Ok(d);
});

// -----------------Update a device------------------
app.MapPatch("/api/tenants/{tenantId:guid}/devices/{deviceId:guid}", 
    async (Guid tenantId, Guid deviceId, InnoviaDbContext db, Device updatedDevice) =>
{
    var d = await db.Devices.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == deviceId);
    if (d == null) return Results.NotFound();

    // Bara uppdatera fälten du vill ändra
    d.Model = updatedDevice.Model ?? d.Model;
    d.Serial = updatedDevice.Serial ?? d.Serial;
    d.Status = updatedDevice.Status ?? d.Status;

    await db.SaveChangesAsync();
    return Results.Ok(d);
});
// ---------------------------------------------------

app.Run();


public class InnoviaDbContext : DbContext
{
    public InnoviaDbContext(DbContextOptions<InnoviaDbContext> o) : base(o) {}
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Device> Devices => Set<Device>();
}

public class Tenant { public Guid Id {get; set;} = Guid.NewGuid(); public string Name {get; set;} = ""; public string Slug {get; set;} = ""; }
public class Device { public Guid Id {get; set;} = Guid.NewGuid(); public Guid TenantId {get; set;} public Guid? RoomId {get; set;} public string Model {get; set;} = ""; public string Serial {get; set;} = ""; public string Status {get; set;} = "active"; }
