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

    // Per-property views
    private readonly Label _rollValueLabel;
    private readonly Label _pitchValueLabel;
    private readonly Label _yawValueLabel;
    private readonly Label _c1ValueLabel;
    private readonly Label _c2ValueLabel;

    public Dashboard()
    {
        Title = "Dashboard";

        InitializeMenu();

        var telemetryHeight = Dim.Absolute(3);

        // We will place 5 boxes side-by-side, each taking 20% width
        int boxCount = 5;
        Dim boxWidth = Dim.Absolute(12);

        // Roll
        var rollFrame = new FrameView
        {
            Title = "Roll",
            X = 0,
            Y = 1,
            Width = boxWidth,
            Height = telemetryHeight,
            TextAlignment = Alignment.End
        };
        _rollValueLabel = new Label
        {
            Text = "0.000",
            // Fill the frame horizontally so right-align works nicely
            X = 0,
            Y = Pos.Center(),
            Width = Dim.Fill(),
            TextAlignment = Alignment.End
        };
        rollFrame.Add(_rollValueLabel);
        Add(rollFrame);

        // Pitch
        var pitchFrame = new FrameView
        {
            Title = "Pitch",
            X = Pos.Right(rollFrame),
            Y = 1,
            Width = boxWidth,
            Height = telemetryHeight
        };
        _pitchValueLabel = new Label
        {
            Text = "0.000",
            X = 0,
            Y = Pos.Center(),
            Width = Dim.Fill(),
            TextAlignment = Alignment.End
        };
        pitchFrame.Add(_pitchValueLabel);
        Add(pitchFrame);

        // Yaw
        var yawFrame = new FrameView
        {
            Title = "Yaw",
            X = Pos.Right(pitchFrame),
            Y = 1,
            Width = boxWidth,
            Height = telemetryHeight
        };
        _yawValueLabel = new Label
        {
            Text = "0.000",
            X = 0,
            Y = Pos.Center(),
            Width = Dim.Fill(),
            TextAlignment = Alignment.End
        };
        yawFrame.Add(_yawValueLabel);
        Add(yawFrame);

        // Custom1
        var c1Frame = new FrameView
        {
            Title = "C1",
            X = Pos.Right(yawFrame),
            Y = 1,
            Width = boxWidth,
            Height = telemetryHeight
        };
        _c1ValueLabel = new Label
        {
            Text = "0.000",
            X = 0,
            Y = Pos.Center(),
            Width = Dim.Fill(),
            TextAlignment = Alignment.End
        };
        c1Frame.Add(_c1ValueLabel);
        Add(c1Frame);

        // Custom2
        var c2Frame = new FrameView
        {
            Title = "C2",
            X = Pos.Right(c1Frame),
            Y = 1,
            Width = boxWidth,
            Height = telemetryHeight
        };
        _c2ValueLabel = new Label
        {
            Text = "0.000",
            X = 0,
            Y = Pos.Center(),
            Width = Dim.Fill(),
            TextAlignment = Alignment.End
        };
        c2Frame.Add(_c2ValueLabel);
        Add(c2Frame);

        // Log view in the bottom portion
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
        // Format values; null-safe with "—" fallback
        static string F(double? v) => v.HasValue ? v.Value.ToString("0.000") : "—";

        _rollValueLabel.Text = F(snapshot.Roll);
        _pitchValueLabel.Text = F(snapshot.Pitch);
        _yawValueLabel.Text = F(snapshot.Yaw);
        _c1ValueLabel.Text = F(snapshot.Custom1);
        _c2ValueLabel.Text = F(snapshot.Custom2);
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