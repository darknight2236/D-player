using NAudio.Wave;

namespace DPlayer.Services;

internal sealed class MediaFoundationAudioSource : IAudioSource
{
    private readonly MediaFoundationReader _reader;

    public MediaFoundationAudioSource(string path) => _reader = new MediaFoundationReader(path);

    public WaveFormat WaveFormat => _reader.WaveFormat;
    public TimeSpan TotalTime => _reader.TotalTime;
    public TimeSpan CurrentTime
    {
        get => _reader.CurrentTime;
        set => _reader.CurrentTime = value;
    }

    public ISampleProvider ToSampleProvider() => _reader.ToSampleProvider();
    public void Dispose() => _reader.Dispose();
}

internal sealed class MediaFoundationAudioSourceFactory : IAudioSourceFactory
{
    public IAudioSource Create(string path) => new MediaFoundationAudioSource(path);
}
