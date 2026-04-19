using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Morpheus.Audio;

public sealed class ElevenLabsTts : ITtsClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private const string Model = "eleven_turbo_v2_5";

    public ElevenLabsTts(string apiKey, HttpClient? http = null)
    {
        _apiKey = apiKey;
        _http = http ?? new HttpClient();
    }

    public async Task<byte[]> SynthesizeAsync(string text, string voiceId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("ElevenLabs API key not set");

        var url = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}";
        var payload = JsonSerializer.Serialize(new { text, model_id = Model });
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("xi-api-key", _apiKey);
        req.Headers.Add("accept", "audio/mpeg");

        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }
}
