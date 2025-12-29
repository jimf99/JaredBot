using System;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Terminal.Gui.Drawing;
using dashboard.Services;

namespace dashboard.Views;

public sealed class Dashboard : Window
{
    public MenuBarv2 MenuBarV2 { get; private set; }
    public LogView LogViewer { get; private set; }

    private readonly DataSimulationService _dataSimulation;

    public Dashboard()
    {
        Title = "Dashboard";

        InitializeMenu();
        InitializeLogView();

        // Initialize data simulation service
        _dataSimulation = new DataSimulationService(message => LogViewer.AddMessage(message));
        _dataSimulation.Start();
    }

    private void InitializeMenu()
    {
        MenuBarV2 = new MenuBarv2(
        [
            new MenuBarItemv2("_File",
            [
                new MenuItemv2("_New", "Create a new file", () => { }),
                new MenuItemv2("_Open", "Open a file", () => { }),
                new MenuItemv2("_Save", "Save the file", () => { }),
                new MenuItemv2("_Quit", "Quit the application", () => Application.RequestStop())
            ]),
            new MenuBarItemv2("_Edit",
            [
                new MenuItemv2("_Cut", "Cut selection", () => { }),
                new MenuItemv2("_Copy", "Copy selection", () => { }),
                new MenuItemv2("_Paste", "Paste clipboard", () => { })
            ]),
            new MenuBarItemv2("_Help",
            [
                new MenuItemv2("_About", "About this app", () =>
                {
                    MessageBox.Query("About", "Terminal.Gui v2 Dashboard Example", "OK");
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
        }
        base.Dispose(disposing);
    }
}