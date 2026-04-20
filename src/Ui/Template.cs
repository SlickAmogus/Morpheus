using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Morpheus.Ui;

public sealed class Insets
{
    public int Top { get; set; }
    public int Left { get; set; }
    public int Right { get; set; }
    public int Bottom { get; set; }
}

public sealed class TemplateManifest
{
    public string Name { get; set; } = "";
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? AvatarFrame { get; set; }
    public string? MessageFrame { get; set; }
    public string? BackgroundColor { get; set; }
    public Insets? AvatarInsets { get; set; }
    public Insets? MessageInsets { get; set; }
    public int TextSize { get; set; } = 16;
    public int LineHeight { get; set; } = 20;
}

public sealed class TemplateEntry
{
    public required string FolderName { get; init; }
    public required string FolderPath { get; init; }
    public required TemplateManifest Manifest { get; init; }
}

public static class TemplateLoader
{
    public static IReadOnlyList<TemplateEntry> Discover(string templatesRoot)
    {
        if (!Directory.Exists(templatesRoot)) return [];
        var results = new List<TemplateEntry>();
        foreach (var dir in Directory.EnumerateDirectories(templatesRoot))
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;
            try
            {
                var json = File.ReadAllText(manifestPath);
                var m = JsonSerializer.Deserialize<TemplateManifest>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                });
                if (m is null) continue;
                results.Add(new TemplateEntry
                {
                    FolderName = Path.GetFileName(dir),
                    FolderPath = dir,
                    Manifest = m,
                });
            }
            catch { }
        }
        return results;
    }
}
