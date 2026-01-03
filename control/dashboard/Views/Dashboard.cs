using System;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Terminal.Gui.Drawing;
using dashboard.Services;

namespace dashboard.Views;

public sealed class Dashboard : Window
{
    public MenuBar MenuBarV2 { get; private set; }
    public LogView LogViewer { get; private set; }

    private readonly DataSimulationService _dataSimulation;
    private readonly SignalRListenerService _signalR;

    public Dashboard()
    {
        Title = "Dashboard";

        InitializeMenu();
        InitializeLogView();

        // Start data simulation (optional: comment this out once SignalR is verified)
        _dataSimulation = new DataSimulationService(message => LogViewer.AddMessage(message));
        _dataSimulation.Start();

        // TODO: replace with your actual hub URL
        var hubUrl = @"http://192.168.1.88/ws";

        _signalR = new SignalRListenerService(hubUrl, message => LogViewer.AddMessage(message));

        // Fire-and-forget; TUI has no async ctor
        _ = _signalR.StartAsync();
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
            _dataSimulation?.Dispose();

            if (_signalR is not null)
            {
                // Best-effort async dispose
                _ = _signalR.DisposeAsync();
            }
        }

        base.Dispose(disposing);
    }
}