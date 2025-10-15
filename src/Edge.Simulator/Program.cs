using MQTTnet;
using MQTTnet.Client;
using System.Text.Json;
using System.Text;

var factory = new MqttFactory();
var client = factory.CreateMqttClient();

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

// Create list of all simulated devices/sensors
var devices = new[]
{
    new { Id = "dev-101", ApiKey = "dev-101-key", Metrics = new[] {"co2"}},
    new { Id = "dev-102", ApiKey = "dev-102-key", Metrics = new[] {"temperature"}},
    new { Id = "dev-103", ApiKey = "dev-103-key", Metrics = new[] {"humidity"}},
    new { Id = "dev-104", ApiKey = "dev-104-key", Metrics = new[] {"light"}},
    new { Id = "dev-105", ApiKey = "dev-105-key", Metrics = new[] {"motion"}},
    new { Id = "dev-106", ApiKey = "dev-106-key", Metrics = new[] {"sound"}},
    new { Id = "dev-107", ApiKey = "dev-107-key", Metrics = new[] {"airQuality"}},
    new { Id = "dev-108", ApiKey = "dev-108-key", Metrics = new[] {"waterLeak"}},
    new { Id = "dev-109", ApiKey = "dev-109-key", Metrics = new[] {"smoke"}},
    new { Id = "dev-110", ApiKey = "dev-110-key", Metrics = new[] {"vibration"}},
};
// Create random
var rand = new Random();

// Simulate new data incoming continuously
while (true)
{
    // Loop through every device in list
    foreach (var device in devices)
    {
        // Create list to obtain measurements
        var metricsList = new List<object>();

        // Loop through every metrics-type in devices
        foreach (var type in device.Metrics)
        {
            // Define how object Metric should look like
            // and simulate value
            object metric = type switch
            {
                "co2" => new { type, value = 800 + rand.Next(0, 500), unit = "ppm" },
                "temperature" => new { type, value = 20 + rand.NextDouble() * 5, unit = "C" },
                "humidity" => new { type, value = 30 + rand.NextDouble() * 40, unit = "%" },
                "light" => new { type, value = 100 + rand.Next(0, 900), unit = "lux" },
                "motion" => new { type, value = rand.Next(0, 2), unit = "bool" },
                "sound" => new { type, value = 30 + rand.Next(0, 70), unit = "dB" },
                "airQuality" => new { type, value = rand.Next(0, 500), unit = "AQI" },
                "waterLeak" => new { type, value = rand.Next(0, 2), unit = "bool" },
                "smoke" => new { type, value = rand.Next(0, 2), unit = "bool" },
                "vibration" => new { type, value = Math.Round(rand.NextDouble() * 5, 2), unit = "m/s²" },
                _ => throw new Exception($"Unknown metric tye: {type}")
            };

            metricsList.Add(metric);
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
        var json = JsonSerializer.Serialize(payload);

        // Create MQTT-message
        // - WithTopic defines which topic the message is sent to
        // - WithPayload contains the data itself (as bytes)
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(Encoding.UTF8.GetBytes(json))
            .Build();

        // Publish message to MQTT-broker (Mosquitto)
        // Represents measuring-updates
        await client.PublishAsync(message);
        Console.WriteLine($"Sent to {topic}: {json}");
    }

    // Update every 10 seconds
    await Task.Delay(TimeSpan.FromSeconds(10));
}
