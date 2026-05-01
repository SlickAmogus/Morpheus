using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Morpheus.Ui.Widgets;

public sealed class Toggle : Widget
{
    public string Label { get; set; } = "";
    public bool Value { get; set; }
    public event Action<bool>? Changed;

    public override void Update(WidgetInput input)
    {
        Hovered = HitTest(input.MouseP);
        if (!Enabled) return;
        if (Hovered && input.Click)
        {
            Value = !Value;
            Changed?.Invoke(Value);
        }
    }

    public override void Draw(SpriteBatch batch, TextRenderer text, Texture2D pixel)
    {
        var box = new Rectangle(Bounds.X, Bounds.Y + (Bounds.Height - 16) / 2, 16, 16);
        DrawFill(batch, pixel, box, new Color(20, 30, 45));
        DrawBorder(batch, pixel, box, AccentColor, 1);
        if (Value)
        {
            var inner = new Rectangle(box.X + 3, box.Y + 3, box.Width - 6, box.Height - 6);
            DrawFill(batch, pixel, inner, AccentColor);
        }
        text.DrawString(batch, Label, new Vector2(box.Right + 8, Bounds.Y + (Bounds.Height - 14) / 2f - 2),
            new Color(220, 230, 240), 13);
    }
}
