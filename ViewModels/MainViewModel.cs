using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using UmaPlayer.Configuration;
using UmaPlayer.Models;
using UmaPlayer.Services;

namespace UmaPlayer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IPlaybackService _player;
    private readonly IFileDialogService _fileDialog;
    private readonly ISettingsPersistence _persistence;
    private readonly IOptions<AppSettings> _options;
    private AppSettings _settings;
    private bool _isInitializing = true;

    [ObservableProperty]
    private bool _isSeeking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionNormalized))]
    private TimeSpan _position;

    [ObservableProperty]
    private TimeSpan _duration;

    [ObservableProperty]
    private PlayState _playState;

    [ObservableProperty]
    private Track? _currentTrack;

    [ObservableProperty]
    private float _volume;

    public double PositionNormalized =>
        Duration.TotalSeconds > 0 ? Position.TotalSeconds / Duration.TotalSeconds : 0;

    public MainViewModel(
        IPlaybackService player,
        IFileDialogService fileDialog,
        ISettingsPersistence persistence,
        IOptions<AppSettings> options)
    {
        _player = player;
        _fileDialog = fileDialog;
        _persistence = persistence;
        _options = options;
        _settings = options.Value;

        _player.PositionChanged += HandlePositionChanged;
        _player.StateChanged += HandleStateChanged;
        _player.DurationChanged += HandleDurationChanged;
        _player.PlaybackError += HandlePlaybackError;

        Initialize();
    }

    private void Initialize()
    {
        try
        {
            _settings = _persistence.LoadAsync().GetAwaiter().GetResult();
        }
        catch
        {
            _settings = _options.Value;
        }
        Volume = _settings.DefaultVolume;
        _isInitializing = false;
    }

    private void HandlePositionChanged(TimeSpan position)
    {
        if (!IsSeeking)
            Position = position;
    }

    private void HandleDurationChanged(TimeSpan duration)
        => Duration = duration;

    private void HandleStateChanged(PlayState state)
        => PlayState = state;

    private void HandlePlaybackError(string error)
    {
        // TODO: first phase — log only, future: show notification
    }

    [RelayCommand]
    private void SeekStarted() => IsSeeking = true;

    [RelayCommand]
    private void SeekCompleted(double normalized)
    {
        IsSeeking = false;
        var target = TimeSpan.FromSeconds(normalized * Duration.TotalSeconds);
        _player.Seek(target);
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (PlayState == PlayState.Playing)
            _player.Pause();
        else
            _player.Play();
    }

    [RelayCommand]
    private void Stop() => _player.Stop();

    [RelayCommand]
    private async Task OpenFilesAsync()
    {
        var files = _fileDialog.OpenFiles("Audio Files|*.mp3;*.wma;*.flac;*.aac;*.wav");
        if (files.Count > 0)
        {
            var file = files[0];
            var track = new Track(
                file,
                Path.GetFileNameWithoutExtension(file),
                null, null, TimeSpan.Zero);
            await _player.LoadAsync(track);
            _player.Play();
        }
    }

    partial void OnVolumeChanged(float value)
    {
        _player.Volume = value;
        if (_isInitializing) return;
        _settings = _settings with { DefaultVolume = value };
        _ = _persistence.SaveAsync(_settings);
    }

    public async Task CleanupAsync()
    {
        _player.PositionChanged -= HandlePositionChanged;
        _player.StateChanged -= HandleStateChanged;
        _player.DurationChanged -= HandleDurationChanged;
        _player.PlaybackError -= HandlePlaybackError;
        _player.Dispose();
        await _persistence.SaveAsync(_settings);
    }
}
