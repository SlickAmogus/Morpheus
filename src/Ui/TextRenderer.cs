using System;
using System.IO;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Morpheus.Ui;

// Loads a TTF once and exposes sized SpriteFontBases for drawing text.
// Tries bundled `assets/fonts/morpheus.ttf` first, then falls back to a system font on Windows.
public sealed class TextRenderer : IDisposable
{
    private readonly FontSystem? _system;

    public bool Available => _system is not null;

    public TextRenderer(GraphicsDevice device)
    {
        var ttf = LocateFont();
        if (ttf is null) return;
        _system = new FontSystem();
        using var fs = File.OpenRead(ttf);
        _system.AddFont(fs);
    }

    public SpriteFontBase? Get(int size) => _system?.GetFont(size);

    public void DrawString(SpriteBatch batch, string text, Vector2 position, Color color, int size = 18)
    {
        var font = Get(size);
        font?.DrawText(batch, text, position, color);
    }

    public Vector2 Measure(string text, int size = 18)
    {
        var font = Get(size);
        return font?.MeasureString(text) ?? Vector2.Zero;
    }

    private static string? LocateFont()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "assets", "fonts", "morpheus.ttf");
        if (File.Exists(local)) return local;

        if (OperatingSystem.IsWindows())
        {
            var sys = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            foreach (var name in new[] { "consola.ttf", "CascadiaMono.ttf", "segoeui.ttf", "arial.ttf" })
            {
                var p = Path.Combine(sys, name);
                if (File.Exists(p)) return p;
            }
        }
        return null;
    }

    public void Dispose() => _system?.Dispose();
}
