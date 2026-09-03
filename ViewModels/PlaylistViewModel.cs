using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DPlayer.Models;
using DPlayer.Services;

namespace DPlayer.ViewModels;

/// <summary>
/// 播放队列 ViewModel —— 负责队列状态、Shuffle/Repeat、自动推进算法。
///
/// 与 PlayerViewModel 的边界：
///   - 本 VM 订阅 IPlaybackService.TrackEnded 触发推进；不订阅 transport 事件
///   - 不持有 PlayerViewModel 引用；通过 IPlaybackService 调用 LoadAsync/Play/Unload/Stop
///
/// Phase 6: 持久化由 PlaylistsViewModel 容器 + MainViewModel debounce save 统一负责;
/// 本 VM 仅暴露 ToRecord() 把当前状态打包为不可变 Playlist record。
/// </summary>
public partial class PlaylistViewModel : ObservableObject
{
    private readonly IPlaybackService _player;
    private readonly IFileDialogService _fileDialog;
    private readonly ITrackMetadataReader _metadataReader;

    /// <summary>由 PlaylistsViewModel 在 HookPlaylistVm 中设置，用于读取全局 Shuffle/Repeat 状态。</summary>
    internal PlaylistsViewModel? Container { get; set; }

    /// <summary>歌单主键(GUID); 创建时一次性确定, 不可变。</summary>
    public string Id { get; }

    /// <summary>文件夹绑定歌单的源文件夹路径; null 表示普通歌单(Phase 10)。</summary>
    public string? SourceFolder { get; }

    /// <summary>是否为文件夹绑定歌单(Phase 10)。sidebar DataTemplate 用。</summary>
    public bool HasSourceFolder => SourceFolder is not null;

    /// <summary>歌单显示名; 仅展示, 可重命名, 可重复。</summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>容器侧设置: 本 VM 是否为当前正在播放的歌单。仅读;由 PlaylistsViewModel 维护。</summary>
    [ObservableProperty]
    private bool _isActivePlaylist;

    /// <summary>该歌单是否正在后台扫描(Phase 10)。由 PlaylistsViewModel 设置。</summary>
    [ObservableProperty]
    private bool _isScanning;

    /// <summary>最近一次扫描是否失败(Phase 10)。由 PlaylistsViewModel 设置。</summary>
    [ObservableProperty]
    private bool _hasScanError;

    // —— 排序状态 ——

    private string? _sortColumn;
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;

    /// <summary>排序后的队列视图。ListBox 绑定此属性而非直接绑 Queue。</summary>
    public ICollectionView SortedView { get; private set; }

    /// <summary>当前播放队列。ObservableCollection 自动通知 UI 增删改。</summary>
    public ObservableCollection<Track> Queue { get; } = new();

    /// <summary>当前播放曲在 Queue 中的索引；-1 表示未选/队列空。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentTrack))]
    [NotifyCanExecuteChangedFor(nameof(PlayCurrentCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextTrackCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrevTrackCommand))]
    private int _currentIndex = -1;

    /// <summary>UI 列表选中项（与"当前播放曲"无关，仅供 Delete 键定位）。</summary>
    [ObservableProperty]
    private Track? _selectedTrack;

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

    /// <summary>全局 Shuffle 状态（代理到 Container）。</summary>
    private bool ShuffleEnabled => Container?.ShuffleEnabled ?? false;

    /// <summary>全局 Repeat 状态（代理到 Container）。</summary>
    private RepeatMode RepeatMode => Container?.RepeatMode ?? RepeatMode.Off;

    /// <summary>当前是否有正在播放的曲（用于 Next/Prev 按钮 CanExecute）。</summary>
    public bool HasCurrentTrack => CurrentIndex >= 0 && CurrentIndex < Queue.Count;

    public PlaylistViewModel(
        Models.Playlist seed,
        IPlaybackService player,
        IFileDialogService fileDialog,
        ITrackMetadataReader metadataReader)
    {
        ArgumentNullException.ThrowIfNull(seed);
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _fileDialog = fileDialog ?? throw new ArgumentNullException(nameof(fileDialog));
        _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));

        Id = seed.Id;
        Name = seed.Name;
        SourceFolder = seed.SourceFolder;

        _player.TrackEnded += HandleTrackEnded;

        // 队列变化时强制刷新 Next/Prev 命令可用性
        Queue.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasCurrentTrack));
            NextTrackCommand.NotifyCanExecuteChanged();
            PrevTrackCommand.NotifyCanExecuteChanged();
            PlayCurrentCommand.NotifyCanExecuteChanged();
        };

        // 排序视图：ListBox 绑定 SortedView，排序通过 SortDescriptions 驱动
        SortedView = CollectionViewSource.GetDefaultView(Queue);

        // Port LoadFromDisk: 过滤不存在的文件, 重映射 CurrentIndex
        var seedItems = seed.Items ?? Array.Empty<string>();
        var existing = seedItems.Where(File.Exists).ToList();
        foreach (var path in existing)
        {
            // 占位 Track —— 与 OpenAndPlay 流程一致；用户首次播放时由 PlayTrackAtAsync 升级为完整元数据
            Queue.Add(_metadataReader.CreateFallback(path));
        }
        CurrentIndex = MapCurrentIndexAfterFilter(seedItems, existing, seed.CurrentIndex);
    }

    /// <summary>
    /// 把当前 VM 状态打包成持久化用 Playlist record。MainViewModel 在 BuildSnapshot 时调用。
    /// 隐式契约：本方法**仅读、无副作用**。
    /// </summary>
    public Models.Playlist ToRecord() => new(
        Id: Id,
        Name: Name,
        Items: Queue.Select(t => t.FilePath).ToArray(),
        CurrentIndex: CurrentIndex,
        ShuffleEnabled: false,
        RepeatMode: RepeatMode.Off,
        SourceFolder: SourceFolder);

    /// <summary>按指定列物理重排 Queue。再次点击同列切换升/降序。</summary>
    public void SortBy(string column)
    {
        if (_sortColumn == column)
            _sortDirection = _sortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        else
        {
            _sortColumn = column;
            _sortDirection = ListSortDirection.Ascending;
        }

        // 记住当前播放曲，排序后更新 CurrentIndex
        var currentTrack = CurrentIndex >= 0 && CurrentIndex < Queue.Count
            ? Queue[CurrentIndex] : null;

        var sorted = _sortDirection == ListSortDirection.Ascending
            ? Queue.OrderBy(t => GetSortKey(t, column)).ToList()
            : Queue.OrderByDescending(t => GetSortKey(t, column)).ToList();

        Queue.Clear();
        foreach (var item in sorted)
            Queue.Add(item);

        // 跟踪当前播放曲的新位置
        if (currentTrack is not null)
            CurrentIndex = Queue.IndexOf(currentTrack);

        OnPropertyChanged(nameof(SortedView));
    }

    private static object? GetSortKey(Track t, string column) => column switch
    {
        "TrackNumber" => t.TrackNumber ?? 0,
        "Title"       => t.Title,
        "Artist"      => t.Artist ?? "",
        "Album"       => t.Album ?? "",
        "Duration"    => t.Duration,
        _             => null
    };

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
            // 文件损坏 / 不存在 → 跳过到下一首（reader 已不会抛，此 catch 兜底 LoadAsync 异常）
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

    /// <summary>
    /// 停止播放并清空 PlayerBar 上与"当前曲"相关的所有 VM 状态。
    /// 必须用 _player.Unload() 而不是 Stop() —— 后者保留底层 reader, 用户再点 Play 会重播刚才那首。
    /// 同时令 _playToken 自增，使任何 in-flight 的 PlayTrackAtAsync 被顶替丢弃。
    ///
    /// 注：本方法只清"队列侧"的状态（CurrentIndex）；PlayerViewModel 的
    /// CurrentTrack/Position/Duration/AlbumArtImage 由 IPlaybackService.Unload()
    /// 引发的事件链路自动清零。
    /// </summary>
    private void UnloadCurrentTrack()
    {
        _playToken++; // 顶替任何 in-flight 的播放调用
        _player.Unload();
        CurrentIndex = -1;
    }

    // —— Phase 2 命令 ——

    /// <summary>文件对话框多选 → 入队（不读元数据，仅占位）。</summary>
    [RelayCommand]
    private async Task AddToQueue()
    {
        var files = _fileDialog.OpenFiles(
            "Audio Files|*.mp3;*.wma;*.flac;*.aac;*.wav",
            multiselect: true);
        if (files.Count == 0) return;

        foreach (var path in files)
        {
            var track = await _metadataReader.ReadAsync(path);
            Queue.Add(track);
        }
    }

    /// <summary>选择文件夹 → 扫描音频文件 → 入队当前歌单。</summary>
    [RelayCommand]
    private async Task ImportFolderToCurrent()
    {
        var folder = _fileDialog.OpenFolder();
        if (folder is null) return;

        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".mp3", ".wma", ".flac", ".aac", ".wav" };

        string[] files;
        try { files = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories); }
        catch { return; }

        foreach (var path in files)
        {
            if (!extensions.Contains(Path.GetExtension(path))) continue;
            try
            {
                var track = await _metadataReader.ReadAsync(path);
                Queue.Add(track);
            }
            catch { /* 跳过损坏文件 */ }
        }
    }

    /// <summary>
    /// 外部文件拖入入队。
    /// 与 AddToQueue 同语义：读取元数据入队，不自动播放。
    /// 与 AddToQueue 区别：入口是 OS DragDrop（V 层已过滤白名单后缀）而非文件对话框。
    ///
    /// View 层契约：传入的 paths 已经过 .mp3/.wma/.flac/.aac/.wav 后缀过滤；
    /// 本命令不再二次过滤，避免双重职责。
    /// </summary>
    [RelayCommand]
    private async Task DropExternalFiles(IReadOnlyList<string> paths)
    {
        if (paths is null || paths.Count == 0) return;

        foreach (var path in paths)
        {
            var track = await _metadataReader.ReadAsync(path);
            Queue.Add(track);
        }
    }

    /// <summary>
    /// 队列内拖拽重排（Phase 5）。
    ///
    /// 算法用**引用身份**（ReferenceEquals / ReferenceEqualityComparer）回找
    /// CurrentIndex 与 _shuffleHistory；不用索引算术，避免多源多目标插入时的
    /// 前移/后移混合错误。Track 是 record（结构相等），不能用 IndexOf —— 同一
    /// 文件加入两次产生两个结构相等但引用不同的占位 Track，IndexOf 会返回错误首匹配。
    ///
    /// 不中断播放：currentTrackObj 在 Queue 重排后仍是同一个 record 引用，
    /// NAudio 不知道 Queue 重排，继续推流；仅 CurrentIndex 跟随对象身份指向新位置。
    ///
    /// 幂等：拖到原位（targetIndex 等于源位置或紧邻）时算法天然 no-op。
    /// </summary>
    [RelayCommand]
    private void MoveTracks(MoveTracksArgs args)
    {
        if (args is null) return;
        var sources = args.SourceIndices;
        if (sources is null || sources.Count == 0) return;

        // 1. 顶替 in-flight，与 RemoveTrack 同纪律
        _playToken++;

        // 2. 缓存被移动对象（按 sources 顺序，保证插入时块内顺序保留）
        var moving = new List<Track>(sources.Count);
        foreach (var i in sources)
        {
            if (i < 0 || i >= Queue.Count) return; // 防御式：越界即放弃，View 层应避免
            moving.Add(Queue[i]);
        }

        // 3. 缓存当前曲对象 + history 对象集合（用对象身份做映射）
        var currentTrackObj = (CurrentIndex >= 0 && CurrentIndex < Queue.Count)
            ? Queue[CurrentIndex] : null;
        var historyObjs = new HashSet<Track>(_shuffleHistory.Count, ReferenceEqualityComparer.Instance);
        foreach (var i in _shuffleHistory)
        {
            if (i >= 0 && i < Queue.Count) historyObjs.Add(Queue[i]);
        }

        // 4. 修正 targetIndex —— 删源位置后，目标位置可能往前缩
        int adjustedTarget = args.TargetIndex;
        foreach (var i in sources)
        {
            if (i < args.TargetIndex) adjustedTarget--;
        }
        if (adjustedTarget < 0) adjustedTarget = 0;

        // 5. 倒序删源
        foreach (var i in sources.OrderByDescending(x => x))
        {
            Queue.RemoveAt(i);
        }

        // 6. 在 adjustedTarget 处依次插入（保留块内顺序）
        if (adjustedTarget > Queue.Count) adjustedTarget = Queue.Count; // 防御式
        for (int k = 0; k < moving.Count; k++)
        {
            Queue.Insert(adjustedTarget + k, moving[k]);
        }

        // 7. 重建 CurrentIndex（按引用身份回找；不能用 Queue.IndexOf —— Track 是 record，
        //    结构相等会让重复 FilePath 占位 Track 的 IndexOf 返回错误首匹配）
        CurrentIndex = -1;
        if (currentTrackObj != null)
        {
            for (int i = 0; i < Queue.Count; i++)
            {
                if (ReferenceEquals(Queue[i], currentTrackObj)) { CurrentIndex = i; break; }
            }
        }

        // 8. 重建 _shuffleHistory(按对象身份回找)
        _shuffleHistory.Clear();
        for (int i = 0; i < Queue.Count; i++)
        {
            if (historyObjs.Contains(Queue[i])) _shuffleHistory.Add(i);
        }
    }

    /// <summary>按索引移除单项；若是当前播放曲则停止播放并同步索引。</summary>
    [RelayCommand]
    private void RemoveTrack(int index)
    {
        if (index < 0 || index >= Queue.Count) return;

        // Phase 5：顶替任何 in-flight PlayTrackAtAsync —— 防止
        // "用户在元数据读取期间删除非当前曲" 让 Queue[i] = meta 写到错位
        // (COUPLING.md §5 残留债)
        _playToken++;

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

    /// <summary>清空已播过历史（由 PlaylistsViewModel.ToggleShuffle 调用）。</summary>
    internal void ClearShuffleHistory()
    {
        _shuffleHistory.Clear();
        if (CurrentIndex >= 0) _shuffleHistory.Add(CurrentIndex);
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

    /// <summary>由 MainViewModel.CleanupAsync / PlaylistsViewModel.UnhookPlaylistVm 调用 —— 解绑 TrackEnded 订阅。</summary>
    public void Cleanup()
    {
        _player.TrackEnded -= HandleTrackEnded;
    }

    /// <summary>
    /// 把过滤前的 CurrentIndex 映射到过滤后的索引。
    ///
    /// 算法：
    ///   - 若原索引仍在 surviving 中 → 直接返回它在 surviving 中的位置
    ///   - 若原索引项丢失 → 向后找原数组中第一个仍存在的项；找不到则向前回退
    ///   - 若 surviving 为空（外层已提前 return 处理）/ 原索引无效 → 返回 -1
    ///
    /// 设计取舍（spec §5.1）：选择"向后滑"而非"重置到 0"——
    /// 用户的"当前曲"语义上是听到了一半，跳到后面延续聆听比回到列表顶部更接近预期。
    /// 静态纯函数：无副作用，便于人工推理。
    /// </summary>
    private static int MapCurrentIndexAfterFilter(
        IReadOnlyList<string> originalItems,
        IReadOnlyList<string> survivingPaths,
        int originalIndex)
    {
        if (survivingPaths.Count == 0) return -1;
        if (originalIndex < 0 || originalIndex >= originalItems.Count) return 0;

        // Case 1: 原项仍存在 —— 直接定位
        var originalPath = originalItems[originalIndex];
        if (File.Exists(originalPath))
        {
            var idx = IndexOf(survivingPaths, originalPath);
            if (idx >= 0) return idx;
        }

        // Case 2: 原项丢失 —— 向后滑：找原数组中 originalIndex 之后第一个仍存在的项
        for (int i = originalIndex + 1; i < originalItems.Count; i++)
        {
            var idx = IndexOf(survivingPaths, originalItems[i]);
            if (idx >= 0) return idx;
        }

        // Case 3: 后方无幸存 —— 向前回退
        for (int i = originalIndex - 1; i >= 0; i--)
        {
            var idx = IndexOf(survivingPaths, originalItems[i]);
            if (idx >= 0) return idx;
        }

        // 理论不可达（surviving 非空意味着 originalItems 中至少一个 File.Exists）
        return 0;
    }

    /// <summary>IReadOnlyList&lt;string&gt; 没有 IndexOf 实例方法（C# 12 / .NET 8 起 ReadOnlySpan 扩展会冲突），手写线性查找。</summary>
    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == value) return i;
        }
        return -1;
    }

    /// <summary>
    /// 启动后用户首次按 ▶ 走的命令：加载并播放 CurrentIndex 指向的曲目。
    /// PlayerBar 的 ▶ 按钮通过 DataTrigger 在 PlayerVM.CurrentTrack==null 时
    /// 跨级绑定到本命令；TrackChanged 触发后回退到 PlayerVM.PlayPauseCommand。
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasCurrentTrack))]
    private async Task PlayCurrent()
    {
        if (CurrentIndex < 0 || CurrentIndex >= Queue.Count) return;
        await PlayTrackAtAsync(CurrentIndex);
    }
}
