using System;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Terminal.Gui.Drawing;
using dashboard.Models;
using dashboard.Services;

namespace dashboard.Views;

public sealed class Dashboard : Window
{
    public MenuBar MenuBarV2 { get; private set; }
    public LogView LogViewer { get; private set; }

    private readonly TelemetryState _telemetry;
    private readonly SignalRListenerService _signalR;

    // Simple telemetry display line
    private readonly Label _telemetryLabel;

    public Dashboard()
    {
        Title = "Dashboard";

        InitializeMenu();

        _telemetryLabel = new Label
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = 1,
            Text = "Telemetry: (waiting for data...)"
        };
        Add(_telemetryLabel);

        InitializeLogView();

        _telemetry = new TelemetryState();

        var hubUrl = "ws://192.168.1.88/ws";

        _signalR = new SignalRListenerService(
            hubUrl,
            message => LogViewer.AddMessage(message),
            _telemetry,
            snapshot => Application.Invoke(() => UpdateTelemetryView(snapshot)));

        _ = _signalR.StartAsync();
    }

    private void UpdateTelemetryView(TelemetrySnapshot snapshot)
    {
        // Simple formatting; adjust names as you like
        var text =
            $"Telemetry: Roll={snapshot.Roll:0.00}, Pitch={snapshot.Pitch:0.00}, Yaw={snapshot.Yaw:0.00}, C1={snapshot.Custom1:0.00}, C2={snapshot.Custom2:0.00}";

        _telemetryLabel.Text = text;
    }

    private void InitializeMenu()
    {
        MenuBarV2 = new MenuBar(
        [
            new MenuBarItem("_File",
            [
                new MenuItem("_New", "Create a new file", () => { }),
                new MenuItem("_Open", "Open a file", () => { }),
                new MenuItem("_Save", "Save the file", () => { }),
                new MenuItem("_Quit", "Quit the application", () => Application.RequestStop())
            ]),
            new MenuBarItem("_Edit",
            [
                new MenuItem("_Cut", "Cut selection", () => { }),
                new MenuItem("_Copy", "Copy selection", () => { }),
                new MenuItem("_Paste", "Paste clipboard", () => { })
            ]),
            new MenuBarItem("_Help",
            [
                new MenuItem("_About", "About this app", () =>
                {
                    MessageBox.Query(App, "Terminal.Gui v2 Dashboard Example", "OK");
                })
            ])
        ]);

        Add(MenuBarV2);
    }

    private void InitializeLogView()
    {
        LogViewer = new LogView
        {
            X = 0,
            Y = Pos.AnchorEnd(10),
            Width = Dim.Fill(),
            Height = 10,
            CanFocus = false,
            BorderStyle = LineStyle.Single,
            Title = "Log"
        };

        Add(LogViewer);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_signalR is not null)
            {
                _ = _signalR.DisposeAsync();
            }
        }

        base.Dispose(disposing);
    }
}