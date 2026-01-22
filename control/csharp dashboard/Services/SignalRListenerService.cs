using System;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace dashboard.Services;

public sealed class SignalRListenerService : IAsyncDisposable
{
    private readonly Uri _uri;
    private readonly Action<string> _onMessage;
    private readonly CancellationTokenSource _cts = new();

    // Backoff settings (same as playground)
    private const int MinBackoffMs = 500;
    private const int MaxBackoffMs = 30_000;
    private const double BackoffFactor = 2.0;

    public SignalRListenerService(string hubUrl, Action<string> onMessage)
    {
        if (!Uri.TryCreate(hubUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "ws" && uri.Scheme != "wss"))
        {
            throw new ArgumentException($"Invalid websocket URI: {hubUrl}", nameof(hubUrl));
        }

        _uri = uri;
        _onMessage = onMessage ?? throw new ArgumentNullException(nameof(onMessage));
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
            catch (WebSocketException wex) when (wex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                _onMessage("[WS] Connection closed prematurely by remote. Will retry.");
                _onMessage($"[WS] WebSocketException: ErrorCode={wex.ErrorCode}, WebSocketErrorCode={wex.WebSocketErrorCode}");
                backoffMs = await WaitWithBackoffAsync(backoffMs);
            }
            catch (Exception ex)
            {
                _onMessage($"[WS] Loop failed: {ex.Message}");

                if (ex is WebSocketException wex)
                    _onMessage($"[WS] WebSocketException: ErrorCode={wex.ErrorCode}, WebSocketErrorCode={wex.WebSocketErrorCode}");

                if (ex.InnerException is SocketException sex)
                    _onMessage($"[WS] SocketException: Code={sex.SocketErrorCode}, Message={sex.Message}");

                backoffMs = await WaitWithBackoffAsync(backoffMs);
            }
        }

        _onMessage("[WS] Listener stopped.");
    }

    private async Task<int> WaitWithBackoffAsync(int backoffMs)
    {
        _onMessage($"[WS] Reconnecting in {backoffMs}ms...");

        try
        {
            await Task.Delay(backoffMs, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            return backoffMs;
        }

        return Math.Min(MaxBackoffMs, (int)(backoffMs * BackoffFactor));
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
                            return gotAnyData;
                        }

                        if (result.Count > 0)
                            gotAnyData = true;

                        ms.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);
                }
                catch (WebSocketException wsex)
                {
                    // This is where ConnectionClosedPrematurely shows up
                    _onMessage($"[WS] Receive failed: {wex.Message}");
                    _onMessage($"[WS] WebSocketState={ws.State}, ErrorCode={wsex.ErrorCode}, WebSocketErrorCode={wsex.WebSocketErrorCode}");
                    // Do NOT rethrow; break out and let StartAsync handle reconnect/backoff
                    break;
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

        // We connected; report whether we saw any data
        return gotAnyData;
    }

    private Task ProcessFrameAsync(WebSocketMessageType type, byte[] payload)
    {
        if (type == WebSocketMessageType.Text)
        {
            var text = SafeUtf8(payload);
            _onMessage(text);

            var pairs = ParseKeyValuePairs(text);
            if (pairs.Count > 0)
            {
                foreach (var kv in pairs)
                    _onMessage($"{kv.Key} = {kv.Value}");
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

    private static string SafeUtf8(byte[] bytes)
    {
        try { return Encoding.UTF8.GetString(bytes); }
        catch { return "<invalid-utf8>"; }
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
                dict[k] = v;
            else
                dict[k] = dict[k] + "," + v;
        }

        return dict;
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts.Cancel(); } catch { }
        _cts.Dispose();
        await Task.CompletedTask;
    }
}