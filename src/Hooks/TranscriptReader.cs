using System.IO;
using System.Text;
using System.Text.Json;

namespace Morpheus.Hooks;

// Reads a Claude Code session JSONL and returns the current turn's
// concatenated assistant text — i.e. every "text" part produced since
// the last real user input (skipping tool_result user entries).
// Deduping by the triggering user-turn uuid lets the game loop detect
// "new turn, same replay-key" without losing text that precedes tool_use.
public static class TranscriptReader
{
    public static ReadResult? ReadCurrentTurn(string transcriptPath)
    {
        if (!File.Exists(transcriptPath)) return null;

        string[] lines;
        try { lines = File.ReadAllLines(transcriptPath); }
        catch { return null; }

        int userLine = -1;
        string? userUuid = null;

        for (int i = lines.Length - 1; i >= 0; i--)
        {
            if (!TryGetRoleContent(lines[i], out var role, out var content, out var uuid)) continue;
            if (role != "user") continue;
            if (!IsRealUserInput(content)) continue;
            userLine = i;
            userUuid = uuid;
            break;
        }
        if (userLine < 0) return null;

        var sb = new StringBuilder();
        for (int i = userLine + 1; i < lines.Length; i++)
        {
            if (!TryGetRoleContent(lines[i], out var role, out var content, out _)) continue;
            if (role != "assistant") continue;
            var text = ExtractText(content);
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(text);
        }

        return new ReadResult(userUuid, sb.ToString());
    }

    // Back-compat shim (single last-text entry)
    public static string? ReadLastAssistantText(string transcriptPath)
        => ReadCurrentTurn(transcriptPath)?.Text;

    public static ReadResult? ReadLastAssistantMessage(string transcriptPath)
        => ReadCurrentTurn(transcriptPath);

    private static bool TryGetRoleContent(string line, out string? role, out JsonElement content, out string? uuid)
    {
        role = null;
        content = default;
        uuid = null;
        if (string.IsNullOrWhiteSpace(line)) return false;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("uuid", out var u)) uuid = u.GetString();
            if (!root.TryGetProperty("message", out var msg)) return false;
            if (!msg.TryGetProperty("role", out var r)) return false;
            role = r.GetString();
            if (!msg.TryGetProperty("content", out content)) return false;
            // Clone so content stays valid after doc disposes.
            content = content.Clone();
            return true;
        }
        catch { return false; }
    }

    private static bool IsRealUserInput(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String) return true;
        if (content.ValueKind != JsonValueKind.Array) return false;

        bool anyText = false;
        foreach (var p in content.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object) continue;
            if (!p.TryGetProperty("type", out var t)) continue;
            var ts = t.GetString();
            if (ts == "tool_result") return false;
            if (ts == "text") anyText = true;
        }
        return anyText;
    }

    private static string ExtractText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String) return content.GetString() ?? "";
        if (content.ValueKind != JsonValueKind.Array) return "";

        var sb = new StringBuilder();
        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Object) continue;
            if (!part.TryGetProperty("type", out var t) || t.GetString() != "text") continue;
            if (!part.TryGetProperty("text", out var txt) || txt.ValueKind != JsonValueKind.String) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(txt.GetString());
        }
        return sb.ToString();
    }
}

public sealed record ReadResult(string? Uuid, string Text);
