using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Morpheus.Hooks;

// Merges morpheus hook entries into a target project's .claude/settings.json.
// Uses http-typed hooks directly to morpheus's local HttpListener — no bridge exe.
// Morpheus-owned entries are tagged with a "statusMessage" containing MORPHEUS_TAG
// so uninstall can remove exactly what we wrote without touching user entries.
public static class HookInstaller
{
    public const string MorpheusTag = "__morpheus__";

    public static string SuggestSettingsPath(string projectDir)
        => Path.Combine(projectDir, ".claude", "settings.json");

    public static void InstallToProject(string projectDir, int port)
    {
        var path = SuggestSettingsPath(projectDir);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        JsonObject root = File.Exists(path)
            ? (JsonNode.Parse(File.ReadAllText(path)) as JsonObject) ?? new JsonObject()
            : new JsonObject();

        if (root["hooks"] is not JsonObject hooks)
        {
            hooks = new JsonObject();
            root["hooks"] = hooks;
        }

        AddHttpHook(hooks, "Stop",        $"http://127.0.0.1:{port}/speak");
        AddHttpHook(hooks, "PreToolUse",  $"http://127.0.0.1:{port}/tool-pre");
        AddHttpHook(hooks, "PostToolUse", $"http://127.0.0.1:{port}/tool-post");

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void UninstallFromProject(string projectDir)
    {
        var path = SuggestSettingsPath(projectDir);
        if (!File.Exists(path)) return;

        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        if (root?["hooks"] is not JsonObject hooks) return;

        foreach (var kind in new[] { "Stop", "PreToolUse", "PostToolUse" })
            StripMorpheus(hooks, kind);

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void AddHttpHook(JsonObject hooks, string eventName, string url)
    {
        if (hooks[eventName] is not JsonArray arr)
        {
            arr = new JsonArray();
            hooks[eventName] = arr;
        }

        // Remove any prior morpheus-tagged group for this event so we don't stack duplicates.
        for (int i = arr.Count - 1; i >= 0; i--)
        {
            if (arr[i] is JsonObject grp && IsMorpheusGroup(grp))
                arr.RemoveAt(i);
        }

        arr.Add(new JsonObject
        {
            ["matcher"] = "",
            ["hooks"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "http",
                    ["url"] = url,
                    ["statusMessage"] = MorpheusTag,
                    ["timeout"] = 5,
                },
            },
        });
    }

    private static void StripMorpheus(JsonObject hooks, string eventName)
    {
        if (hooks[eventName] is not JsonArray arr) return;
        for (int i = arr.Count - 1; i >= 0; i--)
        {
            if (arr[i] is JsonObject grp && IsMorpheusGroup(grp))
                arr.RemoveAt(i);
        }
        if (arr.Count == 0) hooks.Remove(eventName);
    }

    private static bool IsMorpheusGroup(JsonObject group)
    {
        if (group["hooks"] is not JsonArray inner) return false;
        foreach (var h in inner)
        {
            if (h is JsonObject o
                && o["statusMessage"] is JsonValue v
                && v.TryGetValue<string>(out var s)
                && s == MorpheusTag)
                return true;
        }
        return false;
    }
}
