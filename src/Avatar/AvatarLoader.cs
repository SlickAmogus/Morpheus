using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace Morpheus.Avatar;

public sealed class AvatarEntry
{
    public required string FolderName { get; init; }
    public required string FolderPath { get; init; }
    public required AvatarManifest Manifest { get; init; }
}

public static class AvatarLoader
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyList<AvatarEntry> Discover(string avatarsRoot)
    {
        if (!Directory.Exists(avatarsRoot)) return [];

        ExtractPendingZips(avatarsRoot);

        var results = new List<AvatarEntry>();
        foreach (var dir in Directory.EnumerateDirectories(avatarsRoot))
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;
            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<AvatarManifest>(json, JsonOpts);
                if (manifest is null) continue;
                results.Add(new AvatarEntry
                {
                    FolderName = Path.GetFileName(dir),
                    FolderPath = dir,
                    Manifest = manifest,
                });
            }
            catch { /* skip malformed */ }
        }
        return results;
    }

    private static void ExtractPendingZips(string avatarsRoot)
    {
        foreach (var zip in Directory.EnumerateFiles(avatarsRoot, "*.zip"))
        {
            var name = Path.GetFileNameWithoutExtension(zip);
            var target = Path.Combine(avatarsRoot, name);
            if (Directory.Exists(target)) continue;
            try
            {
                ZipFile.ExtractToDirectory(zip, target);
            }
            catch { /* leave zip in place on failure */ }
        }
    }
}
