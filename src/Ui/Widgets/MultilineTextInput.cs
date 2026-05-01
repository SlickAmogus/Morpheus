using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Morpheus.Ui.Widgets;

// Multi-line text area. Cursor is always at end; no click-to-position.
public sealed class MultilineTextInput : Widget
{
    private string _text = "";
    private float _scroll;
    private int _prevWheel;
    private bool _wheelInit;

    public string Text
    {
        get => _text;
        set { _text = value ?? ""; _scroll = 1f; }
    }
    public string Placeholder { get; set; } = "";
    public bool Focused { get; private set; }
    public int MaxLength { get; set; } = 5000;

    public override void Update(WidgetInput input)
    {
        Hovered = HitTest(input.MouseP);
        if (input.Click) Focused = HitTest(input.MouseP);

        if (!_wheelInit) { _prevWheel = input.Mouse.ScrollWheelValue; _wheelInit = true; }
        if (Focused && Bounds.Contains(input.MouseP))
        {
            int delta = input.Mouse.ScrollWheelValue - _prevWheel;
            if (delta != 0) _scroll = Math.Clamp(_scroll - delta / 1200f, 0f, 1f);
        }
        _prevWheel = input.Mouse.ScrollWheelValue;
    }

    public void HandleChar(char c)
    {
        if (!Focused || c < ' ' || c == 127) return;
        if (_text.Length >= MaxLength) return;
        _text += c;
        _scroll = 1f;
    }

    public void HandlePaste(string pastedText)
    {
        if (!Focused) return;
        foreach (var c in pastedText)
        {
            if ((c < ' ' && c != '\n' && c != '\r') || c == 127) continue;
            if (_text.Length >= MaxLength) break;
            if (c == '\r') continue;
            _text += c;
        }
        _scroll = 1f;
    }

    public void HandleKey(Keys key)
    {
        if (!Focused) return;
        switch (key)
        {
            case Keys.Back:
                if (_text.Length > 0) { _text = _text[..^1]; _scroll = 1f; }
                break;
            case Keys.Enter:
                if (_text.Length < MaxLength) { _text += '\n'; _scroll = 1f; }
                break;
            case Keys.Escape:
                Focused = false;
                break;
        }
    }

    public override void Draw(SpriteBatch batch, TextRenderer text, Texture2D pixel)
    {
        var fill   = Focused ? new Color(40, 60, 80)  : new Color(20, 30, 40);
        var border = Focused ? Color.White             : AccentColor;
        DrawFill(batch, pixel, Bounds, fill);
        DrawBorder(batch, pixel, Bounds, border, 1);

        const int pad   = 6;
        const int size  = 13;
        const int lineH = 18;
        const int sbW   = 8;
        int textW    = Math.Max(10, Bounds.Width - pad * 2 - sbW - 4);
        int visLines = Math.Max(1, (Bounds.Height - pad * 2) / lineH);

        var lines = WrapAll(text, _text, textW, size);

        int overflow  = Math.Max(0, lines.Count - visLines);
        int startLine = overflow > 0
            ? Math.Clamp((int)Math.Round(overflow * _scroll), 0, overflow)
            : 0;

        if (lines.Count == 0 && string.IsNullOrEmpty(_text))
        {
            text.DrawString(batch, Placeholder,
                new Vector2(Bounds.X + pad, Bounds.Y + pad),
                new Color(120, 140, 150), size);
        }
        else
        {
            for (int i = 0; i < visLines && startLine + i < lines.Count; i++)
                text.DrawString(batch, lines[startLine + i],
                    new Vector2(Bounds.X + pad, Bounds.Y + pad + i * lineH),
                    Color.White, size);
        }

        // Cursor at end
        if (Focused && lines.Count > 0)
        {
            int lastIdx = lines.Count - 1;
            int displayRow = lastIdx - startLine;
            if (displayRow >= 0 && displayRow < visLines)
            {
                var lastLine = lines[^1];
                int cx = Bounds.X + pad + (int)text.Measure(lastLine, size).X;
                int cy = Bounds.Y + pad + displayRow * lineH;
                bool blink = (int)(Environment.TickCount / 500) % 2 == 0;
                if (blink) batch.Draw(pixel, new Rectangle(cx + 1, cy + 2, 1, lineH - 4), Color.White);
            }
        }

        // Scrollbar
        var sbTrack = new Rectangle(Bounds.Right - sbW - 2, Bounds.Y + 2, sbW, Bounds.Height - 4);
        batch.Draw(pixel, sbTrack, new Color(20, 30, 40, 180));
        if (overflow > 0)
        {
            float ratio  = visLines / (float)Math.Max(1, lines.Count);
            int thumbH   = Math.Max(12, (int)(sbTrack.Height * ratio));
            int thumbY   = sbTrack.Y + (int)((sbTrack.Height - thumbH) * _scroll);
            batch.Draw(pixel,
                new Rectangle(sbTrack.X, thumbY, sbTrack.Width, thumbH),
                new Color((int)AccentColor.R, (int)AccentColor.G, (int)AccentColor.B, 200));
        }
    }

    private static List<string> WrapAll(TextRenderer text, string content, int pixelWidth, int size)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(content)) return result;
        foreach (var paragraph in content.Split('\n'))
        {
            if (paragraph.Length == 0) { result.Add(""); continue; }
            var words = paragraph.Split(' ');
            var line = new StringBuilder();
            foreach (var w in words)
            {
                var candidate = line.Length == 0 ? w : line + " " + w;
                if (text.Measure(candidate, size).X > pixelWidth && line.Length > 0)
                {
                    result.Add(line.ToString());
                    line.Clear().Append(w);
                }
                else line.Clear().Append(candidate);
            }
            if (line.Length > 0) result.Add(line.ToString());
        }
        return result;
    }
}
