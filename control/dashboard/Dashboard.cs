using System;
using System.Collections.Generic;
using System.Threading;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Terminal.Gui.Drawing;
using System.Drawing; // for DrawContext, Rectangle, Size, Colors, etc.

namespace dashboard;

public sealed class Dashboard : Window
{
    public MenuBarv2 MenuBarV2 { get; set; }
    public LogView LogView { get; set; }
    private Timer? _dataSimulationTimer;
    private readonly Random _random = new();

    public Dashboard()
    {
        Title = "Dashboard";

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

        // Add LogView to the lower portion of the dashboard
        LogView = new LogView
        {
            X = 0,
            Y = Pos.AnchorEnd(10), // 10 lines from bottom, adjust as needed
            Width = Dim.Fill(),
            Height = 10, // Fixed height, adjust as needed
            CanFocus = false,
            BorderStyle = LineStyle.Single,
            Title = "Log"
        };

        Add(LogView);

        // Start simulating data
        StartDataSimulation();
    }

    private void StartDataSimulation()
    {
        ScheduleNextDataEvent();
    }

    private void ScheduleNextDataEvent()
    {
        // Random interval between 1-5 seconds
        int interval = _random.Next(1000, 5001);

        _dataSimulationTimer = new Timer(_ =>
        {
            Application.Invoke(() =>
            {
                GenerateAndLogMockData();
                ScheduleNextDataEvent();
            });
        }, null, interval, System.Threading.Timeout.Infinite);
    }

    private void GenerateAndLogMockData()
    {
        var sensors = new[] { "TEMP", "HUMID", "PRESS", "LIGHT", "SOUND" };
        var locations = new[] { "Room-A", "Room-B", "Room-C", "Outdoor" };

        string sensor = sensors[_random.Next(sensors.Length)];
        string location = locations[_random.Next(locations.Length)];
        double value = _random.NextDouble() * 100;
        string status = _random.Next(100) > 10 ? "OK" : "WARN";

        string csvData = $"{sensor},{location},{value:F2},{status}";
        this.AppendLog(csvData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dataSimulationTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}

public static class DashboardExtensions
{
    public static void AppendLog(this Dashboard dashboard, string message)
    {
        dashboard.LogView.AddMessage(message);
    }

    private static LogView? FindFirstLogView(View parent)
    {
        if (parent is LogView found)
        {
            return found;
        }

        foreach (var child in parent.SubViews)
        {
            var result = FindFirstLogView(child);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }
}

public class LogView : View
{
    private readonly List<string> _logMessages = new();

    public void AddMessage(string message)
    {
        _logMessages.Add($"{DateTime.Now:HH:mm:ss}: {message}");

        // In v2, these are the standard invalidation methods.
        // If one of them doesn't exist in your build, you can safely remove that line.
        SetNeedsLayout();
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent()
    {
        base.OnDrawingContent();

        var bounds = Viewport;
        int width = bounds.Width;
        int height = bounds.Height;

        // Decide which subset of lines to show: last "height" lines.
        int startIndex = Math.Max(0, _logMessages.Count - height);
        int line = 0;

        for (int i = startIndex; i < _logMessages.Count && line < height; i++, line++)
        {
            var text = _logMessages[i];

            if (text.Length > width)
            {
                text = text[..width];
            }

            Move(0, line);
            AddStr(text);
        }

        return true;
    }
}
