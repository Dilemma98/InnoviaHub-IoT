namespace Innovia.Shared.DTOs;

public record MetricDto(string Type, double Value, string Unit);
public class MeasurementBatch
{
    public string DeviceId { get; set; } = default!;
    public string ApiKey { get; set; } = default!;
    public DateTimeOffset Timestamp { get; set; }
    public List<MetricDto> Metrics { get; set; } = new();
}
