namespace Morpheus.Audio;

public sealed class VoiceSettings
{
    public float Stability { get; set; } = 0.5f;
    public float SimilarityBoost { get; set; } = 0.75f;
    public float Style { get; set; } = 0.0f;
    public bool UseSpeakerBoost { get; set; } = true;
}
