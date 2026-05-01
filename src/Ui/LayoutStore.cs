using System.IO;
using System.Text.Json;
using Microsoft.Xna.Framework;

namespace Morpheus.Ui;

public sealed class PanelLayout
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public Rectangle ToRect() => new(X, Y, Width, Height);

    public static PanelLayout From(Rectangle r) =>
        new() { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height };
}

public sealed class LayoutConfig
{
    public PanelLayout Voice       { get; set; } = new() { X = 10,  Y = 140, Width = 290, Height = 270 };
    public PanelLayout Sessions    { get; set; } = new() { X = 724, Y = 140, Width = 290, Height = 110 };
    public PanelLayout VoicesExtra { get; set; } = new() { X = 734, Y = 270, Width = 270, Height = 165 };
    public PanelLayout Messages    { get; set; } = new() { X = 40,  Y = 500, Width = 944, Height = 180 };
    public int AvatarOffsetX { get; set; } = -56;
    public int AvatarOffsetY { get; set; } = 66;
    public int AvatarSize    { get; set; } = 0;
    public int WindowWidth   { get; set; } = 1024;
    public int WindowHeight  { get; set; } = 720;
    public int ColorIndex    { get; set; } = 0;
}

public static class LayoutStore
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static LayoutConfig Load(string path)
    {
        if (!File.Exists(path)) return new LayoutConfig();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<LayoutConfig>(json, _opts) ?? new LayoutConfig();
        }
        catch { return new LayoutConfig(); }
    }

    public static void Save(string path, LayoutConfig config)
    {
        try { File.WriteAllText(path, JsonSerializer.Serialize(config, _opts)); }
        catch { }
    }
}
