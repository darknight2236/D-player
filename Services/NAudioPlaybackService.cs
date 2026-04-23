using NAudio.CoreAudioApi;
using NAudio.Wave;
using UmaPlayer.Models;

namespace UmaPlayer.Services;

public sealed class NAudioPlaybackService : IPlaybackService
{
    private readonly SynchronizationContext _syncContext;
    private IWavePlayer? _wavePlayer;
    private MediaFoundationReader? _reader;
    private Track? _currentTrack;
    private PlayState _state = PlayState.Stopped;
    private float _volume = 0.8f;

    // Throttle: 33ms ≈ 30Hz
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
            if (_wavePlayer != null)
                _wavePlayer.Volume = _volume;
        }
    }

    public event Action<PlayState>? StateChanged;
    public event Action<TimeSpan>? PositionChanged;
    public event Action<TimeSpan>? DurationChanged;
    public event Action<string>? PlaybackError;

    public NAudioPlaybackService()
    {
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
    }

    public async Task LoadAsync(Track track)
    {
        await Task.Run(() =>
        {
            DisposePlayback();

            try
            {
                _reader = new MediaFoundationReader(track.FilePath);
                _wavePlayer = new WasapiOut(AudioClientShareMode.Shared, 100);
                _wavePlayer.Volume = _volume;
                _wavePlayer.Init(_reader);

                _wavePlayer.PlaybackStopped += OnPlaybackStopped;
                _currentTrack = track with { Duration = _reader.TotalTime };

                RaiseOnUIThread(DurationChanged, _reader.TotalTime);
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
        _ = PollPositionAsync();
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
        if (_state == newState) return;
        _state = newState;
        RaiseOnUIThread(StateChanged, _state);
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            RaiseOnUIThread(PlaybackError, e.Exception.Message);
        }

        SetState(PlayState.Stopped);
    }

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

    private void RaiseOnUIThread<T>(Action<T>? handler, T value)
    {
        if (handler == null) return;
        _syncContext.Post(_ => handler(value), null);
    }

    private void DisposePlayback()
    {
        if (_wavePlayer != null)
        {
            _wavePlayer.PlaybackStopped -= OnPlaybackStopped;
            _wavePlayer.Stop();
            _wavePlayer.Dispose();
            _wavePlayer = null;
        }
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
