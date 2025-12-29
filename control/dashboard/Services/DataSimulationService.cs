using System;
using System.Threading;
using Terminal.Gui.App;

namespace dashboard.Services;

public class DataSimulationService : IDisposable
{
    private readonly Random _random = new();
    private Timer? _timer;
    private readonly Action<string> _onDataGenerated;

    private static readonly string[] Sensors = { "TEMP", "HUMID", "PRESS", "LIGHT", "SOUND" };
    private static readonly string[] Locations = { "Room-A", "Room-B", "Room-C", "Outdoor" };

    public DataSimulationService(Action<string> onDataGenerated)
    {
        _onDataGenerated = onDataGenerated ?? throw new ArgumentNullException(nameof(onDataGenerated));
    }

    public void Start()
    {
        ScheduleNextEvent();
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void ScheduleNextEvent()
    {
        // Random interval between 1-5 seconds
        int interval = _random.Next(1000, 5001);

        _timer = new Timer(_ =>
        {
            Application.Invoke(() =>
            {
                GenerateData();
                ScheduleNextEvent();
            });
        }, null, interval, System.Threading.Timeout.Infinite);
    }

    private void GenerateData()
    {
        string sensor = Sensors[_random.Next(Sensors.Length)];
        string location = Locations[_random.Next(Locations.Length)];
        double value = _random.NextDouble() * 100;
        string status = _random.Next(100) > 10 ? "OK" : "WARN";

        string csvData = $"{sensor},{location},{value:F2},{status}";
        _onDataGenerated(csvData);
    }

    public void Dispose()
    {
        Stop();
    }
}
