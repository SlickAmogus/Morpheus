using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Morpheus.Ui.Widgets;

public sealed class Button : Widget
{
    public string Label { get; set; } = "";
    public event Action? Clicked;

    public override void Update(WidgetInput input)
    {
        Hovered = HitTest(input.MouseP);
        if (Enabled && Hovered && input.Click) Clicked?.Invoke();
    }

    public override void Draw(SpriteBatch batch, TextRenderer text, Texture2D pixel)
    {
        var fill = !Enabled ? new Color(40, 40, 40)
                 : Hovered ? new Color(30, 70, 90)
                 : new Color(20, 40, 60);
        var border = Enabled ? new Color(0, 200, 255) : new Color(80, 80, 80);

        DrawFill(batch, pixel, Bounds, fill);
        DrawBorder(batch, pixel, Bounds, border, 1);

        var size = text.Measure(Label, 14);
        var pos = new Vector2(Bounds.X + (Bounds.Width - size.X) / 2f,
                              Bounds.Y + (Bounds.Height - size.Y) / 2f);
        text.DrawString(batch, Label, pos, Enabled ? Color.White : Color.Gray, 14);
    }
}
