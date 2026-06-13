# Phase 6 — 多命名歌单 + 偿债 #1 + xUnit 骨架 设计文档

> **状态**: 设计已审定, 待 writing-plans 拆任务
> **创建**: 2026-06-13
> **预算**: ~14h
> **前置**: Phase 5 (drag-drop) 已合并 master, debt #5 已偿还

---

## 1. 目标 (Goal)

为 UmaPlayer 引入"命名歌单(Named Playlists)"概念: 用户可以创建/删除/重命名多个独立的歌单, 在它们之间切换查看而**不打断当前播放**(Spotify 模型)。同时附带两件低成本的清债工作:

- **偿债 #1 (最小版)**: 提供 `byte[] → BitmapImage` 的 IValueConverter, 解锁未来在 XAML 中直接绑 `Track.AlbumArt` 的能力。PlayerViewModel 现有 BitmapImage 字段不在本期拆除。
- **xUnit 骨架**: 新建 `Tests/UmaPlayer.Tests.csproj`, 1 个 smoke test, Coverlet 配齐。为 Phase 7+ 真正的 VM 单测铺路。

## 2. 范围 (Scope)

### 2.1 In-Scope

- 数据模型: `QueueState` 升级为 v2 (含 `Playlist[]` 与 `CurrentPlaylistId`); 启动时自动迁移 v1 → v2
- 持久化: 用 `IPlaylistService` / `JsonPlaylistService` 替换 `IQueuePersistence` / `JsonQueuePersistence`
- ViewModel: 新增 `PlaylistsViewModel` 容器; `PlaylistViewModel` 沿用 + 加 Id/Name + ToRecord 投影
- View: `MainWindow` 新增左侧 160px 侧边栏 (`PlaylistsSidebarView`) + 共享 `PromptDialog`
- "查看 vs 播放"双状态: `ViewedPlaylist` (UI 选中) ≠ `CurrentPlaylist` (NAudio 在播)
- ▶ 标记仅在 `ViewedPlaylist == CurrentPlaylist` 时显示
- 删除最后一个歌单 → 自动重建空"默认歌单"
- `Converters/BytesToBitmapImageConverter.cs` + 注册到 `App.xaml`
- `Tests/UmaPlayer.Tests.csproj` + 1 个 smoke test (Track 结构相等)

### 2.2 Out-of-Scope (显式不做)

- ❌ 跨歌单拖拽 (从 Queue 拖到侧边栏另一歌单) — Phase 7+ 候选
- ❌ PlayerViewModel 中 BitmapImage 字段彻底拆除 — 留作债务部分偿还
- ❌ Phase 5 PlaylistViewModel 9 个命令的回填式单测覆盖 — 超 14h 预算
- ❌ JSON 原子写 / .bak / temp-then-rename — MVP 不破例
- ❌ m3u/PLS 导入导出 — Phase 7+ 候选
- ❌ 内联编辑歌单名 — 用 PromptDialog 替代, 避免 TextBox 焦点状态机
- ❌ CI/CD 配置 — Phase 6 范围只到 `dotnet test` 在本地能跑

## 3. 架构 (Architecture)

### 3.1 三个独立子项与执行顺序

按依赖排序, 子项之间可独立 commit:

```
A. xUnit 骨架 (~2h)         ← 先做, 给后面的重构铺测试网
   └─ Tests/UmaPlayer.Tests.csproj
      + 1 个 smoke test (Track 值相等)
      + Coverlet collector

B. 多命名歌单 (~9h)         ← 主线
   ├─ Models: QueueState v2 + Playlist record
   ├─ Services: IPlaylistService / JsonPlaylistService (替换 IQueuePersistence)
   ├─ ViewModels: PlaylistsViewModel (新, 容器)
   │              PlaylistViewModel (沿用, 加 Id/Name/IsActivePlaylist/ToRecord)
   │              MainViewModel (改造, 删 Queue/CurrentTrack 代理)
   ├─ Views: MainWindow Grid 拆列
   │         PlaylistsSidebarView (新)
   │         PromptDialog (新, 共享给"新建/重命名")
   │         PlaylistView 双击播放路由改走 PlaylistsVM.HandleDoubleClickPlay
   ├─ 迁移: 启动时检测 schemaVersion=1 → 包成"默认歌单"v2
   └─ 边界: 删最后一个时自动重建默认

C. 偿债 #1 最小版 (~3h)     ← 收尾
   ├─ Converters/BytesToBitmapImageConverter.cs
   └─ App.xaml 注册资源 BytesToBitmapImage
```

### 3.2 核心约束

| 约束 | 实现方式 |
|---|---|
| 切换查看 ≠ 切换播放 | MainVM 双状态: `Playlists.CurrentPlaylist` (NAudio 在播) + `Playlists.ViewedPlaylist` (UI 选中) |
| 接管播放 | 仅在双击 `ViewedPlaylist` 内某项时, `CurrentPlaylist = ViewedPlaylist` 然后 PlayTrackAt |
| 各歌单独立 Shuffle/Repeat/CurrentIndex | 这三个字段下沉到 `Playlist` record, 每歌单独立保留 |
| GUID 主键 | `Playlist.Id` 是 GUID, 创建时生成, 不可变; `Name` 仅展示, 可重复可重命名 |
| ▶ 标记 | 仅在 `IsActivePlaylist == true` (即此 PlaylistVM == CurrentPlaylist) 时显示 |

## 4. 数据模型 (Schema v2)

### 4.1 `Models/Playlist.cs` (新增)

```csharp
namespace UmaPlayer.Models;

/// <summary>
/// 一个命名歌单, 不可变 record。Id 是创建时生成的 GUID, 主键; Name 仅展示。
/// ShuffleEnabled / RepeatMode / CurrentIndex 下沉到歌单级别 —— 各歌单独立。
/// </summary>
public sealed record Playlist(
    string Id,
    string Name,
    IReadOnlyList<string> Items,
    int CurrentIndex,
    bool ShuffleEnabled,
    RepeatMode RepeatMode);
```

### 4.2 `Models/QueueState.cs` (重写为 v2)

```csharp
namespace UmaPlayer.Models;

/// <summary>
/// v2: 一份顶层快照, 包含所有歌单与"正在播放"指针。
/// SchemaVersion 永远写 2; v1 在反序列化层一次性迁移, 内存不留 v1 表示。
/// </summary>
public sealed record QueueState(
    IReadOnlyList<Playlist> Playlists,
    string CurrentPlaylistId,
    int SchemaVersion = 2);
```

**注意**:
- v2 顶层不再保存 `Items / CurrentIndex / ShuffleEnabled / RepeatMode` —— 全部下沉到 `Playlist`
- `CurrentPlaylistId` 必须匹配 `Playlists` 中某条 `Id`; LoadAsync 内若不匹配 → 退回 `Playlists[0].Id`
- `ViewedPlaylistId` **不持久化** (UI 状态, 重启回到 `CurrentPlaylistId` 即可)

### 4.3 v1 → v2 迁移 (一次性, 在 `JsonPlaylistService.LoadAsync` 内)

```csharp
// 伪代码 — 真实实现见后续 plan
var doc = JsonDocument.Parse(text);
int version = doc.RootElement.TryGetProperty("SchemaVersion", out var v) ? v.GetInt32() : 1;

if (version == 1) {
    var v1 = JsonSerializer.Deserialize<QueueStateV1>(text, _options);
    var defaultPlaylist = new Playlist(
        Id: Guid.NewGuid().ToString(),
        Name: "默认歌单",
        Items: v1.Items ?? Array.Empty<string>(),
        CurrentIndex: v1.CurrentIndex,
        ShuffleEnabled: v1.ShuffleEnabled,
        RepeatMode: v1.RepeatMode);
    var v2 = new QueueState(
        Playlists: new[] { defaultPlaylist },
        CurrentPlaylistId: defaultPlaylist.Id);
    try { await SaveAsync(v2); } catch { /* swallow — 内存已是 v2, 下次启动重迁 */ }
    return v2;
}
if (version == 2) return JsonSerializer.Deserialize<QueueState>(text, _options) ?? Seed();
return Seed();  // 未来版本回滚保护
```

`QueueStateV1` 是 `internal sealed record` 仅在 `JsonPlaylistService` 文件内定义, 迁移完即不暴露。

### 4.4 删最后一个歌单边界

`PlaylistsViewModel.RemovePlaylistCommand`:

```
if (Playlists.Count == 1) {
    var fresh = new PlaylistViewModel(new Playlist(
        Guid.NewGuid().ToString(), "默认歌单", [], -1, false, RepeatMode.Off), ...);
    Playlists[0] = fresh;
    CurrentPlaylist = fresh;
    ViewedPlaylist = fresh;
} else {
    var idx = Playlists.IndexOf(target);
    Playlists.RemoveAt(idx);
    if (target.Id == CurrentPlaylist.Id) CurrentPlaylist = Playlists[0];
    if (target.Id == ViewedPlaylist.Id)  ViewedPlaylist  = CurrentPlaylist;
}
```

## 5. Service 层

### 5.1 `Services/IPlaylistService.cs` (新增, 替换 IQueuePersistence)

```csharp
namespace UmaPlayer.Services;

public interface IPlaylistService
{
    /// <summary>启动时加载; 包含 v1→v2 自动迁移; 不抛异常, 失败回种子状态。</summary>
    Task<QueueState> LoadAsync();

    /// <summary>保存当前状态; 与 JsonQueuePersistence 一致, IO 失败抛 IOException。</summary>
    Task SaveAsync(QueueState snapshot);
}
```

**契约 (与 Phase 2 沉淀一致, 不打破)**: `Load` 不抛 / `Save` 抛。

### 5.2 `Services/JsonPlaylistService.cs` (新增, 替换 JsonQueuePersistence)

- 路径: `%LocalAppData%\UmaPlayer\queue.json` (沿用文件名, 避免再迁移)
- `SemaphoreSlim(1, 1)`, `JsonStringEnumConverter`, `WriteIndented = true` (沿用)
- `LoadAsync` 分支:
  1. 文件不存在 → 返回种子 (`new QueueState([new Playlist(GUID, "默认歌单", [], -1, false, Off)], 那个 GUID)`)
  2. 文件存在但 JSON 损坏 → 同上, 静默回退 (沿用旧 catch-all)
  3. SchemaVersion == 1 → 走 4.3 迁移分支
  4. SchemaVersion == 2 → 正常反序列化; 若 `CurrentPlaylistId` 不匹配任何 `Playlists[].Id` → 退回 `Playlists[0].Id`
  5. 既非 1 也非 2 → 静默回退到种子
- `SaveAsync` 不变 (SchemaVersion 永远写 2)

### 5.3 不做的事 (YAGNI)

- ❌ 不暴露 `AddPlaylist/Rename/Remove` 细粒度 CRUD 在 Service 上 —— VM 持有 `ObservableCollection<PlaylistViewModel>`, debounce 整体 Save (与 Phase 2 一致)
- ❌ 不做 `.bak` / temp-then-rename / 跨进程并发写保护

### 5.4 持久化触发点 (MainViewModel 集中)

MainVM 在以下事件 debounce 500ms 后 `await _service.SaveAsync(_playlistsVm.BuildSnapshot())`:

- `Playlists.Playlists.CollectionChanged` (增/删歌单)
- 任一 `PlaylistViewModel.Queue.CollectionChanged`
- 任一 `PlaylistViewModel.PropertyChanged` 命中 `Name / CurrentIndex / ShuffleEnabled / RepeatMode`
- `Playlists.PropertyChanged` 命中 `CurrentPlaylist`

`PlaylistsViewModel.BuildSnapshot()` 一遍扫 `Playlists`, 把每个 PlaylistVM 投影回 `Playlist` record。

### 5.5 DI 注册 (App.xaml.cs)

```csharp
// 删除:
services.AddSingleton<IQueuePersistence, JsonQueuePersistence>();

// 新增:
services.AddSingleton<IPlaylistService, JsonPlaylistService>();
services.AddSingleton<PlaylistsViewModel>();
services.AddTransient<Func<Playlist, PlaylistViewModel>>(sp => playlist =>
    new PlaylistViewModel(playlist, sp.GetRequiredService<PlaybackViewModel>(), ...));
```

`IQueuePersistence` / `JsonQueuePersistence` / 旧 v1 字段全部删除 —— break-the-old-and-replace 重构, 不留遗留接口。

## 6. ViewModel 层

### 6.1 `ViewModels/PlaylistsViewModel.cs` (新增)

```csharp
namespace UmaPlayer.ViewModels;

public partial class PlaylistsViewModel : ObservableObject
{
    private readonly IPlaylistService _service;
    private readonly Func<Playlist, PlaylistViewModel> _factory;

    /// <summary>所有歌单, 侧边栏 ItemsSource。</summary>
    public ObservableCollection<PlaylistViewModel> Playlists { get; } = new();

    /// <summary>侧边栏选中(用户在看的)歌单。绑定 ListBox.SelectedItem。</summary>
    [ObservableProperty] private PlaylistViewModel? viewedPlaylist;

    /// <summary>正在播放的歌单, 与 NAudio 绑定。可与 Viewed 不同。</summary>
    [ObservableProperty] private PlaylistViewModel? currentPlaylist;

    public PlaylistsViewModel(IPlaylistService service, Func<Playlist, PlaylistViewModel> factory)
    { _service = service; _factory = factory; }

    /// <summary>启动时调用; 从持久化加载并填充 Playlists/Current/Viewed。</summary>
    public async Task LoadAsync()
    {
        var state = await _service.LoadAsync();
        foreach (var p in state.Playlists) Playlists.Add(_factory(p));
        CurrentPlaylist = Playlists.FirstOrDefault(vm => vm.Id == state.CurrentPlaylistId)
                          ?? Playlists[0];
        ViewedPlaylist = CurrentPlaylist;
        RecomputeIsActiveFlags();
    }

    [RelayCommand]
    private void AddPlaylist()
    {
        var name = PromptDialog.Show("新建歌单", "请输入歌单名");
        if (string.IsNullOrWhiteSpace(name)) return;
        var fresh = _factory(new Playlist(Guid.NewGuid().ToString(), name.Trim(), [], -1, false, RepeatMode.Off));
        Playlists.Add(fresh);
        ViewedPlaylist = fresh;
    }

    [RelayCommand]
    private void RemovePlaylist(PlaylistViewModel target)
    {
        if (MessageBox.Show($"确认删除歌单\"{target.Name}\"?", "确认", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        if (Playlists.Count == 1) {
            var fresh = _factory(new Playlist(Guid.NewGuid().ToString(), "默认歌单", [], -1, false, RepeatMode.Off));
            Playlists[0] = fresh;
            CurrentPlaylist = fresh;
            ViewedPlaylist = fresh;
        } else {
            int idx = Playlists.IndexOf(target);
            Playlists.RemoveAt(idx);
            if (target.Id == CurrentPlaylist?.Id) CurrentPlaylist = Playlists[0];
            if (target.Id == ViewedPlaylist?.Id)  ViewedPlaylist  = CurrentPlaylist;
        }
        RecomputeIsActiveFlags();
    }

    [RelayCommand]
    private void RenamePlaylist(PlaylistViewModel target)
    {
        var name = PromptDialog.Show("重命名歌单", "请输入新名字", target.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        target.Name = name.Trim();
    }

    /// <summary>
    /// PlaylistView 双击列表项时调用。
    /// 双击 ViewedPlaylist 内项 → 接管播放 (CurrentPlaylist = Viewed) + PlayTrackAt。
    /// </summary>
    public void HandleDoubleClickPlay(PlaylistViewModel target, int index)
    {
        if (CurrentPlaylist?.Id != target.Id) {
            CurrentPlaylist = target;
            RecomputeIsActiveFlags();
        }
        target.PlayTrackAtCommand.Execute(index);
    }

    public QueueState BuildSnapshot() => new(
        Playlists: Playlists.Select(vm => vm.ToRecord()).ToArray(),
        CurrentPlaylistId: CurrentPlaylist?.Id ?? Playlists[0].Id);

    partial void OnCurrentPlaylistChanged(PlaylistViewModel? value) => RecomputeIsActiveFlags();

    private void RecomputeIsActiveFlags()
    {
        foreach (var vm in Playlists)
            vm.IsActivePlaylist = (vm.Id == CurrentPlaylist?.Id);
    }
}
```

### 6.2 `ViewModels/PlaylistViewModel.cs` (沿用 + 小幅瘦身)

**保留** (Phase 5 已有, 不动):
- `ObservableCollection<Track> Queue`
- `CurrentIndex`, `ShuffleEnabled`, `RepeatMode`
- `PlayTrackAt`, `Remove`, `Move`, `AddToQueue`, `Clear`, `ToggleShuffle`, `CycleRepeat`, `DropExternalFiles`, `MoveTracks`

**新增字段**:
```csharp
public string Id { get; }                                       // GUID, 构造注入, 不可变
[ObservableProperty] private string name = string.Empty;        // 改名后 PlaylistsVM 触发 Save
[ObservableProperty] private bool isActivePlaylist;             // PlaylistsVM 维护; View 据此画 ▶
```

**新增方法**:
```csharp
public Playlist ToRecord() => new(Id, Name,
    Queue.Select(t => t.FilePath).ToArray(),
    CurrentIndex, ShuffleEnabled, RepeatMode);
```

**构造函数**: 改为接 `Playlist` record + 原服务依赖, 不再接 `IQueuePersistence`:

```csharp
public PlaylistViewModel(
    Playlist seed,
    IPlaybackService player,
    IFileDialogService fileDialog,
    ITrackMetadataReader metadataReader)
{
    _player = player;
    _fileDialog = fileDialog;
    _metadataReader = metadataReader;
    _player.TrackEnded += HandleTrackEnded;

    Id = seed.Id;
    Name = seed.Name;

    // 移植自原 LoadFromDisk: 文件存在性过滤 + CurrentIndex 重映射
    var survivingPaths = seed.Items.Where(File.Exists).ToList();
    if (survivingPaths.Count == 0) {
        ShuffleEnabled = seed.ShuffleEnabled;
        RepeatMode = seed.RepeatMode;
    } else {
        int newIndex = MapCurrentIndexAfterFilter(seed.Items, survivingPaths, seed.CurrentIndex);
        foreach (var path in survivingPaths)
            Queue.Add(_metadataReader.CreateFallback(path));   // 与 Phase 5 占位 Track 一致
        CurrentIndex = newIndex;
        ShuffleEnabled = seed.ShuffleEnabled;
        RepeatMode = seed.RepeatMode;
    }

    Queue.CollectionChanged += (_, _) => {
        OnPropertyChanged(nameof(HasCurrentTrack));
        NextTrackCommand.NotifyCanExecuteChanged();
        PrevTrackCommand.NotifyCanExecuteChanged();
        PlayCurrentCommand.NotifyCanExecuteChanged();
    };
}
```

**删除**:
- 构造函数中 `IQueuePersistence queuePersistence` 参数
- `_queuePersistence` 字段及所有引用
- `LoadFromDisk()` 方法 (其文件过滤 + CurrentIndex 映射逻辑已移入构造函数)
- 任何 `_queuePersistence.SaveAsync()` 调用 (Save 责任全部上交 PlaylistsVM/MainVM)

**保留** 静态方法 `MapCurrentIndexAfterFilter` (Phase 4 已编写、已经过手动验收)。

### 6.3 `MainViewModel` 改造

**删除**:
- `Queue` 代理属性
- `CurrentTrack` 代理属性 (改为读 `Playlists.CurrentPlaylist?.CurrentTrack` 或视图直接绑过去)
- `RepeatMode / ShuffleEnabled` 代理属性 (同上)
- 旧的对单一 `PlaylistViewModel` 的引用

**新增**:
```csharp
public PlaybackViewModel Playback { get; }
public PlaylistsViewModel Playlists { get; }

public async Task InitializeAsync()
{
    await Playlists.LoadAsync();
    WireUpPersistence();  // 订阅 collection / property changes → debounce Save
}
```

**Debounce Save** 沿用 Phase 2 路径 (500ms `DispatcherTimer` 或 `Task.Delay` + token), 监听 5.4 列出的事件源。

### 6.4 "查看 vs 播放" wire-up 速查

| 用户动作 | 影响 |
|---|---|
| 单击侧边栏歌单 X | `Playlists.ViewedPlaylist = X` (仅换 Queue 显示, NAudio 不动) |
| 双击 `ViewedPlaylist` 内某项 | `CurrentPlaylist = ViewedPlaylist`, `PlayTrackAt(index)` |
| 拖文件入 Queue 区 | 入 `ViewedPlaylist.Queue` (用户看到的就是要操作的) |
| Next/Prev/Pause | 永远作用于 `CurrentPlaylist` (NAudio 真正在播的) |
| ▶ 标记 | 仅当 `vm.IsActivePlaylist == true` 时画在该 PlaylistView |

## 7. View 层

### 7.1 `MainWindow.xaml` 布局变化

**改前**:
```
+-------------------------+
| PlayerBar               |
+-------------------------+
| PlaylistView            |
+-------------------------+
```

**改后**:
```
+--------------------------------+
| PlayerBar                      |
+--------+-----------------------+
| Side   |  PlaylistView         |
| bar    |  (DC=ViewedPlaylist)  |
| 160px  |                       |
+--------+-----------------------+
```

`MainWindow.xaml`:
```xml
<Grid Grid.Row="1">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="160"/>
        <ColumnDefinition Width="*"/>
    </Grid.ColumnDefinitions>
    <controls:PlaylistsSidebarView Grid.Column="0" DataContext="{Binding Playlists}"/>
    <controls:PlaylistView         Grid.Column="1" DataContext="{Binding Playlists.ViewedPlaylist}"/>
</Grid>
```

### 7.2 `Views/Controls/PlaylistsSidebarView.xaml` (新增)

```xml
<UserControl ...>
  <!-- DataContext = PlaylistsViewModel -->
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="*"/>
    </Grid.RowDefinitions>

    <Button Grid.Row="0" Content="+ 新建歌单"
            Command="{Binding AddPlaylistCommand}" Margin="8"/>

    <ListBox x:Name="SidebarList" Grid.Row="1"
             ItemsSource="{Binding Playlists}"
             SelectedItem="{Binding ViewedPlaylist, Mode=TwoWay}"
             Background="{StaticResource BackgroundSecondary}"
             BorderThickness="0"
             MouseDoubleClick="SidebarList_MouseDoubleClick">
      <ListBox.ItemContainerStyle>
        <!-- 与 PlaylistView 一致: Padding=0, hover/selected 改 bd 背景 -->
      </ListBox.ItemContainerStyle>
      <ListBox.ItemTemplate>
        <DataTemplate>
          <Grid>
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width="20"/>
              <ColumnDefinition Width="*"/>
              <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBlock Grid.Column="0" Text=""    <!-- code-behind 据 IsActivePlaylist 切换 -->
                       Foreground="{StaticResource AccentPrimary}"
                       VerticalAlignment="Center"/>
            <TextBlock Grid.Column="1" Text="{Binding Name}"
                       TextTrimming="CharacterEllipsis"
                       VerticalAlignment="Center"/>
            <Button   Grid.Column="2" Content="×"
                       Width="20" Height="20" Padding="0"
                       Background="Transparent" BorderThickness="0"
                       Tag="{Binding}"
                       Click="RemoveButton_Click"
                       ToolTip="删除歌单"/>
          </Grid>
        </DataTemplate>
      </ListBox.ItemTemplate>
    </ListBox>
  </Grid>
</UserControl>
```

**code-behind 行为**:
- `RemoveButton_Click` → `vm.RemovePlaylistCommand.Execute(target)`
- `SidebarList_MouseDoubleClick` 在歌单名上 → `vm.RenamePlaylistCommand.Execute(target)` (双击重命名, 复用 PromptDialog)
- 监听 `vm.Playlists` 中各项的 `IsActivePlaylist` PropertyChanged → 重画 ▶ 标记 (类比 PlaylistView 的 RefreshCurrentIndicator, 但只看一个 bool, 实现更简单)

### 7.3 `Views/Dialogs/PromptDialog.xaml` (新增, 共享)

极简对话框, 单 TextBox + 确定/取消, 静态方法:
```csharp
public static string? Show(string title, string label, string defaultValue = "")
{
    var dlg = new PromptDialog { Title = title };
    dlg.LabelText.Text = label;
    dlg.InputBox.Text = defaultValue;
    dlg.InputBox.SelectAll();
    dlg.InputBox.Focus();
    return dlg.ShowDialog() == true ? dlg.InputBox.Text : null;
}
```

新建 / 重命名 共用; 删除走 `MessageBox.Show YesNo`。

### 7.4 `Views/Controls/PlaylistView.xaml.cs` 改动

仅两处:

**(1) 双击播放路由**:
```csharp
private void QueueList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
{
    if (_vm == null) return;
    var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
    if (item == null) return;
    int index = QueueList.ItemContainerGenerator.IndexFromContainer(item);
    if (index < 0) return;

    var playlistsVm = App.GetService<PlaylistsViewModel>();  // DI 单例
    playlistsVm.HandleDoubleClickPlay(_vm, index);
    e.Handled = true;
}
```

**(2) `RefreshCurrentIndicator` 加守卫**:
```csharp
private void RefreshCurrentIndicator()
{
    if (_vm == null) return;
    QueueList.UpdateLayout();
    for (int i = 0; i < QueueList.Items.Count; i++) {
        var container = QueueList.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
        if (container == null) continue;
        var marker = FindChildByOrder<TextBlock>(container, 0);
        var title  = FindChildByOrder<TextBlock>(container, 1);
        if (marker == null || title == null) continue;

        bool show = _vm.IsActivePlaylist && (i == _vm.CurrentIndex);
        marker.Text = show ? "▶" : "";
        title.Foreground = show
            ? (Brush)Application.Current.FindResource("AccentPrimary")
            : (Brush)Application.Current.FindResource("ForegroundPrimary");
    }
}
```

`OnVmPropertyChanged` 同时监听 `IsActivePlaylist` 触发 Refresh。

### 7.5 视觉细节

- 侧边栏背景: `BackgroundSecondary` (与 Queue 圆角矩形保持层级感)
- 选中项: `AccentPressed` (复用 PlaylistView ListBoxItem 风格)
- 工具栏与 Queue 区保持 16px 留白对齐

## 8. xUnit 骨架 (子项 A)

### 8.1 `Tests/UmaPlayer.Tests.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="coverlet.collector" Version="6.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\UmaPlayer.csproj" />
  </ItemGroup>
</Project>
```

### 8.2 `UmaPlayer.sln` 加入新 csproj

使 `dotnet build` / `dotnet test` 在仓库根都跑得动。

### 8.3 `Tests/Smoke/SmokeTests.cs`

```csharp
using UmaPlayer.Models;
using Xunit;

namespace UmaPlayer.Tests.Smoke;

public class SmokeTests
{
    [Fact]
    public void Track_Record_StructuralEquality()
    {
        var a = new Track("a.mp3", "T", null, null, null, null, null, null, TimeSpan.Zero);
        var b = new Track("a.mp3", "T", null, null, null, null, null, null, TimeSpan.Zero);
        Assert.Equal(a, b);
    }
}
```

### 8.4 `.gitignore` 补

```
Tests/bin/
Tests/obj/
coverage.cobertura.xml
```

### 8.5 不做的事

- 不为 Phase 5 现有 PlaylistVM 9 个命令补单测 (Phase 7+ 候选)
- 不为 Phase 6 新增的 PlaylistsVM/JsonPlaylistService 补 VM 级单测 (smoke 已验证项目能跑, VM 行为靠手工验收)
- 不配 CI/CD

## 9. 偿债 #1 最小版 (子项 C)

### 9.1 `Converters/BytesToBitmapImageConverter.cs` (新增)

```csharp
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace UmaPlayer.Converters;

/// <summary>
/// byte[] (内嵌封面原始字节) → BitmapImage (跨线程 Frozen)。
/// 与 PlayerViewModel.CreateAlbumArtImage 行为一致, 抽出来给 XAML 直绑用。
/// </summary>
public sealed class BytesToBitmapImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] bytes || bytes.Length == 0) return null;
        var image = new BitmapImage();
        using var ms = new MemoryStream(bytes);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

### 9.2 `App.xaml` 注册

```xml
<Application.Resources>
  ...
  <converters:BytesToBitmapImageConverter x:Key="BytesToBitmapImage"/>
</Application.Resources>
```

### 9.3 PlayerView.xaml / PlayerViewModel.cs 不动

本期只**提供** Converter, 让未来想绑 `Track.AlbumArt` (byte[]) 的场景能用; PlayerVM 中现有 BitmapImage 字段彻底拆除 → 留作债务**部分偿还**, 见 §13。

## 10. 测试与验收

### 10.1 自动化

- xUnit smoke test 1 个 (Track 值相等)
- 每子任务 commit 后 `dotnet build` 通过 + 应用能启动 + 不崩

### 10.2 手动验收清单 (Phase 6 完成时一次性跑)

**多歌单基础**
1. 首次启动 (无 queue.json) → 侧边栏只有 1 条"默认歌单", 空队列, 无报错
2. 点 "+ 新建歌单" → PromptDialog 输入"测试 1" → 侧边栏出现"测试 1", 自动选中
3. 单击侧边栏切换 ViewedPlaylist 立即更新右侧 Queue 内容
4. 双击歌单名重命名 → 输入新名 → 侧边栏即时更新; 重启后保留

**v1 → v2 迁移**
5. 用 Phase 5 版本运行, 造一个含 5 首歌、CurrentIndex=2、ListLoop 的 queue.json
6. 切换到 Phase 6 启动 → 侧边栏出现"默认歌单", 含原 5 首, CurrentIndex 保留, ListLoop 保留
7. 重启 Phase 6 → queue.json 已是 v2; 二次启动不再触发迁移分支

**查看 vs 播放分离**
8. 默认歌单选中第 3 首播放 → 切到"测试 1"查看, 默认歌单仍在播, ▶ 不在"测试 1"上显示
9. 切回默认歌单查看 → ▶ 标记仍在第 3 首
10. 双击"测试 1"内某项 → 接管播放, ▶ 跳到"测试 1", 默认歌单的 ▶ 消失但 CurrentIndex 不变 (回去时仍指向第 3 首)

**各歌单独立设置**
11. 默认歌单设 ListLoop, "测试 1"设 RepeatOff → 切换查看时图标随之切换 → 重启保留

**删除最后一个边界**
12. 删除"测试 1" → 默认歌单仍在
13. 再删默认歌单 → 自动重建一个空"默认歌单", CurrentPlaylist 跟过去, 无崩溃

**Drag-drop 仍正常 (Phase 5 回归)**
14. 拖外部音频入右侧 Queue → 进入 ViewedPlaylist
15. 内部多选拖拽重排仍工作
16. 跨歌单拖拽 (从 Queue 拖一项到侧边栏另一歌单) → 鼠标显示禁止 / 不响应 (已声明 non-goal)

**偿债 #1**
17. App.xaml 中 `BytesToBitmapImage` 资源能被 XAML 引用而不报错 (写一个临时绑定测试 OK 即可, 不留代码)
18. PlayerBar 封面仍正常显示 (回归)

## 11. 风险与缓解

| 风险 | 缓解 |
|---|---|
| 持久化抖动: 改名/CurrentIndex 频繁触发 Save | 沿用 Phase 2 的 500ms debounce; 新增 collection 监听走同一路径, 不开新管道 |
| PlaylistView 双击播放回路: 现要走 PlaylistsVM, `App.GetService` 失败会哑火 | 直接抛 `InvalidOperationException` 让问题立显, 不静默 |
| ▶ 标记错画: ViewedPlaylist 切换时漏发 PropertyChanged | `IsActivePlaylist` 是 `[ObservableProperty]`; PlaylistView 的 `OnVmPropertyChanged` 同时监听 `CurrentIndex` 和 `IsActivePlaylist`, 二者任一变都 Refresh |
| xUnit TFM 不匹配: net10.0-windows + UseWPF 在 Test Explorer 需桌面 runtime | 文档写清楚: `dotnet test` 必须 Windows 跑; CI 不在 Phase 6 范围 |
| v1 迁移失败 SaveAsync 抛异常 | 迁移分支 try/catch 吞 SaveAsync, 内存仍是 v2; 下次启动文件还是 v1, 会再迁一次 (幂等) |
| MainViewModel 重构波及面: 删 Queue/CurrentTrack 代理, XAML 绑定可能漏改 | 把 MainVM 重构单独一个 commit; build + 启动 smoke 验证后再继续 |
| `App.GetService<T>()` 此前不一定存在 | 若不存在, plan 中加一步: 在 `App.xaml.cs` 暴露 `public static T GetService<T>() => _host.Services.GetRequiredService<T>();` |

## 12. 文档与记账

完成 Phase 6 后:

- `docs/PROJECT.md`: 头部 Phase 5 → Phase 6; §1.1/§1.2 加多歌单行; §3 目录树补 Playlist.cs / IPlaylistService.cs / JsonPlaylistService.cs / PlaylistsViewModel.cs / PlaylistsSidebarView.xaml / PromptDialog.xaml / BytesToBitmapImageConverter.cs / Tests/; §4 加多歌单设计决策 (查看vs播放, GUID 主键, 删最后自动重建); §5 加多歌单 VM 章节; §10 加 Phase 6 spec/plan 链接和 commit 历史
- `docs/COUPLING.md`:
  - 头部 Phase 5 → Phase 6
  - §3 (债务登记): 债 #1 状态从"未还" → "**部分偿还** (Converter 已交付; PlayerVM 中 BitmapImage 字段未拆, 留作 Phase 7+)"
  - §5 (隐式契约): 加 5 条新契约 — IPlaylistService Load 不抛/Save 抛; CurrentPlaylistId 必须匹配 Playlists; ViewedPlaylist 不持久化; 删最后一个自动重建; ▶ 仅 IsActivePlaylist 显示
  - §6: 重命名为"Phase 7 启动检查清单"
  - §7: 加新"don't do"条目
- 个人 memory: 视实际踩坑情况新增 (例: 若 v1 迁移踩到 Json polymorphism, 写一条)

## 13. 不做但记账的债务 (供 Phase 7+ 参考)

- **债 #1 完全偿还**: PlayerViewModel 中 `BitmapImage? _albumArtImage` 字段拆除, PlayerView.xaml 改为 `<Image Source="{Binding CurrentTrack.AlbumArt, Converter={StaticResource BytesToBitmapImage}}"/>`, VM 仅持 byte[]。本期已交付 Converter, 拆 VM 留待后续。
- **Phase 5 PlaylistVM 单测覆盖**: PlayTrackAt / Remove / Move / MoveTracks / DropExternalFiles 等命令的单元测试。
- **跨歌单拖拽**: 从 Queue 拖一项到侧边栏另一歌单 → 移动/复制语义。
- **m3u/PLS 导入导出**: Phase 7+ feature backlog。
- **JSON 原子写**: temp-then-rename + .bak 备份, 防止异常退出导致 queue.json 损坏。

---

## Approval Log

- 第 1 段 (总体架构与执行顺序): 已批准
- 第 2 段 (数据模型 Schema v2): 已批准
- 第 3 段 (Service 层): 已批准
- 第 4 段 (ViewModel 层): 已批准
- 第 5 段 (View 层): 已批准
- 第 6 段 (xUnit 骨架 + 偿债 #1): 已批准
- 第 7 段 (测试与验收方案 + 风险): 已批准

下一步: writing-plans 拆任务。
