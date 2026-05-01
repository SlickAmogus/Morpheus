using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Morpheus.Audio;
using Morpheus.Avatar;
using Morpheus.Hooks;
using Morpheus.Personality;
using Morpheus.Sessions;
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
    private readonly BackgroundRenderer _bgRenderer = new();
    private readonly AudioPlayer _player = new();
    private readonly HookListener _listener = new();
    private readonly ConfigUi _ui = new();
    private MorpheusSettings _settings = new();
    private ProjectConfig _project = new();
    private string _settingsPath = "";
    private string _projectPath = "";
    private Texture2D? _avatarFrame;
    private Texture2D? _messageFrame;
    private Texture2D? _uiForwardTex;
    private Texture2D? _uiBackwardTex;
    private TemplateEntry? _activeTemplate;
    private Color _bg = Color.Black;

    private readonly ConcurrentQueue<Action> _mainThread = new();
    private KeyboardState _prevKeys;
    private MouseState _prevMouse;
    private string? _currentTurnUuid;
    private int _spokenLen;
    private readonly Queue<SpeechTask> _speechQueue = new();
    private bool _speechActive;
    private static readonly System.Text.RegularExpressions.Regex EmotionTagRegex =
        new(@"\[emotion:\s*(\w+)\s*\]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
          | System.Text.RegularExpressions.RegexOptions.Compiled);
    private readonly VoicePanel _voicePanel = new();

    private readonly Random _rng = new();
    private double _gameSeconds;
    private double _nextIdleAt;
    private double _currentIdleEndAt;
    private IdlePick? _currentIdle;

    private readonly MessageView _messageView = new();
    private readonly Dropdown _sessionsDropdown = new();
    private readonly Button _clearSessionsBtn = new() { Label = "clear history" };
    private SessionLog _liveSession = null!;
    private SessionLog _viewSession = null!;
    private bool _isViewingLive = true;

    private readonly VoicesExtraPanel _voicesExtra = new();
    private List<VoiceInfo> _libraryVoices = new();

    private int _avatarOffsetX = 0;
    private int _avatarOffsetY = 0;
    private int? _avatarSizeOverride = null;
    private bool _avatarDragging = false;
    private bool _avatarResizing = false;
    private Point _dragStartMouse;
    private int _dragStartOffsetX;
    private int _dragStartOffsetY;
    private int? _dragStartSize;
    private double _avatarAspectRatio = 1.0;

    private bool _compactMode = false;
    private string? _lastSoundEmotion;

    // Color palette cycled by F7
    private static readonly (string Name, Color Tint, float VortexHue)[] Palette =
    {
        ("cyan",   new Color(  0, 180, 255), 0.50f),
        ("red",    new Color(255,  60,  60), 0.00f),
        ("green",  new Color( 60, 255, 100), 0.33f),
        ("purple", new Color(160,  60, 255), 0.75f),
        ("orange", new Color(255, 150,  30), 0.08f),
        ("pink",   new Color(255,  60, 200), 0.88f),
        ("white",  new Color(220, 230, 255), 0.62f),
    };
    private Color ActiveTint    => Palette[Math.Abs(_project.ColorIndex) % Palette.Length].Tint;
    private float ActiveVortexHue => Palette[Math.Abs(_project.ColorIndex) % Palette.Length].VortexHue;

    // Per-panel layout rects (position + size, user-draggable/resizable)
    private Rectangle _voiceRect;
    private Rectangle _sessionsRect;
    private Rectangle _voicesExtraRect;
    private Rectangle _messagesRect;

    // Panel drag/resize state
    private enum DragPanel { None, Voice, Sessions, VoicesExtra, Messages }
    private DragPanel _activeDragPanel = DragPanel.None;
    private bool _panelResizing = false;
    private Point _panelDragStartMouse;
    private Rectangle _panelDragStartRect;

    // Panel collapse state
    private bool _voiceCollapsed      = false;
    private bool _sessionsCollapsed   = false;
    private bool _voicesExtraCollapsed = false;
    private const int CollapsedW = 20;

    // Layout persistence (now part of ProjectConfig / morpheus.cfg in working dir)

    // API key panel (F4)
    private readonly ApiKeyPanel _apiKeyPanel = new();
    private bool _showApiKeyPanel = false;
    private string _keystorePath = "";
    private string _ollamaModel        = "qwen3.5";
    private string _geminiKeystorePath   = "";
    private string _openAiKeystorePath   = "";
    private string _claudeKeystorePath   = "";
    private string _deepSeekKeystorePath = "";

    // Template stop texture
    private Texture2D? _uiStopTex;
    private Rectangle  _uiStopRect;

    // Avatar-anchored action buttons (computed each frame from frameBox)
    private Rectangle _frameBox;
    private readonly Button _btnReplay       = new() { Label = "replay" };
    private readonly Button _btnSummary      = new() { Label = "summary" };
    private readonly Button _btnCustomTts    = new() { Label = "tts" };
    private readonly Button _btnCustomPrompt = new() { Label = "prompt" };
    private readonly Button _btnSaveFile     = new() { Label = "save file" };
    private readonly Button _btnViewLog      = new() { Label = "view log" };

    // Modals
    private readonly CustomTtsPanel    _customTtsPanel    = new();
    private readonly CustomPromptPanel _customPromptPanel = new();
    private bool _showCustomTts;
    private bool _showCustomPrompt;

    // Last synthesized audio (for save-file)
    private byte[]? _lastMp3;

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
        AppLogger.Initialize(AppContext.BaseDirectory);
        _settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.local.json");
        _keystorePath = Path.Combine(AppContext.BaseDirectory, "keystore.local.dat");
        _settings = SettingsStore.Load(_settingsPath);

        // Per-project config lives in the working directory (where Morpheus was launched from)
        _projectPath = Path.Combine(Environment.CurrentDirectory, "morpheus.cfg");
        _project = ProjectConfigStore.Load(_projectPath);

        // One-time migration: if morpheus.cfg doesn't exist yet, pull per-project fields
        // from the old settings.local.json + layout.local.json so the user keeps their config.
        if (!File.Exists(_projectPath))
        {
            MigrateLegacyToProject();
            ProjectConfigStore.Save(_projectPath, _project);
        }

        _bg = ParseColor(_project.BackgroundColor) ?? Color.Black;

        // Load ElevenLabs key; migrate from old settings.local.json if needed.
        var storedKey = SecureKeyStore.Load(_keystorePath);
        if (!string.IsNullOrEmpty(storedKey))
        {
            _settings.ElevenLabsApiKey = storedKey;
        }
        else if (!string.IsNullOrEmpty(_settings.ElevenLabsApiKey))
        {
            SecureKeyStore.Save(_keystorePath, _settings.ElevenLabsApiKey);
            SettingsStore.Save(_settingsPath, _settings);
        }

        _geminiKeystorePath   = Path.Combine(AppContext.BaseDirectory, "keystore-gemini.local.dat");
        _openAiKeystorePath   = Path.Combine(AppContext.BaseDirectory, "keystore-openai.local.dat");
        _claudeKeystorePath   = Path.Combine(AppContext.BaseDirectory, "keystore-claude.local.dat");
        _deepSeekKeystorePath = Path.Combine(AppContext.BaseDirectory, "keystore-deepseek.local.dat");

        var storedGeminiKey   = SecureKeyStore.Load(_geminiKeystorePath);
        if (!string.IsNullOrEmpty(storedGeminiKey))   _settings.GeminiApiKey   = storedGeminiKey;
        var storedOpenAiKey   = SecureKeyStore.Load(_openAiKeystorePath);
        if (!string.IsNullOrEmpty(storedOpenAiKey))   _settings.OpenAiApiKey   = storedOpenAiKey;
        var storedClaudeKey   = SecureKeyStore.Load(_claudeKeystorePath);
        if (!string.IsNullOrEmpty(storedClaudeKey))   _settings.ClaudeApiKey   = storedClaudeKey;
        var storedDeepSeekKey = SecureKeyStore.Load(_deepSeekKeystorePath);
        if (!string.IsNullOrEmpty(storedDeepSeekKey)) _settings.DeepSeekApiKey = storedDeepSeekKey;

        // Detect Ollama model at startup (fire and forget).
        _ = DetectOllamaModelAsync();

        if (_project.WindowWidth > 0 && _project.WindowHeight > 0)
        {
            _graphics.PreferredBackBufferWidth  = _project.WindowWidth;
            _graphics.PreferredBackBufferHeight = _project.WindowHeight;
            _graphics.ApplyChanges();
        }
        _avatarOffsetX = _project.AvatarOffsetX;
        _avatarOffsetY = _project.AvatarOffsetY;
        if (_project.AvatarSize > 0) _avatarSizeOverride = _project.AvatarSize;

        _liveSession = SessionStore.CreateNew();
        _viewSession = _liveSession;
        SessionStore.Save(_liveSession);

        _listener.OnStop += e => _mainThread.Enqueue(() => OnAssistantMessage(e));
        _listener.OnTool += e => _mainThread.Enqueue(() => OnTool(e));
        _listener.OnBindChanged += sid => _mainThread.Enqueue(() =>
        {
            _ui.BoundSessionId = sid;
            _ui.StatusLine = $"bound to session {Short(sid)}";
        });
        _listener.OnPollTick += msg => _mainThread.Enqueue(() => _ui.StatusLine = msg);
        _player.Finished += () => _mainThread.Enqueue(() =>
        {
            _avatarState.MouthOpen = false;
            StartNextSpeech();
        });

        _listener.FilterCwd = Environment.CurrentDirectory;
        int hookPort = _project.HookPort ?? _settings.HookPort;
        _ = _listener.StartAsync(hookPort);

        Exiting += OnWindowExiting;
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _batch = new SpriteBatch(GraphicsDevice);
        _text = new TextRenderer(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _bgRenderer.LoadContent(GraphicsDevice);
        _pixel.SetData(new[] { Color.White });

        // Initialize panel rects — use saved project config if present, else viewport-relative defaults
        var vp0 = GraphicsDevice.Viewport.Bounds;
        bool hasLayout = File.Exists(_projectPath);
        _voiceRect        = hasLayout ? _project.Voice.ToRect()       : new Rectangle(10, 140, 290, 270);
        _sessionsRect     = hasLayout ? _project.Sessions.ToRect()    : new Rectangle(vp0.Width - 300, 140, 290, 110);
        _voicesExtraRect  = hasLayout ? _project.VoicesExtra.ToRect() : new Rectangle(vp0.Width - 290, 270, 270, 165);
        _messagesRect     = hasLayout ? _project.Messages.ToRect()    : new Rectangle(40, vp0.Height - 220, vp0.Width - 80, 180);

        var avatarsRoot = Path.Combine(AppContext.BaseDirectory, "avatars");
        var templatesRoot = Path.Combine(AppContext.BaseDirectory, "templates");
        _ui.Avatars = AvatarLoader.Discover(avatarsRoot);
        var templates = TemplateLoader.Discover(templatesRoot);
        _ui.Templates = Map(templates, t => t.FolderName);

        if (_project.SelectedAvatar is { } savedAv)
        {
            for (int i = 0; i < _ui.Avatars.Count; i++)
                if (string.Equals(_ui.Avatars[i].FolderName, savedAv, StringComparison.OrdinalIgnoreCase))
                { _ui.AvatarIndex = i; break; }
        }
        if (_project.SelectedTemplate is { } savedTpl)
        {
            for (int i = 0; i < templates.Count; i++)
                if (string.Equals(templates[i].FolderName, savedTpl, StringComparison.OrdinalIgnoreCase))
                { _ui.TemplateIndex = i; break; }
        }

        ApplyAvatar();
        ApplyTemplate(templates);

        _voicePanel.BindFromSettings(_project);
        ApplyAccentColor();
        _voicePanel.Save.Clicked += () =>
        {
            _voicePanel.WriteToSettings(_project);
            SaveProjectConfig();
            _ui.StatusLine = "voice settings saved";
        };
        _voicePanel.Preview.Clicked += () =>
        {
            _voicePanel.WriteToSettings(_project);
            SaveProjectConfig();
            TestSpeak();
        };
        _voicePanel.Reset.Clicked += () =>
        {
            _voicePanel.Stability.Value  = 0.5f;
            _voicePanel.Similarity.Value = 0.75f;
            _voicePanel.Style.Value      = 0.0f;
            _ui.StatusLine = "voice sliders reset to defaults";
        };
        _voicePanel.Voice.Changed += id =>
        {
            _project.ElevenLabsVoiceId = id;
            SaveProjectConfig();
        };

        _sessionsDropdown.Changed += id => SelectSession(id);
        _clearSessionsBtn.Clicked += () =>
        {
            int n = SessionStore.ClearAll(_liveSession.Path);
            RefreshSessionsDropdown();
            _ui.StatusLine = $"cleared {n} past session(s)";
        };
        RefreshSessionsDropdown();
        _messageView.SetPages(_liveSession.Pages);
        _btnReplay.Clicked       += () => { ReplaySpeech(); AppLogger.Log("replay"); };
        _btnSummary.Clicked      += () => SummarizePage();
        _btnCustomTts.Clicked    += OpenCustomTts;
        _btnCustomPrompt.Clicked += OpenCustomPrompt;
        _btnSaveFile.Clicked     += SaveAudioFile;
        _btnViewLog.Clicked      += OpenLogFile;

        _customTtsPanel.PlayClicked   += RunCustomTts;
        _customTtsPanel.Cancelled     += () => _showCustomTts = false;
        _customPromptPanel.Confirmed  += RunCustomPrompt;
        _customPromptPanel.Cancelled  += () => _showCustomPrompt = false;

        _voicePanel.Refresh.Clicked += () =>
        {
            _ui.StatusLine = "refreshing voices…";
            _ = LoadVoicesAsync();
        };

        _voicesExtra.AddCustom.Clicked += () =>
        {
            var name = _voicesExtra.CustomName.Text.Trim();
            var id = _voicesExtra.CustomId.Text.Trim();
            if (string.IsNullOrEmpty(id)) { _ui.StatusLine = "voice id required"; return; }
            if (string.IsNullOrEmpty(name)) name = id[..Math.Min(8, id.Length)];
            // Replace existing custom with the same id (so re-naming works).
            _project.CustomVoices.RemoveAll(v => string.Equals(v.VoiceId, id, StringComparison.OrdinalIgnoreCase));
            _project.CustomVoices.Add(new CustomVoice { Name = name, VoiceId = id });
            _project.ElevenLabsVoiceId = id;
            SaveProjectConfig();
            _voicesExtra.CustomName.Text = "";
            _voicesExtra.CustomId.Text = "";
            RebuildVoiceDropdown();
            _ui.StatusLine = $"added '{name}' and selected";
        };

        Action triggerSharedSearch = () =>
        {
            var q = _voicesExtra.Search.Text.Trim();
            _ui.StatusLine = $"searching shared library: {q}";
            _ = SearchSharedAsync(q);
        };
        _voicesExtra.SearchBtn.Clicked += () => triggerSharedSearch();
        _voicesExtra.Search.Submitted += _ => triggerSharedSearch();

        _voicesExtra.UseShared.Clicked += () =>
        {
            var id = _voicesExtra.SharedResults.SelectedId;
            if (string.IsNullOrEmpty(id)) { _ui.StatusLine = "no shared voice selected"; return; }
            var name = _voicesExtra.SharedResults.SelectedDisplay;
            _project.CustomVoices.RemoveAll(v => string.Equals(v.VoiceId, id, StringComparison.OrdinalIgnoreCase));
            _project.CustomVoices.Add(new CustomVoice { Name = $"shared: {name}", VoiceId = id });
            _project.ElevenLabsVoiceId = id;
            SaveProjectConfig();
            RebuildVoiceDropdown();
            _ui.StatusLine = $"using shared voice {name}";
        };

        Window.TextInput += OnWindowTextInput;

        // Pre-fill panel
        _apiKeyPanel.KeyInput.Text    = _settings.ElevenLabsApiKey ?? "";
        _apiKeyPanel.KeyInput2.Text   = KeyForProvider(_settings.SummaryProvider);
        _apiKeyPanel.SelectedProvider = _settings.SummaryProvider;

        _apiKeyPanel.WireButtons(
            async key => await VoiceService.TestKeyAsync(key),
            async key => await Ai.GeminiClient.TestKeyAsync(key),
            async key => await Ai.OpenAiClient.TestKeyAsync(key),
            async key => await Ai.ClaudeClient.TestKeyAsync(key),
            async key => await Ai.DeepSeekClient.TestKeyAsync(key));

        _apiKeyPanel.Saved += key =>
        {
            _settings.ElevenLabsApiKey = key;
            SecureKeyStore.Save(_keystorePath, key);
            _showApiKeyPanel = false;
            _ui.StatusLine = "ElevenLabs key saved";
            _ = LoadVoicesAsync();
        };
        _apiKeyPanel.GeminiSaved += key =>
        {
            _settings.GeminiApiKey = key;
            SecureKeyStore.Save(_geminiKeystorePath, key);
            _showApiKeyPanel = false;
            _ui.StatusLine = "Gemini key saved";
        };
        _apiKeyPanel.OpenAiSaved += key =>
        {
            _settings.OpenAiApiKey = key;
            SecureKeyStore.Save(_openAiKeystorePath, key);
            _showApiKeyPanel = false;
            _ui.StatusLine = "OpenAI key saved";
        };
        _apiKeyPanel.ClaudeSaved += key =>
        {
            _settings.ClaudeApiKey = key;
            SecureKeyStore.Save(_claudeKeystorePath, key);
            _showApiKeyPanel = false;
            _ui.StatusLine = "Claude key saved";
        };
        _apiKeyPanel.DeepSeekSaved += key =>
        {
            _settings.DeepSeekApiKey = key;
            SecureKeyStore.Save(_deepSeekKeystorePath, key);
            _showApiKeyPanel = false;
            _ui.StatusLine = "DeepSeek key saved";
        };
        _apiKeyPanel.ProviderChanged += p =>
        {
            _settings.SummaryProvider = p;
            SaveSettings();
            _apiKeyPanel.KeyInput2.Text = KeyForProvider(p);
        };
        _apiKeyPanel.Cancelled += () =>
        {
            _showApiKeyPanel = false;
            _apiKeyPanel.Reset();
        };

        RebuildVoiceDropdown();
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
                _libraryVoices = voices;
                RebuildVoiceDropdown();
                _ui.StatusLine = $"loaded {voices.Count} voices";
            });
        }
        catch (Exception ex)
        {
            _mainThread.Enqueue(() => _ui.StatusLine = $"voice load failed: {ex.Message}");
        }
    }

    private void RebuildVoiceDropdown()
    {
        var merged = new List<VoiceInfo>(_libraryVoices);
        foreach (var c in _project.CustomVoices)
            merged.Add(new VoiceInfo(c.VoiceId, c.Name + "  [custom]", "custom", null));
        _voicePanel.PopulateVoices(merged, _project.ElevenLabsVoiceId);
    }

    private async System.Threading.Tasks.Task SearchSharedAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(_settings.ElevenLabsApiKey))
        {
            _mainThread.Enqueue(() => _ui.StatusLine = "no api key set");
            return;
        }
        try
        {
            var voices = await VoiceService.SearchSharedAsync(_settings.ElevenLabsApiKey, query);
            _mainThread.Enqueue(() =>
            {
                _voicesExtra.PopulateSharedResults(voices);
                _ui.StatusLine = voices.Count == 0
                    ? "no shared voices matched"
                    : $"shared library: {voices.Count} results";
            });
        }
        catch (Exception ex)
        {
            _mainThread.Enqueue(() => _ui.StatusLine = $"shared search failed: {ex.Message}");
        }
    }

    private void OnWindowTextInput(object? sender, TextInputEventArgs e)
    {
        if (_showApiKeyPanel)
        {
            var fi = _apiKeyPanel.GetFocusedInput();
            if (fi is not null) { fi.HandleChar(e.Character); return; }
            return;
        }
        if (_showCustomTts)
        {
            _customTtsPanel.FocusedMultiline?.HandleChar(e.Character);
            _customTtsPanel.FocusedTextInput?.HandleChar(e.Character);
            return;
        }
        if (_showCustomPrompt)
        {
            _customPromptPanel.FocusedMultiline?.HandleChar(e.Character);
            return;
        }
        foreach (var t in _voicesExtra.TextInputs)
        {
            if (!t.Focused) continue;
            t.HandleChar(e.Character);
            return;
        }
    }

    protected override void Update(GameTime gameTime)
    {
        while (_mainThread.TryDequeue(out var a)) a();

        var dt = gameTime.ElapsedGameTime.TotalSeconds;
        _gameSeconds += dt;
        _avatarRenderer.Update(dt);
        _bgRenderer.Update(dt);
        _bgRenderer.UpdateDiagonal(dt);

        var vp = GraphicsDevice.Viewport.Bounds;
        LayoutForFrame(vp);

        var k = Keyboard.GetState();
        bool inputFocused = AnyTextInputFocused();
        if (Pressed(k, Keys.Escape))
        {
            if (_showApiKeyPanel)   { _showApiKeyPanel = false; _apiKeyPanel.Reset(); }
            else if (_showCustomTts)    _showCustomTts    = false;
            else if (_showCustomPrompt) _showCustomPrompt = false;
            else if (inputFocused) DispatchKeyToFocused(Keys.Escape);
            else Exit();
        }
        if (!inputFocused)
        {
            if (Pressed(k, Keys.F1)) InstallHooksForCwd();
            if (Pressed(k, Keys.F2)) { _ui.CycleAvatar(+1); ApplyAvatar(); SaveProjectConfig(); }
            if (Pressed(k, Keys.F3)) { _ui.CycleTemplate(+1); ApplyTemplate(TemplateLoader.Discover(Path.Combine(AppContext.BaseDirectory, "templates"))); SaveProjectConfig(); }
            if (Pressed(k, Keys.F4)) { _showApiKeyPanel = !_showApiKeyPanel; if (_showApiKeyPanel) { var vp2 = GraphicsDevice.Viewport; _apiKeyPanel.Layout(vp2.Width / 2, vp2.Height / 2); _apiKeyPanel.AccentColor = ActiveTint; _apiKeyPanel.KeyInput.Text = _settings.ElevenLabsApiKey ?? ""; _apiKeyPanel.SelectedProvider = _settings.SummaryProvider; _apiKeyPanel.OllamaModelLabel = _ollamaModel; _apiKeyPanel.KeyInput2.Text = KeyForProvider(_settings.SummaryProvider); _apiKeyPanel.Reset(); } }
            if (Pressed(k, Keys.F5)) TestSpeak();
            if (Pressed(k, Keys.F6)) InstallActivePersonality();
            if (Pressed(k, Keys.F7)) CycleColor();
            if (Pressed(k, Keys.F8)) { _compactMode = !_compactMode; _ui.StatusLine = _compactMode ? "compact mode ON" : "compact mode OFF"; }
            if (Pressed(k, Keys.F9)) ResetLayout();
        }
        else
        {
            if (Pressed(k, Keys.Back))  DispatchKeyToFocused(Keys.Back);
            if (Pressed(k, Keys.Enter)) DispatchKeyToFocused(Keys.Enter);
        }

        // Ctrl+V paste into focused text input (works regardless of inputFocused gating)
        bool ctrlHeld = k.IsKeyDown(Keys.LeftControl) || k.IsKeyDown(Keys.RightControl);
        if (ctrlHeld && Pressed(k, Keys.V))
            DispatchPasteToFocused(Ui.Widgets.ClipboardHelper.GetText() ?? "");

        _prevKeys = k;

        var m = Mouse.GetState();
        if (_showApiKeyPanel)
        {
            var input2 = new WidgetInput { Mouse = m, PrevMouse = _prevMouse };
            _apiKeyPanel.Update(input2);
            _prevMouse = m;
            base.Update(gameTime);
            return;
        }
        if (_showCustomTts || _showCustomPrompt)
        {
            var input2 = new WidgetInput { Mouse = m, PrevMouse = _prevMouse };
            if (_showCustomTts)    _customTtsPanel.Update(input2);
            if (_showCustomPrompt) _customPromptPanel.Update(input2);
            _prevMouse = m;
            base.Update(gameTime);
            return;
        }
        UpdateAvatarDragResize(vp, m, _prevMouse);

        // Collapse button click detection — must run before drag/resize so the
        // collapse strip doesn't accidentally start a panel drag.
        bool collapseClicked = false;
        {
            bool clickNow2 = m.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
            if (clickNow2)
            {
                var mp2 = new Point(m.X, m.Y);
                if (VoiceCollapseBtn().Contains(mp2))      { _voiceCollapsed      = !_voiceCollapsed;      collapseClicked = true; }
                else if (SessionsCollapseBtn().Contains(mp2))   { _sessionsCollapsed   = !_sessionsCollapsed;   collapseClicked = true; }
                else if (VoicesExtraCollapseBtn().Contains(mp2)) { _voicesExtraCollapsed = !_voicesExtraCollapsed; collapseClicked = true; }
            }
        }
        if (!collapseClicked) UpdatePanelDragResize(m, _prevMouse);

        var input = new WidgetInput { Mouse = m, PrevMouse = _prevMouse };
        if (!_voiceCollapsed) _voicePanel.Update(input);
        // Capture Open BEFORE Update — the dropdown sets Open=false the same
        // frame it consumes a row click, so checking after lets that click
        // fall through to widgets sitting under the open list.
        bool sessionsListWasOpen = !_sessionsCollapsed && _sessionsDropdown.Open;
        if (!_sessionsCollapsed) _sessionsDropdown.Update(input);
        bool sharedListWasOpen = !_voicesExtraCollapsed && _voicesExtra.SharedResults.Open;
        if (!_voicesExtraCollapsed) _voicesExtra.Update(input);
        if (!sessionsListWasOpen && !sharedListWasOpen)
        {
            if (!_sessionsCollapsed) _clearSessionsBtn.Update(input);
            _messageView.Update(m, _prevMouse);
        }

        // Avatar action buttons
        _btnReplay.Update(input);
        _btnSummary.Update(input);
        _btnCustomTts.Update(input);
        _btnCustomPrompt.Update(input);
        _btnSaveFile.Update(input);
        _btnViewLog.Update(input);

        // UI_stop click
        bool clickedNow = m.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        if (clickedNow && _uiStopRect.Contains(m.X, m.Y)) StopSpeech();

        _prevMouse = m;

        _avatarState.MouthOpen = _player.IsPlaying;

        TickIdleAnimations();

        base.Update(gameTime);
    }

    private void LayoutForFrame(Rectangle vp)
    {
        var tpl = _activeTemplate?.Manifest;
        int textSize   = tpl?.TextSize   > 0 ? tpl.TextSize   : 16;
        int lineHeight = tpl?.LineHeight > 0 ? tpl.LineHeight : 20;

        _messageView.Layout(_messagesRect, tpl?.MessageInsets, textSize, lineHeight, 32, 6);
        _voicePanel.Layout(_voiceRect.X, _voiceRect.Y, _voiceRect.Width);

        // Sessions widgets sit inside the stored sessions rect
        _sessionsDropdown.Bounds = new Rectangle(
            _sessionsRect.X + 10, _sessionsRect.Y + 20,
            Math.Max(60, _sessionsRect.Width - 20), 28);
        _clearSessionsBtn.Bounds = new Rectangle(
            _sessionsRect.X + 10, _sessionsRect.Y + 58,
            Math.Min(130, Math.Max(60, _sessionsRect.Width - 20)), 26);

        _voicesExtra.Layout(_voicesExtraRect.X, _voicesExtraRect.Y, _voicesExtraRect.Width);

        // Compute and cache frameBox so Draw() and Update() share the same value
        var sz = _ui.SelectedAvatar?.Manifest.Size;
        int baseW = sz?.Width  is > 0 ? sz.Width  : 400;
        int baseH = sz?.Height is > 0 ? sz.Height : 400;
        int aw = _avatarSizeOverride ?? baseW;
        int ah = (int)(aw / ((double)baseW / baseH));
        int ax = vp.Width / 2 - aw / 2 + 60 + _avatarOffsetX;
        int ay = 80 + _avatarOffsetY;
        _frameBox = new Rectangle(ax, ay, aw, ah);

        // Six avatar buttons in 3 column-pairs below the avatar frame
        int btnH   = 24;
        int btnGapY = 4;
        int colW   = _frameBox.Width / 3;
        int rowY   = _frameBox.Bottom + btnGapY;
        int halfW  = colW / 2 - 2;

        Button[][] pairs = [
            [_btnReplay,    _btnSummary],
            [_btnCustomTts, _btnCustomPrompt],
            [_btnSaveFile,  _btnViewLog],
        ];
        for (int col = 0; col < 3; col++)
        {
            int bx = _frameBox.X + col * colW;
            pairs[col][0].Bounds = new Rectangle(bx,           rowY, halfW, btnH);
            pairs[col][1].Bounds = new Rectangle(bx + halfW + 4, rowY, halfW, btnH);
        }

        // UI_stop: top-right area of the message box, inset left and down slightly
        int stopSz = Math.Clamp(_messagesRect.Height / 4, 22, 52);
        _uiStopRect = new Rectangle(
            _messagesRect.Right - stopSz * 2 - 4,
            _messagesRect.Y + stopSz / 3,
            stopSz, stopSz);

        // Propagate accent to avatar buttons and modal panels
        var accent = ActiveTint;
        foreach (var b in new[] { _btnReplay, _btnSummary, _btnCustomTts, _btnCustomPrompt, _btnSaveFile, _btnViewLog })
            b.AccentColor = accent;
        _customTtsPanel.AccentColor    = accent;
        _customPromptPanel.AccentColor = accent;
    }

    // Generic shape: emotion-or-clip, gap min/max, hold duration, weight.
    private record IdlePick(string? Emotion, string? ClipFile, float Min, float Max, float Duration);

    private void TickIdleAnimations()
    {
        var manifest = _ui.SelectedAvatar?.Manifest;
        var anims = manifest?.IdleAnimations;
        var clips = manifest?.IdleClips;
        bool hasAnims = anims is { Count: > 0 };
        bool hasClips = clips is { Count: > 0 };
        if (!hasAnims && !hasClips) return;

        bool trulyIdle = !_player.IsPlaying && _avatarState.ActiveTool is null;

        if (!trulyIdle)
        {
            if (_currentIdle is not null)
            {
                _currentIdle = null;
                _avatarState.OverrideSprite = null;
            }
            _nextIdleAt = _gameSeconds + 3;
            return;
        }

        if (_currentIdle is not null)
        {
            if (_gameSeconds < _currentIdleEndAt) return;
            var ended = _currentIdle;
            _currentIdle = null;
            _avatarState.OverrideSprite = null;
            _nextIdleAt = _gameSeconds + RandomGap(ended.Min, ended.Max);
            return;
        }

        if (_gameSeconds < _nextIdleAt) return;

        var pick = PickIdle(anims, clips);
        if (pick is null)
        {
            _nextIdleAt = _gameSeconds + 5;
            return;
        }
        _currentIdle = pick;
        _currentIdleEndAt = _gameSeconds + Math.Max(0.05, pick.Duration);
        if (pick.ClipFile is { Length: > 0 })
            _avatarState.OverrideSprite = pick.ClipFile;
        else if (pick.Emotion is { Length: > 0 })
            _avatarState.Emotion = pick.Emotion;
    }

    private double RandomGap(float min, float max)
    {
        var lo = Math.Max(0, min);
        var hi = Math.Max(lo, max);
        return lo + _rng.NextDouble() * (hi - lo);
    }

    private IdlePick? PickIdle(List<IdleAnimation>? anims, List<IdleClip>? clips)
    {
        double total = 0;
        if (anims is not null) foreach (var a in anims) total += Math.Max(0, a.Weight);
        if (clips is not null) foreach (var c in clips) total += Math.Max(0, c.Weight);
        if (total <= 0) return null;

        double r = _rng.NextDouble() * total;
        double acc = 0;
        if (anims is not null)
        {
            foreach (var a in anims)
            {
                acc += Math.Max(0, a.Weight);
                if (r <= acc) return new IdlePick(a.Emotion, null, a.MinSeconds, a.MaxSeconds, a.DurationSeconds);
            }
        }
        if (clips is not null)
        {
            foreach (var c in clips)
            {
                acc += Math.Max(0, c.Weight);
                if (r <= acc) return new IdlePick(null, c.File, c.MinSeconds, c.MaxSeconds, c.DurationSeconds);
            }
        }
        return null;
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(_bg);

        var vp = GraphicsDevice.Viewport.Bounds;
        var tpl = _activeTemplate?.Manifest;

        // Full-window background grid (dimmer)
        _bgRenderer.Draw(vp, ActiveTint);

        _batch.Begin(blendState: BlendState.AlphaBlend, samplerState: SamplerState.LinearClamp);

        var frameBox  = _frameBox;
        var avatarBox = Inset(frameBox, tpl?.AvatarInsets);

        // Inset rect that fits inside ui_avatar.png's border — used for fill, vortex and static
        int insetH = (int)(frameBox.Width  * 0.05f);
        int insetV = (int)(frameBox.Height * 0.05f);
        var vortexBox = new Rectangle(
            frameBox.X + insetH, frameBox.Y + insetV,
            frameBox.Width - insetH * 2, frameBox.Height - insetV * 2);

        // Layer 1: black fill only for the interior (edges stay transparent so grid shows through frame border)
        _batch.Draw(_pixel, vortexBox, Color.Black);

        // Layer 2: vortex effect inside the border
        _batch.End();
        _bgRenderer.DrawDiagonal(vortexBox, ActiveTint, ActiveVortexHue);
        _batch.Begin(blendState: BlendState.AlphaBlend, samplerState: SamplerState.LinearClamp);

        // Layer 3: avatar shifted down so it clips into frame bottom (TV-screen feel)
        int avatarShiftDown = (int)(avatarBox.Height * 0.03f);
        var shiftedAvatarBox = new Rectangle(
            avatarBox.X, avatarBox.Y + avatarShiftDown,
            avatarBox.Width, avatarBox.Height);
        _avatarRenderer.Draw(_batch, shiftedAvatarBox, _avatarState);

        // Layer 3.5: TV static on top of avatar, inside the inset border
        _bgRenderer.DrawStatic(_batch, _pixel, vortexBox);

        // Layer 4: UI frame overlay on top of avatar
        if (_avatarFrame is not null) _batch.Draw(_avatarFrame, frameBox, ActiveTint);

        if (_messageFrame is not null) _batch.Draw(_messageFrame, _messagesRect, ActiveTint);

        _messageView.ForwardTex = _uiForwardTex;
        _messageView.BackwardTex = _uiBackwardTex;
        _messageView.Draw(_batch, _text, _pixel);
        DrawResizeHandle(_messagesRect);

        if (!_compactMode)
        {
            _ui.Draw(_batch, _text, vp, ActiveTint);

            if (!_voiceCollapsed)
            {
                _voicePanel.Draw(_batch, _text, _pixel, _voiceRect);
                DrawResizeHandle(_voiceRect);
            }
            else
            {
                // Collapsed voice: draw thin background strip
                var cs = new Rectangle(_voiceRect.X, _voiceRect.Y, CollapsedW, _voiceRect.Height);
                _batch.Draw(_pixel, cs, new Color(0, 10, 20, 180));
                _batch.Draw(_pixel, new Rectangle(cs.X, cs.Y,          cs.Width, 1), ActiveTint);
                _batch.Draw(_pixel, new Rectangle(cs.X, cs.Bottom - 1, cs.Width, 1), ActiveTint);
                _batch.Draw(_pixel, new Rectangle(cs.Right - 1, cs.Y,  1, cs.Height), ActiveTint);
            }
            DrawCollapseBtn(VoiceCollapseBtn(), _voiceCollapsed); // ► expand / ◄ collapse

            if (!_sessionsCollapsed)
            {
                DrawSessionsPanel();
                DrawResizeHandle(_sessionsRect);
            }
            else
            {
                var cs = new Rectangle(_sessionsRect.Right - CollapsedW, _sessionsRect.Y, CollapsedW, _sessionsRect.Height);
                _batch.Draw(_pixel, cs, new Color(0, 10, 20, 180));
                _batch.Draw(_pixel, new Rectangle(cs.X, cs.Y,          cs.Width, 1), ActiveTint);
                _batch.Draw(_pixel, new Rectangle(cs.X, cs.Bottom - 1, cs.Width, 1), ActiveTint);
                _batch.Draw(_pixel, new Rectangle(cs.X, cs.Y,          1, cs.Height), ActiveTint);
            }
            DrawCollapseBtn(SessionsCollapseBtn(), !_sessionsCollapsed); // ► collapse / ◄ expand

            if (!_voicesExtraCollapsed)
            {
                _voicesExtra.Draw(_batch, _text, _pixel, _voicesExtraRect);
                DrawResizeHandle(_voicesExtraRect);
            }
            else
            {
                var cs = new Rectangle(_voicesExtraRect.Right - CollapsedW, _voicesExtraRect.Y, CollapsedW, _voicesExtraRect.Height);
                _batch.Draw(_pixel, cs, new Color(0, 10, 20, 180));
                _batch.Draw(_pixel, new Rectangle(cs.X, cs.Y,          cs.Width, 1), ActiveTint);
                _batch.Draw(_pixel, new Rectangle(cs.X, cs.Bottom - 1, cs.Width, 1), ActiveTint);
                _batch.Draw(_pixel, new Rectangle(cs.X, cs.Y,          1, cs.Height), ActiveTint);
            }
            DrawCollapseBtn(VoicesExtraCollapseBtn(), !_voicesExtraCollapsed); // ► collapse / ◄ expand
        }

        // Avatar action buttons (below avatar frame)
        foreach (var b in new[] { _btnReplay, _btnSummary, _btnCustomTts, _btnCustomPrompt, _btnSaveFile, _btnViewLog })
            b.Draw(_batch, _text, _pixel);

        // UI_stop (right of message box)
        if (_uiStopTex is not null)
            _batch.Draw(_uiStopTex, _uiStopRect, ActiveTint);
        else
        {
            _batch.Draw(_pixel, _uiStopRect, new Color(10, 20, 30, 210));
            int sq = _uiStopRect.Width / 3;
            _batch.Draw(_pixel,
                new Rectangle(_uiStopRect.X + sq, _uiStopRect.Y + sq, sq, sq),
                ActiveTint);
        }

        if (_showApiKeyPanel)
            _apiKeyPanel.Draw(_batch, _text, _pixel);

        if (_showCustomTts)
            _customTtsPanel.Draw(_batch, _text, _pixel);

        if (_showCustomPrompt)
            _customPromptPanel.Draw(_batch, _text, _pixel);

        _batch.End();
        base.Draw(gameTime);
    }

    private void DrawSessionsPanel()
    {
        var r = _sessionsRect;
        var tint = ActiveTint;
        _batch.Draw(_pixel, r, new Color(0, 10, 20, 180));
        _batch.Draw(_pixel, new Rectangle(r.X, r.Y,          r.Width, 1), tint);
        _batch.Draw(_pixel, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), tint);
        _batch.Draw(_pixel, new Rectangle(r.X, r.Y, 1,          r.Height), tint);
        _batch.Draw(_pixel, new Rectangle(r.Right - 1, r.Y, 1, r.Height), tint);
        _text.DrawString(_batch, "sessions", new Vector2(r.X + 8, r.Y + 4), tint, 14);
        _clearSessionsBtn.Draw(_batch, _text, _pixel);
        _sessionsDropdown.Draw(_batch, _text, _pixel);
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

    protected override void UnloadContent()
    {
        _player.Dispose();
        _listener.Dispose();
        _avatarRenderer.Dispose();
        _bgRenderer.Dispose();
        _text.Dispose();
        _avatarFrame?.Dispose();
        _messageFrame?.Dispose();
        _uiForwardTex?.Dispose();
        _uiBackwardTex?.Dispose();
        _uiStopTex?.Dispose();
        _pixel.Dispose();
    }



    // --- actions ---

    private void OnAssistantMessage(StopHookEvent e)
    {
        var uuid = e.MessageUuid;

        if (string.IsNullOrWhiteSpace(e.AssistantMessage))
        {
            if (uuid is { Length: > 0 } && uuid != _currentTurnUuid)
            {
                _currentTurnUuid = uuid;
                _spokenLen = 0;
                _ui.StatusLine = "turn had no text (tool-only)";
            }
            return;
        }

        var fullText = e.AssistantMessage!;

        bool newTurn = uuid != _currentTurnUuid;
        if (newTurn)
        {
            _currentTurnUuid = uuid;
            _spokenLen = 0;
            _avatarRenderer.RerollVariant();
            _messageView.ResetAutoScroll();
        }

        // Parse emotion tags and create (emotion, text) segments for real-time emotion switching
        var segments = ParseEmotionSegments(fullText);

        // Build the full clean text (without emotion tags) for session storage
        var effectiveText = string.Concat(segments.Select(s => s.Text));

        if (effectiveText.Length <= _spokenLen && !newTurn) return;

        var newTextLen = effectiveText.Length;
        var chunkStartPos = _spokenLen;
        _spokenLen = newTextLen;

        // Persist to live session and refresh the view if we're following live.
        bool wasOnLast = _messageView.IsOnLastPage;
        bool pageAdded = _liveSession.AppendOrUpdate(uuid, effectiveText);
        SessionStore.Save(_liveSession);

        if (_isViewingLive)
        {
            int preferIdx = (pageAdded && wasOnLast)
                ? _liveSession.Pages.Count - 1
                : _messageView.PageIndex;
            _messageView.SetPages(_liveSession.Pages, preferIdx);
            if (pageAdded) RefreshSessionsDropdown();
        }

        _ui.StatusLine = "speaking…";

        // Queue speech segments — emotion is applied when each chunk starts playing, not now
        int currentPos = 0;
        foreach (var segment in segments)
        {
            if (currentPos >= chunkStartPos)
            {
                if (!string.IsNullOrEmpty(segment.Text))
                    EnqueueSpeech(segment.Text, segment.Emotion);
            }
            else if (currentPos + segment.Text.Length > chunkStartPos)
            {
                int skipChars = chunkStartPos - currentPos;
                var newPart = segment.Text[skipChars..];
                if (!string.IsNullOrEmpty(newPart))
                    EnqueueSpeech(newPart, segment.Emotion);
            }
            currentPos += segment.Text.Length;
        }
    }

    private record EmotionSegment(string? Emotion, string Text);
    private record SpeechTask(string Text, string? Emotion);

    private List<EmotionSegment> ParseEmotionSegments(string text)
    {
        var segments = new List<EmotionSegment>();
        if (string.IsNullOrEmpty(text))
            return segments;

        string? currentEmotion = null;
        int lastPos = 0;

        foreach (System.Text.RegularExpressions.Match match in EmotionTagRegex.Matches(text))
        {
            if (match.Index > lastPos)
            {
                var textBefore = text[lastPos..match.Index];
                if (!string.IsNullOrEmpty(textBefore))
                    segments.Add(new EmotionSegment(currentEmotion, textBefore));
            }

            // Accept any emotion tag the LLM produces — avatar renderer falls back to idle for unknowns
            currentEmotion = match.Groups[1].Value.ToLowerInvariant();
            lastPos = match.Index + match.Length;
        }

        if (lastPos < text.Length)
        {
            var textRemaining = text[lastPos..];
            if (!string.IsNullOrEmpty(textRemaining))
                segments.Add(new EmotionSegment(currentEmotion, textRemaining));
        }

        // If no emotion tag was found in the text, inject a random available emotion at the start
        if (currentEmotion is null && segments.Count > 0)
        {
            var emotions = _ui.SelectedAvatar?.Manifest.Sprites.Emotions;
            if (emotions is { Count: > 0 })
            {
                var keys = new List<string>(emotions.Keys);
                var picked = keys[_rng.Next(keys.Count)];
                segments[0] = new EmotionSegment(picked, segments[0].Text);
            }
        }

        return segments;
    }

    // emotion is applied to the avatar when this chunk *starts* playing (deferred, not immediate).
    // Only the first chunk of a split gets the emotion; subsequent chunks get null (emotion persists).
    private void EnqueueSpeech(string text, string? emotion = null)
    {
        if (string.IsNullOrWhiteSpace(_settings.ElevenLabsApiKey)
            || string.IsNullOrWhiteSpace(_project.ElevenLabsVoiceId))
            return;

        const int hardCap = 4980;
        if (text.Length > hardCap)
        {
            int breakPoint = hardCap;
            int lastPeriod = text.LastIndexOf('.', Math.Min(hardCap - 1, text.Length - 1));
            if (lastPeriod > hardCap * 0.75) breakPoint = lastPeriod + 1;

            _speechQueue.Enqueue(new SpeechTask(text[..breakPoint].TrimEnd(), emotion));

            var remainder = text[breakPoint..].TrimStart();
            if (!string.IsNullOrEmpty(remainder))
                _speechQueue.Enqueue(new SpeechTask(remainder, null));
        }
        else
        {
            _speechQueue.Enqueue(new SpeechTask(text, emotion));
        }

        if (!_speechActive) StartNextSpeech();
    }

    private void StartNextSpeech()
    {
        if (_speechQueue.Count == 0)
        {
            if (_speechActive) PlayTemplateSound("speak_end");
            _speechActive = false;
            return;
        }
        var next = _speechQueue.Dequeue();

        // Apply emotion change at playback time, not at enqueue time
        if (next.Emotion is not null)
        {
            _avatarState.Emotion = next.Emotion;
            if (next.Emotion != _lastSoundEmotion)
            {
                PlayTemplateSound(next.Emotion);
                _lastSoundEmotion = next.Emotion;
            }
        }

        if (!_speechActive) PlayTemplateSound("speak_start");
        _speechActive = true;
        _ = SynthesizeAndPlayAsync(next.Text);
    }

    private async System.Threading.Tasks.Task SynthesizeAndPlayAsync(string text)
    {
        try
        {
            var voiceSettings = new VoiceSettings
            {
                Stability = _project.VoiceStability,
                SimilarityBoost = _project.VoiceSimilarity,
                Style = _project.VoiceStyle,
                UseSpeakerBoost = _project.VoiceSpeakerBoost,
            };
            var tts = new ElevenLabsTts(_settings.ElevenLabsApiKey!, voiceSettings);
            // Prepend a soft pause to prevent ElevenLabs from clipping the first word
            var mp3 = await tts.SynthesizeAsync("… " + text, _project.ElevenLabsVoiceId!);
            _mainThread.Enqueue(() => { _lastMp3 = mp3; _player.PlayMp3(mp3); });
        }
        catch (Exception ex)
        {
            _mainThread.Enqueue(() =>
            {
                _ui.StatusLine = $"tts error: {ex.Message}";
                StartNextSpeech();
            });
        }
    }

    private void OnTool(ToolHookEvent e)
    {
        if (e.Phase == "pre") _avatarState.ActiveTool = e.ToolName;
        else _avatarState.ActiveTool = null;
        _ui.StatusLine = $"tool {e.Phase}: {e.ToolName}";
        PlayTemplateSound(e.Phase == "pre" ? "tool_start" : "tool_end");
    }

    private void StopSpeech()
    {
        _player.Stop();
        _speechQueue.Clear();
        _speechActive = false;
        _ui.StatusLine = "stopped";
    }

    private void ReplaySpeech()
    {
        StopSpeech();
        var pageText = _messageView.CurrentText;
        if (string.IsNullOrWhiteSpace(pageText)) { _ui.StatusLine = "nothing to replay"; return; }
        _messageView.ResetAutoScroll();
        EnqueueSpeech(pageText);
        _ui.StatusLine = "replaying…";
    }

    private async System.Threading.Tasks.Task DetectOllamaModelAsync()
    {
        var model = await Ai.OllamaClient.GetFirstModelAsync();
        if (model is not null)
            _mainThread.Enqueue(() =>
            {
                _ollamaModel = model;
                _apiKeyPanel.OllamaModelLabel = model;
            });
    }

    private void SummarizePage()
    {
        var pageText = _messageView.CurrentText;
        if (string.IsNullOrWhiteSpace(pageText)) { _ui.StatusLine = "nothing to summarize"; return; }
        if (_settings.SummaryProvider != AiProvider.Ollama
            && string.IsNullOrWhiteSpace(KeyForProvider(_settings.SummaryProvider)))
        {
            var name = _settings.SummaryProvider.ToString();
            _ui.StatusLine = $"no {name} key — press F4 to add it";
            return;
        }
        var label = _settings.SummaryProvider switch
        {
            AiProvider.Gemini   => "Gemini",
            AiProvider.OpenAi   => "OpenAI",
            AiProvider.Claude   => "Claude",
            AiProvider.DeepSeek => "DeepSeek",
            _                   => _ollamaModel,
        };
        _ui.StatusLine = $"summarizing via {label}…";
        _ = SummarizePageAsync(pageText);
    }

    private async System.Threading.Tasks.Task SummarizePageAsync(string pageText)
    {
        try
        {
            var summary = _settings.SummaryProvider switch
            {
                AiProvider.Gemini   => await Ai.GeminiClient.SummarizeAsync(_settings.GeminiApiKey!, pageText),
                AiProvider.OpenAi   => await Ai.OpenAiClient.SummarizeAsync(_settings.OpenAiApiKey!, pageText),
                AiProvider.Claude   => await Ai.ClaudeClient.SummarizeAsync(_settings.ClaudeApiKey!, pageText),
                AiProvider.DeepSeek => await Ai.DeepSeekClient.SummarizeAsync(_settings.DeepSeekApiKey!, pageText),
                _                   => await Ai.OllamaClient.SummarizeAsync(_ollamaModel, pageText),
            };
            if (string.IsNullOrWhiteSpace(summary))
            {
                _mainThread.Enqueue(() => _ui.StatusLine = "summary failed");
                return;
            }
            _mainThread.Enqueue(() =>
            {
                _ui.StatusLine = "summary ready";
                StopSpeech();
                var segments    = ParseEmotionSegments(summary);
                var summaryText = string.Concat(segments.Select(s => s.Text));
                foreach (var seg in segments)
                    if (!string.IsNullOrEmpty(seg.Text))
                        EnqueueSpeech(seg.Text, seg.Emotion);
                if (!string.IsNullOrWhiteSpace(summaryText))
                    _messageView.SetSummaryForCurrentPage(summaryText);
            });
        }
        catch (Exception ex)
        {
            _mainThread.Enqueue(() => _ui.StatusLine = $"summary error: {ex.Message}");
        }
    }

    private void OpenCustomTts()
    {
        var vp = GraphicsDevice.Viewport;
        _customTtsPanel.AccentColor = ActiveTint;
        _customTtsPanel.Layout(vp.Width / 2, vp.Height / 2);
        var emotions = _ui.SelectedAvatar?.Manifest.Sprites.Emotions?.Keys.ToList()
                       ?? new System.Collections.Generic.List<string>();
        _customTtsPanel.SetEmotions(emotions);
        _showCustomTts = true;
        AppLogger.Log("Custom TTS panel opened");
    }

    private void OpenCustomPrompt()
    {
        var vp = GraphicsDevice.Viewport;
        _customPromptPanel.AccentColor = ActiveTint;
        _customPromptPanel.SetAvatarName(_ui.SelectedAvatar?.Manifest.Name ?? "Avatar");
        _customPromptPanel.Layout(vp.Width / 2, vp.Height / 2);
        _showCustomPrompt = true;
        AppLogger.Log("Custom prompt panel opened");
    }

    private void RunCustomTts(string text, string? emotion, int repeatCount)
    {
        _showCustomTts = false;
        AppLogger.Log($"Custom TTS: repeat={repeatCount}, emotion={emotion ?? "none"}, text='{Truncate(text, 60)}'");

        // Add text as a new session page so it shows in the message box
        var page = new Sessions.SessionPage { Uuid = Guid.NewGuid().ToString(), At = DateTime.UtcNow, Text = text };
        _liveSession.Pages.Add(page);
        Sessions.SessionStore.Save(_liveSession);
        _messageView.SetPages(_liveSession.Pages, _liveSession.Pages.Count - 1);
        RefreshSessionsDropdown();
        _messageView.ResetAutoScroll();

        // First repetition carries the chosen emotion; the rest inherit it (null = keep current)
        for (int i = 0; i < repeatCount; i++) EnqueueSpeech(text, i == 0 ? emotion : null);
    }

    private void RunCustomPrompt(string promptText, bool useClaudeContext)
    {
        _showCustomPrompt = false;
        AppLogger.Log($"Custom prompt (useContext={useClaudeContext}): '{Truncate(promptText, 80)}'");
        _ui.StatusLine = "sending custom prompt…";
        _ = RunCustomPromptAsync(promptText, useClaudeContext);
    }

    private async System.Threading.Tasks.Task RunCustomPromptAsync(string promptText, bool useClaudeContext)
    {
        try
        {
            var avatarName = _ui.SelectedAvatar?.Manifest.Name ?? "Avatar";
            var desc       = _ui.SelectedAvatar?.Manifest.Description ?? "";

            var ctxBlock = new System.Text.StringBuilder();
            if (useClaudeContext && _liveSession.Pages.Count > 0)
            {
                ctxBlock.AppendLine("\n\nRecent session context:");
                foreach (var pg in _liveSession.Pages.TakeLast(5))
                    ctxBlock.AppendLine(pg.Text);
            }

            var fullPrompt =
                $"You are {avatarName}, an AI avatar assistant. {desc}{ctxBlock}" +
                $"\n\nUser: {promptText}\n\n" +
                $"Respond as {avatarName}. Be concise and in character. " +
                $"Use [emotion:name] tags where fitting (happy, excited, curious, thoughtful, sad, playful).";

            string? response = _settings.SummaryProvider switch
            {
                AiProvider.Gemini   => await Ai.GeminiClient.PromptAsync(_settings.GeminiApiKey   ?? "", fullPrompt),
                AiProvider.OpenAi   => await Ai.OpenAiClient.PromptAsync(_settings.OpenAiApiKey   ?? "", fullPrompt),
                AiProvider.Claude   => await Ai.ClaudeClient.PromptAsync(_settings.ClaudeApiKey   ?? "", fullPrompt),
                AiProvider.DeepSeek => await Ai.DeepSeekClient.PromptAsync(_settings.DeepSeekApiKey ?? "", fullPrompt),
                _                   => await Ai.OllamaClient.PromptAsync(_ollamaModel, fullPrompt),
            };

            if (string.IsNullOrWhiteSpace(response)) response = "(no response)";
            AppLogger.Log($"Custom prompt response (raw): '{Truncate(response, 200)}'");

            _mainThread.Enqueue(() =>
            {
                var segments  = ParseEmotionSegments(response!);
                var cleanText = string.Concat(segments.Select(s => s.Text));

                var page = new Sessions.SessionPage { Uuid = Guid.NewGuid().ToString(), At = DateTime.UtcNow, Text = cleanText };
                _liveSession.Pages.Add(page);
                Sessions.SessionStore.Save(_liveSession);
                _messageView.SetPages(_liveSession.Pages, _liveSession.Pages.Count - 1);
                RefreshSessionsDropdown();
                _ui.StatusLine = "custom prompt complete";

                foreach (var seg in segments)
                    if (!string.IsNullOrEmpty(seg.Text))
                        EnqueueSpeech(seg.Text, seg.Emotion);
            });
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Custom prompt error: {ex.Message}");
            _mainThread.Enqueue(() => _ui.StatusLine = $"prompt error: {ex.Message}");
        }
    }

    private void SaveAudioFile()
    {
        if (_lastMp3 is null || _lastMp3.Length == 0)
        {
            _ui.StatusLine = "no audio to save yet";
            AppLogger.Log("Save audio: nothing to save");
            return;
        }
        var filename = $"Morpheus_audio_{DateTime.Now:yyyyMMdd_HHmmss}.mp3";
        var path = Path.Combine(AppContext.BaseDirectory, filename);
        try
        {
            File.WriteAllBytes(path, _lastMp3);
            _ui.StatusLine = $"saved: {filename}";
            AppLogger.Log($"Audio saved: {path}");
        }
        catch (Exception ex)
        {
            _ui.StatusLine = $"save error: {ex.Message}";
            AppLogger.Log($"Save audio error: {ex.Message}");
        }
    }

    private void OpenLogFile()
    {
        var logPath = AppLogger.LogPath;
        if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath))
        {
            _ui.StatusLine = "no log file found";
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(logPath)
            {
                UseShellExecute = true,
            });
            AppLogger.Log("Log file opened");
        }
        catch (Exception ex) { _ui.StatusLine = $"can't open log: {ex.Message}"; }
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    private void TestSpeak()
    {
        if (!string.IsNullOrWhiteSpace(_settings.ElevenLabsApiKey)
            && !string.IsNullOrWhiteSpace(_project.ElevenLabsVoiceId))
        {
            const string line = "Morpheus online. Text to speech is wired.";
            _ui.StatusLine = "synthesizing test line…";
            _ = SynthesizeAndPlayAsync(line);
            return;
        }
        var clip = Path.Combine(AppContext.BaseDirectory, "assets", "test", "test_clip.mp3");
        if (!File.Exists(clip)) { _ui.StatusLine = "no tts configured and no bundled clip"; return; }
        _player.PlayMp3(File.ReadAllBytes(clip));
        _ui.StatusLine = "playing bundled clip (no ElevenLabs key in settings.local.json)";
    }

    private void InstallHooksForCwd()
    {
        var cwd = Environment.CurrentDirectory;
        try
        {
            HookInstaller.InstallToProject(cwd, _project.HookPort ?? _settings.HookPort);
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

    // --- session plumbing ---

    private void RefreshSessionsDropdown()
    {
        var entries = SessionStore.Discover();
        var opts = new List<(string, string)>
        {
            ("__live", $"live  ({_liveSession.Pages.Count}p)")
        };
        foreach (var e in entries)
        {
            if (string.Equals(e.Path, _liveSession.Path, StringComparison.OrdinalIgnoreCase)) continue;
            var local = e.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            opts.Add((e.Id, $"{local}  ({e.PageCount}p)"));
        }
        _sessionsDropdown.Options = opts;
        _sessionsDropdown.SelectById(_isViewingLive ? "__live" : _viewSession.Id);
    }

    private void SelectSession(string id)
    {
        if (string.Equals(id, "__live", StringComparison.Ordinal))
        {
            _viewSession = _liveSession;
            _isViewingLive = true;
            _messageView.SetPages(_liveSession.Pages);
            _ui.StatusLine = "viewing live session";
            return;
        }

        var entry = FindSessionEntry(id);
        if (entry is null) { _ui.StatusLine = "session not found"; return; }
        var loaded = SessionStore.Load(entry.Path);
        if (loaded is null) { _ui.StatusLine = "failed to load session"; return; }
        _viewSession = loaded;
        _isViewingLive = false;
        _messageView.SetPages(loaded.Pages);
        _ui.StatusLine = $"viewing session {entry.Id}";
    }

    private static SessionFileEntry? FindSessionEntry(string id)
    {
        foreach (var e in SessionStore.Discover())
            if (string.Equals(e.Id, id, StringComparison.Ordinal)) return e;
        return null;
    }

    // --- helpers ---

    private void ApplyAvatar()
    {
        var av = _ui.SelectedAvatar;
        if (av is null) return;
        _avatarRenderer.LoadAvatar(av, GraphicsDevice);
        _project.SelectedAvatar = av.FolderName;
        _avatarOffsetX = 0;
        _avatarOffsetY = 0;
        _avatarSizeOverride = null;

        if (av.Manifest.PersonalityFile is { Length: > 0 } file)
        {
            var src = Path.Combine(av.FolderPath, file);
            if (File.Exists(src))
            {
                try
                {
                    PersonalityInstaller.Install(src, av.FolderName);
                    _ui.StatusLine = $"personality installed. activate: /config → Output style → morpheus-{av.FolderName}";
                }
                catch { }
            }
        }
    }

    private void ApplyTemplate(IReadOnlyList<TemplateEntry> templates)
    {
        _avatarFrame?.Dispose();
        _messageFrame?.Dispose();
        _uiForwardTex?.Dispose();
        _uiBackwardTex?.Dispose();
        _uiStopTex?.Dispose();
        _avatarFrame = null;
        _messageFrame = null;
        _uiForwardTex = null;
        _uiBackwardTex = null;
        _uiStopTex = null;
        _activeTemplate = null;

        if (templates.Count == 0) return;
        var tpl = templates[_ui.TemplateIndex % templates.Count];
        _activeTemplate = tpl;
        _project.SelectedTemplate = tpl.FolderName;
        _avatarFrame = LoadTex(Path.Combine(tpl.FolderPath, tpl.Manifest.AvatarFrame ?? ""));
        _messageFrame = LoadTex(Path.Combine(tpl.FolderPath, tpl.Manifest.MessageFrame ?? ""));
        _uiForwardTex  = LoadTex(Path.Combine(tpl.FolderPath, "uiforward.png"));
        _uiBackwardTex = LoadTex(Path.Combine(tpl.FolderPath, "uibackward.png"));
        _uiStopTex     = LoadTex(Path.Combine(tpl.FolderPath, "UI_stop.png"));
        if (tpl.Manifest.BackgroundColor is { } c && ParseColor(c) is { } col) _bg = col;
    }

    private Texture2D? LoadTex(string path)
    {
        if (!File.Exists(path)) return null;
        using var fs = File.OpenRead(path);
        return Texture2D.FromStream(GraphicsDevice, fs);
    }

    private void SaveSettings() => SettingsStore.Save(_settingsPath, _settings);

    private void SaveProjectConfig() => ProjectConfigStore.Save(_projectPath, _project);

    // Pull per-project fields from old settings.local.json + layout.local.json created before
    // morpheus.cfg existed. Called only when morpheus.cfg is missing on startup.
    private void MigrateLegacyToProject()
    {
        // Migrate per-project fields from old settings.local.json
        var oldSettingsPath = Path.Combine(AppContext.BaseDirectory, "settings.local.json");
        if (File.Exists(oldSettingsPath))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(oldSettingsPath));
                var r = doc.RootElement;
                if (r.TryGetProperty("selectedAvatar",   out var av)   && av.ValueKind   == System.Text.Json.JsonValueKind.String) _project.SelectedAvatar   = av.GetString();
                if (r.TryGetProperty("selectedTemplate", out var tpl)  && tpl.ValueKind  == System.Text.Json.JsonValueKind.String) _project.SelectedTemplate = tpl.GetString();
                if (r.TryGetProperty("elevenLabsVoiceId", out var vid) && vid.ValueKind  == System.Text.Json.JsonValueKind.String) _project.ElevenLabsVoiceId = vid.GetString();
                if (r.TryGetProperty("backgroundColor",  out var bgc)  && bgc.ValueKind  == System.Text.Json.JsonValueKind.String) _project.BackgroundColor  = bgc.GetString()!;
                if (r.TryGetProperty("voiceStability",   out var vs)   && vs.ValueKind   == System.Text.Json.JsonValueKind.Number) _project.VoiceStability   = vs.GetSingle();
                if (r.TryGetProperty("voiceSimilarity",  out var vsim) && vsim.ValueKind == System.Text.Json.JsonValueKind.Number) _project.VoiceSimilarity  = vsim.GetSingle();
                if (r.TryGetProperty("voiceStyle",       out var vst)  && vst.ValueKind  == System.Text.Json.JsonValueKind.Number) _project.VoiceStyle       = vst.GetSingle();
                if (r.TryGetProperty("voiceSpeakerBoost", out var vsb) && (vsb.ValueKind == System.Text.Json.JsonValueKind.True || vsb.ValueKind == System.Text.Json.JsonValueKind.False)) _project.VoiceSpeakerBoost = vsb.GetBoolean();
            }
            catch { }
        }

        // Migrate layout from old layout.local.json
        var oldLayoutPath = Path.Combine(AppContext.BaseDirectory, "layout.local.json");
        if (File.Exists(oldLayoutPath))
        {
            var old = LayoutStore.Load(oldLayoutPath);
            _project.Voice       = old.Voice;
            _project.Sessions    = old.Sessions;
            _project.VoicesExtra = old.VoicesExtra;
            _project.Messages    = old.Messages;
            _project.AvatarOffsetX = old.AvatarOffsetX;
            _project.AvatarOffsetY = old.AvatarOffsetY;
            _project.AvatarSize    = old.AvatarSize;
            _project.WindowWidth   = old.WindowWidth;
            _project.WindowHeight  = old.WindowHeight;
            _project.ColorIndex    = old.ColorIndex;
        }
    }

    private bool Pressed(KeyboardState k, Keys key) => k.IsKeyDown(key) && _prevKeys.IsKeyUp(key);

    private bool AnyTextInputFocused()
    {
        if (_showApiKeyPanel && _apiKeyPanel.GetFocusedInput() is not null) return true;
        if (_showCustomTts    && (_customTtsPanel.FocusedMultiline is not null || _customTtsPanel.FocusedTextInput is not null)) return true;
        if (_showCustomPrompt && _customPromptPanel.FocusedMultiline is not null) return true;
        foreach (var t in _voicesExtra.TextInputs)
            if (t.Focused) return true;
        return false;
    }

    private void UpdateAvatarDragResize(Rectangle vp, MouseState m, MouseState prevM)
    {
        var sz = _ui.SelectedAvatar?.Manifest.Size;
        int baseWidth = sz?.Width is > 0 ? sz.Width : 400;
        int baseHeight = sz?.Height is > 0 ? sz.Height : 400;
        _avatarAspectRatio = (double)baseWidth / baseHeight;

        int width = _avatarSizeOverride ?? baseWidth;
        int height = (int)(width / _avatarAspectRatio);
        int ax = vp.Width / 2 - width / 2 + 60 + _avatarOffsetX;
        int ay = 80 + _avatarOffsetY;
        var avatarBox = new Rectangle(ax, ay, width, height);

        var mouseP = new Point(m.X, m.Y);
        bool onAvatar = avatarBox.Contains(mouseP);
        bool nearCorner = mouseP.X >= avatarBox.Right - 30 && mouseP.Y >= avatarBox.Bottom - 30
                          && mouseP.X < avatarBox.Right + 5 && mouseP.Y < avatarBox.Bottom + 5;

        if (m.LeftButton == ButtonState.Pressed && prevM.LeftButton == ButtonState.Released)
        {
            if (nearCorner)
            {
                _avatarResizing = true;
                _dragStartMouse = mouseP;
                _dragStartSize = _avatarSizeOverride ?? baseWidth;
            }
            else if (onAvatar)
            {
                _avatarDragging = true;
                _dragStartMouse = mouseP;
                _dragStartOffsetX = _avatarOffsetX;
                _dragStartOffsetY = _avatarOffsetY;
            }
        }

        if (m.LeftButton == ButtonState.Released && prevM.LeftButton == ButtonState.Pressed)
        {
            if (_avatarDragging || _avatarResizing) SaveLayout();
            _avatarDragging = false;
            _avatarResizing = false;
        }

        if (_avatarDragging)
        {
            int dx = m.X - _dragStartMouse.X;
            int dy = m.Y - _dragStartMouse.Y;
            _avatarOffsetX = _dragStartOffsetX + dx;
            _avatarOffsetY = _dragStartOffsetY + dy;
        }

        if (_avatarResizing && _dragStartSize.HasValue)
        {
            int dx = m.X - _dragStartMouse.X;
            int newSize = Math.Max(200, _dragStartSize.Value + dx);
            _avatarSizeOverride = newSize;
        }
    }

    private void DispatchKeyToFocused(Keys key)
    {
        if (_showApiKeyPanel)
        {
            var fi = _apiKeyPanel.GetFocusedInput();
            if (fi is not null)
            {
                if (key == Keys.Escape) { _showApiKeyPanel = false; _apiKeyPanel.Reset(); return; }
                fi.HandleKey(key);
                return;
            }
        }
        if (_showCustomTts)
        {
            _customTtsPanel.FocusedMultiline?.HandleKey(key);
            _customTtsPanel.FocusedTextInput?.HandleKey(key);
            return;
        }
        if (_showCustomPrompt)
        {
            _customPromptPanel.FocusedMultiline?.HandleKey(key);
            return;
        }
        foreach (var t in _voicesExtra.TextInputs)
            if (t.Focused) { t.HandleKey(key); return; }
    }

    private void DispatchPasteToFocused(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (_showApiKeyPanel)
        {
            var fi = _apiKeyPanel.GetFocusedInput();
            if (fi is not null) { fi.HandlePaste(text); return; }
        }
        if (_showCustomTts)
        {
            if (_customTtsPanel.FocusedMultiline is { } mlt) { mlt.HandlePaste(text); return; }
            if (_customTtsPanel.FocusedTextInput is { } fi)  { fi.HandlePaste(text);  return; }
            return;
        }
        if (_showCustomPrompt)
        {
            _customPromptPanel.FocusedMultiline?.HandlePaste(text);
            return;
        }
        foreach (var t in _voicesExtra.TextInputs)
            if (t.Focused) { t.HandlePaste(text); return; }
    }

    private static Color? ParseColor(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var v = s.Trim();
        if (v.StartsWith("#")) v = v[1..];
        if (v.Length == 6)
        {
            if (int.TryParse(v[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
                && int.TryParse(v[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
                && int.TryParse(v[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                return new Color(r, g, b);
        }
        else if (v.Length == 8)
        {
            if (int.TryParse(v[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
                && int.TryParse(v[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
                && int.TryParse(v[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)
                && int.TryParse(v[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a))
                return new Color(r, g, b, a);
        }
        return null;
    }

    private void UpdatePanelDragResize(MouseState m, MouseState prevM)
    {
        var mouseP = new Point(m.X, m.Y);

        // Release
        if (m.LeftButton == ButtonState.Released && prevM.LeftButton == ButtonState.Pressed)
        {
            if (_activeDragPanel != DragPanel.None) SaveLayout();
            _activeDragPanel = DragPanel.None;
            _panelResizing = false;
            return;
        }

        // Continue active drag/resize
        if (_activeDragPanel != DragPanel.None && m.LeftButton == ButtonState.Pressed)
        {
            int dx = m.X - _panelDragStartMouse.X;
            int dy = m.Y - _panelDragStartMouse.Y;
            Rectangle next;
            if (_panelResizing)
            {
                next = new Rectangle(
                    _panelDragStartRect.X, _panelDragStartRect.Y,
                    Math.Max(150, _panelDragStartRect.Width  + dx),
                    Math.Max(80,  _panelDragStartRect.Height + dy));
            }
            else
            {
                next = new Rectangle(
                    _panelDragStartRect.X + dx, _panelDragStartRect.Y + dy,
                    _panelDragStartRect.Width, _panelDragStartRect.Height);
            }
            SetPanelRect(_activeDragPanel, next);
            return;
        }

        // Start drag/resize — only if no avatar drag is active
        if (m.LeftButton != ButtonState.Pressed || prevM.LeftButton != ButtonState.Released) return;
        if (_avatarDragging || _avatarResizing) return;

        // In compact mode only the message box is interactive
        DragPanel[] order = _compactMode
            ? new[] { DragPanel.Messages }
            : new[] { DragPanel.VoicesExtra, DragPanel.Sessions, DragPanel.Voice, DragPanel.Messages };
        foreach (var panel in order)
        {
            // Collapsed panels respond only to the collapse button click (handled before this method)
            if (panel == DragPanel.Voice       && _voiceCollapsed)      continue;
            if (panel == DragPanel.Sessions    && _sessionsCollapsed)   continue;
            if (panel == DragPanel.VoicesExtra && _voicesExtraCollapsed) continue;

            var rect = GetPanelRect(panel);
            bool nearCorner = mouseP.X >= rect.Right  - 20 && mouseP.Y >= rect.Bottom - 20
                           && mouseP.X <  rect.Right  + 5  && mouseP.Y <  rect.Bottom + 5;
            bool inTitleBar = !nearCorner && rect.Contains(mouseP) && mouseP.Y < rect.Y + 18;
            if (!nearCorner && !inTitleBar) continue;
            _activeDragPanel    = panel;
            _panelResizing      = nearCorner;
            _panelDragStartMouse = mouseP;
            _panelDragStartRect  = rect;
            return;
        }
    }

    private Rectangle GetPanelRect(DragPanel panel) => panel switch
    {
        DragPanel.Voice       => _voiceRect,
        DragPanel.Sessions    => _sessionsRect,
        DragPanel.VoicesExtra => _voicesExtraRect,
        DragPanel.Messages    => _messagesRect,
        _                     => Rectangle.Empty,
    };

    private void SetPanelRect(DragPanel panel, Rectangle rect)
    {
        switch (panel)
        {
            case DragPanel.Voice:       _voiceRect       = rect; break;
            case DragPanel.Sessions:    _sessionsRect    = rect; break;
            case DragPanel.VoicesExtra: _voicesExtraRect = rect; break;
            case DragPanel.Messages:    _messagesRect    = rect; break;
        }
    }

    private void DrawResizeHandle(Rectangle rect)
    {
        var t = ActiveTint;
        _batch.Draw(_pixel, new Rectangle(rect.Right - 8, rect.Bottom - 8, 6, 6),
            new Color((int)t.R, (int)t.G, (int)t.B, 160));
    }

    // Returns the clickable collapse/expand button rect for each panel.
    // Collapsed: the entire thin strip. Expanded: a small tab at the panel edge.
    private Rectangle VoiceCollapseBtn()
    {
        if (_voiceCollapsed) return new Rectangle(_voiceRect.X, _voiceRect.Y, CollapsedW, _voiceRect.Height);
        int tabH = Math.Clamp(_voiceRect.Height / 3, 30, 60);
        return new Rectangle(_voiceRect.Right - CollapsedW, _voiceRect.Y + (_voiceRect.Height - tabH) / 2, CollapsedW, tabH);
    }
    private Rectangle SessionsCollapseBtn()
    {
        if (_sessionsCollapsed) return new Rectangle(_sessionsRect.Right - CollapsedW, _sessionsRect.Y, CollapsedW, _sessionsRect.Height);
        int tabH = Math.Clamp(_sessionsRect.Height / 3, 30, 60);
        return new Rectangle(_sessionsRect.X, _sessionsRect.Y + (_sessionsRect.Height - tabH) / 2, CollapsedW, tabH);
    }
    private Rectangle VoicesExtraCollapseBtn()
    {
        if (_voicesExtraCollapsed) return new Rectangle(_voicesExtraRect.Right - CollapsedW, _voicesExtraRect.Y, CollapsedW, _voicesExtraRect.Height);
        int tabH = Math.Clamp(_voicesExtraRect.Height / 3, 30, 60);
        return new Rectangle(_voicesExtraRect.X, _voicesExtraRect.Y + (_voicesExtraRect.Height - tabH) / 2, CollapsedW, tabH);
    }

    // Draws the collapse/expand tab background + border + chevron arrow.
    // pointRight=true → ► (expand/right), false → ◄ (collapse/left).
    private void DrawCollapseBtn(Rectangle r, bool pointRight)
    {
        var tint = ActiveTint;
        _batch.Draw(_pixel, r, new Color(0, 24, 40, 220));
        _batch.Draw(_pixel, new Rectangle(r.X, r.Y,          r.Width, 1), tint);
        _batch.Draw(_pixel, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), tint);
        _batch.Draw(_pixel, new Rectangle(r.X, r.Y, 1,          r.Height), tint);
        _batch.Draw(_pixel, new Rectangle(r.Right - 1, r.Y, 1, r.Height), tint);
        DrawChevron(r.X + r.Width / 2 - 2, r.Y + r.Height / 2 - 4, pointRight, tint);
    }

    // Draws a 4×8 pixel chevron. pointRight=true: ►, false: ◄.
    private void DrawChevron(int cx, int cy, bool pointRight, Color color)
    {
        for (int i = 0; i < 4; i++)
        {
            int col  = pointRight ? i : (3 - i);
            int rowH = 8 - i * 2;
            _batch.Draw(_pixel, new Rectangle(cx + col, cy + i, 1, rowH), color);
        }
    }

    private void SaveLayout()
    {
        _project.Voice       = PanelLayout.From(_voiceRect);
        _project.Sessions    = PanelLayout.From(_sessionsRect);
        _project.VoicesExtra = PanelLayout.From(_voicesExtraRect);
        _project.Messages    = PanelLayout.From(_messagesRect);
        _project.AvatarOffsetX = _avatarOffsetX;
        _project.AvatarOffsetY = _avatarOffsetY;
        _project.AvatarSize    = _avatarSizeOverride ?? 0;
        SaveProjectConfig();
    }

    private void OnWindowExiting(object? sender, EventArgs args)
    {
        _project.WindowWidth  = GraphicsDevice.Viewport.Width;
        _project.WindowHeight = GraphicsDevice.Viewport.Height;
        SaveLayout();
    }

    private void ApplyAccentColor()
    {
        _voicePanel.AccentColor     = ActiveTint;
        _voicesExtra.AccentColor    = ActiveTint;
        _sessionsDropdown.AccentColor = ActiveTint;
        _clearSessionsBtn.AccentColor = ActiveTint;
        _messageView.AccentColor    = ActiveTint;
        _apiKeyPanel.AccentColor    = ActiveTint;
    }

    private void CycleColor()
    {
        _project.ColorIndex = (_project.ColorIndex + 1) % Palette.Length;
        ApplyAccentColor();
        _ui.StatusLine = $"color: {Palette[_project.ColorIndex].Name}";
        SaveLayout();
    }

    private void ResetLayout()
    {
        var vp = GraphicsDevice.Viewport.Bounds;
        _voiceRect       = new Rectangle(10, 140, 290, 270);
        _sessionsRect    = new Rectangle(vp.Width - 300, 140, 290, 110);
        _voicesExtraRect = new Rectangle(vp.Width - 290, 270, 270, 165);
        _messagesRect    = new Rectangle(40, vp.Height - 220, vp.Width - 80, 180);
        _avatarOffsetX   = -56;
        _avatarOffsetY   = 66;
        _avatarSizeOverride = null;
        SaveLayout();
        _ui.StatusLine = "layout reset";
    }

    private void PlayTemplateSound(string key)
    {
        var sfx = _activeTemplate?.Manifest.SoundEffects;
        if (sfx is null || !sfx.Enabled) return;
        if (_activeTemplate?.FolderPath is not { } folder) return;
        string? file = null;
        if (!sfx.EventSounds.TryGetValue(key, out file))
            sfx.EmotionSounds.TryGetValue(key, out file);
        if (string.IsNullOrEmpty(file)) return;
        Audio.SoundEffectPlayer.Play(Path.Combine(folder, file));
    }

    private string KeyForProvider(AiProvider p) => p switch
    {
        AiProvider.Gemini   => _settings.GeminiApiKey   ?? "",
        AiProvider.OpenAi   => _settings.OpenAiApiKey   ?? "",
        AiProvider.Claude   => _settings.ClaudeApiKey   ?? "",
        AiProvider.DeepSeek => _settings.DeepSeekApiKey ?? "",
        _                   => "",
    };

    private static string Short(string s) => s.Length <= 8 ? s : s[..8];

    private static List<TOut> Map<TIn, TOut>(IEnumerable<TIn> src, Func<TIn, TOut> f)
    {
        var r = new List<TOut>();
        foreach (var x in src) r.Add(f(x));
        return r;
    }
}
