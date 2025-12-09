using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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

public static class Program
{
    // Reconnect settings (used by raw websocket loop)
    const int MinBackoffMs = 500;
    const int MaxBackoffMs = 30_000;
    const double BackoffFactor = 2.0;

    public static async Task Main(string[] args)
    {
        // Simple CLI:
        //   dotnet run -- [uri] [--raw] [--signalr] [--skip-negotiation] [--help]
        // If mode flags are omitted, default is:
        //   - signalr for http/https URIs
        //   - raw for ws/wss URIs
        if (args.Any(a => a.Equals("--help", StringComparison.OrdinalIgnoreCase) || a.Equals("-h", StringComparison.OrdinalIgnoreCase)))
        {
            PrintUsage();
            return;
        }

        // Extract flags and uri argument (first non-flag)
        var flagArgs = args.Where(a => a.StartsWith("-", StringComparison.Ordinal)).ToArray();
        var uriArg = args.FirstOrDefault(a => !a.StartsWith("-", StringComparison.Ordinal));
        var uriString = string.IsNullOrWhiteSpace(uriArg) ? "ws://192.168.1.88/ws" : uriArg;

        var explicitlySignalR = flagArgs.Any(a => a.Equals("--signalr", StringComparison.OrdinalIgnoreCase) || a.Equals("-s", StringComparison.OrdinalIgnoreCase));
        var explicitlyRaw = flagArgs.Any(a => a.Equals("--raw", StringComparison.OrdinalIgnoreCase) || a.Equals("-r", StringComparison.OrdinalIgnoreCase));
        var skipNegotiation = flagArgs.Any(a => a.Equals("--skip-negotiation", StringComparison.OrdinalIgnoreCase));

        if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
        {
            AnsiConsole.MarkupLine($"[red]Invalid URI:[/] {EscapeMarkup(uriString)}");
            return;
        }

        // Decide mode
        bool useSignalR;
        if (explicitlySignalR && explicitlyRaw)
        {
            AnsiConsole.MarkupLine("[red]Both --signalr and --raw specified. Choose only one.[/]");
            return;
        }
        else if (explicitlySignalR) useSignalR = true;
        else if (explicitlyRaw) useSignalR = false;
        else
        {
            // Auto-detect by scheme
            useSignalR = uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        }

        AnsiConsole.MarkupLine($"[underline yellow]Mode[/]: [green]{(useSignalR ? "SignalR client" : "Raw WebSocket client")}[/]  [underline yellow]URI[/]: [green]{EscapeMarkup(uriString)}[/]");

        using var shutdownCts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            AnsiConsole.MarkupLine("[yellow]Shutdown requested...[/]");
            shutdownCts.Cancel();
        };

        if (useSignalR)
        {
            await RunSignalRClientAsync(uriString, skipNegotiation, shutdownCts.Token);
        }
        else
        {
            var backoffMs = MinBackoffMs;
            while (!shutdownCts.IsCancellationRequested)
            {
                try
                {
                    await ConnectAndReceiveLoopAsync(uri, shutdownCts.Token);
                    backoffMs = MinBackoffMs;
                }
                catch (OperationCanceledException) when (shutdownCts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]WebSocket loop failed:[/] {EscapeMarkup(ex.Message)}");
                    if (ex is WebSocketException wex)
                        AnsiConsole.MarkupLine($"[grey]WebSocketException: ErrorCode={wex.ErrorCode}, WebSocketState={wex.WebSocketErrorCode}[/]");
                    if (ex.InnerException is SocketException sex)
                        AnsiConsole.MarkupLine($"[grey]SocketException: Code={sex.SocketErrorCode}, Message={EscapeMarkup(sex.Message)}[/]");

                    AnsiConsole.MarkupLine($"[yellow]Reconnecting in {backoffMs}ms...[/]");
                    try { await Task.Delay(backoffMs, shutdownCts.Token); } catch (OperationCanceledException) { break; }
                    backoffMs = Math.Min(MaxBackoffMs, (int)(backoffMs * BackoffFactor));
                }
            }

            AnsiConsole.MarkupLine("[grey]Client shutdown complete.[/]");
        }
    }

    static void PrintUsage()
    {
        AnsiConsole.MarkupLine(@"[yellow]Usage:[/]
  dotnet run -- [uri] [--raw | --signalr] [--skip-negotiation] [--help]

Examples:
  dotnet run -- ws://192.168.1.88/ws           # raw websocket (default for ws)
  dotnet run -- http://host/hub --signalr      # Force SignalR client (use http/https if server expects negotiate)
  dotnet run -- ws://host/ws --signalr --skip-negotiation  # SignalR over ws, skipping negotiate");
    }

    // -------- SignalR client mode --------
    static async Task RunSignalRClientAsync(string hubUrl, bool skipNegotiation, CancellationToken cancellation)
    {
        AnsiConsole.MarkupLine($"[underline yellow]Starting SignalR client[/] connecting to: [green]{EscapeMarkup(hubUrl)}[/]");

        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                if (skipNegotiation) options.SkipNegotiation = true;

                // Example: allow insecure certs in dev via handler (keeps parity with earlier samples)
                options.HttpMessageHandlerFactory = handler =>
                {
                    if (handler is HttpClientHandler clientHandler)
                    {
                        clientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                    }
                    return handler;
                };
            })
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddConsole();
            })
            .WithAutomaticReconnect()
            .Build();

        connection.Reconnecting += error =>
        {
            AnsiConsole.MarkupLine("[orange1]Reconnecting...[/]" + (error is null ? string.Empty : $" Reason: {EscapeMarkup(error.Message)}"));
            return Task.CompletedTask;
        };

        connection.Reconnected += connectionId =>
        {
            AnsiConsole.MarkupLine($"[green]Reconnected[/] (connectionId: [grey]{EscapeMarkup(connectionId)}[/])");
            return Task.CompletedTask;
        };

        connection.Closed += async error =>
        {
            AnsiConsole.MarkupLine("[red]Connection closed[/]" + (error is null ? string.Empty : $" Reason: {EscapeMarkup(error.Message)}"));
            // With automatic reconnect enabled this may not be necessary, but attempt a restart for robustness.
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
                await connection.StartAsync();
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to restart connection:[/] {EscapeMarkup(ex.Message)}");
            }
        };

        // Handlers - adapt to your Hub contract
        connection.On<string>("Message", msg => LogReceived("Message(string)", msg));
        connection.On<byte[]>("RawBinary", data =>
        {
            var preview = data.Length <= 64 ? Convert.ToHexString(data) : Convert.ToHexString(data, 0, 64) + "...";
            AnsiConsole.MarkupLine($"[magenta]{DateTime.UtcNow:O}[/] [bold]RawBinary[/] -> length={data.Length} preview=[grey]{EscapeMarkup(preview)}[/]");
        });

        try
        {
            await connection.StartAsync(cancellation);
            AnsiConsole.MarkupLine("[green]Connected.[/] Waiting for messages. Press Ctrl+C to exit.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to start SignalR connection:[/] {EscapeMarkup(ex.Message)}");
            return;
        }

        try
        {
            await Task.Delay(Timeout.Infinite, cancellation);
        }
        catch (OperationCanceledException) { /* expected */ }

        AnsiConsole.MarkupLine("[grey]Stopping SignalR connection...[/]");
        try
        {
            await connection.StopAsync();
            await connection.DisposeAsync();
            AnsiConsole.MarkupLine("[green]Stopped.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error stopping connection:[/] {EscapeMarkup(ex.Message)}");
        }
    }

    // -------- Raw websocket mode (unchanged) --------
    static async Task ConnectAndReceiveLoopAsync(Uri uri, CancellationToken cancellation)
    {
        using var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

        // Short connect timeout but allow overall cancellation
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(15));

        await ws.ConnectAsync(uri, connectTimeout.Token);
        AnsiConsole.MarkupLine("[green]Connected.[/] Receiving frames until shutdown or remote close...");

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
                            AnsiConsole.MarkupLine($"[orange1]Server sent close:[/] {result.CloseStatus} - {EscapeMarkup(result.CloseStatusDescription)}");
                            // A proper close handshake: send close in response and exit the loop normally.
                            await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client ack", CancellationToken.None);
                            return;
                        }

                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);
                }
                catch (WebSocketException wsex)
                {
                    AnsiConsole.MarkupLine($"[red]Receive failed: {EscapeMarkup(wsex.Message)}[/]");
                    AnsiConsole.MarkupLine($"[grey]WebSocketState={ws.State}, ErrorCode={wsex.ErrorCode}, WebSocketErrorCode={wsex.WebSocketErrorCode}[/]");
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
    }

    static Task ProcessFrameAsync(WebSocketMessageType type, byte[] payload)
    {
        var utc = DateTime.UtcNow;
        AnsiConsole.MarkupLine($"[aqua]{utc:O}[/] [bold]Frame[/] -> type=[green]{type}[/] length=[grey]{payload.Length}[/]");

        if (type == WebSocketMessageType.Text)
        {
            var text = SafeUtf8(payload);
            AnsiConsole.MarkupLine($"[white]{EscapeMarkup(text)}[/]");

            var pairs = ParseKeyValuePairs(text);
            if (pairs.Count > 0)
            {
                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .AddColumn("Key")
                    .AddColumn("Value");

                foreach (var kv in pairs)
                    table.AddRow(kv.Key, kv.Value);

                AnsiConsole.Write(table);
            }
            else
            {
                AnsiConsole.MarkupLine("[grey]No key/value pairs parsed from text frame.[/]");
            }
        }
        else
        {
            var previewLen = Math.Min(payload.Length, 128);
            var hex = Convert.ToHexString(payload, 0, previewLen);
            var preview = payload.Length <= previewLen ? hex : hex + "...";
            AnsiConsole.MarkupLine($"[magenta]Binary preview (hex):[/] [grey]{EscapeMarkup(preview)}[/]");
            var base64 = Convert.ToBase64String(payload);
            AnsiConsole.MarkupLine($"[grey]Base64 (first 256 chars):[/] {EscapeMarkup(base64.Substring(0, Math.Min(256, base64.Length)))}");
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

    static void LogReceived(string method, string payload)
    {
        AnsiConsole.MarkupLine($"[aqua]{DateTime.UtcNow:O}[/] [bold]{EscapeMarkup(method)}[/] -> [grey]{EscapeMarkup(payload)}[/]");
    }

    static string EscapeMarkup(string? s) => s is null ? string.Empty : s.Replace("[", "&#91;").Replace("]", "&#93;");
}