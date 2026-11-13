using System;
using System.Collections.Generic;
using NAudio.Wave;
using DPlayer.Services;

namespace DPlayer.Tests.TestSupport;

internal sealed class FakeWavePlayer : IWavePlayer
{
    private readonly ManualResetEventSlim? _initGate;
    private readonly ManualResetEventSlim? _initEntered;
    private EventHandler<StoppedEventArgs>? _playbackStopped;

    public FakeWavePlayer(ManualResetEventSlim? initGate = null, ManualResetEventSlim? initEntered = null)
    {
        _initGate = initGate;
        _initEntered = initEntered;
    }

    public PlaybackState PlaybackState { get; private set; } = PlaybackState.Stopped;
    public float Volume { get; set; } = 1f;
    public WaveFormat OutputWaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    public List<string> Log { get; } = new();
    public bool RaiseStoppedSynchronouslyOnStop { get; set; }
    public Exception? StopException { get; set; }

    public event EventHandler<StoppedEventArgs>? PlaybackStopped
    {
        add { Log.Add("Subscribe"); _playbackStopped += value; }
        remove { Log.Add("Unsubscribe"); _playbackStopped -= value; }
    }

    public void Init(IWaveProvider waveProvider)
    {
        Log.Add("Init");
        _initEntered?.Set();
        _initGate?.Wait();
    }

    public void Play()
    {
        Log.Add("Play");
        PlaybackState = PlaybackState.Playing;
    }

    public void Pause()
    {
        Log.Add("Pause");
        PlaybackState = PlaybackState.Paused;
    }

    public void Stop()
    {
        Log.Add("Stop");
        PlaybackState = PlaybackState.Stopped;
        if (RaiseStoppedSynchronouslyOnStop)
            _playbackStopped?.Invoke(this, new StoppedEventArgs(StopException!));
    }

    public void RaiseStopped(Exception? exception = null)
        => _playbackStopped?.Invoke(this, new StoppedEventArgs(exception!));

    public void Dispose() => Log.Add("Dispose");
}

internal sealed class FakeAudioOutputFactory : IAudioOutputFactory
{
    private readonly Func<FakeWavePlayer> _create;

    public FakeAudioOutputFactory(Func<FakeWavePlayer> create) => _create = create;

    public FakeWavePlayer Last { get; private set; } = null!;

    public IWavePlayer CreateOutput() => Last = _create();
}
