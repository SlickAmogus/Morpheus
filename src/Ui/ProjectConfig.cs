using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Morpheus.Ui;

public sealed class ProjectConfig
{
    // Avatar / template
    public string? SelectedAvatar   { get; set; }
    public string? SelectedTemplate { get; set; }

    // Voice
    public string? ElevenLabsVoiceId { get; set; }
    public float   VoiceStability    { get; set; } = 0.5f;
    public float   VoiceSimilarity   { get; set; } = 0.75f;
    public float   VoiceStyle        { get; set; } = 0.0f;
    public bool    VoiceSpeakerBoost { get; set; } = true;
    public List<CustomVoice> CustomVoices { get; set; } = new();

    // Background color
    public string BackgroundColor { get; set; } = "#000000";

    // Panel layout (was LayoutConfig)
    public PanelLayout Voice       { get; set; } = new() { X = 10,  Y = 140, Width = 290, Height = 270 };
    public PanelLayout Sessions    { get; set; } = new() { X = 724, Y = 140, Width = 290, Height = 110 };
    public PanelLayout VoicesExtra { get; set; } = new() { X = 734, Y = 270, Width = 270, Height = 165 };
    public PanelLayout Messages    { get; set; } = new() { X = 40,  Y = 500, Width = 944, Height = 180 };
    public int AvatarOffsetX { get; set; } = -56;
    public int AvatarOffsetY { get; set; } = 66;
    public int AvatarSize    { get; set; } = 0;
    public int WindowWidth   { get; set; } = 1024;
    public int WindowHeight  { get; set; } = 720;
    public int ColorIndex    { get; set; } = 0;

    // Per-project port override (null = use default from settings.local.json)
    public int? HookPort { get; set; }
}

public static class ProjectConfigStore
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private const string Header =
        "// morpheus.cfg — created by Morpheus\n" +
        "// Per-project settings: avatar, template, voice, layout.\n" +
        "// Global settings (API keys, default port) live in settings.local.json next to Morpheus.exe.\n";

    public static ProjectConfig Load(string path)
    {
        if (!File.Exists(path)) return new ProjectConfig();
        try
        {
            var lines = File.ReadAllLines(path);
            var sb = new StringBuilder();
            foreach (var line in lines)
                if (!line.TrimStart().StartsWith("//"))
                    sb.AppendLine(line);
            return JsonSerializer.Deserialize<ProjectConfig>(sb.ToString(), Opts) ?? new ProjectConfig();
        }
        catch { return new ProjectConfig(); }
    }

    public static void Save(string path, ProjectConfig cfg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(cfg, Opts);
            File.WriteAllText(path, Header + json);
        }
        catch { }
    }
}
