using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using UmaPlayer.Configuration;
using UmaPlayer.Models;
using UmaPlayer.Services;

namespace UmaPlayer.ViewModels;

/// <summary>
/// 播放器 ViewModel —— 负责 transport 状态（播放/暂停/位置/音量）和当前曲信息展示。
///
/// 与 PlaylistViewModel 的边界：
///   - 本 VM 订阅 IPlaybackService 的 Position/Duration/State/Track/Error 事件
///   - PlaylistViewModel 单独订阅 TrackEnded 推进队列
///   - 两个 VM 互不持引用；通过 IPlaybackService 单例共享底层状态
///
/// 注：BitmapImage 在此 VM 中暂时保留（COUPLING.md 债 #1，Phase 4 单测前再还）。
/// </summary>
public partial class PlayerViewModel : ObservableObject
{
    private readonly IPlaybackService _player;
    private readonly ISettingsPersistence _persistence;
    private readonly IOptions<AppSettings> _options;

    // 标记构造期间：避免 OnVolumeChanged 在初始化时写磁盘
    private bool _isInitializing = true;

    /// <summary>拖动进度条时为 true —— 抑制 PositionChanged 回写，避免滑块被服务"拽回"。</summary>
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
    [NotifyPropertyChangedFor(nameof(SampleRateText))]
    private Track? _currentTrack;

    [ObservableProperty]
    private BitmapImage? _albumArtImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeIcon))]
    private float _volume;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeIcon))]
    private bool _isMuted;

    /// <summary>静音前的音量快照，用于"取消静音"时恢复。</summary>
    private float _volumeBeforeMute;

    // —— 派生只读属性，供 XAML 绑定 ——

    public string VolumeIcon => IsMuted ? "\U0001F507" : "\U0001F50A"; // 🔇 / 🔊

    public string SampleRateText =>
        CurrentTrack?.SampleRate is { } sr ? $"{sr:N0} Hz" : "";

    /// <summary>进度条用归一化 [0,1] 值；Duration 为 0 时返回 0 防止除零。</summary>
    public double PositionNormalized =>
        Duration.TotalSeconds > 0 ? Position.TotalSeconds / Duration.TotalSeconds : 0;

    public PlayerViewModel(
        IPlaybackService player,
        ISettingsPersistence persistence,
        IOptions<AppSettings> options)
    {
        _player = player;
        _persistence = persistence;
        _options = options;

        // 订阅播放服务事件 —— 所有事件已由服务封送到 UI 线程，handler 可直接更新属性
        _player.PositionChanged += HandlePositionChanged;
        _player.StateChanged += HandleStateChanged;
        _player.DurationChanged += HandleDurationChanged;
        _player.TrackChanged += HandleTrackChanged;
        _player.PlaybackError += HandlePlaybackError;

        Initialize();
    }

    /// <summary>从持久化加载音量；失败回落到 appsettings.json 默认值。</summary>
    private void Initialize()
    {
        AppSettings settings;
        try
        {
            // 构造期同步阻塞读盘（小文件、毫秒级），避免 async 构造器复杂度
            settings = _persistence.LoadAsync().GetAwaiter().GetResult();
        }
        catch
        {
            settings = _options.Value;
        }
        Volume = settings.DefaultVolume;
        _isInitializing = false;
    }

    // —— 播放服务事件 handler ——

    private void HandlePositionChanged(TimeSpan position)
    {
        // 拖动时不更新 Position，否则用户拖到的位置会被服务每 33ms 覆盖回去
        if (!IsSeeking)
            Position = position;
    }

    private void HandleDurationChanged(TimeSpan duration)
        => Duration = duration;

    private void HandleTrackChanged(Track track)
    {
        CurrentTrack = track;
        AlbumArtImage = CreateAlbumArtImage(track.AlbumArt);
    }

    private void HandleStateChanged(PlayState state)
        => PlayState = state;

    private void HandlePlaybackError(string error)
    {
        // TODO: 首期暂不处理；后续可弹 toast 或写日志
    }

    // —— UI 命令 ——

    /// <summary>用户开始拖动进度条 thumb 时由 View code-behind 调用。</summary>
    [RelayCommand]
    private void SeekStarted() => IsSeeking = true;

    /// <summary>拖动完成 / 单击跳转 —— 把归一化位置 [0,1] 转回 TimeSpan 并通知播放服务。</summary>
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

    /// <summary>切换静音：静音时记忆当前音量，恢复时还原。</summary>
    [RelayCommand]
    private void ToggleMute()
    {
        if (IsMuted)
        {
            IsMuted = false;
            Volume = _volumeBeforeMute;
        }
        else
        {
            _volumeBeforeMute = Volume;
            IsMuted = true;
            Volume = 0f;
        }
    }

    /// <summary>
    /// 音量变化钩子（源生成器自动调用）：
    ///   1) 同步到播放服务  2) 拖滑块时若处于静音则自动取消静音  3) 锁内持久化到磁盘
    /// </summary>
    partial void OnVolumeChanged(float value)
    {
        _player.Volume = value;
        if (_isInitializing) return; // 跳过初始化期间的写盘

        // 静音时直接拖动滑块到非零 → 自动解除静音状态
        if (IsMuted && value > 0f)
            IsMuted = false;

        _ = _persistence.UpdateAsync(s => s with { DefaultVolume = value });
    }

    /// <summary>
    /// 由 MainViewModel.CleanupAsync 调用 —— 解绑事件并持久化最后一次音量。
    /// 注意：不在这里 Dispose IPlaybackService（PlaylistViewModel 还在用，
    /// Facade 层统一 Dispose）。
    /// </summary>
    public async Task CleanupAsync()
    {
        _player.PositionChanged -= HandlePositionChanged;
        _player.StateChanged -= HandleStateChanged;
        _player.DurationChanged -= HandleDurationChanged;
        _player.TrackChanged -= HandleTrackChanged;
        _player.PlaybackError -= HandlePlaybackError;
        await _persistence.UpdateAsync(s => s with { DefaultVolume = Volume });
    }

    /// <summary>
    /// 从字节数组创建可跨线程使用的 BitmapImage：
    ///   - DecodePixelWidth=200：解码时即缩放，省内存（封面渲染区只有 80px）
    ///   - Freeze()：冻结后可被任意线程读取，且 WPF 渲染更高效
    /// </summary>
    private static BitmapImage? CreateAlbumArtImage(byte[]? data)
    {
        if (data is not { Length: > 0 }) return null;

        var image = new BitmapImage();
        using var ms = new MemoryStream(data);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad; // 一次性把流读入内存，立即释放 MemoryStream
        image.StreamSource = ms;
        image.DecodePixelWidth = 200;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
