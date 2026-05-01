using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Morpheus.Ui.Widgets;

namespace Morpheus.Ui;

public sealed class CustomPromptPanel
{
    public readonly MultilineTextInput TextBox = new()
    {
        Placeholder = "What would you like to ask?",
        MaxLength   = 2000,
    };

    private readonly Button _confirmBtn = new() { Label = "confirm" };
    private readonly Button _cancelBtn  = new() { Label = "cancel" };

    private Rectangle _panel;
    private Rectangle _checkboxRect;
    private bool      _useClaudeContext;
    private string    _avatarName = "Avatar";

    public bool  UseClaudeContext => _useClaudeContext;
    public Color AccentColor { get; set; } = new Color(0, 200, 255);

    // (prompt text, useClaudeContext)
    public event Action<string, bool>? Confirmed;
    public event Action? Cancelled;

    public CustomPromptPanel()
    {
        _confirmBtn.Clicked += OnConfirm;
        _cancelBtn.Clicked  += () => Cancelled?.Invoke();
    }

    private void OnConfirm()
    {
        var t = TextBox.Text.Trim();
        if (string.IsNullOrEmpty(t)) return;
        Confirmed?.Invoke(t, _useClaudeContext);
    }

    public void SetAvatarName(string name) => _avatarName = name;

    public void Layout(int cx, int cy)
    {
        const int w = 480, h = 300;
        _panel = new Rectangle(cx - w / 2, cy - h / 2, w, h);
        int ix = _panel.X + 12;
        int iw = w - 24;

        TextBox.Bounds = new Rectangle(ix, _panel.Y + 30, iw, 165);

        _checkboxRect = new Rectangle(ix, _panel.Y + 208, 14, 14);

        int btnY = _panel.Bottom - 36;
        int btnW = (iw - 8) / 2;
        _confirmBtn.Bounds = new Rectangle(ix,           btnY, btnW, 26);
        _cancelBtn.Bounds  = new Rectangle(ix + btnW + 8, btnY, btnW, 26);

        foreach (Widget wid in new Widget[] { TextBox, _confirmBtn, _cancelBtn })
            wid.AccentColor = AccentColor;
    }

    public void Update(WidgetInput input)
    {
        TextBox.Update(input);
        _confirmBtn.Update(input);
        _cancelBtn.Update(input);

        if (input.Click && _checkboxRect.Contains(input.MouseP))
            _useClaudeContext = !_useClaudeContext;
    }

    public MultilineTextInput? FocusedMultiline => TextBox.Focused ? TextBox : null;
    public bool ContainsPoint(Point p) => _panel.Contains(p);

    public void Draw(SpriteBatch batch, TextRenderer text, Texture2D pixel)
    {
        var r = _panel;
        batch.Draw(pixel, r, new Color(0, 10, 20, 220));
        batch.Draw(pixel, new Rectangle(r.X, r.Y,          r.Width, 1), AccentColor);
        batch.Draw(pixel, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), AccentColor);
        batch.Draw(pixel, new Rectangle(r.X, r.Y, 1,          r.Height), AccentColor);
        batch.Draw(pixel, new Rectangle(r.Right - 1, r.Y, 1, r.Height), AccentColor);

        text.DrawString(batch, $"Ask {_avatarName} anything:",
            new Vector2(r.X + 12, r.Y + 10), AccentColor, 13);
        TextBox.Draw(batch, text, pixel);

        // Checkbox
        var cb = _checkboxRect;
        batch.Draw(pixel, cb, new Color(20, 40, 60));
        batch.Draw(pixel, new Rectangle(cb.X, cb.Y,          cb.Width, 1), AccentColor);
        batch.Draw(pixel, new Rectangle(cb.X, cb.Bottom - 1, cb.Width, 1), AccentColor);
        batch.Draw(pixel, new Rectangle(cb.X, cb.Y, 1,          cb.Height), AccentColor);
        batch.Draw(pixel, new Rectangle(cb.Right - 1, cb.Y, 1, cb.Height), AccentColor);
        if (_useClaudeContext)
            batch.Draw(pixel,
                new Rectangle(cb.X + 3, cb.Y + 3, cb.Width - 6, cb.Height - 6),
                AccentColor);

        text.DrawString(batch, "use claude context",
            new Vector2(cb.Right + 6, cb.Y - 1), new Color(160, 200, 220), 13);

        _confirmBtn.Draw(batch, text, pixel);
        _cancelBtn.Draw(batch, text, pixel);
    }
}
