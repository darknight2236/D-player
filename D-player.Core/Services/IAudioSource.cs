using NAudio.Wave;

namespace DPlayer.Services;

internal interface IAudioSource : IDisposable
{
    WaveFormat WaveFormat { get; }
    TimeSpan TotalTime { get; }
    TimeSpan CurrentTime { get; set; }
    ISampleProvider ToSampleProvider();
}

internal interface IAudioSourceFactory
{
    IAudioSource Create(string path);
}
