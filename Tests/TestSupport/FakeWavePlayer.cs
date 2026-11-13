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
    // 最后一个曾订阅过的 handler：解订阅不移除这份引用，供测试模拟"播放线程上
    // 仍在途的回调"（真实 WASAPI 下 Stop/Dispose 与回调可能并发）。
    private EventHandler<StoppedEventArgs>? _lastKnownPlaybackStopped;

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
        add
        {
            Log.Add("Subscribe");
            _playbackStopped += value;
            _lastKnownPlaybackStopped = value;
        }
        remove
        {
            Log.Add("Unsubscribe");
            _playbackStopped -= value;
            // 刻意保留 _lastKnownPlaybackStopped：解订阅后仍可复现晚到的在途回调
        }
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

    /// <summary>
    /// 向"最后一个曾订阅过的 handler"投递 PlaybackStopped，即使它已被解订阅。
    /// 用来模拟播放线程上晚到的在途回调（服务侧必须靠当前链身份判定丢弃它）。
    /// </summary>
    public void RaiseStoppedToLastKnownHandler(Exception? exception = null)
        => _lastKnownPlaybackStopped?.Invoke(this, new StoppedEventArgs(exception!));

    public void Dispose() => Log.Add("Dispose");
}

internal sealed class FakeAudioOutputFactory : IAudioOutputFactory
{
    private readonly Func<FakeWavePlayer> _create;

    public FakeAudioOutputFactory(Func<FakeWavePlayer> create) => _create = create;

    public FakeWavePlayer Last { get; private set; } = null!;

    public IWavePlayer CreateOutput() => Last = _create();
}
