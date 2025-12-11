using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using Spectre.Console;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection; // Add this using directive
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Net.Sockets;
using System.Globalization;
using System.Reflection;

public static class Program
{
    // Reconnect settings
    const int MinBackoffMs = 500;
    const int MaxBackoffMs = 30_000;
    const double BackoffFactor = 2.0;

    // Panel settings
    const int PanelHeight = 4; // visible lines in the top panel

    static readonly LogBuffer Logs = new LogBuffer(PanelHeight);

    public static async Task Main(string[] args)
    {
        // Start the live panel and key handler so all output goes into the top panel instead of scrolling the console.
        using var uiCts = new CancellationTokenSource();
        var liveTask = Task.Run(() => RunLivePanelLoop(uiCts.Token), CancellationToken.None);
        var keyTask = Task.Run(() => RunKeyLoop(uiCts.Token), CancellationToken.None);

        var uriString = args.Length > 0 ? args[0] : "ws://192.168.1.88/ws";

        if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "ws" && uri.Scheme != "wss"))
        {
            Log($"Invalid websocket URI: {uriString}");
            await ShutdownUI(uiCts, liveTask, keyTask);
            return;
        }

        Log($"Raw WebSocket client connecting to: {uriString}");

        using var shutdownCts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            Log("Shutdown requested...");
            shutdownCts.Cancel();
        };

        var backoffMs = MinBackoffMs;

        while (!shutdownCts.IsCancellationRequested)
        {
            try
            {
                await ConnectAndReceiveLoopAsync(uri, shutdownCts.Token);
                // If ConnectAndReceiveLoopAsync returns normally (server closed gracefully), reset backoff and retry
                backoffMs = MinBackoffMs;
            }
            catch (OperationCanceledException) when (shutdownCts.IsCancellationRequested)
            {
                // Shutdown requested -> exit
                break;
            }
            catch (Exception ex)
            {
                // Detect abrupt socket close scenarios and log diagnostics
                Log($"WebSocket loop failed: {ex.Message}");
                if (ex is WebSocketException wex)
                {
                    Log($"WebSocketException: ErrorCode={wex.ErrorCode}, WebSocketState={wex.WebSocketErrorCode}");
                }
                if (ex.InnerException is SocketException sex)
                {
                    Log($"SocketException: Code={sex.SocketErrorCode}, Message={sex.Message}");
                }

                // Exponential backoff before retrying
                Log($"Reconnecting in {backoffMs}ms...");
                try { await Task.Delay(backoffMs, shutdownCts.Token); } catch (OperationCanceledException) { break; }
                backoffMs = Math.Min(MaxBackoffMs, (int)(backoffMs * BackoffFactor));
            }
        }

        Log("Client shutdown complete.");

        await ShutdownUI(uiCts, liveTask, keyTask);
    }

    static async Task ConnectAndReceiveLoopAsync(Uri uri, CancellationToken cancellation)
    {
        using var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

        // Short connect timeout but allow overall cancellation
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(15));

        await ws.ConnectAsync(uri, connectTimeout.Token);
        Log("Connected. Receiving frames until shutdown or remote close...");

        var buffer = new byte[16 * 1024];

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
                            Log($"Server sent close: {result.CloseStatus} - {result.CloseStatusDescription}");
                            // A proper close handshake: send close in response and exit the loop normally.
                            await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client ack", CancellationToken.None);
                            return;
                        }

                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);
                }
                catch (WebSocketException wsex)
                {
                    // This is commonly where "remote party closed ... without completing the close handshake" appears.
                    // Log details and throw to trigger outer reconnect/backoff handling.
                    Log($"Receive failed: {wsex.Message}");
                    Log($"WebSocketState={ws.State}, ErrorCode={wsex.ErrorCode}, WebSocketErrorCode={wsex.WebSocketErrorCode}");
                    throw;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    // shutdown requested -> break
                    break;
                }

                var messageBytes = ms.ToArray();
                await ProcessFrameAsync(result.MessageType, messageBytes);
            }
        }
        finally
        {
            // Try to close cleanly if still open; ignore errors
            if (ws.State == WebSocketState.Open)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client done", CancellationToken.None); }
                catch { }
            }
        }
    }

    static Task ProcessFrameAsync(WebSocketMessageType type, byte[] payload)
    {
        var utc = DateTime.UtcNow;
        Log($"{utc:O} Frame -> type={type} length={payload.Length}");

        if (type == WebSocketMessageType.Text)
        {
            var text = SafeUtf8(payload);
            Log(text);

            var pairs = ParseKeyValuePairs(text);
            if (pairs.Count > 0)
            {
                foreach (var kv in pairs)
                    Log($"{kv.Key} = {kv.Value}");
            }
            else
            {
                Log("No key/value pairs parsed from text frame.");
            }
        }
        else
        {
            var previewLen = Math.Min(payload.Length, 128);
            var hex = Convert.ToHexString(payload, 0, previewLen);
            var preview = payload.Length <= previewLen ? hex : hex + "...";
            Log($"Binary preview (hex): {preview}");
            var base64 = Convert.ToBase64String(payload);
            Log($"Base64 (first 256 chars): {base64.Substring(0, Math.Min(256, base64.Length))}");
        }

        return Task.CompletedTask;
    }

    static System.Collections.Generic.Dictionary<string, string> ParseKeyValuePairs(string input)
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

    static string SafeUtf8(byte[] bytes)
    {
        try { return Encoding.UTF8.GetString(bytes); }
        catch { return "<invalid-utf8>"; }
    }

    // ---------------- UI / live panel ----------------

    static void RunLivePanelLoop(CancellationToken cancellation)
    {
        // Initial renderable (LogBuffer now returns a cached panel instance)
        var panel = Logs.GetPanelRenderable();

        // Use Live to keep the panel at the top and update it frequently.
        // This prevents other console text from scrolling the screen because we avoid writing to the console directly.
        AnsiConsole.Live(panel)
            .AutoClear(false)
            .Overflow(VerticalOverflow.Crop)
            .Cropping(VerticalOverflowCropping.Top)
            .Start(ctx =>
            {
                while (!cancellation.IsCancellationRequested)
                {
                    // UpdateTarget will receive the same Panel instance (its internals are updated in-place).
                    var updated = Logs.GetPanelRenderable();
                    ctx.UpdateTarget(updated);
                    ctx.Refresh();
                    Thread.Sleep(100);
                }
            });
    }

    static async Task RunKeyLoop(CancellationToken cancellation)
    {
        // Key controls:
        // Up/Down: scroll one line
        // PageUp/PageDown: scroll page
        // Home: go to top
        // End: go to bottom (resume auto-scroll)
        // A: toggle auto-scroll
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                if (!Console.KeyAvailable)
                {
                    await Task.Delay(100, cancellation);
                    continue;
                }

                var key = Console.ReadKey(true);
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        Logs.ScrollUp(1);
                        break;
                    case ConsoleKey.DownArrow:
                        Logs.ScrollDown(1);
                        break;
                    case ConsoleKey.PageUp:
                        Logs.PageUp();
                        break;
                    case ConsoleKey.PageDown:
                        Logs.PageDown();
                        break;
                    case ConsoleKey.Home:
                        Logs.Top();
                        break;
                    case ConsoleKey.End:
                        Logs.Bottom();
                        break;
                    case ConsoleKey.A:
                        Logs.ToggleAutoScroll();
                        break;
                }
            }
            catch (OperationCanceledException) { break; }
            catch { /* ignore unexpected read errors */ }
        }
    }

    static async Task ShutdownUI(CancellationTokenSource uiCts, Task liveTask, Task keyTask)
    {
        try { uiCts.Cancel(); } catch { }
        try { await Task.WhenAll(liveTask, keyTask); } catch { }
    }

    // Central logging helper that pushes output into the top panel instead of the console.
    static void Log(string message)
    {
        Logs.Add(message);
    }

    static void Log(string format, params object[] args)
    {
        Logs.Add(string.Format(format, args));
    }

    // ---------------- LogBuffer implementation ----------------
    class LogBuffer
    {
        readonly object _lock = new object();
        readonly List<string> _lines = new List<string>();
        readonly int _viewHeight;
        int _viewportStart = 0; // index of first visible line
        bool _autoScroll = true;
        const int MaxStoredLines = 10_000;

        // Reused panel instance (we update its internals rather than creating a new Panel every refresh)
        Panel _cachedPanel;

        public LogBuffer(int viewHeight)
        {
            _viewHeight = Math.Max(1, viewHeight);

            // Create an initial empty panel that will be reused.
            var emptyLines = Enumerable.Range(0, _viewHeight).Select(_ => string.Empty);
            var initialContent = string.Join("\n", emptyLines);
            var headerRaw = $"Logs (0 total) [autoscroll]";
            _cachedPanel = new Panel(new Markup(Markup.Escape(initialContent)))
            {
                Header = new PanelHeader(Markup.Escape(headerRaw)),
                Expand = true,
            };
        }

        public void Add(string raw)
        {
            var line = raw?.TrimEnd() ?? string.Empty;
            lock (_lock)
            {
                _lines.Add(line);
                if (_lines.Count > MaxStoredLines)
                {
                    var remove = _lines.Count - MaxStoredLines;
                    _lines.RemoveRange(0, remove);
                    _viewportStart = Math.Max(0, _viewportStart - remove);
                }

                if (_autoScroll)
                {
                    _viewportStart = Math.Max(0, _lines.Count - _viewHeight);
                }
                else
                {
                    // keep viewportStart within valid range
                    _viewportStart = Math.Min(_viewportStart, Math.Max(0, _lines.Count - _viewHeight));
                }
            }
        }

        public void ScrollUp(int lines)
        {
            lock (_lock)
            {
                _autoScroll = false;
                _viewportStart = Math.Max(0, _viewportStart - Math.Max(1, lines));
            }
        }

        public void ScrollDown(int lines)
        {
            lock (_lock)
            {
                _viewportStart = Math.Min(Math.Max(0, _lines.Count - _viewHeight), _viewportStart + Math.Max(1, lines));
            }
        }

        public void PageUp() => ScrollUp(_viewHeight);
        public void PageDown() => ScrollDown(_viewHeight);
        public void Top()
        {
            lock (_lock)
            {
                _autoScroll = false;
                _viewportStart = 0;
            }
        }

        public void Bottom()
        {
            lock (_lock)
            {
                _autoScroll = true;
                _viewportStart = Math.Max(0, _lines.Count - _viewHeight);
            }
        }

        public void ToggleAutoScroll()
        {
            lock (_lock)
            {
                _autoScroll = !_autoScroll;
                if (_autoScroll)
                    _viewportStart = Math.Max(0, _lines.Count - _viewHeight);
            }
        }

        public Panel GetPanelRenderable()
        {
            lock (_lock)
            {
                var visible = new List<string>();
                //for (int i = 0; i < _viewHeight; i++)
                //{
                //    var idx = _viewportStart + i;
                //    if (idx >= 0 && idx < _lines.Count)
                //        visible.Add(Program.EscapeMarkup(_lines[idx]));
                //    else
                //        visible.Add(string.Empty);
                //}

                var headerRaw = $"Logs ({_lines.Count} total) {(_autoScroll ? "[autoscroll]" : "[manual]")}";
                var headerEscaped = Program.EscapeMarkup(headerRaw);
                var content = string.Join("", visible);
                var markup = new Markup(content);

                // Try to update the cached panel's internal renderable in-place to avoid creating a new Panel instance.
                // We set the public Header and attempt to set the panel's private renderable field via reflection.
                _cachedPanel.Header = new PanelHeader(headerEscaped);

                // Reflection: try several likely private field/backing-field names used across versions.
                var panelType = typeof(Panel);
                FieldInfo? field = panelType.GetField("_renderable", BindingFlags.Instance | BindingFlags.NonPublic)
                                       ?? panelType.GetField("renderable", BindingFlags.Instance | BindingFlags.NonPublic)
                                       ?? panelType.GetField("<Renderable>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

                if (field != null)
                {
                    field.SetValue(_cachedPanel, markup);
                }
                else
                {
                    // Fallback: if reflection failed, replace cached panel with a new instance
                    _cachedPanel = new Panel(markup)
                    {
                        Header = new PanelHeader(headerEscaped),
                        Expand = true,
                    };
                }

                return _cachedPanel;
            }
        }
    }

    static string EscapeMarkup(string? s) => s is null ? string.Empty : Markup.Escape(s);

    // Helper to crop by text elements (avoids splitting surrogate pairs / combined glyphs)
    static string CropTextElements(string s, int startElement, int elementCount)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;

        var info = new StringInfo(s);
        var total = info.LengthInTextElements;
        if (startElement >= total)
            return string.Empty;

        var take = Math.Min(elementCount, total - startElement);
        var sub = info.SubstringByTextElements(startElement, take) ?? string.Empty;

        // pad to width so lines are equal length (keeps layout stable)
        if (sub.Length < elementCount)
            sub = sub.PadRight(elementCount);

        return sub;
    }
}