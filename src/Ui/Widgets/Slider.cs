using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Morpheus.Ui.Widgets;

public sealed class Slider : Widget
{
    public string Label { get; set; } = "";
    public float Value { get; set; }
    public float Min { get; set; } = 0f;
    public float Max { get; set; } = 1f;
    public event Action<float>? Changed;

    private bool _dragging;

    public override void Update(WidgetInput input)
    {
        var track = TrackRect();
        Hovered = track.Contains(input.MouseP);

        if (!Enabled) { _dragging = false; return; }
        if (Hovered && input.Click) _dragging = true;
        if (input.Release) _dragging = false;
        if (_dragging) SetValueFromMouse(input.Mouse.X, track);
    }

    private Rectangle TrackRect()
        => new(Bounds.X, Bounds.Y + Bounds.Height - 12, Bounds.Width, 8);

    private void SetValueFromMouse(int mx, Rectangle track)
    {
        float t = MathHelper.Clamp((mx - track.X) / (float)track.Width, 0f, 1f);
        var newVal = MathHelper.Lerp(Min, Max, t);
        if (System.Math.Abs(newVal - Value) < 1e-4) return;
        Value = newVal;
        Changed?.Invoke(Value);
    }

    public override void Draw(SpriteBatch batch, TextRenderer text, Texture2D pixel)
    {
        var labelPos = new Vector2(Bounds.X, Bounds.Y);
        text.DrawString(batch, $"{Label}: {Value:0.00}", labelPos, new Color(200, 220, 230), 13);

        var track = TrackRect();
        DrawFill(batch, pixel, track, new Color(30, 40, 55));
        DrawBorder(batch, pixel, track, new Color(AccentColor.R / 2, AccentColor.G / 2, AccentColor.B / 2), 1);

        float t = (Value - Min) / (Max - Min);
        t = MathHelper.Clamp(t, 0f, 1f);
        var fill = new Rectangle(track.X + 1, track.Y + 1,
            System.Math.Max(0, (int)((track.Width - 2) * t)), track.Height - 2);
        DrawFill(batch, pixel, fill, AccentColor);

        int knobX = track.X + (int)((track.Width - 10) * t);
        var knob = new Rectangle(knobX, track.Y - 3, 10, track.Height + 6);
        DrawFill(batch, pixel, knob, _dragging ? Color.White : AccentColor);
    }
}
