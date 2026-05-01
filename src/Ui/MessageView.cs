using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Morpheus.Sessions;

namespace Morpheus.Ui;

// Paged reader for session pages (one page = one assistant turn).
// Inside the message box: scrollable text + right-edge scrollbar.
// Outside (sides of the box): prev/next nav buttons from template assets.
// Top bar: replay (left) and stop (right) overlay buttons.
public sealed class MessageView
{
    public List<SessionPage> Pages { get; set; } = new();
    public int PageIndex { get; private set; }
    public event Action<int>? PageChanged;
    public event Action? ReplayClicked;
    public event Action? SummaryClicked;
    public event Action? StopClicked;

    public Texture2D? ForwardTex { get; set; }
    public Texture2D? BackwardTex { get; set; }

    // Overlay button sizes — set before calling Layout().
    public int ReplayBtnWidth   { get; set; } = 52;
    public int SummaryBtnWidth  { get; set; } = 66;
    public int StopBtnWidth     { get; set; } = 52;
    public int OverlayBtnHeight { get; set; } = 20;

    private float _scroll;
    private bool _dragging;
    private int _prevWheel;
    private bool _wheelInitialized;
    private bool _userScrolled;
    private int _prevTotalLines;

    private Rectangle _box;
    private Insets? _insets;
    private Rectangle _textArea;
    private Rectangle _scrollbarTrack;
    private Rectangle _scrollbarThumb;
    private Rectangle _backBtn;
    private Rectangle _forwardBtn;
    private Rectangle _pageLabelRect;
    private Rectangle _replayBtn;
    private Rectangle _summaryBtn;
    private Rectangle _stopBtn;
    private int _textSize = 16;
    private int _lineHeight = 20;
    private int _totalLines;
    private int _visibleLines;

    // Per-page summaries keyed by SessionPage.Uuid; persist until overwritten by a new summary.
    private readonly System.Collections.Generic.Dictionary<string, string> _summaries = new();

    public void SetSummaryForCurrentPage(string summary)
    {
        var uuid = (PageIndex >= 0 && PageIndex < Pages.Count) ? Pages[PageIndex].Uuid : null;
        if (uuid is not null) _summaries[uuid] = summary;
    }

    private string? CurrentSummary
    {
        get
        {
            var uuid = (PageIndex >= 0 && PageIndex < Pages.Count) ? Pages[PageIndex].Uuid : null;
            return uuid is not null && _summaries.TryGetValue(uuid, out var s) ? s : null;
        }
    }

    public Color AccentColor { get; set; } = new Color(0, 200, 255);

    public string CurrentText =>
        (PageIndex >= 0 && PageIndex < Pages.Count) ? Pages[PageIndex].Text : "";

    public bool IsOnLastPage => Pages.Count == 0 || PageIndex == Pages.Count - 1;

    public void SetPages(IReadOnlyList<SessionPage> pages, int? preferIndex = null)
    {
        Pages = new List<SessionPage>(pages);
        if (Pages.Count == 0) { PageIndex = 0; _scroll = 0; return; }
        var idx = preferIndex ?? Pages.Count - 1;
        PageIndex = Math.Clamp(idx, 0, Pages.Count - 1);
        _scroll = 0;
        PageChanged?.Invoke(PageIndex);
    }

    public void GoTo(int index)
    {
        if (Pages.Count == 0) { PageIndex = 0; _scroll = 0; return; }
        var clamped = Math.Clamp(index, 0, Pages.Count - 1);
        if (clamped == PageIndex) return;
        PageIndex = clamped;
        _scroll = 0;
        _userScrolled = false;
        PageChanged?.Invoke(PageIndex);
    }

    // Call when a new assistant turn begins — lets auto-scroll take over until the user touches scroll.
    public void ResetAutoScroll()
    {
        _userScrolled = false;
        _prevTotalLines = 0;
    }

    // Call when live text grows — scrolls to bottom if user hasn't manually scrolled.
    public void NotifyTextUpdated()
    {
        if (!_userScrolled)
            _scroll = 1f;
    }

    public void Layout(Rectangle box, Insets? insets, int textSize, int lineHeight,
                       int btnSize, int btnSideGap)
    {
        _box = box;
        _insets = insets;
        _textSize = textSize;
        _lineHeight = lineHeight;

        var interior = InsetRect(box, insets);
        const int scrollbarW = 10;
        int overlayH = OverlayBtnHeight + 4; // strip height including gap

        // Overlay buttons sit at the top of the interior
        _replayBtn  = new Rectangle(interior.X + 2,                                        interior.Y + 2, ReplayBtnWidth,  OverlayBtnHeight);
        _summaryBtn = new Rectangle(interior.X + 2 + ReplayBtnWidth + 4,                   interior.Y + 2, SummaryBtnWidth, OverlayBtnHeight);
        _stopBtn    = new Rectangle(interior.Right - scrollbarW - StopBtnWidth - 4,         interior.Y + 2, StopBtnWidth,    OverlayBtnHeight);

        // Text area and scrollbar start below the overlay strip
        _scrollbarTrack = new Rectangle(interior.Right - scrollbarW, interior.Y + overlayH, scrollbarW, interior.Height - overlayH);
        _textArea = new Rectangle(interior.X, interior.Y + overlayH, interior.Width - scrollbarW - 6, interior.Height - overlayH);

        int by = box.Y + (box.Height - btnSize) / 2;
        _backBtn    = new Rectangle(box.X - btnSize - btnSideGap, by, btnSize, btnSize);
        _forwardBtn = new Rectangle(box.Right + btnSideGap,       by, btnSize, btnSize);

        _pageLabelRect = new Rectangle(box.X, box.Y - 18, box.Width, 14);
    }

    public void Update(MouseState m, MouseState prev)
    {
        if (!_wheelInitialized) { _prevWheel = m.ScrollWheelValue; _wheelInitialized = true; }
        int wheelDelta = m.ScrollWheelValue - _prevWheel;
        _prevWheel = m.ScrollWheelValue;
        if (wheelDelta != 0 && _box.Contains(m.X, m.Y))
        {
            _userScrolled = true;
            ApplyScrollLines(-wheelDelta / 120f * 3f);
        }

        bool clickNow = m.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released;
        bool releaseNow = m.LeftButton == ButtonState.Released && prev.LeftButton == ButtonState.Pressed;
        bool down = m.LeftButton == ButtonState.Pressed;
        var p = new Point(m.X, m.Y);

        if (clickNow)
        {
            if (_replayBtn.Contains(p))  { ReplayClicked?.Invoke();   return; }
            if (_summaryBtn.Contains(p)) { SummaryClicked?.Invoke(); return; }
            if (_stopBtn.Contains(p))    { StopClicked?.Invoke();    return; }
            if (_backBtn.Contains(p))    { GoTo(PageIndex - 1); return; }
            if (_forwardBtn.Contains(p)) { GoTo(PageIndex + 1); return; }
            if (_scrollbarThumb.Contains(p)) { _dragging = true; _userScrolled = true; }
            else if (_scrollbarTrack.Contains(p))
            { SetScrollFromY(m.Y); _userScrolled = true; }
        }
        if (releaseNow) _dragging = false;
        if (down && _dragging) { SetScrollFromY(m.Y); _userScrolled = true; }
    }

    private void SetScrollFromY(int mouseY)
    {
        if (_scrollbarTrack.Height <= 0) return;
        float ratio = (mouseY - _scrollbarTrack.Y) / (float)_scrollbarTrack.Height;
        _scroll = Math.Clamp(ratio, 0f, 1f);
    }

    private void ApplyScrollLines(float lines)
    {
        if (_totalLines <= _visibleLines) { _scroll = 0; return; }
        int overflow = _totalLines - _visibleLines;
        float step = 1f / Math.Max(1, overflow);
        _scroll = Math.Clamp(_scroll + lines * step, 0f, 1f);
    }

    public void Draw(SpriteBatch batch, TextRenderer text, Texture2D pixel)
    {
        // ── Summary header (fixed, non-scrolling) ──────────────────────────
        int contentY = _textArea.Y;
        if (!string.IsNullOrEmpty(CurrentSummary))
        {
            var summaryColor = new Color((int)AccentColor.R, (int)AccentColor.G, (int)AccentColor.B, 220);
            var summaryLines = WrapLines(text, "Summary: " + CurrentSummary, _textArea.Width, _textSize);
            foreach (var sl in summaryLines)
            {
                if (contentY + _lineHeight > _textArea.Bottom) break;
                text.DrawString(batch, sl, new Vector2(_textArea.X, contentY), summaryColor, _textSize);
                contentY += _lineHeight;
            }
            int sepY = contentY + 2;
            if (sepY < _textArea.Bottom)
            {
                batch.Draw(pixel, new Rectangle(_textArea.X, sepY, _textArea.Width, 1),
                    new Color((int)AccentColor.R / 2, (int)AccentColor.G / 2, (int)AccentColor.B / 2, 160));
                contentY = sepY + 6;
            }
        }

        // ── Page content (scrollable, below any summary header) ───────────
        int contentHeight = Math.Max(0, _textArea.Bottom - contentY);
        var lines = WrapLines(text, CurrentText, _textArea.Width, _textSize);
        _totalLines   = lines.Count;
        _visibleLines = Math.Max(1, contentHeight / _lineHeight);

        // Auto-scroll when new lines arrive and user hasn't manually scrolled
        if (!_userScrolled && _totalLines > _prevTotalLines && _totalLines > _visibleLines)
            _scroll = 1f;
        _prevTotalLines = _totalLines;

        int startLine = 0;
        if (_totalLines > _visibleLines)
        {
            int overflow = _totalLines - _visibleLines;
            startLine = Math.Clamp((int)Math.Round(overflow * _scroll), 0, overflow);
        }
        for (int i = 0; i < _visibleLines && startLine + i < lines.Count; i++)
        {
            text.DrawString(batch, lines[startLine + i],
                new Vector2(_textArea.X, contentY + i * _lineHeight),
                Color.White, _textSize);
        }

        batch.Draw(pixel, _scrollbarTrack, new Color(20, 30, 40, 180));
        if (_totalLines > _visibleLines)
        {
            float ratio = _visibleLines / (float)_totalLines;
            int thumbH = Math.Max(16, (int)(_scrollbarTrack.Height * ratio));
            int thumbY = _scrollbarTrack.Y + (int)((_scrollbarTrack.Height - thumbH) * _scroll);
            _scrollbarThumb = new Rectangle(_scrollbarTrack.X, thumbY, _scrollbarTrack.Width, thumbH);
        }
        else
        {
            _scrollbarThumb = _scrollbarTrack;
        }
        batch.Draw(pixel, _scrollbarThumb, new Color((int)AccentColor.R, (int)AccentColor.G, (int)AccentColor.B, 200));

        if (Pages.Count > 0)
        {
            var label = $"{PageIndex + 1} / {Pages.Count}";
            var size = text.Measure(label, 12);
            var pos = new Vector2(
                _pageLabelRect.X + (_pageLabelRect.Width - size.X) / 2f,
                _pageLabelRect.Y);
            text.DrawString(batch, label, pos, new Color(160, 200, 220), 12);
        }

        DrawNav(batch, pixel, _backBtn,    BackwardTex, PageIndex > 0,                AccentColor);
        DrawNav(batch, pixel, _forwardBtn, ForwardTex,  PageIndex < Pages.Count - 1, AccentColor);

        DrawOverlayBtn(batch, text, pixel, _replayBtn,  "replay",   AccentColor);
        DrawOverlayBtn(batch, text, pixel, _summaryBtn, "summary", AccentColor);
        DrawOverlayBtn(batch, text, pixel, _stopBtn,    "stop",    AccentColor);
    }

    private static void DrawOverlayBtn(SpriteBatch batch, TextRenderer text, Texture2D pixel,
        Rectangle r, string label, Color accent)
    {
        batch.Draw(pixel, r, new Color(10, 20, 30, 210));
        batch.Draw(pixel, new Rectangle(r.X, r.Y,          r.Width, 1), accent);
        batch.Draw(pixel, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), accent);
        batch.Draw(pixel, new Rectangle(r.X, r.Y, 1,          r.Height), accent);
        batch.Draw(pixel, new Rectangle(r.Right - 1, r.Y, 1, r.Height), accent);
        var sz = text.Measure(label, 12);
        text.DrawString(batch, label,
            new Vector2(r.X + (r.Width - sz.X) / 2f, r.Y + (r.Height - sz.Y) / 2f - 1),
            Color.White, 12);
    }

    private static void DrawNav(SpriteBatch batch, Texture2D pixel, Rectangle r, Texture2D? tex, bool enabled, Color accent)
    {
        var tint = enabled ? Color.White : new Color(120, 120, 120, 180);
        if (tex is not null)
        {
            batch.Draw(tex, r, tint);
            return;
        }
        batch.Draw(pixel, r, enabled ? new Color(20, 40, 60) : new Color(20, 20, 20));
        var border = enabled ? accent : new Color(80, 80, 80);
        batch.Draw(pixel, new Rectangle(r.X, r.Y, r.Width, 1), border);
        batch.Draw(pixel, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), border);
        batch.Draw(pixel, new Rectangle(r.X, r.Y, 1, r.Height), border);
        batch.Draw(pixel, new Rectangle(r.Right - 1, r.Y, 1, r.Height), border);
    }

    private static Rectangle InsetRect(Rectangle r, Insets? i)
    {
        if (i is null) return r;
        return new Rectangle(
            r.X + i.Left,
            r.Y + i.Top,
            Math.Max(0, r.Width - i.Left - i.Right),
            Math.Max(0, r.Height - i.Top - i.Bottom));
    }

    private static List<string> WrapLines(TextRenderer text, string content, int pixelWidth, int size)
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
                else
                {
                    line.Clear().Append(candidate);
                }
            }
            if (line.Length > 0) result.Add(line.ToString());
        }
        return result;
    }
}
