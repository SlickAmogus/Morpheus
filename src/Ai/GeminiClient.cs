using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Morpheus.Ai;

public static class GeminiClient
{
    private static readonly HttpClient Http = new();
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent";

    // Returns null on success, error string on failure.
    public static async Task<string?> TestKeyAsync(string apiKey)
    {
        try { await SendAsync(apiKey, "Reply with just the word: OK", 10); return null; }
        catch (Exception ex) { return ex.Message; }
    }

    public static Task<string?> PromptAsync(string apiKey, string fullPrompt)
        => SendAsync(apiKey, fullPrompt, 1000)!;

    public static async Task<string?> SummarizeAsync(string apiKey, string text)
    {
        var prompt =
            "Summarize the following text in 2-3 sentences. " +
            "Start your response with an emotion tag like [emotion:happy] — choose the most fitting from: " +
            "happy, excited, curious, thoughtful, sad, playful. " +
            "Output only the summary — no preamble.\n\n" + text;
        return await SendAsync(apiKey, prompt, 300);
    }

    private static async Task<string> SendAsync(string apiKey, string prompt, int maxTokens)
    {
        var body = JsonSerializer.Serialize(new
        {
            contents         = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new { maxOutputTokens = maxTokens },
        });

        var url = $"{BaseUrl}?key={Uri.EscapeDataString(apiKey)}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var resp = await Http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync();
            try
            {
                using var doc = JsonDocument.Parse(err);
                var msg = doc.RootElement.GetProperty("error").GetProperty("message").GetString();
                throw new Exception($"{(int)resp.StatusCode}: {msg}");
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new Exception($"{(int)resp.StatusCode}: {err[..Math.Min(120, err.Length)]}");
            }
        }

        var json = await resp.Content.ReadAsStringAsync();
        using var doc2 = JsonDocument.Parse(json);
        return doc2.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? "";
    }
}
