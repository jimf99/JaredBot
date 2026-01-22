using System;
using dashboard.Models;

namespace dashboard.Services;

public sealed class TelemetryState
{
    private readonly object _lock = new();
    private TelemetrySnapshot? _latest;

    public void Update(TelemetrySnapshot snapshot)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

        lock (_lock)
        {
            _latest = snapshot;
        }
    }

    public TelemetrySnapshot? GetLatest()
    {
        lock (_lock)
        {
            return _latest;
        }
    }
}
