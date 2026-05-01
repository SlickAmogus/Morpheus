using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Morpheus.Ai;

public static class OllamaClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(180) };
    private const string BaseUrl = "http://localhost:11434";
    private static readonly Regex ThinkTag = new(@"<think>[\s\S]*?</think>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Returns the first available model name, or null if Ollama isn't reachable.
    public static async Task<string?> GetFirstModelAsync()
    {
        try
        {
            var resp = await Http.GetStringAsync($"{BaseUrl}/api/tags");
            using var doc = JsonDocument.Parse(resp);
            var models = doc.RootElement.GetProperty("models");
            if (models.GetArrayLength() == 0) return null;
            return models[0].GetProperty("name").GetString();
        }
        catch { return null; }
    }

    public static async Task<string?> PromptAsync(string model, string fullPrompt)
    {
        var body = JsonSerializer.Serialize(new
        {
            model,
            messages = new[] { new { role = "user", content = fullPrompt } },
            stream   = false,
            options  = new { num_predict = 1000 },
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/chat/completions");
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var raw = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
        return ThinkTag.Replace(raw, "").Trim();
    }

    public static async Task<string?> SummarizeAsync(string model, string text)
    {
        var prompt =
            "Summarize the following text in 2-3 sentences. " +
            "Start your response with an emotion tag like [emotion:happy] — choose the most fitting from: " +
            "happy, excited, curious, thoughtful, sad, playful. " +
            "Output only the summary — no preamble, no explanation.\n\n" + text;

        var body = JsonSerializer.Serialize(new
        {
            model,
            messages  = new[] { new { role = "user", content = prompt } },
            stream    = false,
            options   = new { num_predict = 300 },
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/chat/completions");
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var resp = await Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var raw = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";

        // Strip <think>...</think> blocks that some models emit
        return ThinkTag.Replace(raw, "").Trim();
    }
}
