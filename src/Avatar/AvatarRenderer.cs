using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Morpheus.Avatar;

public sealed class AvatarState
{
    public string Emotion { get; set; } = "idle";
    public string? ActiveTool { get; set; }
    public bool MouthOpen { get; set; }
}

public sealed class AvatarRenderer : IDisposable
{
    private readonly Dictionary<string, Texture2D> _cache = new(StringComparer.OrdinalIgnoreCase);
    private AvatarEntry? _entry;
    private GraphicsDevice? _device;

    public AvatarEntry? Current => _entry;

    public void LoadAvatar(AvatarEntry entry, GraphicsDevice device)
    {
        Clear();
        _entry = entry;
        _device = device;
    }

    public void Draw(SpriteBatch batch, Rectangle bounds, AvatarState state)
    {
        var tex = Resolve(state);
        if (tex is null) return;
        batch.Draw(tex, bounds, Color.White);
    }

    private Texture2D? Resolve(AvatarState state)
    {
        if (_entry is null) return null;
        var s = _entry.Manifest.Sprites;

        if (state.ActiveTool is { Length: > 0 } tool
            && s.Tools.TryGetValue(tool, out var toolFile)
            && LoadSafe(toolFile) is { } toolTex)
        {
            return toolTex;
        }

        if (!string.IsNullOrEmpty(state.Emotion)
            && s.Emotions.TryGetValue(state.Emotion, out var pair))
        {
            var file = state.MouthOpen ? (pair.Open ?? pair.Closed) : (pair.Closed ?? pair.Open);
            if (LoadSafe(file) is { } emoTex) return emoTex;
        }

        var idleFile = state.MouthOpen ? (s.Idle.Open ?? s.Idle.Closed) : (s.Idle.Closed ?? s.Idle.Open);
        if (LoadSafe(idleFile) is { } idleTex) return idleTex;

        return LoadSafe(s.Generic);
    }

    private Texture2D? LoadSafe(string? relFile)
    {
        if (_entry is null || _device is null || string.IsNullOrWhiteSpace(relFile)) return null;
        if (_cache.TryGetValue(relFile, out var cached)) return cached;
        var path = Path.Combine(_entry.FolderPath, relFile);
        if (!File.Exists(path)) return null;
        try
        {
            using var fs = File.OpenRead(path);
            var tex = Texture2D.FromStream(_device, fs);
            _cache[relFile] = tex;
            return tex;
        }
        catch
        {
            return null;
        }
    }

    public void Clear()
    {
        foreach (var t in _cache.Values) t.Dispose();
        _cache.Clear();
        _entry = null;
    }

    public void Dispose() => Clear();
}
