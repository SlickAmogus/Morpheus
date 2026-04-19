using System.IO;
using System.Text.Json;

namespace Morpheus.Hooks;

// Claude Code writes session history to a JSONL file, one message per line.
// Assistant messages look roughly like:
//   {"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"..."}]}}
// We walk from EOF backward and return the last assistant text we find.
public static class TranscriptReader
{
    public static string? ReadLastAssistantText(string transcriptPath)
    {
        if (!File.Exists(transcriptPath)) return null;

        string[] lines;
        try
        {
            lines = File.ReadAllLines(transcriptPath);
        }
        catch
        {
            return null;
        }

        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var tProp)) continue;
                if (tProp.GetString() != "assistant") continue;
                if (!root.TryGetProperty("message", out var msg)) continue;
                if (!msg.TryGetProperty("content", out var content)) continue;

                if (content.ValueKind == JsonValueKind.String)
                    return content.GetString();

                if (content.ValueKind == JsonValueKind.Array)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var part in content.EnumerateArray())
                    {
                        if (part.TryGetProperty("type", out var pt) && pt.GetString() == "text"
                            && part.TryGetProperty("text", out var txt)
                            && txt.ValueKind == JsonValueKind.String)
                        {
                            if (sb.Length > 0) sb.Append('\n');
                            sb.Append(txt.GetString());
                        }
                    }
                    if (sb.Length > 0) return sb.ToString();
                }
            }
            catch
            {
                // malformed line — keep walking backward
            }
        }
        return null;
    }
}
