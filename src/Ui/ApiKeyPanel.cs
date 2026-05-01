using System;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Morpheus.Ui.Widgets;

namespace Morpheus.Ui;

public sealed class ApiKeyPanel
{
    // ── Section 1: ElevenLabs ─────────────────────────────────────────────
    public readonly TextInput KeyInput = new()
    {
        Placeholder = "paste your ElevenLabs API key here",
        MaxLength   = 512,
    };
    private readonly Button _connectBtn = new() { Label = "connect", Enabled = false };
    private readonly Button _saveBtn    = new() { Label = "save",    Enabled = false };

    // ── Section 2: AI provider ────────────────────────────────────────────
    // Tabs drawn manually for selected-state styling.
    private readonly Rectangle[] _tabs = new Rectangle[5];
    private static readonly string[] TabLabels = { "ollama", "gemini", "openai", "claude", "deepseek" };
    private static readonly AiProvider[] TabProviders =
        { AiProvider.Ollama, AiProvider.Gemini, AiProvider.OpenAi, AiProvider.Claude, AiProvider.DeepSeek };

    public readonly TextInput KeyInput2 = new()
    {
        Placeholder = "paste your API key here",
        MaxLength   = 512,
    };
    private readonly Button _connectBtn2 = new() { Label = "connect", Enabled = false };
    private readonly Button _saveBtn2    = new() { Label = "save",    Enabled = false };

    // ── Shared ────────────────────────────────────────────────────────────
    private readonly Button _cancelBtn = new() { Label = "cancel" };

    private enum Status { Idle, Testing, Success, Failure }
    private Status _status1 = Status.Idle;
    private string _statusMsg1 = "";
    private Status _status2 = Status.Idle;
    private string _statusMsg2 = "";

    private Rectangle _panel;
    private Rectangle _statusRect1;
    private Rectangle _statusRect2;
    private int       _dividerY;
    private int       _aiLabelY;
    private Rectangle _modelInfoRect;

    private AiProvider _provider = AiProvider.Ollama;
    public  AiProvider SelectedProvider
    {
        get => _provider;
        set { _provider = value; UpdatePlaceholder(); }
    }

    private string _ollamaModelLabel = "detecting…";
    public string OllamaModelLabel
    {
        get => _ollamaModelLabel;
        set => _ollamaModelLabel = value;
    }

    public Color AccentColor { get; set; } = new Color(0, 200, 255);

    public event Action<string>?     Saved;
    public event Action<string>?     GeminiSaved;
    public event Action<string>?     OpenAiSaved;
    public event Action<string>?     ClaudeSaved;
    public event Action<string>?     DeepSeekSaved;
    public event Action<AiProvider>? ProviderChanged;
    public event Action?             Cancelled;

    public void Layout(int cx, int cy)
    {
        const int w = 500, h = 370;
        _panel = new Rectangle(cx - w / 2, cy - h / 2, w, h);

        int ix   = _panel.X + 12;
        int btnW2 = (w - 36) / 2;

        // ── ElevenLabs ──
        int iy = _panel.Y + 28;
        KeyInput.Bounds    = new Rectangle(ix, iy, w - 24, 26);
        _statusRect1       = new Rectangle(ix, iy + 36, w - 24, 38);
        int btnY1          = _panel.Y + 106;
        _connectBtn.Bounds = new Rectangle(ix,              btnY1, btnW2, 24);
        _saveBtn.Bounds    = new Rectangle(ix + btnW2 + 12, btnY1, btnW2, 24);

        _dividerY  = _panel.Y + 142;
        _aiLabelY  = _panel.Y + 150;

        // ── 5 provider tabs in one row ──
        int tabY  = _panel.Y + 170;
        int tabW  = (w - 24 - 32) / 5;   // 4 gaps of 8px
        for (int i = 0; i < 5; i++)
            _tabs[i] = new Rectangle(ix + i * (tabW + 8), tabY, tabW, 24);

        // ── Key input (all non-Ollama providers) ──
        int iy2 = _panel.Y + 202;
        KeyInput2.Bounds    = new Rectangle(ix, iy2, w - 24, 26);
        _statusRect2        = new Rectangle(ix, iy2 + 36, w - 24, 38);
        int btnY2           = _panel.Y + 282;
        _connectBtn2.Bounds = new Rectangle(ix,              btnY2, btnW2, 24);
        _saveBtn2.Bounds    = new Rectangle(ix + btnW2 + 12, btnY2, btnW2, 24);

        _modelInfoRect = new Rectangle(ix, iy2, w - 24, 26);

        _cancelBtn.Bounds = new Rectangle(ix, _panel.Bottom - 34, w - 24, 24);

        foreach (var w2 in new Widget[] { KeyInput, _connectBtn, _saveBtn, KeyInput2, _connectBtn2, _saveBtn2, _cancelBtn })
            w2.AccentColor = AccentColor;

        UpdatePlaceholder();
    }

    public void Update(WidgetInput input)
    {
        var m    = input.Mouse;
        var prev = input.PrevMouse;
        bool click = m.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released;
        var p = new Point(m.X, m.Y);

        // ElevenLabs
        KeyInput.Update(input);
        _connectBtn.Enabled = KeyInput.Text.Length > 0 && _status1 != Status.Testing;
        _saveBtn.Enabled    = _status1 == Status.Success;
        _connectBtn.Update(input);
        _saveBtn.Update(input);

        // Provider tabs
        if (click)
            for (int i = 0; i < 5; i++)
                if (_tabs[i].Contains(p)) { SetProvider(TabProviders[i]); break; }

        // Key input (non-Ollama)
        if (_provider != AiProvider.Ollama)
        {
            KeyInput2.Update(input);
            _connectBtn2.Enabled = KeyInput2.Text.Length > 0 && _status2 != Status.Testing;
            _saveBtn2.Enabled    = _status2 == Status.Success;
            _connectBtn2.Update(input);
            _saveBtn2.Update(input);
        }

        _cancelBtn.Update(input);
    }

    private void SetProvider(AiProvider p)
    {
        if (_provider == p) return;
        _provider   = p;
        _status2    = Status.Idle;
        _statusMsg2 = "";
        UpdatePlaceholder();
        ProviderChanged?.Invoke(p);
    }

    private void UpdatePlaceholder()
    {
        KeyInput2.Placeholder = _provider switch
        {
            AiProvider.Gemini   => "paste your Gemini API key here",
            AiProvider.OpenAi   => "paste your OpenAI API key here",
            AiProvider.Claude   => "paste your Anthropic API key here",
            AiProvider.DeepSeek => "paste your DeepSeek API key here",
            _                   => "paste your API key here",
        };
    }

    public void WireButtons(
        Func<string, Task<int>>     testElevenLabsFunc,
        Func<string, Task<string?>> testGeminiFunc,
        Func<string, Task<string?>> testOpenAiFunc,
        Func<string, Task<string?>> testClaudeFunc,
        Func<string, Task<string?>> testDeepSeekFunc)
    {
        _connectBtn.Clicked += () =>
        {
            var key = KeyInput.Text.Trim();
            if (string.IsNullOrEmpty(key)) return;
            _status1    = Status.Testing;
            _statusMsg1 = "testing…";
            _ = RunTestEl(testElevenLabsFunc, key);
        };
        _saveBtn.Clicked += () => Saved?.Invoke(KeyInput.Text.Trim());

        _connectBtn2.Clicked += () =>
        {
            var key = KeyInput2.Text.Trim();
            if (string.IsNullOrEmpty(key)) return;
            _status2    = Status.Testing;
            _statusMsg2 = "testing…";
            Func<string, Task<string?>> fn = _provider switch
            {
                AiProvider.Gemini   => testGeminiFunc,
                AiProvider.OpenAi   => testOpenAiFunc,
                AiProvider.Claude   => testClaudeFunc,
                AiProvider.DeepSeek => testDeepSeekFunc,
                _                   => testGeminiFunc,
            };
            _ = RunTestAi(fn, key);
        };
        _saveBtn2.Clicked += () =>
        {
            var key = KeyInput2.Text.Trim();
            switch (_provider)
            {
                case AiProvider.Gemini:   GeminiSaved?.Invoke(key);   break;
                case AiProvider.OpenAi:   OpenAiSaved?.Invoke(key);   break;
                case AiProvider.Claude:   ClaudeSaved?.Invoke(key);   break;
                case AiProvider.DeepSeek: DeepSeekSaved?.Invoke(key); break;
            }
        };

        _cancelBtn.Clicked += () => Cancelled?.Invoke();
    }

    private async Task RunTestEl(Func<string, Task<int>> testFunc, string key)
    {
        try
        {
            int count = await testFunc(key);
            _status1    = count >= 0 ? Status.Success : Status.Failure;
            _statusMsg1 = count >= 0
                ? $"connected! {count} voice{(count != 1 ? "s" : "")} found"
                : "invalid key or connection failed";
        }
        catch (Exception ex) { _status1 = Status.Failure; _statusMsg1 = $"error: {ex.Message}"; }
    }

    private async Task RunTestAi(Func<string, Task<string?>> testFunc, string key)
    {
        try
        {
            var err     = await testFunc(key);
            _status2    = err is null ? Status.Success : Status.Failure;
            _statusMsg2 = err is null ? "connected!" : err;
        }
        catch (Exception ex) { _status2 = Status.Failure; _statusMsg2 = $"error: {ex.Message}"; }
    }

    public void Reset()
    {
        _status1 = Status.Idle; _statusMsg1 = ""; _saveBtn.Enabled  = false;
        _status2 = Status.Idle; _statusMsg2 = ""; _saveBtn2.Enabled = false;
    }

    public TextInput? GetFocusedInput()
    {
        if (KeyInput.Focused)  return KeyInput;
        if (KeyInput2.Focused) return KeyInput2;
        return null;
    }

    public bool ContainsPoint(Point p) => _panel.Contains(p);

    public void Draw(SpriteBatch batch, TextRenderer text, Texture2D pixel)
    {
        var r = _panel;

        batch.Draw(pixel, r, new Color(0, 10, 20, 220));
        batch.Draw(pixel, new Rectangle(r.X, r.Y,          r.Width, 1), AccentColor);
        batch.Draw(pixel, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), AccentColor);
        batch.Draw(pixel, new Rectangle(r.X, r.Y, 1,          r.Height), AccentColor);
        batch.Draw(pixel, new Rectangle(r.Right - 1, r.Y, 1, r.Height), AccentColor);

        // ElevenLabs section
        text.DrawString(batch, "API key  (ElevenLabs)",
            new Vector2(r.X + 12, r.Y + 6), AccentColor, 14);
        KeyInput.Draw(batch, text, pixel);
        DrawStatus(batch, text, _statusMsg1, _status1, _statusRect1);
        _connectBtn.Draw(batch, text, pixel);
        _saveBtn.Draw(batch, text, pixel);

        // Divider
        var dim = new Color(AccentColor.R / 3, AccentColor.G / 3, AccentColor.B / 3, 180);
        batch.Draw(pixel, new Rectangle(r.X + 12, _dividerY, r.Width - 24, 1), dim);

        // AI provider section
        text.DrawString(batch, "summary AI",
            new Vector2(r.X + 12, _aiLabelY + 2), AccentColor, 14);

        for (int i = 0; i < 5; i++)
            DrawTab(batch, text, pixel, _tabs[i], TabLabels[i], _provider == TabProviders[i]);

        if (_provider == AiProvider.Ollama)
        {
            text.DrawString(batch, $"model: {_ollamaModelLabel}",
                new Vector2(_modelInfoRect.X, _modelInfoRect.Y + 4),
                new Color(160, 200, 220), 13);
        }
        else
        {
            KeyInput2.Draw(batch, text, pixel);
            DrawStatus(batch, text, _statusMsg2, _status2, _statusRect2);
            _connectBtn2.Draw(batch, text, pixel);
            _saveBtn2.Draw(batch, text, pixel);
        }

        _cancelBtn.Draw(batch, text, pixel);
    }

    private void DrawTab(SpriteBatch batch, TextRenderer text, Texture2D pixel,
        Rectangle tab, string label, bool selected)
    {
        var bg     = selected ? new Color((int)AccentColor.R, (int)AccentColor.G, (int)AccentColor.B, 60) : new Color(10, 20, 30, 180);
        var border = selected ? AccentColor : new Color(80, 80, 80);
        var fg     = selected ? AccentColor : new Color(160, 160, 160);

        batch.Draw(pixel, tab, bg);
        batch.Draw(pixel, new Rectangle(tab.X, tab.Y,          tab.Width, 1), border);
        batch.Draw(pixel, new Rectangle(tab.X, tab.Bottom - 1, tab.Width, 1), border);
        batch.Draw(pixel, new Rectangle(tab.X, tab.Y, 1,          tab.Height), border);
        batch.Draw(pixel, new Rectangle(tab.Right - 1, tab.Y, 1, tab.Height), border);

        var sz = text.Measure(label, 12);
        text.DrawString(batch, label,
            new Vector2(tab.X + (tab.Width - sz.X) / 2f, tab.Y + (tab.Height - sz.Y) / 2f - 1),
            fg, 12);
    }

    private static void DrawStatus(SpriteBatch batch, TextRenderer text,
        string msg, Status status, Rectangle rect)
    {
        if (string.IsNullOrEmpty(msg)) return;
        var col = status switch
        {
            Status.Success => new Color(60, 230, 100),
            Status.Failure => new Color(230, 60, 60),
            _              => new Color(180, 180, 180),
        };
        text.DrawString(batch, msg, new Vector2(rect.X, rect.Y + 4), col, 13);
    }
}
