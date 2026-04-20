using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Morpheus.Ui.Widgets;

public sealed class Dropdown : Widget
{
    public string Label { get; set; } = "";
    public List<(string Id, string Display)> Options { get; set; } = new();
    public int SelectedIndex { get; set; } = -1;
    public bool Open { get; private set; }
    public event Action<string>? Changed; // fires with selected Id

    public string? SelectedId => (SelectedIndex >= 0 && SelectedIndex < Options.Count)
        ? Options[SelectedIndex].Id : null;

    public string SelectedDisplay =>
        (SelectedIndex >= 0 && SelectedIndex < Options.Count) ? Options[SelectedIndex].Display : "<none>";

    public void SelectById(string? id)
    {
        if (id is null) { SelectedIndex = -1; return; }
        for (int i = 0; i < Options.Count; i++)
            if (string.Equals(Options[i].Id, id, StringComparison.Ordinal))
            { SelectedIndex = i; return; }
    }

    public override bool HitTest(Point p)
    {
        if (Bounds.Contains(p)) return true;
        if (Open && ListBounds().Contains(p)) return true;
        return false;
    }

    private Rectangle ListBounds()
    {
        int rowH = 22;
        int h = System.Math.Min(Options.Count, 8) * rowH;
        return new Rectangle(Bounds.X, Bounds.Bottom, Bounds.Width, h);
    }

    public override void Update(WidgetInput input)
    {
        Hovered = Bounds.Contains(input.MouseP);
        if (!Enabled) { Open = false; return; }

        if (input.Click)
        {
            if (Bounds.Contains(input.MouseP))
            {
                Open = !Open;
                return;
            }
            if (Open)
            {
                var list = ListBounds();
                if (list.Contains(input.MouseP))
                {
                    int row = (input.Mouse.Y - list.Y) / 22;
                    if (row >= 0 && row < Options.Count)
                    {
                        SelectedIndex = row;
                        Open = false;
                        Changed?.Invoke(Options[row].Id);
                    }
                }
                else
                {
                    Open = false;
                }
            }
        }
    }

    public override void Draw(SpriteBatch batch, TextRenderer text, Texture2D pixel)
    {
        if (!string.IsNullOrEmpty(Label))
            text.DrawString(batch, Label, new Vector2(Bounds.X, Bounds.Y - 16),
                new Color(200, 220, 230), 12);

        var fill = Hovered ? new Color(30, 70, 90) : new Color(20, 40, 60);
        DrawFill(batch, pixel, Bounds, fill);
        DrawBorder(batch, pixel, Bounds, new Color(0, 200, 255), 1);
        text.DrawString(batch, SelectedDisplay, new Vector2(Bounds.X + 8, Bounds.Y + 6), Color.White, 14);

        // caret
        int cx = Bounds.Right - 14, cy = Bounds.Y + Bounds.Height / 2 - 2;
        batch.Draw(pixel, new Rectangle(cx, cy, 8, 1), Color.White);
        batch.Draw(pixel, new Rectangle(cx + 1, cy + 1, 6, 1), Color.White);
        batch.Draw(pixel, new Rectangle(cx + 2, cy + 2, 4, 1), Color.White);
        batch.Draw(pixel, new Rectangle(cx + 3, cy + 3, 2, 1), Color.White);

        if (Open)
        {
            var list = ListBounds();
            DrawFill(batch, pixel, list, new Color(10, 20, 30));
            DrawBorder(batch, pixel, list, new Color(0, 200, 255), 1);
            int mouseY = 0;
            // highlight handled via hover row
            for (int i = 0; i < Options.Count && i < 8; i++)
            {
                var row = new Rectangle(list.X, list.Y + i * 22, list.Width, 22);
                if (row.Contains(new Point(batch.GraphicsDevice.Viewport.X, mouseY))) { } // placeholder
                text.DrawString(batch, Options[i].Display,
                    new Vector2(row.X + 8, row.Y + 4),
                    i == SelectedIndex ? new Color(0, 255, 230) : Color.White, 13);
            }
        }
    }
}
