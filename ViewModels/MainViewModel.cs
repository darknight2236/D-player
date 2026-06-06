using System.Collections.ObjectModel;
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
/// 主窗口的 ViewModel —— 唯一的 VM，聚合所有播放、UI 状态和持久化逻辑。
///
/// 依赖：
///   - IPlaybackService    ：底层播放控制
///   - IFileDialogService  ：文件选择
///   - ISettingsPersistence：磁盘读写音量等设置
///   - IOptions&lt;AppSettings&gt;：启动默认值（持久化加载失败时的回落值）
///
/// 使用 CommunityToolkit.Mvvm 源生成器：
///   - [ObservableProperty] 自动生成属性 + 通知
///   - [RelayCommand]       自动生成 ICommand
///   - partial void On{Prop}Changed —— 属性变更钩子
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IPlaybackService _player;
    private readonly IFileDialogService _fileDialog;
    private readonly ISettingsPersistence _persistence;
    private readonly IOptions<AppSettings> _options;
    private AppSettings _settings;

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

    #region Phase 2 — Playlist Queue

    /// <summary>当前播放队列。ObservableCollection 自动通知 UI 增删改。</summary>
    public ObservableCollection<Track> Queue { get; } = new();

    /// <summary>当前播放曲在 Queue 中的索引；-1 表示未选/队列空。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentTrack))]
    private int _currentIndex = -1;

    /// <summary>UI 列表选中项（与"当前播放曲"无关，仅供 Delete 键定位）。</summary>
    [ObservableProperty]
    private Track? _selectedTrack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShuffleBrushKey))]
    private bool _shuffleEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepeatActive))]
    private RepeatMode _repeatMode = RepeatMode.Off;

    /// <summary>随机模式下"已播过"的索引集合。切换 ShuffleEnabled 或清空队列时重置。</summary>
    private readonly HashSet<int> _shuffleHistory = new();

    /// <summary>用于 Shuffle 模式随机选曲；构造一次复用。</summary>
    private readonly Random _random = new();

    // —— 派生属性 ——

    /// <summary>循环按钮是否处于"激活"状态（List 或 One 都算）。</summary>
    public bool RepeatActive => RepeatMode != RepeatMode.Off;

    /// <summary>暴露给 XAML 的 Shuffle 高亮指示（直接绑 ShuffleEnabled 即可，留作语义清晰）。</summary>
    public bool ShuffleBrushKey => ShuffleEnabled;

    /// <summary>当前是否有正在播放的曲（用于 Next/Prev 按钮 CanExecute）。</summary>
    public bool HasCurrentTrack => CurrentIndex >= 0 && CurrentIndex < Queue.Count;

    #endregion

    // —— 派生只读属性，供 XAML 绑定 ——

    public string VolumeIcon => IsMuted ? "\U0001F507" : "\U0001F50A"; // 🔇 / 🔊

    public string SampleRateText =>
        CurrentTrack?.SampleRate is { } sr ? $"{sr:N0} Hz" : "";

    /// <summary>进度条用归一化 [0,1] 值；Duration 为 0 时返回 0 防止除零。</summary>
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

        // 订阅播放服务事件 —— 所有事件已由服务封送到 UI 线程，handler 可直接更新属性
        _player.PositionChanged += HandlePositionChanged;
        _player.StateChanged += HandleStateChanged;
        _player.DurationChanged += HandleDurationChanged;
        _player.TrackChanged += HandleTrackChanged;
        _player.PlaybackError += HandlePlaybackError;

        Initialize();
    }

    /// <summary>从持久化加载用户设置；失败回落到 appsettings.json 默认值。</summary>
    private void Initialize()
    {
        try
        {
            // 构造期同步阻塞读盘（小文件、毫秒级），避免 async 构造器复杂度
            _settings = _persistence.LoadAsync().GetAwaiter().GetResult();
        }
        catch
        {
            _settings = _options.Value;
        }
        Volume = _settings.DefaultVolume;
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
    /// 打开文件 → 读元数据 → 加载到播放器 → 自动播放。
    /// 取消选择不做任何事。
    /// </summary>
    [RelayCommand]
    private async Task OpenFilesAsync()
    {
        var files = _fileDialog.OpenFiles("Audio Files|*.mp3;*.wma;*.flac;*.aac;*.wav");
        if (files.Count > 0)
        {
            var file = files[0];
            var track = await ReadTrackMetadataAsync(file);
            await _player.LoadAsync(track);
            _player.Play();
        }
    }

    /// <summary>
    /// 通过 z440.atl.core 读取音频标签（ID3、Vorbis Comment、APE 等）。
    /// 文件损坏或读不出有效音频时回落到仅含文件名的 fallback Track。
    /// 在后台线程执行，避免大文件首次解析卡 UI。
    /// </summary>
    private static Task<Track> ReadTrackMetadataAsync(string filePath)
    {
        return Task.Run(() =>
        {
            try
            {
                var atlTrack = new ATL.Track(filePath);

                // DurationMs == 0 通常意味着没有解析到有效音频数据 → 走 fallback
                if (atlTrack.DurationMs <= 0)
                    return CreateFallbackTrack(filePath);

                var title = !string.IsNullOrWhiteSpace(atlTrack.Title)
                    ? atlTrack.Title
                    : Path.GetFileNameWithoutExtension(filePath);

                // 只取第一张内嵌封面（多数情况下只有一张）
                var albumArt = atlTrack.EmbeddedPictures.Count > 0
                    ? atlTrack.EmbeddedPictures[0].PictureData
                    : null;

                return new Track(
                    FilePath: filePath,
                    Title: title,
                    Artist: atlTrack.Artist,
                    Album: atlTrack.Album,
                    Genre: atlTrack.Genre,
                    Year: atlTrack.Year > 0 ? atlTrack.Year : null,
                    SampleRate: atlTrack.SampleRate > 0 ? (int?)atlTrack.SampleRate : null,
                    AlbumArt: albumArt,
                    Duration: TimeSpan.Zero); // Duration 由播放服务加载完成后回填
            }
            catch
            {
                return CreateFallbackTrack(filePath);
            }
        });
    }

    /// <summary>退化版 Track：仅含文件路径与文件名作为标题。</summary>
    private static Track CreateFallbackTrack(string filePath)
    {
        return new Track(
            FilePath: filePath,
            Title: Path.GetFileNameWithoutExtension(filePath),
            Artist: null,
            Album: null,
            Genre: null,
            Year: null,
            SampleRate: null,
            AlbumArt: null,
            Duration: TimeSpan.Zero);
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

    /// <summary>
    /// 音量变化钩子（源生成器自动调用）：
    ///   1) 同步到播放服务  2) 拖滑块时若处于静音则自动取消静音  3) 持久化到磁盘
    /// </summary>
    partial void OnVolumeChanged(float value)
    {
        _player.Volume = value;
        if (_isInitializing) return; // 跳过初始化期间的写盘

        // 静音时直接拖动滑块到非零 → 自动解除静音状态
        if (IsMuted && value > 0f)
            IsMuted = false;

        _settings = _settings with { DefaultVolume = value };
        _ = _persistence.SaveAsync(_settings); // fire-and-forget；下次写覆盖前者
    }

    /// <summary>
    /// 窗口关闭时由 MainWindow.Window_Closing 调用 —— 解绑事件、释放播放器、保存设置。
    /// 注意：此方法不再触发 UI 更新，事件解绑后即使有残留回调也不会 NRE。
    /// </summary>
    public async Task CleanupAsync()
    {
        _player.PositionChanged -= HandlePositionChanged;
        _player.StateChanged -= HandleStateChanged;
        _player.DurationChanged -= HandleDurationChanged;
        _player.TrackChanged -= HandleTrackChanged;
        _player.PlaybackError -= HandlePlaybackError;
        _player.Dispose();
        await _persistence.SaveAsync(_settings);
    }
}
