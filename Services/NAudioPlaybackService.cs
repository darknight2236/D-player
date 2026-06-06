using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UmaPlayer.Models;

namespace UmaPlayer.Services;

/// <summary>
/// 基于 NAudio 的播放服务实现。
///
/// 播放链路：MediaFoundationReader → VolumeSampleProvider → WasapiOut(Shared)
///   - MediaFoundationReader：调用 Windows Media Foundation 原生解码 MP3/WMA/FLAC/AAC/WAV
///   - VolumeSampleProvider ：在样本层做线性音量缩放
///   - WasapiOut(Shared)    ：共享模式输出，100ms 缓冲（低延迟与稳定性的折中）
///
/// 线程模型：构造时捕获 UI 线程 SynchronizationContext，
///          所有事件通过 _syncContext.Post 派发，VM 可直接绑定属性。
/// </summary>
public sealed class NAudioPlaybackService : IPlaybackService
{
    private readonly SynchronizationContext _syncContext;
    private IWavePlayer? _wavePlayer;
    private MediaFoundationReader? _reader;
    private VolumeSampleProvider? _volumeProvider;
    private Track? _currentTrack;
    private PlayState _state = PlayState.Stopped;
    private float _volume = 0.8f;

    // 位置事件节流：33ms ≈ 30Hz，刚好覆盖 60Hz 屏的"每两帧一次"，再高对感知无帮助
    private static readonly TimeSpan PositionThrottle = TimeSpan.FromMilliseconds(33);
    private DateTime _lastPositionEvent = DateTime.MinValue;

    public PlayState State => _state;
    public Track? CurrentTrack => _currentTrack;
    public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_volumeProvider != null)
                _volumeProvider.Volume = _volume;
        }
    }

    public event Action<PlayState>? StateChanged;
    public event Action<TimeSpan>? PositionChanged;
    public event Action<TimeSpan>? DurationChanged;
    public event Action<Track>? TrackChanged;
    public event Action<string>? PlaybackError;

    public NAudioPlaybackService()
    {
        // App.OnStartup 在 UI 线程解析本服务，因此 Current 一定非空；
        // 容错给一个新 SyncContext，避免单元测试场景 NRE。
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
    }

    /// <summary>
    /// 加载一首曲目：销毁旧播放链 → 新建解码器/音量/输出 → 回填 Duration 与 SampleRate。
    /// 重量级操作放在 Task.Run 上避免阻塞 UI。
    /// </summary>
    public async Task LoadAsync(Track track)
    {
        await Task.Run(() =>
        {
            DisposePlayback();

            try
            {
                _reader = new MediaFoundationReader(track.FilePath);
                _volumeProvider = new VolumeSampleProvider(_reader.ToSampleProvider())
                {
                    Volume = _volume
                };
                _wavePlayer = new WasapiOut(AudioClientShareMode.Shared, 100);
                _wavePlayer.Init(_volumeProvider);

                _wavePlayer.PlaybackStopped += OnPlaybackStopped;

                // 用解码器实测值回填 Track；调用方传入的 Duration 通常为 Zero
                _currentTrack = track with
                {
                    Duration = _reader.TotalTime,
                    SampleRate = _reader.WaveFormat.SampleRate
                };

                RaiseOnUIThread(DurationChanged, _reader.TotalTime);
                RaiseOnUIThread(TrackChanged, _currentTrack);
            }
            catch (Exception ex)
            {
                RaiseOnUIThread(PlaybackError, ex.Message);
                throw;
            }
        });
    }

    public void Play()
    {
        if (_wavePlayer == null) return;
        _wavePlayer.Play();
        SetState(PlayState.Playing);
        _ = PollPositionAsync(); // fire-and-forget：循环在 PlaybackState!=Playing 时自然退出
    }

    public void Pause()
    {
        if (_wavePlayer == null) return;
        _wavePlayer.Pause();
        SetState(PlayState.Paused);
    }

    public void Stop()
    {
        if (_wavePlayer == null) return;
        _wavePlayer.Stop();
        // 同时将播放头归零，下次 Play 从头开始
        if (_reader != null)
            _reader.CurrentTime = TimeSpan.Zero;
        SetState(PlayState.Stopped);
    }

    public void Seek(TimeSpan position)
    {
        if (_reader == null) return;
        _reader.CurrentTime = position;
    }

    private void SetState(PlayState newState)
    {
        if (_state == newState) return; // 去重，避免连续触发同状态事件
        _state = newState;
        RaiseOnUIThread(StateChanged, _state);
    }

    /// <summary>
    /// NAudio 在以下情况触发 PlaybackStopped：
    /// (1) 播放到曲尾  (2) 用户调用 Stop()  (3) 设备出错
    /// 仅 (3) 携带 Exception，作为错误上报通道。
    /// </summary>
    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            RaiseOnUIThread(PlaybackError, e.Exception.Message);
        }

        SetState(PlayState.Stopped);
    }

    /// <summary>
    /// 后台轮询循环：每 ~33ms 上报一次 Position；
    /// 通过 PlaybackState != Playing 自动退出（Pause/Stop 都会让其下一轮终止）。
    /// </summary>
    private async Task PollPositionAsync()
    {
        while (_wavePlayer?.PlaybackState == PlaybackState.Playing)
        {
            await Task.Delay(33); // ~30Hz

            var now = DateTime.UtcNow;
            if (now - _lastPositionEvent >= PositionThrottle)
            {
                _lastPositionEvent = now;
                var pos = _reader?.CurrentTime ?? TimeSpan.Zero;
                RaiseOnUIThread(PositionChanged, pos);
            }
        }
    }

    /// <summary>将事件回调封送到 UI 线程；VM 中所有 handler 可直接更新绑定属性。</summary>
    private void RaiseOnUIThread<T>(Action<T>? handler, T value)
    {
        if (handler == null) return;
        _syncContext.Post(_ => handler(value), null);
    }

    /// <summary>释放当前播放链；切歌前与 Dispose 都会调用。顺序：解订阅 → 停止 → 释放。</summary>
    private void DisposePlayback()
    {
        if (_wavePlayer != null)
        {
            _wavePlayer.PlaybackStopped -= OnPlaybackStopped;
            _wavePlayer.Stop();
            _wavePlayer.Dispose();
            _wavePlayer = null;
        }
        _volumeProvider = null;
        if (_reader != null)
        {
            _reader.Dispose();
            _reader = null;
        }
    }

    public void Dispose()
    {
        DisposePlayback();
    }
}
