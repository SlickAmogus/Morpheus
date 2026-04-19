using System.Collections.Generic;

namespace Morpheus.Avatar;

public sealed class AvatarManifest
{
    public string Name { get; set; } = "";
    public string? Author { get; set; }
    public string? Description { get; set; }
    public int Fps { get; set; } = 8;
    public float LipsyncThreshold { get; set; } = 0.05f;
    public AvatarSprites Sprites { get; set; } = new();
    public string? PersonalityFile { get; set; }
    public string? PreviewImage { get; set; }
}

public sealed class AvatarSprites
{
    public MouthPair Idle { get; set; } = new();
    public string? Generic { get; set; }
    public Dictionary<string, MouthPair> Emotions { get; set; } = new();
    public Dictionary<string, string> Tools { get; set; } = new();
}

public sealed class MouthPair
{
    public string? Closed { get; set; }
    public string? Open { get; set; }
}
