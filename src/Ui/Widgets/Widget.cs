using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Morpheus.Ui.Widgets;

public abstract class Widget
{
    public Rectangle Bounds { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Hovered { get; protected set; }

    public virtual bool HitTest(Point p) => Bounds.Contains(p);

    public virtual void Update(WidgetInput input) { }
    public abstract void Draw(SpriteBatch batch, TextRenderer text, Texture2D pixel);

    protected static void DrawFill(SpriteBatch b, Texture2D px, Rectangle r, Color c)
        => b.Draw(px, r, c);

    protected static void DrawBorder(SpriteBatch b, Texture2D px, Rectangle r, Color c, int w = 1)
    {
        b.Draw(px, new Rectangle(r.X, r.Y, r.Width, w), c);
        b.Draw(px, new Rectangle(r.X, r.Bottom - w, r.Width, w), c);
        b.Draw(px, new Rectangle(r.X, r.Y, w, r.Height), c);
        b.Draw(px, new Rectangle(r.Right - w, r.Y, w, r.Height), c);
    }
}

public struct WidgetInput
{
    public MouseState Mouse;
    public MouseState PrevMouse;
    public Point MouseP => new(Mouse.X, Mouse.Y);
    public bool Click => Mouse.LeftButton == ButtonState.Pressed && PrevMouse.LeftButton == ButtonState.Released;
    public bool Release => Mouse.LeftButton == ButtonState.Released && PrevMouse.LeftButton == ButtonState.Pressed;
    public bool Down => Mouse.LeftButton == ButtonState.Pressed;
}
