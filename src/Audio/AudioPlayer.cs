using System;
using System.IO;

namespace Morpheus.Audio;

// Stub — next pass: NAudio WaveOutEvent + Mp3FileReader/StreamMediaFoundationReader,
// amplitude sampling tick for lipsync (RMS or peak) exposed via CurrentLevel.
public sealed class AudioPlayer : IDisposable
{
    public float CurrentLevel { get; private set; }
    public bool IsPlaying { get; private set; }
    public event Action? Finished;

    public void PlayMp3(byte[] mp3Bytes) { }
    public void Stop() { }
    public void Dispose() { }
}
