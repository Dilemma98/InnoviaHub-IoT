using MQTTnet;
using MQTTnet.Client;
using System.Text.Json;
using System.Text;

// Create MQTT-client
var factory = new MqttFactory();
var client = factory.CreateMqttClient();

// Read devices and it's measurments-list
var path = Path.Combine(AppContext.BaseDirectory, "Data", "devices.json");
var json = await File.ReadAllTextAsync(path);
var devices = JsonSerializer.Deserialize<List<Device>>(json);

// Configure MQTT-connection
var options = new MqttClientOptionsBuilder()
    .WithTcpServer("localhost", 1883)
    .Build();

Console.WriteLine("Edge.Simulator starting… connecting to MQTT at localhost:1883");
try
{
    await client.ConnectAsync(options);
    Console.WriteLine("✅ Connected to MQTT broker.");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Failed to connect to MQTT broker: {ex.Message}");
    throw;
}


// Create random-generator
var rand = new Random();

// Loop continuously and send measurements-data
while (true)
{
    // Loop through every device in list
    foreach (var device in devices!)
    {
        var metricsList = new List<object>();
        // Loop through every metric in devices metrics-list
        foreach (var metric in device.Metrics)
        {

            double value = metric.Unit == "bool"
                ? rand.Next(0, 2)
                : metric.Min + rand.NextDouble() * (metric.Max - metric.Min);

            metricsList.Add(new
            {
                type = metric.Type,
                value = Math.Round(value, 2),
                Unit = metric.Unit
            });
        }

        // Create payload object to send
        var payload = new
        {
            deviceId = device.Id,
            apiKey = device.ApiKey,
            timestamp = DateTimeOffset.UtcNow,
            metrics = metricsList
        };

        // Create topic-string for MQTT
        // formated as Ingest.Gateway wants it
        // tenant -> device -> measurements
        var topic = $"tenants/innovia/devices/{device.Id}/measurements";

        // Serialize payload-object to JSON-text
        // (what's acutally sent to MQTT)
        var jsonPayload = JsonSerializer.Serialize(payload);

        // Create MQTT-message
        // - WithTopic defines which topic the message is sent to
        // - WithPayload contains the data itself (as bytes)
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(jsonPayload))
            .Build();

        // Publish message to MQTT-broker (Mosquitto)
        // Represents measuring-updates
        await client.PublishAsync(message);
        Console.WriteLine($"Sent to {topic}: {jsonPayload}");

    }  
        // Update every 5 seconds
        await Task.Delay(TimeSpan.FromSeconds(5));
}

// Records for MetricsList and Devices in json-file
public record MetricDef(string Type, string Unit, double Min, double Max);
public record Device(string Id, string ApiKey, List<MetricDef> Metrics);