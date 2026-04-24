using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Morpheus.Sessions;

public sealed class SessionFileEntry
{
    public required string Id { get; init; }
    public required string Path { get; init; }
    public DateTime StartedAt { get; init; }
    public int PageCount { get; init; }
}

// Sessions live next to morpheus.exe under `sessions/morpheus_<timestamp>.json`.
// Each morpheus run creates one new file; pages are appended/updated in place
// as assistant turns stream in.
public static class SessionStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };

    public static string Folder
    {
        get
        {
            var p = Path.Combine(AppContext.BaseDirectory, "sessions");
            Directory.CreateDirectory(p);
            return p;
        }
    }

    public static SessionLog CreateNew()
    {
        var now = DateTime.UtcNow;
        var id = now.ToString("yyyyMMddTHHmmssZ");
        return new SessionLog
        {
            Id = id,
            StartedAt = now,
            Path = Path.Combine(Folder, $"morpheus_{id}.json"),
        };
    }

    public static void Save(SessionLog log)
    {
        if (string.IsNullOrEmpty(log.Path)) return;
        try
        {
            File.WriteAllText(log.Path, JsonSerializer.Serialize(log, JsonOpts));
        }
        catch { }
    }

    public static IReadOnlyList<SessionFileEntry> Discover()
    {
        var folder = Folder;
        var results = new List<SessionFileEntry>();
        if (!Directory.Exists(folder)) return results;

        foreach (var f in Directory.EnumerateFiles(folder, "morpheus_*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(f);
            var id = name.StartsWith("morpheus_", StringComparison.Ordinal)
                ? name["morpheus_".Length..] : name;
            var started = File.GetCreationTimeUtc(f);
            int pageCount = 0;
            try
            {
                using var fs = File.OpenRead(f);
                using var doc = JsonDocument.Parse(fs);
                if (doc.RootElement.TryGetProperty("startedAt", out var s)
                    && s.ValueKind == JsonValueKind.String
                    && DateTime.TryParse(s.GetString(), out var parsed))
                    started = parsed;
                if (doc.RootElement.TryGetProperty("pages", out var p)
                    && p.ValueKind == JsonValueKind.Array)
                    pageCount = p.GetArrayLength();
            }
            catch { }
            results.Add(new SessionFileEntry
            {
                Id = id,
                Path = f,
                StartedAt = started,
                PageCount = pageCount,
            });
        }
        results.Sort((a, b) => b.StartedAt.CompareTo(a.StartedAt));
        return results;
    }

    public static SessionLog? Load(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var log = JsonSerializer.Deserialize<SessionLog>(json, JsonOpts);
            if (log is null) return null;
            log.Path = path;
            return log;
        }
        catch { return null; }
    }

    // Deletes every session file except the optional `exceptPath` (typically the live one).
    public static int ClearAll(string? exceptPath)
    {
        int deleted = 0;
        foreach (var f in Directory.EnumerateFiles(Folder, "morpheus_*.json"))
        {
            if (exceptPath is not null && string.Equals(f, exceptPath, StringComparison.OrdinalIgnoreCase))
                continue;
            try { File.Delete(f); deleted++; } catch { }
        }
        return deleted;
    }
}
