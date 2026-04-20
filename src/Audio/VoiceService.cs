using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Morpheus.Audio;

public sealed record VoiceInfo(string VoiceId, string Name, string? Category, string? Description);

public static class VoiceService
{
    public static async Task<List<VoiceInfo>> FetchAsync(string apiKey, CancellationToken ct = default)
    {
        var results = new List<VoiceInfo>();
        if (string.IsNullOrWhiteSpace(apiKey)) return results;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.elevenlabs.io/v1/voices");
        req.Headers.Add("xi-api-key", apiKey);

        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return results;

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("voices", out var voices)) return results;

        foreach (var v in voices.EnumerateArray())
        {
            string? id = v.TryGetProperty("voice_id", out var i) ? i.GetString() : null;
            string? name = v.TryGetProperty("name", out var n) ? n.GetString() : null;
            string? cat = v.TryGetProperty("category", out var c) ? c.GetString() : null;
            string? desc = v.TryGetProperty("description", out var d) ? d.GetString() : null;
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) continue;
            results.Add(new VoiceInfo(id!, name!, cat, desc));
        }
        results.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return results;
    }
}
