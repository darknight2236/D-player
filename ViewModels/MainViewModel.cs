using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    private readonly ITrackMetadataReader _metadataReader;
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

    /// <summary>
    /// PlayTrackAtAsync 重入哨兵：每次入口自增；await 完成后若 token 不匹配，则丢弃本次结果。
    /// 防止用户连续 Next 时多个 LoadAsync 互相覆盖 NAudio 资源。
    /// </summary>
    private int _playToken;

    // —— 派生属性 ——

    /// <summary>循环按钮是否处于"激活"状态（List 或 One 都算）。</summary>
    public bool RepeatActive => RepeatMode != RepeatMode.Off;

    /// <summary>暴露给 XAML 的 Shuffle 高亮指示（直接绑 ShuffleEnabled 即可，留作语义清晰）。</summary>
    public bool ShuffleBrushKey => ShuffleEnabled;

    /// <summary>当前是否有正在播放的曲（用于 Next/Prev 按钮 CanExecute）。</summary>
    public bool HasCurrentTrack => CurrentIndex >= 0 && CurrentIndex < Queue.Count;

    /// <summary>
    /// 计算下一首曲目的索引。
    /// </summary>
    /// <param name="failedIndex">本轮已确认播放失败的索引，候选集合需排除它（防止无限循环）。</param>
    /// <returns>下一首索引；-1 表示无下一首（队列空或循环关闭已到底）。</returns>
    /// <remarks>
    /// 注意：RepeatOne 的"重播当前"逻辑不在本方法处理，由调用方
    /// (HandleTrackEnded) 直接返回 CurrentIndex。本方法只处理 Shuffle/顺序 × Repeat 组合。
    /// </remarks>
    private int CalculateNextIndex(int? failedIndex = null)
    {
        if (Queue.Count == 0) return -1;

        if (ShuffleEnabled)
        {
            // 候选 = 所有索引 - 已播过 - 失败过
            var candidates = Enumerable.Range(0, Queue.Count)
                .Where(i => !_shuffleHistory.Contains(i) && i != failedIndex)
                .ToList();

            if (candidates.Count == 0)
            {
                // 全部播过 → 视循环模式决定
                if (RepeatMode == RepeatMode.List)
                {
                    _shuffleHistory.Clear();
                    candidates = Enumerable.Range(0, Queue.Count)
                        .Where(i => i != failedIndex)
                        .ToList();
                    if (candidates.Count == 0) return -1;
                }
                else
                {
                    return -1; // RepeatOff/One 且 Shuffle 已耗尽 → 停
                }
            }

            return candidates[_random.Next(candidates.Count)];
        }
        else
        {
            // 顺序模式
            var next = CurrentIndex + 1;
            if (next < Queue.Count) return next;
            return RepeatMode == RepeatMode.List ? 0 : -1;
        }
    }

    /// <summary>
    /// 计算上一首索引。Shuffle 模式下不维护历史栈（MVP 简化）, 直接退到 0 或 Count-1。
    /// </summary>
    private int CalculatePrevIndex()
    {
        if (Queue.Count == 0) return -1;

        if (ShuffleEnabled)
        {
            // MVP: Shuffle 下 Prev 不回溯历史, 简单退到 0；后续可加历史栈
            return CurrentIndex > 0 ? CurrentIndex - 1 : 0;
        }
        else
        {
            var prev = CurrentIndex - 1;
            if (prev >= 0) return prev;
            return RepeatMode == RepeatMode.List ? Queue.Count - 1 : -1;
        }
    }

    /// <summary>
    /// 播放指定索引的曲目。失败时尝试跳过到下一首，最多连跳 3 次防无限循环。
    /// 使用 _playToken 哨兵防止重入：若 await 期间用户触发了新一轮播放，旧调用会静默退出。
    /// </summary>
    private async Task PlayTrackAtAsync(int index, int skipCount = 0)
    {
        if (index < 0 || index >= Queue.Count)
        {
            _player.Stop();
            CurrentIndex = -1;
            return;
        }

        if (skipCount >= 3)
        {
            // 连续 3 个文件失败 → 停止，避免无限错误循环
            _player.Stop();
            CurrentIndex = -1;
            return;
        }

        // 抢占 token：之后任何 await 若发现 token 已变，说明被新调用顶替，立即放弃
        int myToken = ++_playToken;

        CurrentIndex = index;
        _shuffleHistory.Add(index); // 不管是否 Shuffle 都登记，便于切换时无缝

        try
        {
            // 读元数据并回写到 Queue[index]（占位 Track → 完整 Track）
            var meta = await _metadataReader.ReadAsync(Queue[index].FilePath);
            if (myToken != _playToken) return; // 被顶替, 静默退出
            Queue[index] = meta; // ObservableCollection.set[i] 触发 Replace, UI 自动刷新

            await _player.LoadAsync(meta);
            if (myToken != _playToken) return; // 被顶替, 不再 Play
            _player.Play();
        }
        catch
        {
            if (myToken != _playToken) return; // 被顶替, 不再 fallback
            // 文件损坏 / 不存在 → 跳过到下一首
            var failed = index;
            var next = CalculateNextIndex(failedIndex: failed);
            if (next == -1 || next == failed)
            {
                _player.Stop();
                CurrentIndex = -1;
                return;
            }
            await PlayTrackAtAsync(next, skipCount + 1);
        }
    }

    // —— Phase 2 命令 ——

    /// <summary>文件对话框多选 → 入队（不读元数据，仅占位）。</summary>
    [RelayCommand]
    private void AddToQueue()
    {
        var files = _fileDialog.OpenFiles(
            "Audio Files|*.mp3;*.wma;*.flac;*.aac;*.wav",
            multiselect: true);
        if (files.Count == 0) return;

        foreach (var path in files)
        {
            // 轻量占位 Track：仅文件名作为 Title，其他字段为空
            Queue.Add(_metadataReader.CreateFallback(path));
        }
    }

    /// <summary>按索引移除单项；若是当前播放曲则停止播放并同步索引。</summary>
    [RelayCommand]
    private void RemoveTrack(int index)
    {
        if (index < 0 || index >= Queue.Count) return;

        bool isCurrent = (index == CurrentIndex);
        Queue.RemoveAt(index);

        // 修正 CurrentIndex
        if (isCurrent)
        {
            UnloadCurrentTrack();
        }
        else if (index < CurrentIndex)
        {
            CurrentIndex--; // 当前曲位置前的项被删，当前曲索引下移 1
        }

        // 修正 _shuffleHistory：
        // 1) 删除该索引本身  2) 大于该索引的全部 -1
        var rebuilt = new HashSet<int>();
        foreach (var i in _shuffleHistory)
        {
            if (i == index) continue;
            rebuilt.Add(i > index ? i - 1 : i);
        }
        _shuffleHistory.Clear();
        foreach (var i in rebuilt) _shuffleHistory.Add(i);
    }

    /// <summary>清空整个队列 → 停止播放，重置索引和历史。</summary>
    [RelayCommand]
    private void ClearQueue()
    {
        UnloadCurrentTrack();
        Queue.Clear();
        _shuffleHistory.Clear();
    }

    /// <summary>
    /// 停止播放并清空 PlayerBar 上与"当前曲"相关的所有 VM 状态。
    /// 必须用 _player.Unload() 而不是 Stop() —— 后者保留底层 reader, 用户再点 Play 会重播刚才那首。
    /// 同时令 _playToken 自增，使任何 in-flight 的 PlayTrackAtAsync 被顶替丢弃。
    /// </summary>
    private void UnloadCurrentTrack()
    {
        _playToken++; // 顶替任何 in-flight 的播放调用
        _player.Unload();
        CurrentIndex = -1;
        CurrentTrack = null;
        AlbumArtImage = null;
        Position = TimeSpan.Zero;
        Duration = TimeSpan.Zero;
    }

    /// <summary>双击列表项 → 播放该索引曲目。重置 shuffleHistory（视为新会话）。</summary>
    [RelayCommand]
    private async Task PlayTrackAt(int index)
    {
        _shuffleHistory.Clear();
        await PlayTrackAtAsync(index);
    }

    /// <summary>下一首按钮（用户手动）。RepeatOne 下也跳走，不重播当前。</summary>
    [RelayCommand(CanExecute = nameof(HasCurrentTrack))]
    private async Task NextTrack()
    {
        var next = CalculateNextIndex();
        if (next == -1) return;
        await PlayTrackAtAsync(next);
    }

    /// <summary>上一首按钮。</summary>
    [RelayCommand(CanExecute = nameof(HasCurrentTrack))]
    private async Task PrevTrack()
    {
        var prev = CalculatePrevIndex();
        if (prev == -1) return;
        await PlayTrackAtAsync(prev);
    }

    /// <summary>切换 Shuffle 开关。同时清空已播过历史（避免状态语义混乱）。</summary>
    [RelayCommand]
    private void ToggleShuffle()
    {
        ShuffleEnabled = !ShuffleEnabled;
        _shuffleHistory.Clear();
        if (CurrentIndex >= 0) _shuffleHistory.Add(CurrentIndex); // 当前曲不应再被随机选中
    }

    /// <summary>循环模式三态循环：Off → List → One → Off。</summary>
    [RelayCommand]
    private void CycleRepeat()
    {
        RepeatMode = RepeatMode switch
        {
            RepeatMode.Off  => RepeatMode.List,
            RepeatMode.List => RepeatMode.One,
            _               => RepeatMode.Off,
        };
    }

    /// <summary>
    /// IPlaybackService.TrackEnded 订阅：根据循环/随机模式自动推进。
    /// </summary>
    private async void HandleTrackEnded()
    {
        // 单曲循环：仅在自动播完时重播当前
        if (RepeatMode == RepeatMode.One && CurrentIndex >= 0)
        {
            await PlayTrackAtAsync(CurrentIndex);
            return;
        }

        var next = CalculateNextIndex();
        if (next == -1)
        {
            // 列表播完且不循环 → 维持 Stopped, 当前索引保留以便用户重新点击 Play
            return;
        }
        await PlayTrackAtAsync(next);
    }

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
        IOptions<AppSettings> options,
        ITrackMetadataReader metadataReader)
    {
        _player = player;
        _fileDialog = fileDialog;
        _persistence = persistence;
        _options = options;
        _metadataReader = metadataReader;
        _settings = options.Value;

        // 订阅播放服务事件 —— 所有事件已由服务封送到 UI 线程，handler 可直接更新属性
        _player.PositionChanged += HandlePositionChanged;
        _player.StateChanged += HandleStateChanged;
        _player.DurationChanged += HandleDurationChanged;
        _player.TrackChanged += HandleTrackChanged;
        _player.PlaybackError += HandlePlaybackError;
        _player.TrackEnded += HandleTrackEnded;

        // 队列变化时强制刷新 Next/Prev 命令可用性
        Queue.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasCurrentTrack));
            NextTrackCommand.NotifyCanExecuteChanged();
            PrevTrackCommand.NotifyCanExecuteChanged();
        };

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
    /// PlayerBar 上的 📂 按钮：选文件 → 全部入队 → 从第一首新加入的开始播。
    /// 与 PlaylistView 的 [+ 添加] 区别：本命令会立即触发播放。
    /// </summary>
    [RelayCommand]
    private async Task OpenFilesAsync()
    {
        var files = _fileDialog.OpenFiles(
            "Audio Files|*.mp3;*.wma;*.flac;*.aac;*.wav",
            multiselect: true);
        if (files.Count == 0) return;

        int firstNewIndex = Queue.Count;
        foreach (var path in files)
        {
            Queue.Add(_metadataReader.CreateFallback(path));
        }

        _shuffleHistory.Clear();
        await PlayTrackAtAsync(firstNewIndex);
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
        _player.TrackEnded -= HandleTrackEnded;
        _player.Dispose();
        await _persistence.SaveAsync(_settings);
    }
}
