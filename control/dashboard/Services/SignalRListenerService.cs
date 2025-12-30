using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

namespace dashboard.Services;

public sealed class SignalRListenerService : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly Action<string> _onMessage;
    private readonly CancellationTokenSource _cts = new();

    public SignalRListenerService(string hubUrl, Action<string> onMessage)
    {
        _onMessage = onMessage ?? throw new ArgumentNullException(nameof(onMessage));

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        // Generic string message handler — tweak method name/parameters
        _connection.On<string>("ReceiveMessage", message =>
        {
            _onMessage($"[SignalR] {message}");
        });

        _connection.Reconnecting += error =>
        {
            _onMessage($"[SignalR] Reconnecting: {error?.Message}");
            return Task.CompletedTask;
        };

        _connection.Reconnected += connectionId =>
        {
            _onMessage($"[SignalR] Reconnected. ConnectionId={connectionId}");
            return Task.CompletedTask;
        };

        _connection.Closed += error =>
        {
            _onMessage($"[SignalR] Closed: {error?.Message}");
            return Task.CompletedTask;
        };
    }

    public async Task StartAsync()
    {
        try
        {
            await _connection.StartAsync(_cts.Token);
            _onMessage("[SignalR] Connected.");
        }
        catch (Exception ex)
        {
            _onMessage($"[SignalR] Failed to connect: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _cts.Cancel();
        }
        catch
        {
        }

        try
        {
            await _connection.StopAsync();
        }
        catch
        {
        }

        await _connection.DisposeAsync();
        _cts.Dispose();
    }
}
