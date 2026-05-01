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
    public bool OpenUpward { get; set; } = false;
    public event Action<string>? Changed;

    private int _scrollOffset = 0;
    private const int MaxVisible = 8;
    private const int RowH = 22;
    private const int ScrollBarW = 8;

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
        int visible = Math.Min(Options.Count, MaxVisible);
        int h = visible * RowH;
        int y = OpenUpward ? Bounds.Y - h : Bounds.Bottom;
        return new Rectangle(Bounds.X, y, Bounds.Width, h);
    }

    private bool NeedsScrollBar => Options.Count > MaxVisible;

    public override void Update(WidgetInput input)
    {
        Hovered = Bounds.Contains(input.MouseP);
        if (!Enabled) { Open = false; return; }

        if (Open)
        {
            var list = ListBounds();

            // Mouse wheel scroll
            int wheelDelta = input.Mouse.ScrollWheelValue - input.PrevMouse.ScrollWheelValue;
            if (wheelDelta != 0 && list.Contains(input.MouseP))
            {
                _scrollOffset = Math.Clamp(
                    _scrollOffset - wheelDelta / 120,
                    0, Math.Max(0, Options.Count - MaxVisible));
            }

            if (input.Click)
            {
                if (Bounds.Contains(input.MouseP))
                {
                    Open = false;
                    _scrollOffset = 0;
                    return;
                }
                if (list.Contains(input.MouseP))
                {
                    int itemX = list.X;
                    int itemW = NeedsScrollBar ? list.Width - ScrollBarW : list.Width;
                    // Only register click if not on scrollbar column
                    if (input.MouseP.X < itemX + itemW)
                    {
                        int row = (input.Mouse.Y - list.Y) / RowH + _scrollOffset;
                        if (row >= 0 && row < Options.Count)
                        {
                            SelectedIndex = row;
                            Open = false;
                            _scrollOffset = 0;
                            Changed?.Invoke(Options[row].Id);
                        }
                    }
                    return;
                }
                // Click outside — close
                Open = false;
                _scrollOffset = 0;
            }
            return;
        }

        if (input.Click && Bounds.Contains(input.MouseP))
        {
            Open = true;
            // Scroll to show selected item when opening
            if (SelectedIndex >= 0)
                _scrollOffset = Math.Clamp(SelectedIndex - MaxVisible / 2, 0,
                    Math.Max(0, Options.Count - MaxVisible));
        }
    }

    public override void Draw(SpriteBatch batch, TextRenderer text, Texture2D pixel)
    {
        if (!string.IsNullOrEmpty(Label))
            text.DrawString(batch, Label, new Vector2(Bounds.X, Bounds.Y - 16),
                new Color(200, 220, 230), 12);

        var fill = Hovered ? new Color(30, 70, 90) : new Color(20, 40, 60);
        DrawFill(batch, pixel, Bounds, fill);
        DrawBorder(batch, pixel, Bounds, AccentColor, 1);
        // Leave room for caret (14px) + small margin
        int headerTextW = Bounds.Width - 8 - 18;
        text.DrawString(batch, FitText(text, SelectedDisplay, headerTextW, 14),
            new Vector2(Bounds.X + 8, Bounds.Y + 6), Color.White, 14);

        // caret
        int cx = Bounds.Right - 14, cy = Bounds.Y + Bounds.Height / 2 - 2;
        batch.Draw(pixel, new Rectangle(cx, cy, 8, 1), Color.White);
        batch.Draw(pixel, new Rectangle(cx + 1, cy + 1, 6, 1), Color.White);
        batch.Draw(pixel, new Rectangle(cx + 2, cy + 2, 4, 1), Color.White);
        batch.Draw(pixel, new Rectangle(cx + 3, cy + 3, 2, 1), Color.White);

        if (!Open) return;

        var list = ListBounds();
        DrawFill(batch, pixel, list, new Color(10, 20, 30));
        DrawBorder(batch, pixel, list, new Color(0, 200, 255), 1);

        bool scrollBar = NeedsScrollBar;
        int itemW = scrollBar ? list.Width - ScrollBarW : list.Width;
        int visible = Math.Min(Options.Count, MaxVisible);

        for (int vi = 0; vi < visible; vi++)
        {
            int i = vi + _scrollOffset;
            if (i >= Options.Count) break;

            var row = new Rectangle(list.X, list.Y + vi * RowH, itemW, RowH);
            bool isSelected = i == SelectedIndex;
            if (isSelected)
                DrawFill(batch, pixel, row, new Color(0, 60, 80));

            int rowTextW = itemW - 6 - 4;
            text.DrawString(batch, FitText(text, Options[i].Display, rowTextW, 13),
                new Vector2(row.X + 6, row.Y + 4),
                isSelected ? new Color(0, 255, 230) : Color.White, 13);
        }

        if (scrollBar)
        {
            var track = new Rectangle(list.Right - ScrollBarW, list.Y, ScrollBarW, list.Height);
            DrawFill(batch, pixel, track, new Color(20, 30, 40));

            float thumbFraction = (float)MaxVisible / Options.Count;
            int thumbH = Math.Max(12, (int)(list.Height * thumbFraction));
            float scrollFraction = Options.Count > MaxVisible
                ? (float)_scrollOffset / (Options.Count - MaxVisible)
                : 0f;
            int thumbY = list.Y + (int)((list.Height - thumbH) * scrollFraction);
            DrawFill(batch, pixel, new Rectangle(track.X + 1, thumbY, track.Width - 2, thumbH),
                new Color(0, 160, 200));
        }
    }

    private static string FitText(TextRenderer text, string s, int maxWidth, int fontSize)
    {
        if (maxWidth <= 0 || string.IsNullOrEmpty(s)) return s;
        if (text.Measure(s, fontSize).X <= maxWidth) return s;

        // Binary-search for the longest prefix that fits with ellipsis.
        int lo = 0, hi = s.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (text.Measure(s[..mid] + "…", fontSize).X <= maxWidth)
                lo = mid;
            else
                hi = mid - 1;
        }
        return lo == 0 ? "…" : s[..lo] + "…";
    }
}
