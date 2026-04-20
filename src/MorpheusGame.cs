using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Morpheus.Audio;
using Morpheus.Avatar;
using Morpheus.Hooks;
using Morpheus.Personality;
using Morpheus.Ui;
using Morpheus.Ui.Widgets;

namespace Morpheus;

public class MorpheusGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _batch = null!;
    private TextRenderer _text = null!;
    private Texture2D _pixel = null!;

    private readonly AvatarRenderer _avatarRenderer = new();
    private readonly AvatarState _avatarState = new();
    private readonly AudioPlayer _player = new();
    private readonly HookListener _listener = new();
    private readonly ConfigUi _ui = new();
    private MorpheusSettings _settings = new();
    private string _settingsPath = "";
    private Texture2D? _avatarFrame;
    private Texture2D? _messageFrame;
    private TemplateEntry? _activeTemplate;
    private Color _bg = Color.Black;

    private readonly ConcurrentQueue<Action> _mainThread = new();
    private string _currentSubtitle = "";
    private KeyboardState _prevKeys;
    private MouseState _prevMouse;
    private string? _lastPlayedUuid;
    private readonly VoicePanel _voicePanel = new();

    public MorpheusGame()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1024,
            PreferredBackBufferHeight = 720,
            SynchronizeWithVerticalRetrace = true,
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.Title = "Morpheus";
        Window.AllowUserResizing = true;
    }

    protected override void Initialize()
    {
        _settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.local.json");
        _settings = SettingsStore.Load(_settingsPath);
        _bg = ParseColor(_settings.BackgroundColor) ?? Color.Black;

        _listener.OnStop += e => _mainThread.Enqueue(() => OnAssistantMessage(e));
        _listener.OnTool += e => _mainThread.Enqueue(() => OnTool(e));
        _listener.OnBindChanged += sid => _mainThread.Enqueue(() =>
        {
            _ui.BoundSessionId = sid;
            _ui.StatusLine = $"bound to session {Short(sid)}";
        });
        _player.Finished += () => _mainThread.Enqueue(() =>
        {
            _avatarState.MouthOpen = false;
        });

        _ = _listener.StartAsync(_settings.HookPort);

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _batch = new SpriteBatch(GraphicsDevice);
        _text = new TextRenderer(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });

        var avatarsRoot = Path.Combine(AppContext.BaseDirectory, "avatars");
        var templatesRoot = Path.Combine(AppContext.BaseDirectory, "templates");
        _ui.Avatars = AvatarLoader.Discover(avatarsRoot);
        var templates = TemplateLoader.Discover(templatesRoot);
        _ui.Templates = Map(templates, t => t.FolderName);

        if (_settings.SelectedAvatar is { } savedAv)
        {
            for (int i = 0; i < _ui.Avatars.Count; i++)
                if (string.Equals(_ui.Avatars[i].FolderName, savedAv, StringComparison.OrdinalIgnoreCase))
                { _ui.AvatarIndex = i; break; }
        }
        if (_settings.SelectedTemplate is { } savedTpl)
        {
            for (int i = 0; i < templates.Count; i++)
                if (string.Equals(templates[i].FolderName, savedTpl, StringComparison.OrdinalIgnoreCase))
                { _ui.TemplateIndex = i; break; }
        }

        ApplyAvatar();
        ApplyTemplate(templates);

        _voicePanel.Layout(15, 145, 280);
        _voicePanel.BindFromSettings(_settings);
        _voicePanel.Save.Clicked += () =>
        {
            _voicePanel.WriteToSettings(_settings);
            SaveSettings();
            _ui.StatusLine = "voice settings saved";
        };
        _voicePanel.Preview.Clicked += () =>
        {
            _voicePanel.WriteToSettings(_settings);
            SaveSettings();
            TestSpeak();
        };
        _voicePanel.Voice.Changed += id =>
        {
            _settings.ElevenLabsVoiceId = id;
            SaveSettings();
        };

        _ = LoadVoicesAsync();
    }

    private async System.Threading.Tasks.Task LoadVoicesAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.ElevenLabsApiKey)) return;
        try
        {
            var voices = await VoiceService.FetchAsync(_settings.ElevenLabsApiKey);
            _mainThread.Enqueue(() =>
            {
                _voicePanel.PopulateVoices(voices, _settings.ElevenLabsVoiceId);
                _ui.StatusLine = $"loaded {voices.Count} voices";
            });
        }
        catch (Exception ex)
        {
            _mainThread.Enqueue(() => _ui.StatusLine = $"voice load failed: {ex.Message}");
        }
    }

    protected override void Update(GameTime gameTime)
    {
        while (_mainThread.TryDequeue(out var a)) a();

        var k = Keyboard.GetState();
        if (Pressed(k, Keys.Escape)) Exit();
        if (Pressed(k, Keys.F1)) InstallHooksForCwd();
        if (Pressed(k, Keys.F2)) { _ui.CycleAvatar(+1); ApplyAvatar(); SaveSettings(); }
        if (Pressed(k, Keys.F3)) { _ui.CycleTemplate(+1); ApplyTemplate(TemplateLoader.Discover(Path.Combine(AppContext.BaseDirectory, "templates"))); SaveSettings(); }
        if (Pressed(k, Keys.F5)) TestSpeak();
        if (Pressed(k, Keys.F6)) InstallActivePersonality();
        _prevKeys = k;

        var m = Mouse.GetState();
        _voicePanel.Update(new WidgetInput { Mouse = m, PrevMouse = _prevMouse });
        _prevMouse = m;

        _avatarState.MouthOpen = _player.IsPlaying
            && _player.CurrentLevel > (_ui.SelectedAvatar?.Manifest.LipsyncThreshold ?? 0.05f);

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(_bg);
        _batch.Begin(samplerState: SamplerState.LinearClamp);

        var vp = GraphicsDevice.Viewport.Bounds;
        var tpl = _activeTemplate?.Manifest;

        var frameBox = new Rectangle(vp.Width / 2 - 140, 80, 400, 400);
        var avatarBox = Inset(frameBox, tpl?.AvatarInsets);
        _avatarRenderer.Draw(_batch, avatarBox, _avatarState);
        if (_avatarFrame is not null) _batch.Draw(_avatarFrame, frameBox, Color.White);

        var msgBox = new Rectangle(40, vp.Height - 220, vp.Width - 80, 180);
        if (_messageFrame is not null) _batch.Draw(_messageFrame, msgBox, Color.White);

        if (!string.IsNullOrEmpty(_currentSubtitle))
        {
            var textBox = Inset(msgBox, tpl?.MessageInsets);
            int textSize = tpl?.TextSize > 0 ? tpl.TextSize : 16;
            int lineHeight = tpl?.LineHeight > 0 ? tpl.LineHeight : 20;
            DrawScrollingSubtitle(_currentSubtitle, textBox, textSize, lineHeight);
        }

        _ui.Draw(_batch, _text, vp);

        var panelRect = new Rectangle(10, 140, 290, 300);
        _voicePanel.Draw(_batch, _text, _pixel, panelRect);

        _batch.End();
        base.Draw(gameTime);
    }

    private static Rectangle Inset(Rectangle r, Insets? i)
    {
        if (i is null) return r;
        return new Rectangle(
            r.X + i.Left,
            r.Y + i.Top,
            System.Math.Max(0, r.Width - i.Left - i.Right),
            System.Math.Max(0, r.Height - i.Top - i.Bottom));
    }

    private void DrawScrollingSubtitle(string text, Rectangle box, int textSize, int lineHeight)
    {
        var lines = WrapLines(text, box.Width, textSize);
        int maxLines = System.Math.Max(1, box.Height / lineHeight);

        int startLine = 0;
        if (lines.Count > maxLines)
        {
            float progress = _player.IsPlaying && _player.TotalSeconds > 0.1 ? _player.Progress : 0f;
            int overflow = lines.Count - maxLines;
            startLine = (int)System.Math.Round(overflow * progress);
            startLine = System.Math.Clamp(startLine, 0, overflow);
        }

        for (int i = 0; i < maxLines && startLine + i < lines.Count; i++)
        {
            _text.DrawString(_batch, lines[startLine + i],
                new Vector2(box.X, box.Y + i * lineHeight), Color.White, textSize);
        }
    }

    private List<string> WrapLines(string text, int pixelWidth, int size)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text)) return result;

        foreach (var paragraph in text.Split('\n'))
        {
            if (paragraph.Length == 0) { result.Add(""); continue; }
            var words = paragraph.Split(' ');
            var line = new System.Text.StringBuilder();
            foreach (var w in words)
            {
                var candidate = line.Length == 0 ? w : line + " " + w;
                if (_text.Measure(candidate, size).X > pixelWidth && line.Length > 0)
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

    protected override void UnloadContent()
    {
        _player.Dispose();
        _listener.Dispose();
        _avatarRenderer.Dispose();
        _text.Dispose();
        _avatarFrame?.Dispose();
        _messageFrame?.Dispose();
        _pixel.Dispose();
    }

    // --- actions ---

    private void OnAssistantMessage(StopHookEvent e)
    {
        if (e.MessageUuid is { Length: > 0 } uuid && uuid == _lastPlayedUuid)
        {
            if (!string.IsNullOrWhiteSpace(e.AssistantMessage))
                _ui.StatusLine = "same turn — skipped";
            return;
        }

        if (string.IsNullOrWhiteSpace(e.AssistantMessage))
        {
            _ui.StatusLine = "turn had no text (tool-only)";
            _lastPlayedUuid = e.MessageUuid;
            return;
        }

        _lastPlayedUuid = e.MessageUuid;

        const int hardCap = 4800; // ElevenLabs per-request limit is 5000
        var text = e.AssistantMessage!;
        if (text.Length > hardCap) text = text[..hardCap] + "…";

        _currentSubtitle = text;
        _avatarState.Emotion = "idle";
        _ui.StatusLine = "speaking…";

        // v1: TTS only if key configured; else skip synthesis.
        if (!string.IsNullOrWhiteSpace(_settings.ElevenLabsApiKey)
            && !string.IsNullOrWhiteSpace(_settings.ElevenLabsVoiceId))
        {
            _ = SynthesizeAndPlayAsync(text);
        }
    }

    private async System.Threading.Tasks.Task SynthesizeAndPlayAsync(string text)
    {
        try
        {
            var voiceSettings = new VoiceSettings
            {
                Stability = _settings.VoiceStability,
                SimilarityBoost = _settings.VoiceSimilarity,
                Style = _settings.VoiceStyle,
                UseSpeakerBoost = _settings.VoiceSpeakerBoost,
            };
            var tts = new ElevenLabsTts(_settings.ElevenLabsApiKey!, voiceSettings);
            var mp3 = await tts.SynthesizeAsync(text, _settings.ElevenLabsVoiceId!);
            _mainThread.Enqueue(() => _player.PlayMp3(mp3));
        }
        catch (Exception ex)
        {
            _mainThread.Enqueue(() => _ui.StatusLine = $"tts error: {ex.Message}");
        }
    }

    private void OnTool(ToolHookEvent e)
    {
        if (e.Phase == "pre") _avatarState.ActiveTool = e.ToolName;
        else _avatarState.ActiveTool = null;
        _ui.StatusLine = $"tool {e.Phase}: {e.ToolName}";
    }

    private void TestSpeak()
    {
        if (!string.IsNullOrWhiteSpace(_settings.ElevenLabsApiKey)
            && !string.IsNullOrWhiteSpace(_settings.ElevenLabsVoiceId))
        {
            const string line = "Morpheus online. Text to speech is wired.";
            _currentSubtitle = line;
            _ui.StatusLine = "synthesizing test line…";
            _ = SynthesizeAndPlayAsync(line);
            return;
        }
        var clip = Path.Combine(AppContext.BaseDirectory, "assets", "test", "test_clip.mp3");
        if (!File.Exists(clip)) { _ui.StatusLine = "no tts configured and no bundled clip"; return; }
        _player.PlayMp3(File.ReadAllBytes(clip));
        _currentSubtitle = "[test clip — bundled audio]";
        _ui.StatusLine = "playing bundled clip (no ElevenLabs key in settings.local.json)";
    }

    private void InstallHooksForCwd()
    {
        var cwd = Environment.CurrentDirectory;
        try
        {
            HookInstaller.InstallToProject(cwd, _settings.HookPort);
            _ui.StatusLine = $"hooks installed to {cwd}\\.claude\\settings.json";
        }
        catch (Exception ex)
        {
            _ui.StatusLine = $"hook install failed: {ex.Message}";
        }
    }

    private void InstallActivePersonality()
    {
        var av = _ui.SelectedAvatar;
        if (av?.Manifest.PersonalityFile is not { Length: > 0 } file)
        {
            _ui.StatusLine = "active avatar has no personality file";
            return;
        }
        var src = Path.Combine(av.FolderPath, file);
        if (!File.Exists(src))
        {
            _ui.StatusLine = $"personality file not found: {src}";
            return;
        }
        try
        {
            PersonalityInstaller.Install(src, av.FolderName);
            _ui.StatusLine = $"installed. activate via /config → Output style → morpheus-{av.FolderName}";
        }
        catch (Exception ex)
        {
            _ui.StatusLine = $"install failed: {ex.Message}";
        }
    }

    // --- helpers ---

    private void ApplyAvatar()
    {
        var av = _ui.SelectedAvatar;
        if (av is null) return;
        _avatarRenderer.LoadAvatar(av, GraphicsDevice);
        _settings.SelectedAvatar = av.FolderName;
    }

    private void ApplyTemplate(IReadOnlyList<TemplateEntry> templates)
    {
        _avatarFrame?.Dispose();
        _messageFrame?.Dispose();
        _avatarFrame = null;
        _messageFrame = null;
        _activeTemplate = null;

        if (templates.Count == 0) return;
        var tpl = templates[_ui.TemplateIndex % templates.Count];
        _activeTemplate = tpl;
        _settings.SelectedTemplate = tpl.FolderName;
        _avatarFrame = LoadTex(Path.Combine(tpl.FolderPath, tpl.Manifest.AvatarFrame ?? ""));
        _messageFrame = LoadTex(Path.Combine(tpl.FolderPath, tpl.Manifest.MessageFrame ?? ""));
        if (tpl.Manifest.BackgroundColor is { } c && ParseColor(c) is { } col) _bg = col;
    }

    private Texture2D? LoadTex(string path)
    {
        if (!File.Exists(path)) return null;
        using var fs = File.OpenRead(path);
        return Texture2D.FromStream(GraphicsDevice, fs);
    }

    private void SaveSettings() => SettingsStore.Save(_settingsPath, _settings);

    private bool Pressed(KeyboardState k, Keys key) => k.IsKeyDown(key) && _prevKeys.IsKeyUp(key);

    private static Color? ParseColor(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var v = s.Trim();
        if (v.StartsWith("#")) v = v[1..];
        if (v.Length != 6) return null;
        if (int.TryParse(v[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && int.TryParse(v[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && int.TryParse(v[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            return new Color(r, g, b);
        return null;
    }

    private static string Short(string s) => s.Length <= 8 ? s : s[..8];

    private static List<TOut> Map<TIn, TOut>(IEnumerable<TIn> src, Func<TIn, TOut> f)
    {
        var r = new List<TOut>();
        foreach (var x in src) r.Add(f(x));
        return r;
    }

}
