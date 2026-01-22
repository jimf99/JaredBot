using System;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using dashboard.Models;

namespace dashboard.Services;

public sealed class SignalRListenerService : IAsyncDisposable
{
    private readonly Uri _uri;
    private readonly Action<string> _onMessage;
    private readonly Action<TelemetrySnapshot>? _onTelemetry;
    private readonly TelemetryState? _telemetry;
    private readonly CancellationTokenSource _cts = new();

    // Backoff settings (same as playground)
    private const int MinBackoffMs = 500;
    private const int MaxBackoffMs = 30_000;
    private const double BackoffFactor = 2.0;

    public SignalRListenerService(
        string hubUrl,
        Action<string> onMessage,
        TelemetryState? telemetry = null,
        Action<TelemetrySnapshot>? onTelemetry = null)
    {
        if (!Uri.TryCreate(hubUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "ws" && uri.Scheme != "wss"))
        {
            throw new ArgumentException($"Invalid websocket URI: {hubUrl}", nameof(hubUrl));
        }

        _uri = uri;
        _onMessage = onMessage ?? throw new ArgumentNullException(nameof(onMessage));
        _telemetry = telemetry;
        _onTelemetry = onTelemetry;
    }

    public async Task StartAsync()
    {
        _onMessage($"[WS] Connecting to {_uri} (press 'Q' in dashboard to quit).");

        var backoffMs = MinBackoffMs;

        while (!_cts.IsCancellationRequested)
        {
            var connected = false;

            try
            {
                connected = await ConnectAndReceiveLoopAsync(_uri, _cts.Token);
                if (connected)
                {
                    // We had a successful session (connect + at least one receive or clean close)
                    backoffMs = MinBackoffMs;
                }
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _onMessage($"[WS] Loop failed: {ex.Message}");

                if (ex is WebSocketException wex)
                    _onMessage($"[WS] WebSocketException: ErrorCode={wex.ErrorCode}, WebSocketErrorCode={wex.WebSocketErrorCode}");

                if (ex.InnerException is SocketException sex)
                    _onMessage($"[WS] SocketException: Code={sex.SocketErrorCode}, Message={sex.Message}");

                _onMessage($"[WS] Reconnecting in {backoffMs}ms...");

                try
                {
                    await Task.Delay(backoffMs, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                backoffMs = Math.Min(MaxBackoffMs, (int)(backoffMs * BackoffFactor));
            }
        }

        _onMessage("[WS] Listener stopped.");
    }

    private async Task<bool> ConnectAndReceiveLoopAsync(Uri uri, CancellationToken cancellation)
    {
        using var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(0); // disable built-in pings

        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(30)); // give ESP32 more time

        await ws.ConnectAsync(uri, connectTimeout.Token);
        _onMessage("[WS] Connected. Receiving frames until shutdown or remote close...");

        var buffer = new byte[16 * 1024];
        var gotAnyData = false;

        try
        {
            while (!cancellation.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                using var ms = new System.IO.MemoryStream();
                WebSocketReceiveResult result;

                try
                {
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _onMessage($"[WS] Server sent close: {result.CloseStatus} - {result.CloseStatusDescription}");
                            await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client ack", CancellationToken.None);
                            return false;
                        }

                        if (result.Count > 0)
                            gotAnyData = true;

                        ms.Write(buffer, 0, result.Count);
                    } 
                    while (!result.EndOfMessage);
                }
                catch (WebSocketException wsex)
                {
                    _onMessage($"[WS] Receive failed: {wsex.Message}");
                    _onMessage($"[WS] WebSocketState={ws.State}, ErrorCode={wsex.ErrorCode}, WebSocketErrorCode={wsex.WebSocketErrorCode}");
                    throw;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    break;
                }

                var messageBytes = ms.ToArray();
                await ProcessFrameAsync(result.MessageType, messageBytes);
            }
        }
        finally
        {
            if (ws.State == WebSocketState.Open)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client done", CancellationToken.None); }
                catch { }
            }
        }

        return gotAnyData || true; // we had a clean session
    }

    private Task ProcessFrameAsync(WebSocketMessageType type, byte[] payload)
    {
        if (type == WebSocketMessageType.Text)
        {
            var text = SafeUtf8(payload);
            _onMessage(text); // raw line to log

            if (text.StartsWith("T:", StringComparison.OrdinalIgnoreCase))
            {
                var snapshot = ParseTelemetryCsv(text);
                if ( snapshot is not null)
                {
                    // Update shared state if used
                    _telemetry?.Update(snapshot);
                    // Immediate UI update callback
                    _onTelemetry?.Invoke(snapshot);
                }
            }
            else
            {
                // optional: keep key=value parsing for non-telemetry messages
                var pairs = ParseKeyValuePairs(text);
                if (pairs.Count > 0)
                {
                    foreach (var kv in pairs)
                        _onMessage($"{kv.Key} = {kv.Value}");
                }
            }
        }
        else
        {
            var previewLen = Math.Min(payload.Length, 128);
            var hex = Convert.ToHexString(payload, 0, previewLen);
            var preview = payload.Length <= previewLen ? hex : hex + "...";
            _onMessage($"Binary preview (hex): {preview}");
        }

        return Task.CompletedTask;
    }

    private TelemetrySnapshot? ParseTelemetryCsv(string line)
    {
        // Example: T:-0.01,0.36,6.02,2.00,2.00
        var span = line.AsSpan().Trim();

        if (span.StartsWith("T:", StringComparison.OrdinalIgnoreCase))
        {
            span = span[2..].TrimStart();
        }

        var csv = span.ToString();
        var parts = csv.Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length < 3)
            return null; // not enough data

        double? TryDoubleAt(int index)
        {
            if (index < 0 || index >= parts.Length)
                return null;

            var s = parts[index];
            if (double.TryParse(
                    s,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var v))
            {
                return v;
            }

            return null;
        }

        return new TelemetrySnapshot
        {
            UtcTimestamp = DateTime.UtcNow,
            RawLine = line,

            Roll = TryDoubleAt(0),
            Pitch = TryDoubleAt(1),
            Yaw = TryDoubleAt(2),

            Custom1 = TryDoubleAt(3),
            Custom2 = TryDoubleAt(4)
        };
    }

    private static string SafeUtf8(byte[] bytes)
    {
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return "<invalid-utf8>";
        }
    }

    private static System.Collections.Generic.Dictionary<string, string> ParseKeyValuePairs(string input)
    {
        var dict = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pattern = new Regex(@"(?<k>[A-Za-z0-9_]+)\s*=\s*(?<v>[^\s]+)", RegexOptions.Compiled);

        foreach (Match m in pattern.Matches(input))
        {
            var k = m.Groups["k"].Value;
            var v = m.Groups["v"].Value;

            if (!dict.ContainsKey(k))
            {
                dict[k] = v;
            }
            else
            {
                dict[k] = dict[k] + "," + v;
            }
        }

        return dict;
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

        _cts.Dispose();
        await Task.CompletedTask;
    }
}
