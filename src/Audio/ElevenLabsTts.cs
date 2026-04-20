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
    private readonly VoiceSettings? _settings;
    private const string Model = "eleven_turbo_v2_5";

    public ElevenLabsTts(string apiKey, VoiceSettings? settings = null, HttpClient? http = null)
    {
        _apiKey = apiKey;
        _settings = settings;
        _http = http ?? new HttpClient();
    }

    public async Task<byte[]> SynthesizeAsync(string text, string voiceId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("ElevenLabs API key not set");

        var url = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}";

        object payloadObj = _settings is null
            ? new { text, model_id = Model }
            : new
            {
                text,
                model_id = Model,
                voice_settings = new
                {
                    stability = _settings.Stability,
                    similarity_boost = _settings.SimilarityBoost,
                    style = _settings.Style,
                    use_speaker_boost = _settings.UseSpeakerBoost,
                },
            };
        var payload = JsonSerializer.Serialize(payloadObj);

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("xi-api-key", _apiKey);
        req.Headers.Add("accept", "audio/mpeg");

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"elevenlabs {(int)resp.StatusCode}: {Truncate(body, 200)}");
        }
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];
}
