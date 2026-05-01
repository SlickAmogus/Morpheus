using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Morpheus.Ui.Widgets;

// Single-line text input. The host wires Window.TextInput → HandleChar
// for printable input + IME, and HandleKey for backspace/enter/etc.
public sealed class TextInput : Widget
{
    public string Text { get; set; } = "";
    public string Placeholder { get; set; } = "";
    public bool Focused { get; private set; }
    public int MaxLength { get; set; } = 256;
    public event Action<string>? Submitted;

    public override void Update(WidgetInput input)
    {
        Hovered = HitTest(input.MouseP);
        if (input.Click) Focused = HitTest(input.MouseP);
    }

    public void HandleChar(char c)
    {
        if (!Focused) return;
        if (c < ' ' || c == 127) return; // skip control chars; backspace is HandleKey
        if (Text.Length >= MaxLength) return;
        Text += c;
    }

    public void HandlePaste(string text)
    {
        if (!Focused) return;
        foreach (var c in text)
        {
            if (c < ' ' || c == 127) continue;
            if (Text.Length >= MaxLength) break;
            Text += c;
        }
    }

    public void HandleKey(Keys key)
    {
        if (!Focused) return;
        switch (key)
        {
            case Keys.Back:
                if (Text.Length > 0) Text = Text[..^1];
                break;
            case Keys.Enter:
                Submitted?.Invoke(Text);
                break;
            case Keys.Escape:
                Focused = false;
                break;
        }
    }

    public override void Draw(SpriteBatch batch, TextRenderer text, Texture2D pixel)
    {
        var fill = Focused ? new Color(40, 60, 80) : new Color(20, 30, 40);
        var border = Focused ? Color.White : AccentColor;
        DrawFill(batch, pixel, Bounds, fill);
        DrawBorder(batch, pixel, Bounds, border, 1);

        var shown = Text.Length > 0 ? Text : Placeholder;
        var color = Text.Length > 0 ? Color.White : new Color(120, 140, 150);
        text.DrawString(batch, shown, new Vector2(Bounds.X + 6, Bounds.Y + 5), color, 13);

        if (Focused)
        {
            var w = (int)text.Measure(Text, 13).X;
            var cx = Bounds.X + 6 + w;
            var blink = (int)(Environment.TickCount / 500) % 2 == 0;
            if (blink) batch.Draw(pixel, new Rectangle(cx + 1, Bounds.Y + 4, 1, Bounds.Height - 8), Color.White);
        }
    }
}
