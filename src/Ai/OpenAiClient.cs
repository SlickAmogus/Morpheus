using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Morpheus.Ai;

public static class OpenAiClient
{
    private static readonly HttpClient Http = new();
    private const string ApiUrl = "https://api.openai.com/v1/chat/completions";
    private const string Model   = "gpt-4o-mini";

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
            model      = Model,
            max_tokens = maxTokens,
            messages   = new[] { new { role = "user", content = prompt } },
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        req.Headers.Add("Authorization", $"Bearer {apiKey}");
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
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
    }
}
