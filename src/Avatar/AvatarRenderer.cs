using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Morpheus.Avatar;

public sealed class AvatarState
{
    public string Emotion { get; set; } = "idle";
    public string? ActiveTool { get; set; }
    public bool MouthOpen { get; set; }
    // Optional one-shot override (e.g., a random idle WebP clip). When set,
    // the renderer plays this file's frames instead of resolving from emotion.
    public string? OverrideSprite { get; set; }
}

public sealed class AvatarRenderer : IDisposable
{
    // Cached frame sets keyed by manifest-relative base path.
    // A "frame set" is one entry for static sprites, multiple for `_0/_1/...`
    // sequences, or every frame of an animated WebP.
    private readonly Dictionary<string, Texture2D[]> _frames = new(StringComparer.OrdinalIgnoreCase);
    // Per-frame duration in seconds (only set for WebP — PNG sequences use manifest FPS).
    private readonly Dictionary<string, double[]> _frameDurations = new(StringComparer.OrdinalIgnoreCase);
    // Cached total duration per WebP (sum of _frameDurations[key]).
    private readonly Dictionary<string, double> _frameDurationTotals = new(StringComparer.OrdinalIgnoreCase);
    private AvatarEntry? _entry;
    private GraphicsDevice? _device;

    private string? _activeIntentBase;
    private double _animElapsed;

    // Variant tracking — re-rolls when emotion key changes OR when caller flips _variantRerollPending
    // (e.g., on a new dialog turn).
    private string? _activeVariantKey;
    private int _variantIdx;
    private bool _variantRerollPending = true;
    private readonly Random _variantRng = new();

    // Blink overlay: when a `<base>_blink.png` (or `_blink_N.png`) sibling exists
    // for the current state's sprite, fire a brief blink at a random interval.
    private string? _blinkBase;
    private double _blinkElapsed;
    private double _blinkUntil = -1;
    private double _nextBlinkAt;
    private double _nowSeconds;
    private readonly Random _blinkRng = new();

    public AvatarEntry? Current => _entry;

    public void LoadAvatar(AvatarEntry entry, GraphicsDevice device)
    {
        Clear();
        _entry = entry;
        _device = device;
        _nextBlinkAt = 2.0;
    }

    public void Update(double deltaSeconds)
    {
        _animElapsed += deltaSeconds;
        _blinkElapsed += deltaSeconds;
        _nowSeconds += deltaSeconds;
        TickBlink();
    }

    private void TickBlink()
    {
        var m = _entry?.Manifest;
        if (m is null) return;

        if (_blinkBase is not null && _nowSeconds >= _blinkUntil)
        {
            _blinkBase = null;
        }

        if (_blinkBase is null && _nowSeconds >= _nextBlinkAt && _activeIntentBase is not null)
        {
            var candidate = DeriveBlinkPath(_activeIntentBase);
            if (candidate is not null && LoadFrames(candidate) is { Length: > 0 })
            {
                _blinkBase = candidate;
                _blinkElapsed = 0;
                _blinkUntil = _nowSeconds + Math.Max(0.05, m.BlinkDurationSeconds);
            }
            ScheduleNextBlink(m);
        }
    }

    private void ScheduleNextBlink(AvatarManifest m)
    {
        double lo = Math.Max(0.5, m.BlinkMinSeconds);
        double hi = Math.Max(lo, m.BlinkMaxSeconds);
        _nextBlinkAt = _nowSeconds + lo + _blinkRng.NextDouble() * (hi - lo);
    }

    public void RerollVariant() => _variantRerollPending = true;

    private MouthPair PickVariant(string emotionKey, MouthPair pair)
    {
        var variants = pair.Variants;
        if (variants is null || variants.Count == 0) return pair;
        if (_variantRerollPending || !string.Equals(emotionKey, _activeVariantKey, StringComparison.Ordinal))
        {
            _activeVariantKey = emotionKey;
            // 0 = base pair; 1..N = variants[i-1].
            _variantIdx = _variantRng.Next(0, variants.Count + 1);
            _variantRerollPending = false;
        }
        return _variantIdx == 0 ? pair : variants[_variantIdx - 1];
    }

    private static string? DeriveBlinkPath(string basePath)
    {
        var dir = Path.GetDirectoryName(basePath) ?? "";
        var stem = Path.GetFileNameWithoutExtension(basePath);
        var ext = Path.GetExtension(basePath);
        if (string.IsNullOrEmpty(stem)) return null;
        if (string.IsNullOrEmpty(ext)) ext = ".png";
        return Path.Combine(dir, $"{stem}_blink{ext}");
    }

    public void Draw(SpriteBatch batch, Rectangle bounds, AvatarState state)
    {
        var intentBase = ResolveBasePath(state);
        if (intentBase is null) return;

        if (!string.Equals(intentBase, _activeIntentBase, StringComparison.OrdinalIgnoreCase))
        {
            _activeIntentBase = intentBase;
            _animElapsed = 0;
            _blinkBase = null; // cancel any in-flight blink from the old state
        }

        var drawBase = _blinkBase ?? intentBase;
        var frames = LoadFrames(drawBase);
        if (frames is null || frames.Length == 0) return;

        int idx = 0;
        if (frames.Length > 1)
        {
            double clock = _blinkBase is not null ? _blinkElapsed : _animElapsed;
            // If this is a WebP with per-frame timings, use them; else manifest FPS.
            if (_frameDurations.TryGetValue(drawBase, out var durations) && durations.Length == frames.Length
                && _frameDurationTotals.TryGetValue(drawBase, out var total) && total > 0)
            {
                double t = clock % total;
                double acc = 0;
                for (int i = 0; i < durations.Length; i++)
                {
                    acc += durations[i];
                    if (t < acc) { idx = i; break; }
                }
            }
            else
            {
                int fps = Math.Max(1, _entry?.Manifest.Fps ?? 8);
                idx = (int)(clock * fps) % frames.Length;
                if (idx < 0) idx = 0;
            }
        }
        var crop = _entry?.Manifest.Crop;
        if (crop is not null && (crop.Horizontal > 0 || crop.Vertical > 0))
        {
            var tex = frames[idx];
            int sx = Math.Clamp(crop.Horizontal, 0, Math.Max(0, tex.Width / 2 - 1));
            int sy = Math.Clamp(crop.Vertical,   0, Math.Max(0, tex.Height / 2 - 1));
            var src = new Rectangle(sx, sy, tex.Width - sx * 2, tex.Height - sy * 2);
            batch.Draw(tex, bounds, src, Color.White);
        }
        else
        {
            batch.Draw(frames[idx], bounds, Color.White);
        }
    }

    private string? ResolveBasePath(AvatarState state)
    {
        if (_entry is null) return null;
        var s = _entry.Manifest.Sprites;

        // Override wins over everything (used for random idle clips, etc.).
        if (state.OverrideSprite is { Length: > 0 } over) return over;

        if (state.ActiveTool is { Length: > 0 } tool
            && s.Tools.TryGetValue(tool, out var toolFile)
            && !string.IsNullOrWhiteSpace(toolFile))
        {
            return toolFile;
        }

        if (!string.IsNullOrEmpty(state.Emotion)
            && s.Emotions.TryGetValue(state.Emotion, out var pair))
        {
            var picked = PickVariant(state.Emotion, pair);
            var f = state.MouthOpen
                ? (picked.Open ?? pair.Open ?? picked.Closed ?? pair.Closed)
                : (picked.Closed ?? pair.Closed ?? picked.Open ?? pair.Open);
            if (!string.IsNullOrWhiteSpace(f)) return f;
        }

        var idlePicked = PickVariant("__idle", s.Idle);
        var idleFile = state.MouthOpen
            ? (idlePicked.Open ?? s.Idle.Open ?? idlePicked.Closed ?? s.Idle.Closed)
            : (idlePicked.Closed ?? s.Idle.Closed ?? idlePicked.Open ?? s.Idle.Open);
        if (!string.IsNullOrWhiteSpace(idleFile)) return idleFile;

        return s.Generic;
    }

    private Texture2D[]? LoadFrames(string baseRel)
    {
        if (_entry is null || _device is null) return null;
        if (_frames.TryGetValue(baseRel, out var cached)) return cached;

        var fullBase = Path.Combine(_entry.FolderPath, baseRel);
        var ext = Path.GetExtension(baseRel);
        if (string.IsNullOrEmpty(ext)) ext = ".png";

        // Animated WebP: every frame inside the file is one Texture2D.
        // Honors per-frame duration from the WebP itself.
        if (string.Equals(ext, ".webp", StringComparison.OrdinalIgnoreCase) && File.Exists(fullBase))
        {
            try
            {
                var webpFrames = WebpLoader.LoadFrames(_device, fullBase);
                _frames[baseRel] = webpFrames;
                if (webpFrames.Length > 1)
                {
                    var durs = WebpLoader.LoadFrameDurations(fullBase);
                    if (durs.Length == webpFrames.Length)
                    {
                        _frameDurations[baseRel] = durs;
                        double total = 0;
                        foreach (var d in durs) total += d;
                        _frameDurationTotals[baseRel] = total;
                    }
                }
                return webpFrames;
            }
            catch
            {
                _frames[baseRel] = Array.Empty<Texture2D>();
                return _frames[baseRel];
            }
        }

        // PNG / JPG path: discover `<stem>_<N>.<ext>` siblings; fallback to single image.
        var dir = Path.GetDirectoryName(fullBase) ?? _entry.FolderPath;
        var stem = Path.GetFileNameWithoutExtension(baseRel);

        var indexed = new List<(int Index, string Path)>();
        if (Directory.Exists(dir))
        {
            foreach (var path in Directory.EnumerateFiles(dir, $"{stem}_*{ext}"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (name.Length <= stem.Length + 1) continue;
                var suffix = name.Substring(stem.Length + 1);
                if (int.TryParse(suffix, out var n)) indexed.Add((n, path));
            }
            indexed.Sort((a, b) => a.Index.CompareTo(b.Index));
        }

        var sources = new List<string>();
        foreach (var (_, p) in indexed) sources.Add(p);
        if (sources.Count == 0 && File.Exists(fullBase)) sources.Add(fullBase);

        var loaded = new List<Texture2D>(sources.Count);
        foreach (var src in sources)
        {
            try
            {
                using var fs = File.OpenRead(src);
                loaded.Add(Texture2D.FromStream(_device, fs));
            }
            catch { }
        }

        var arr = loaded.ToArray();
        ApplyChromaKeyIfSet(arr);
        _frames[baseRel] = arr;
        return arr;
    }

    private void ApplyChromaKeyIfSet(Texture2D[] frames)
    {
        if (frames.Length == 0) return;
        if (!TryParseHexColor(_entry?.Manifest.TransparentColor, out var key)) return;
        foreach (var tex in frames) ApplyChromaKey(tex, key);
    }

    private static void ApplyChromaKey(Texture2D tex, Color key)
    {
        var px = new Color[tex.Width * tex.Height];
        tex.GetData(px);
        bool changed = false;
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            if (c.R == key.R && c.G == key.G && c.B == key.B)
            {
                px[i] = Color.Transparent;
                changed = true;
            }
        }
        if (changed) tex.SetData(px);
    }

    private static bool TryParseHexColor(string? hex, out Color color)
    {
        color = Color.Transparent;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var v = hex.Trim().TrimStart('#');
        if (v.Length != 6 && v.Length != 8) return false;
        if (!int.TryParse(v[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
         || !int.TryParse(v[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
         || !int.TryParse(v[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            return false;
        color = new Color(r, g, b);
        return true;
    }

    public void Clear()
    {
        foreach (var arr in _frames.Values)
            foreach (var t in arr) t?.Dispose();
        _frames.Clear();
        _frameDurations.Clear();
        _frameDurationTotals.Clear();
        _entry = null;
        _activeIntentBase = null;
        _activeVariantKey = null;
        _variantIdx = 0;
        _variantRerollPending = true;
        _animElapsed = 0;
        _blinkBase = null;
        _blinkElapsed = 0;
        _blinkUntil = -1;
        _nextBlinkAt = 0;
        _nowSeconds = 0;
    }

    public void Dispose() => Clear();
}
