using System;
using System.Collections.Generic;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Terminal.Gui.Drawing;
using System.Drawing; // for DrawContext, Rectangle, Size, Colors, etc.

namespace dashboard;

public sealed class Dashboard : Window
{
    public MenuBarv2 MenuBarV2 { get; set; }

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
    }
}

public static class DashboardExtensions
{
    public static void AppendLog(this Dashboard dashboard, string message)
    {
        var logView = FindFirstLogView(dashboard);
        if (logView == null)
        {
            logView = new LogView
            {
                X = 0,
                Y = Pos.Bottom(dashboard.MenuBarV2),
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                CanFocus = false
            };
            dashboard.Add(logView);
        }

        logView.AddMessage(message);
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

    public override void Draw(DrawContext drawContext)
    {
        // Always call base first so background, border, etc. are drawn.
        base.Draw(drawContext);

        // The area we can draw into, relative to this view.
        Rectangle bounds = drawContext.Bounds;

        int width = bounds.Size.Width;
        int height = bounds.Size.Height;

        // Decide which subset of lines to show: last "height" lines.
        int startIndex = Math.Max(0, _logMessages.Count - height);
        int line = 0;

        // Use the context to draw text. Most builds expose a "MakeContent" / "AddRune" API;
        // but Move/AddStr still work as helpers from View.
        for (int i = startIndex; i < _logMessages.Count && line < height; i++, line++)
        {
            // Position within our local bounds (x is relative to left edge of this view)
            Move(bounds.X, bounds.Y + line);

            var text = _logMessages[i];

            if (text.Length > width)
            {
                text = text[..width];
            }

            AddStr(text);
        }
    }
}
