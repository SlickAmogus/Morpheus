using System.IO;
using System.Text;
using System.Text.Json;

namespace Morpheus.Hooks;

// Claude Code writes session history to JSONL. One line per record.
// Assistant message records look like:
//   { "parentUuid":..., "isSidechain":false,
//     "message": { "role":"assistant", "content":[{"type":"text","text":"..."},...], ...},
//     "uuid":"...", "timestamp":"...", ... }
// Tool-use parts live alongside text parts inside content — we concatenate text parts only.
public static class TranscriptReader
{
    public static ReadResult? ReadLastAssistantMessage(string transcriptPath)
    {
        if (!File.Exists(transcriptPath)) return null;

        string[] lines;
        try { lines = File.ReadAllLines(transcriptPath); }
        catch { return null; }

        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (!root.TryGetProperty("message", out var msg)) continue;
                if (!msg.TryGetProperty("role", out var roleEl)) continue;
                if (roleEl.GetString() != "assistant") continue;
                if (!msg.TryGetProperty("content", out var content)) continue;

                var text = ExtractText(content);
                if (string.IsNullOrWhiteSpace(text)) continue;

                string? uuid = root.TryGetProperty("uuid", out var u) ? u.GetString() : null;
                return new ReadResult(uuid, text);
            }
            catch
            {
                // malformed / partially-written line — keep walking backward
            }
        }
        return null;
    }

    // Back-compat shim for older callers.
    public static string? ReadLastAssistantText(string transcriptPath)
        => ReadLastAssistantMessage(transcriptPath)?.Text;

    private static string ExtractText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? "";

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
