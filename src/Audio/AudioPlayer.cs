using System;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Morpheus.Audio;

public sealed class AudioPlayer : IDisposable
{
    private WaveOutEvent? _out;
    private Mp3FileReader? _reader;
    private MemoryStream? _source;
    private readonly object _lock = new();

    public float CurrentLevel { get; private set; }
    public bool IsPlaying { get; private set; }
    public event Action? Finished;

    public double PositionSeconds
    {
        get { lock (_lock) return _reader?.CurrentTime.TotalSeconds ?? 0; }
    }

    public double TotalSeconds
    {
        get { lock (_lock) return _reader?.TotalTime.TotalSeconds ?? 0; }
    }

    public float Progress
    {
        get
        {
            var t = TotalSeconds;
            if (t <= 0) return 0f;
            var p = PositionSeconds / t;
            return (float)System.Math.Clamp(p, 0.0, 1.0);
        }
    }

    public void PlayMp3(byte[] mp3Bytes)
    {
        lock (_lock)
        {
            StopInternal();

            _source = new MemoryStream(mp3Bytes, writable: false);
            _reader = new Mp3FileReader(_source);
            var sample = _reader.ToSampleProvider();
            var framesPerWindow = Math.Max(1, sample.WaveFormat.SampleRate / 30);
            var meter = new MeteringSampleProvider(sample, framesPerWindow);
            meter.StreamVolume += OnMeter;

            _out = new WaveOutEvent();
            _out.Init(meter);
            _out.PlaybackStopped += OnStopped;
            _out.Play();
            IsPlaying = true;
        }
    }

    public void Stop()
    {
        lock (_lock) StopInternal();
    }

    private void OnMeter(object? sender, StreamVolumeEventArgs e)
    {
        float max = 0f;
        foreach (var v in e.MaxSampleValues) if (v > max) max = v;
        CurrentLevel = max;
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        lock (_lock)
        {
            IsPlaying = false;
            CurrentLevel = 0f;
        }
        Finished?.Invoke();
    }

    private void StopInternal()
    {
        if (_out is not null)
        {
            _out.PlaybackStopped -= OnStopped;
            try { _out.Stop(); } catch { }
            _out.Dispose();
            _out = null;
        }
        _reader?.Dispose();
        _reader = null;
        _source?.Dispose();
        _source = null;
        IsPlaying = false;
        CurrentLevel = 0f;
    }

    public void Dispose() => Stop();
}
