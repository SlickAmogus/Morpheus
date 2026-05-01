using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Morpheus.Ui;

public enum AiProvider { Ollama, Gemini, OpenAi, Claude, DeepSeek }

public sealed class CustomVoice
{
    public string Name { get; set; } = "";
    public string VoiceId { get; set; } = "";
}

public sealed class MorpheusSettings
{
    [JsonIgnore]  // key is stored in keystore.local.dat, not here
    public string? ElevenLabsApiKey { get; set; }
    public int HookPort { get; set; } = 47921;
    public AiProvider SummaryProvider { get; set; } = AiProvider.Ollama;
    [JsonIgnore] public string? GeminiApiKey   { get; set; }
    [JsonIgnore] public string? OpenAiApiKey   { get; set; }
    [JsonIgnore] public string? ClaudeApiKey   { get; set; }
    [JsonIgnore] public string? DeepSeekApiKey { get; set; }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static MorpheusSettings Load(string path)
    {
        if (!File.Exists(path)) return new MorpheusSettings();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<MorpheusSettings>(json, Opts) ?? new MorpheusSettings();
        }
        catch { return new MorpheusSettings(); }
    }

    public static void Save(string path, MorpheusSettings s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(s, Opts));
    }
}
