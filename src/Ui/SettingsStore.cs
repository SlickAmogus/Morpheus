using System.IO;
using System.Text.Json;

namespace Morpheus.Ui;

public sealed class MorpheusSettings
{
    public string? SelectedAvatar { get; set; }
    public string? SelectedTemplate { get; set; }
    public string? ElevenLabsApiKey { get; set; }
    public string? ElevenLabsVoiceId { get; set; }
    public int HookPort { get; set; } = 47921;
    public string BackgroundColor { get; set; } = "#000000";
    public float VoiceStability { get; set; } = 0.5f;
    public float VoiceSimilarity { get; set; } = 0.75f;
    public float VoiceStyle { get; set; } = 0.0f;
    public bool VoiceSpeakerBoost { get; set; } = true;
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
