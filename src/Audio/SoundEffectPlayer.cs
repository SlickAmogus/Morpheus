using System;
using System.IO;
using System.Threading;
using NAudio.Wave;

namespace Morpheus.Audio;

public static class SoundEffectPlayer
{
    public static void Play(string filePath)
    {
        if (!File.Exists(filePath)) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                using var reader = new WaveFileReader(filePath);
                using var output = new WaveOutEvent();
                output.Init(reader);
                output.Play();
                while (output.PlaybackState == PlaybackState.Playing)
                    Thread.Sleep(50);
            }
            catch { }
        });
    }
}
