using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Morpheus.Ui.Widgets;

namespace Morpheus.Ui;

public sealed class CustomTtsPanel
{
    public readonly MultilineTextInput TextBox = new()
    {
        Placeholder = "Type or paste your text here…",
        MaxLength   = 4000,
    };
    public readonly TextInput RepeatInput = new() { Text = "1", MaxLength = 3 };

    private readonly Dropdown _emotionDropdown = new() { OpenUpward = true };
    private readonly Button   _playBtn         = new() { Label = "play" };
    private readonly Button   _cancelBtn       = new() { Label = "cancel" };

    private Rectangle _panel;
    private string    _repeatError = "";

    public Color AccentColor { get; set; } = new Color(0, 200, 255);

    // (text, emotion or null, repeatCount)
    public event Action<string, string?, int>? PlayClicked;
    public event Action? Cancelled;

    public CustomTtsPanel()
    {
        _playBtn.Clicked   += OnPlay;
        _cancelBtn.Clicked += () => Cancelled?.Invoke();
    }

    private void OnPlay()
    {
        var rawText = TextBox.Text.Trim();
        if (string.IsNullOrEmpty(rawText)) return;

        if (!int.TryParse(RepeatInput.Text.Trim(), out int repeat) || repeat < 1 || repeat > 100)
        {
            _repeatError = "(1-100)";
            return;
        }
        _repeatError = "";

        string? emotion = (_emotionDropdown.SelectedIndex > 0 && _emotionDropdown.Options.Count > 0)
            ? _emotionDropdown.Options[_emotionDropdown.SelectedIndex].Id
            : null;

        PlayClicked?.Invoke(rawText, emotion, repeat);
    }

    public void SetEmotions(IReadOnlyList<string> emotions)
    {
        _emotionDropdown.Options.Clear();
        _emotionDropdown.Options.Add(("", "<none>"));
        foreach (var e in emotions) _emotionDropdown.Options.Add((e, e));
        _emotionDropdown.SelectedIndex = 0;
    }

    public void Layout(int cx, int cy)
    {
        const int w = 480, h = 320;
        _panel = new Rectangle(cx - w / 2, cy - h / 2, w, h);
        int ix = _panel.X + 12;
        int iw = w - 24;

        TextBox.Bounds       = new Rectangle(ix, _panel.Y + 30, iw, 150);
        RepeatInput.Bounds   = new Rectangle(ix + 58, _panel.Y + 194, 50, 22);
        _emotionDropdown.Bounds = new Rectangle(ix + 230, _panel.Y + 192, iw - 230, 24);

        int btnY = _panel.Bottom - 36;
        int btnW = (iw - 8) / 2;
        _playBtn.Bounds   = new Rectangle(ix,          btnY, btnW, 26);
        _cancelBtn.Bounds = new Rectangle(ix + btnW + 8, btnY, btnW, 26);

        ApplyAccent();
    }

    private void ApplyAccent()
    {
        foreach (Widget w in new Widget[] { TextBox, RepeatInput, _playBtn, _cancelBtn, _emotionDropdown })
            w.AccentColor = AccentColor;
    }

    public void Update(WidgetInput input)
    {
        TextBox.Update(input);
        RepeatInput.Update(input);
        _emotionDropdown.Update(input);
        if (!_emotionDropdown.Open)
        {
            _playBtn.Update(input);
            _cancelBtn.Update(input);
        }
    }

    public TextInput?          FocusedTextInput => RepeatInput.Focused ? RepeatInput : null;
    public MultilineTextInput? FocusedMultiline  => TextBox.Focused   ? TextBox     : null;
    public bool ContainsPoint(Point p) => _panel.Contains(p);

    public void Draw(SpriteBatch batch, TextRenderer text, Texture2D pixel)
    {
        var r = _panel;
        batch.Draw(pixel, r, new Color(0, 10, 20, 220));
        batch.Draw(pixel, new Rectangle(r.X, r.Y,          r.Width, 1), AccentColor);
        batch.Draw(pixel, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), AccentColor);
        batch.Draw(pixel, new Rectangle(r.X, r.Y, 1,          r.Height), AccentColor);
        batch.Draw(pixel, new Rectangle(r.Right - 1, r.Y, 1, r.Height), AccentColor);

        text.DrawString(batch, "Enter your text:",
            new Vector2(r.X + 12, r.Y + 10), AccentColor, 13);
        TextBox.Draw(batch, text, pixel);

        var dim = new Color(160, 200, 220);
        text.DrawString(batch, "repeat:", new Vector2(r.X + 12, _panel.Y + 198), dim, 12);
        RepeatInput.Draw(batch, text, pixel);
        if (!string.IsNullOrEmpty(_repeatError))
            text.DrawString(batch, _repeatError,
                new Vector2(RepeatInput.Bounds.Right + 4, RepeatInput.Bounds.Y + 4),
                new Color(230, 60, 60), 12);

        text.DrawString(batch, "emotion:", new Vector2(r.X + 168, _panel.Y + 198), dim, 12);
        _emotionDropdown.Draw(batch, text, pixel);

        _playBtn.Draw(batch, text, pixel);
        _cancelBtn.Draw(batch, text, pixel);
    }
}
