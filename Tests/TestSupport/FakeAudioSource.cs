using System;
using NAudio.Wave;
using DPlayer.Services;

namespace DPlayer.Tests.TestSupport;

internal sealed class FakeAudioSource : IAudioSource
{
    private readonly WaveFormat _format;

    public FakeAudioSource(TimeSpan totalTime, int sampleRate = 48000, int channels = 2)
    {
        _format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        TotalTime = totalTime;
        Provider = new FakeSampleProvider(_format);
    }

    public WaveFormat WaveFormat => _format;
    public TimeSpan TotalTime { get; set; }
    public TimeSpan CurrentTime { get; set; }
    public int DisposeCount { get; private set; }
    public FakeSampleProvider Provider { get; }

    public ISampleProvider ToSampleProvider() => Provider;

    public void Dispose() => DisposeCount++;
}

internal sealed class FakeSampleProvider : ISampleProvider
{
    public FakeSampleProvider(WaveFormat format) => WaveFormat = format;

    public WaveFormat WaveFormat { get; }

    public int Read(Span<float> buffer)
    {
        buffer.Clear();
        return buffer.Length;
    }
}

internal sealed class FakeAudioSourceFactory : IAudioSourceFactory
{
    private readonly Func<string, FakeAudioSource> _create;

    public FakeAudioSourceFactory(Func<string, FakeAudioSource> create) => _create = create;

    public FakeAudioSource Last { get; private set; } = null!;

    public IAudioSource Create(string path) => Last = _create(path);
}
