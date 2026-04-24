using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using XColor = Microsoft.Xna.Framework.Color;

namespace Morpheus.Avatar;

// Decodes a (possibly animated) WebP file into a sequence of MonoGame
// textures. Each WebP frame becomes one Texture2D so the existing renderer
// frame-cycling logic works the same way it does for `_N.png` sequences.
public static class WebpLoader
{
    public static Texture2D[] LoadFrames(GraphicsDevice device, string path)
    {
        using var image = Image.Load<Rgba32>(path);
        int w = image.Width;
        int h = image.Height;
        int count = image.Frames.Count;

        var result = new List<Texture2D>(count);
        var pixelBuf = new Rgba32[w * h];
        var colorBuf = new XColor[w * h];

        for (int f = 0; f < count; f++)
        {
            var frame = image.Frames[f];
            frame.CopyPixelDataTo(pixelBuf);
            for (int i = 0; i < pixelBuf.Length; i++)
            {
                var p = pixelBuf[i];
                // Convert white pixels to transparent (common artifact in WebP)
                if (p.R == 255 && p.G == 255 && p.B == 255)
                    colorBuf[i] = new XColor(0, 0, 0, 0);
                else
                    colorBuf[i] = new XColor(p.R, p.G, p.B, p.A);
            }
            var tex = new Texture2D(device, w, h);
            tex.SetData(colorBuf);
            result.Add(tex);
        }
        return result.ToArray();
    }

    // Returns per-frame durations in seconds for WebP (uses the file's own timing).
    // Empty array means: caller should fall back to manifest FPS.
    public static double[] LoadFrameDurations(string path)
    {
        try
        {
            using var image = Image.Load<Rgba32>(path);
            int count = image.Frames.Count;
            if (count <= 1) return Array.Empty<double>();
            var durations = new double[count];
            for (int i = 0; i < count; i++)
            {
                var meta = image.Frames[i].Metadata.GetWebpMetadata();
                // FrameDelay is in milliseconds (per spec).
                durations[i] = Math.Max(0.01, meta.FrameDelay / 1000.0);
            }
            return durations;
        }
        catch { return Array.Empty<double>(); }
    }
}
