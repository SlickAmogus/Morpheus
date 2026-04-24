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
    public List<IdleAnimation> IdleAnimations { get; set; } = new();
    public List<IdleClip> IdleClips { get; set; } = new();
    public AvatarSize? Size { get; set; }
    public AvatarCrop? Crop { get; set; }
    // "#RRGGBB" — pixels matching this color are made fully transparent at load.
    public string? TransparentColor { get; set; }
    // "#RRGGBB" — render this color behind avatar as background (for transparent areas).
    public string? BackgroundColor { get; set; }
    public float BlinkMinSeconds { get; set; } = 3.0f;
    public float BlinkMaxSeconds { get; set; } = 7.0f;
    public float BlinkDurationSeconds { get; set; } = 0.18f;
}

public sealed class AvatarSize
{
    public int Width { get; set; } = 400;
    public int Height { get; set; } = 400;
}

// Inward crop applied symmetrically — Horizontal trims that many pixels off
// BOTH left and right; Vertical trims that many pixels off BOTH top and bottom.
public sealed class AvatarCrop
{
    public int Horizontal { get; set; }
    public int Vertical { get; set; }
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
    // Alternate poses for the same emotion. Picked at random when the emotion
    // is entered (and on each new dialog turn). Each variant may set its own
    // Closed/Open or fall back to the base pair's values.
    public List<MouthPair> Variants { get; set; } = new();
}

// Random expression that fires while truly idle (no speech, no active tool).
// Emotion must match a key in Sprites.Emotions; the entry's frames play once
// then state reverts to plain idle.
public sealed class IdleAnimation
{
    public string Emotion { get; set; } = "";
    public float MinSeconds { get; set; } = 6f;       // min gap before next play
    public float MaxSeconds { get; set; } = 15f;      // max gap
    public float DurationSeconds { get; set; } = 0.6f; // how long to hold the expression
    public float Weight { get; set; } = 1f;           // relative pick probability
}

// File-based random idle clip. Useful for WebP-style avatars where each clip is
// a self-contained animation file (e.g. `idle_smirk.webp`). The file path is
// avatar-folder relative; the renderer plays it through OverrideSprite for the
// configured duration, then reverts to the base emotion.
public sealed class IdleClip
{
    public string File { get; set; } = "";
    public float MinSeconds { get; set; } = 8f;
    public float MaxSeconds { get; set; } = 20f;
    public float DurationSeconds { get; set; } = 2f;
    public float Weight { get; set; } = 1f;
}
