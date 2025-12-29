using System;
using System.Collections.Generic;
using System.Drawing;
using Terminal.Gui.ViewBase;

namespace dashboard.Views;

public class LogView : View
{
    private readonly List<string> _logMessages = new();

    public void AddMessage(string message)
    {
        _logMessages.Add($"{DateTime.Now:HH:mm:ss}: {message}");
        SetNeedsLayout();
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent()
    {
        base.OnDrawingContent();

        var bounds = Viewport;
        int width = bounds.Width;
        int height = bounds.Height;

        // Show last "height" lines
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
