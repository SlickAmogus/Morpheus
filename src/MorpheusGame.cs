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
    private readonly AudioPlayer _player = new();
    private readonly HookListener _listener = new();
    private readonly ConfigUi _ui = new();
    private MorpheusSettings _settings = new();
    private string _settingsPath = "";
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
    private readonly Queue<string> _speechQueue = new();
    private bool _speechActive;
    private static readonly System.Text.RegularExpressions.Regex EmotionTagRegex =
        new(@"^\s*\[emotion:\s*(\w+)\s*\]\s*",
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

        _sessionsDropdown.Changed += id => SelectSession(id);
        _clearSessionsBtn.Clicked += () =>
        {
            int n = SessionStore.ClearAll(_liveSession.Path);
            RefreshSessionsDropdown();
            _ui.StatusLine = $"cleared {n} past session(s)";
        };
        RefreshSessionsDropdown();
        _messageView.SetPages(_liveSession.Pages);

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
            _settings.CustomVoices.RemoveAll(v => string.Equals(v.VoiceId, id, StringComparison.OrdinalIgnoreCase));
            _settings.CustomVoices.Add(new CustomVoice { Name = name, VoiceId = id });
            _settings.ElevenLabsVoiceId = id;
            SaveSettings();
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
            _settings.CustomVoices.RemoveAll(v => string.Equals(v.VoiceId, id, StringComparison.OrdinalIgnoreCase));
            _settings.CustomVoices.Add(new CustomVoice { Name = $"shared: {name}", VoiceId = id });
            _settings.ElevenLabsVoiceId = id;
            SaveSettings();
            RebuildVoiceDropdown();
            _ui.StatusLine = $"using shared voice {name}";
        };

        Window.TextInput += OnWindowTextInput;

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
        foreach (var c in _settings.CustomVoices)
            merged.Add(new VoiceInfo(c.VoiceId, c.Name + "  [custom]", "custom", null));
        _voicePanel.PopulateVoices(merged, _settings.ElevenLabsVoiceId);
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
        // Dispatch printable chars to whichever input is focused; backspace/enter via key.
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

        var vp = GraphicsDevice.Viewport.Bounds;
        LayoutForFrame(vp);

        var k = Keyboard.GetState();
        bool inputFocused = AnyTextInputFocused();
        if (Pressed(k, Keys.Escape))
        {
            if (inputFocused) DispatchKeyToFocused(Keys.Escape);
            else Exit();
        }
        if (!inputFocused)
        {
            if (Pressed(k, Keys.F1)) InstallHooksForCwd();
            if (Pressed(k, Keys.F2)) { _ui.CycleAvatar(+1); ApplyAvatar(); SaveSettings(); }
            if (Pressed(k, Keys.F3)) { _ui.CycleTemplate(+1); ApplyTemplate(TemplateLoader.Discover(Path.Combine(AppContext.BaseDirectory, "templates"))); SaveSettings(); }
            if (Pressed(k, Keys.F5)) TestSpeak();
            if (Pressed(k, Keys.F6)) InstallActivePersonality();
            if (Pressed(k, Keys.F8)) { _compactMode = !_compactMode; _ui.StatusLine = _compactMode ? "compact mode ON" : "compact mode OFF"; }
        }
        else
        {
            if (Pressed(k, Keys.Back))  DispatchKeyToFocused(Keys.Back);
            if (Pressed(k, Keys.Enter)) DispatchKeyToFocused(Keys.Enter);
        }
        _prevKeys = k;

        var m = Mouse.GetState();
        UpdateAvatarDragResize(vp, m, _prevMouse);
        var input = new WidgetInput { Mouse = m, PrevMouse = _prevMouse };
        _voicePanel.Update(input);
        // Capture Open BEFORE Update — the dropdown sets Open=false the same
        // frame it consumes a row click, so checking after lets that click
        // fall through to widgets sitting under the open list.
        bool sessionsListWasOpen = _sessionsDropdown.Open;
        _sessionsDropdown.Update(input);
        bool sharedListWasOpen = _voicesExtra.SharedResults.Open;
        _voicesExtra.Update(input);
        if (!sessionsListWasOpen && !sharedListWasOpen)
        {
            _clearSessionsBtn.Update(input);
            _messageView.Update(m, _prevMouse);
        }
        _prevMouse = m;

        _avatarState.MouthOpen = _player.IsPlaying;

        TickIdleAnimations();

        base.Update(gameTime);
    }

    private void LayoutForFrame(Rectangle vp)
    {
        var tpl = _activeTemplate?.Manifest;
        int textSize = tpl?.TextSize > 0 ? tpl.TextSize : 16;
        int lineHeight = tpl?.LineHeight > 0 ? tpl.LineHeight : 20;
        var msgBox = new Rectangle(40, vp.Height - 220, vp.Width - 80, 180);
        _messageView.Layout(msgBox, tpl?.MessageInsets, textSize, lineHeight, 32, 6);

        var sz = _ui.SelectedAvatar?.Manifest.Size;
        int baseWidth = sz?.Width is > 0 ? sz.Width : 400;
        int baseHeight = sz?.Height is > 0 ? sz.Height : 400;
        double aspectRatio = (double)baseWidth / baseHeight;

        int aw = _avatarSizeOverride ?? baseWidth;
        int ax = vp.Width / 2 - aw / 2 + 60 + _avatarOffsetX;
        int avatarRight = ax + aw;

        // Position right panels at least 20px from avatar right edge, or at default position (vp.Width - 300)
        int rightEdgeMin = avatarRight + 20;
        int px = Math.Max(vp.Width - 300, rightEdgeMin);
        int panelWidth = Math.Max(200, vp.Width - px - 10);

        _sessionsDropdown.Bounds = new Rectangle(px + 10, 160, Math.Min(270, panelWidth - 20), 28);
        _clearSessionsBtn.Bounds = new Rectangle(px + 10, 198, Math.Min(130, panelWidth - 20), 26);

        // Extra voices panel sits below the sessions panel on the right.
        _voicesExtra.Layout(px + 10, 270, Math.Min(270, panelWidth - 20));
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
        _batch.Begin(blendState: BlendState.AlphaBlend, samplerState: SamplerState.LinearClamp);

        var vp = GraphicsDevice.Viewport.Bounds;
        var tpl = _activeTemplate?.Manifest;

        var sz = _ui.SelectedAvatar?.Manifest.Size;
        int baseWidth = sz?.Width is > 0 ? sz.Width : 400;
        int baseHeight = sz?.Height is > 0 ? sz.Height : 400;
        double aspectRatio = (double)baseWidth / baseHeight;

        int aw = _avatarSizeOverride ?? baseWidth;
        int ah = (int)(aw / aspectRatio);
        // Center horizontally with the same +60px nudge the original layout used to
        // clear the left voice sidebar; vertical anchor stays at y=80.
        int ax = vp.Width / 2 - aw / 2 + 60 + _avatarOffsetX;
        int ay = 80 + _avatarOffsetY;
        var frameBox = new Rectangle(ax, ay, aw, ah);
        var avatarBox = Inset(frameBox, tpl?.AvatarInsets);

        // Draw frame overlay before avatar so avatar renders on top
        if (_avatarFrame is not null) _batch.Draw(_avatarFrame, frameBox, Color.White);
        _avatarRenderer.Draw(_batch, avatarBox, _avatarState);

        var msgBox = new Rectangle(40, vp.Height - 220, vp.Width - 80, 180);
        if (_messageFrame is not null) _batch.Draw(_messageFrame, msgBox, Color.White);

        _messageView.ForwardTex = _uiForwardTex;
        _messageView.BackwardTex = _uiBackwardTex;
        _messageView.Draw(_batch, _text, _pixel);

        if (!_compactMode)
        {
            _ui.Draw(_batch, _text, vp);

            var voiceRect = new Rectangle(10, 140, 290, 300);
            _voicePanel.Draw(_batch, _text, _pixel, voiceRect);

            DrawSessionsPanel(vp);

            int px2 = vp.Width - 300;
            var voicesExtraRect = new Rectangle(px2, 260, 290, 200);
            _voicesExtra.Draw(_batch, _text, _pixel, voicesExtraRect);
        }

        _batch.End();
        base.Draw(gameTime);
    }

    private void DrawSessionsPanel(Rectangle vp)
    {
        int px = vp.Width - 300;
        var rect = new Rectangle(px, 140, 290, 110);
        _batch.Draw(_pixel, rect, new Color(0, 10, 20, 180));
        _batch.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), new Color(0, 200, 255));
        _batch.Draw(_pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), new Color(0, 200, 255));
        _batch.Draw(_pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), new Color(0, 200, 255));
        _batch.Draw(_pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), new Color(0, 200, 255));
        _text.DrawString(_batch, "sessions", new Vector2(rect.X + 8, rect.Y + 4),
            new Color(0, 220, 255), 14);

        _clearSessionsBtn.Draw(_batch, _text, _pixel);
        // Dropdown last so its open list renders above.
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
        _text.Dispose();
        _avatarFrame?.Dispose();
        _messageFrame?.Dispose();
        _uiForwardTex?.Dispose();
        _uiBackwardTex?.Dispose();
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
        }

        // Strip and process all emotion tags, both leading and mid-message
        var effectiveText = StripEmotionTags(fullText);

        if (effectiveText.Length <= _spokenLen && !newTurn) return;

        var chunk = effectiveText.Length > _spokenLen ? effectiveText[_spokenLen..] : string.Empty;
        _spokenLen = effectiveText.Length;

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

        if (!string.IsNullOrEmpty(chunk)) EnqueueSpeech(chunk);
    }

    private string StripEmotionTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var result = new System.Text.StringBuilder();
        var emotionRegex = new System.Text.RegularExpressions.Regex(@"\[emotion:\s*(\w+)\s*\]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        int lastPos = 0;
        foreach (System.Text.RegularExpressions.Match match in emotionRegex.Matches(text))
        {
            // Add text before the tag
            result.Append(text, lastPos, match.Index - lastPos);

            // Apply emotion change
            var emotion = match.Groups[1].Value.ToLowerInvariant();
            var availableEmotions = _ui.SelectedAvatar?.Manifest.Sprites.Emotions;
            if (emotion == "idle" || (availableEmotions is not null && availableEmotions.ContainsKey(emotion)))
            {
                _avatarState.Emotion = emotion;
            }

            lastPos = match.Index + match.Length;
        }

        // Add remaining text after last tag
        result.Append(text, lastPos, text.Length - lastPos);
        return result.ToString();
    }

    private void EnqueueSpeech(string text)
    {
        if (string.IsNullOrWhiteSpace(_settings.ElevenLabsApiKey)
            || string.IsNullOrWhiteSpace(_settings.ElevenLabsVoiceId))
            return;

        const int hardCap = 4980; // ElevenLabs per-request limit is 5000, leave 20 char buffer
        if (text.Length > hardCap)
        {
            // Try to break at a sentence boundary before the limit
            int breakPoint = hardCap;
            int lastPeriod = text.LastIndexOf('.', Math.Min(hardCap - 1, text.Length - 1));
            if (lastPeriod > hardCap * 0.75) breakPoint = lastPeriod + 1;

            var chunk = text[..breakPoint].TrimEnd();
            _speechQueue.Enqueue(chunk);

            // Queue remainder for next cycle if there's more
            var remainder = text[breakPoint..].TrimStart();
            if (!string.IsNullOrEmpty(remainder))
                _speechQueue.Enqueue(remainder);
        }
        else
        {
            _speechQueue.Enqueue(text);
        }

        if (!_speechActive) StartNextSpeech();
    }

    private void StartNextSpeech()
    {
        if (_speechQueue.Count == 0) { _speechActive = false; return; }
        var next = _speechQueue.Dequeue();
        _speechActive = true;
        _ = SynthesizeAndPlayAsync(next);
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
    }

    private void TestSpeak()
    {
        if (!string.IsNullOrWhiteSpace(_settings.ElevenLabsApiKey)
            && !string.IsNullOrWhiteSpace(_settings.ElevenLabsVoiceId))
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
        _settings.SelectedAvatar = av.FolderName;
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
        _avatarFrame = null;
        _messageFrame = null;
        _uiForwardTex = null;
        _uiBackwardTex = null;
        _activeTemplate = null;

        if (templates.Count == 0) return;
        var tpl = templates[_ui.TemplateIndex % templates.Count];
        _activeTemplate = tpl;
        _settings.SelectedTemplate = tpl.FolderName;
        _avatarFrame = LoadTex(Path.Combine(tpl.FolderPath, tpl.Manifest.AvatarFrame ?? ""));
        _messageFrame = LoadTex(Path.Combine(tpl.FolderPath, tpl.Manifest.MessageFrame ?? ""));
        _uiForwardTex = LoadTex(Path.Combine(tpl.FolderPath, "uiforward.png"));
        _uiBackwardTex = LoadTex(Path.Combine(tpl.FolderPath, "uibackward.png"));
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

    private bool AnyTextInputFocused()
    {
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
        foreach (var t in _voicesExtra.TextInputs)
            if (t.Focused) { t.HandleKey(key); return; }
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

    private static string Short(string s) => s.Length <= 8 ? s : s[..8];

    private static List<TOut> Map<TIn, TOut>(IEnumerable<TIn> src, Func<TIn, TOut> f)
    {
        var r = new List<TOut>();
        foreach (var x in src) r.Add(f(x));
        return r;
    }
}
