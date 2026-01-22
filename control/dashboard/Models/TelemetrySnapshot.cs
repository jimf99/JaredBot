namespace dashboard.Models;

public sealed class TelemetrySnapshot
{
    public DateTime UtcTimestamp { get; init; }

    public double? Roll { get; init; }
    public double? Pitch { get; init; }
    public double? Yaw { get; init; }

    public double? Custom1 { get; init; }
    public double? Custom2 { get; init; }

    public string? RawLine { get; init; }
}