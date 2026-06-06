# Phase 2 — 播放列表（当前队列）实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 UmaPlayer 加入内存中的当前播放队列：多文件入队、上/下一首、自动推进、随机/循环模式、单项删除。关闭即丢，不持久化。

**Architecture:** 不拆分 ViewModel，所有新字段/命令直接加到 `MainViewModel`。新增 `PlaylistView` UserControl 与现有 `PlayerBar` 平级，绑定同一个 VM。`IPlaybackService` 扩展 `TrackEnded` 事件以区分"自然播完"与"用户 Stop"。

**Tech Stack:** WPF (.NET 10) · CommunityToolkit.Mvvm · NAudio · 现有 MVVM + DI 框架（无新增依赖）

**对应 Spec：** [`docs/superpowers/specs/2026-06-06-uma-player-playlist-design.md`](../specs/2026-06-06-uma-player-playlist-design.md)

---

## ⚠️ 测试策略说明（与 TDD 默认不同）

**本计划不采用 TDD。** 原因：
- 项目当前**无单元测试基础设施**（Phase 1 也未建立）
- Spec §5 明确决定 Phase 2 不引入测试，与现有项目风格一致
- 引入 xUnit/NUnit + WPF UI 测试工具属于另一个子项目

**替代循环：** 每个 task 用 **"编辑 → 编译 → commit"** 替代 "测试 → 实现 → 通过 → commit"。
最终的 §Task 14 是**手动验收清单**，对应 Spec §11，必须人工逐项打勾。

如果未来引入测试基础设施，重新审视本计划以补回 TDD 循环。

---

## 文件结构总览

### 新增文件（5 个）

| 文件 | 职责 | 估算行数 |
|------|------|----------|
| `Models/RepeatMode.cs` | enum 三态：Off / List / One | 5 |
| `Converters/RepeatModeToIconConverter.cs` | RepeatMode → 图标字符 (`⇄` / `🔁` / `🔂`) | 25 |
| `Converters/BoolToAccentBrushConverter.cs` | bool → 强调色 / 次要色画刷（用于 Shuffle 开关高亮） | 30 |
| `Views/Controls/PlaylistView.xaml` | 队列 UI：工具栏 + ListBox | 90 |
| `Views/Controls/PlaylistView.xaml.cs` | 双击、Delete 键、× 按钮事件转发 | 50 |

### 修改文件（7 个）

| 文件 | 改动概述 |
|------|----------|
| `Services/IPlaybackService.cs` | +`event Action? TrackEnded` |
| `Services/NAudioPlaybackService.cs` | `OnPlaybackStopped` 增加 200ms 容差判定，分发 TrackEnded |
| `Services/IFileDialogService.cs` | `OpenFiles` 增加 `bool multiselect = false` 参数 |
| `Services/Win32FileDialogService.cs` | 透传 multiselect 到 `OpenFileDialog.Multiselect` |
| `ViewModels/MainViewModel.cs` | 加 Queue / CurrentIndex / Shuffle / Repeat 字段与命令；订阅 TrackEnded |
| `Views/MainWindow.xaml` | 两行 Grid：PlayerBar 上、PlaylistView 下；默认 Height 改 650 |
| `Views/MainWindow.xaml.cs` | 启动时若 WindowHeight < 500 提升到 650（一次性迁移） |

### 文档更新（2 个）

| 文件 | 改动 |
|------|------|
| `docs/PROJECT.md` | 更新已实现清单、目录结构、添加 Phase 2 模块到 §5 |
| `docs/COUPLING.md` | 债 #2 标记已偿；更新 Phase 3 检查清单 |

---

## 任务编排

任务设计为**线性串行**，因为后续 task 依赖前面 task 的类型/方法。
推荐顺序：基础设施（1~4）→ VM 扩展（5~10）→ UI 接入（11~13）→ 验收+文档（14~15）。

---

### Task 1: 扩展 IFileDialogService 支持多选

**Files:**
- Modify: `Services/IFileDialogService.cs`
- Modify: `Services/Win32FileDialogService.cs`

- [ ] **Step 1: 修改接口签名（保持向下兼容）**

替换 `Services/IFileDialogService.cs` 的整个 interface 定义：

```csharp
namespace UmaPlayer.Services;

/// <summary>
/// 文件选择对话框抽象，便于单元测试以 Mock 替换。
///
/// [STA Thread Required] —— Win32 OpenFileDialog 必须在 STA 线程调用。
/// 当前由 VM 的 RelayCommand 在 UI 线程触发，符合要求；
/// 若从后台线程调用会抛 InvalidOperationException。
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// 弹出文件选择对话框。
    /// </summary>
    /// <param name="filter">WPF 格式过滤器，如 "Audio Files|*.mp3;*.wav"。</param>
    /// <param name="multiselect">是否允许多选；默认 false 保持原行为。</param>
    /// <returns>用户选中的文件路径列表；取消则返回空集合。</returns>
    IReadOnlyList<string> OpenFiles(string filter, bool multiselect = false);
}
```

- [ ] **Step 2: 修改实现类**

替换 `Services/Win32FileDialogService.cs` 中 `OpenFiles` 方法：

```csharp
public IReadOnlyList<string> OpenFiles(string filter, bool multiselect = false)
{
    var dialog = new OpenFileDialog
    {
        Filter = filter,
        Multiselect = multiselect
    };

    // ShowDialog() == true 表示用户点击"打开"；其他情况（取消/关闭）返回空集合
    return dialog.ShowDialog() == true
        ? dialog.FileNames.ToList().AsReadOnly()
        : Array.Empty<string>();
}
```

- [ ] **Step 3: 编译验证**

```bash
dotnet build UmaPlayer.csproj -c Debug -nologo --verbosity quiet
```

Expected: `已成功生成。0 个警告 0 个错误`

注意：现有 `MainViewModel.OpenFilesAsync` 调用 `OpenFiles("Audio Files|...")` 不传第二参数，由默认值 `false` 兜底，行为不变。

- [ ] **Step 4: 提交**

```bash
git add Services/IFileDialogService.cs Services/Win32FileDialogService.cs
git commit -m "feat(file-dialog): support multiselect via optional parameter

新增 OpenFiles(filter, multiselect=false) 重载, 默认 false 保持向下兼容。
为 Phase 2 播放队列多文件入队铺路。"
```

---

### Task 2: 为 IPlaybackService 加 TrackEnded 事件

**Files:**
- Modify: `Services/IPlaybackService.cs`
- Modify: `Services/NAudioPlaybackService.cs`

- [ ] **Step 1: 接口加事件声明**

在 `Services/IPlaybackService.cs` 的事件列表底部（最后一个 `event` 行后）添加：

```csharp
    /// <summary>
    /// 曲目「自然播完」时触发（区别于用户 Stop 或异常）。
    /// 用于实现自动下一首：VM 订阅此事件计算并播放下一首。
    /// </summary>
    event Action? TrackEnded;
```

最终事件块应为：

```csharp
    // —— 事件（所有事件保证在 UI 线程触发） ——
    event Action<PlayState> StateChanged;
    event Action<TimeSpan> PositionChanged;
    event Action<TimeSpan> DurationChanged;
    event Action<Track> TrackChanged;
    event Action<string>? PlaybackError;

    /// <summary>
    /// 曲目「自然播完」时触发（区别于用户 Stop 或异常）。
    /// 用于实现自动下一首：VM 订阅此事件计算并播放下一首。
    /// </summary>
    event Action? TrackEnded;
```

- [ ] **Step 2: 实现类加字段**

在 `Services/NAudioPlaybackService.cs` 的事件字段块（`public event Action<string>? PlaybackError;` 之后）追加：

```csharp
    public event Action? TrackEnded;
```

- [ ] **Step 3: 加 RaiseOnUIThread 无参重载**

`NAudioPlaybackService` 现有 `RaiseOnUIThread<T>(Action<T>?, T)` 只支持带参事件。`TrackEnded` 是无参事件，需要新重载。

在现有 `RaiseOnUIThread<T>` 方法**之后**添加：

```csharp
    /// <summary>将无参事件回调封送到 UI 线程。</summary>
    private void RaiseOnUIThread(Action? handler)
    {
        if (handler == null) return;
        _syncContext.Post(_ => handler(), null);
    }
```

- [ ] **Step 4: 修改 OnPlaybackStopped 加入自然播完判定**

完整替换 `OnPlaybackStopped` 方法及其上方的 doc-comment：

```csharp
    /// <summary>
    /// NAudio 在以下情况触发 PlaybackStopped：
    /// (1) 播放到曲尾  (2) 用户调用 Stop()  (3) 设备出错
    /// 区分逻辑:
    ///   - 有异常 → 上报 PlaybackError + Stopped 状态
    ///   - 无异常 + 播放位置接近 TotalTime (200ms 容差) → 自然播完 → 触发 TrackEnded
    ///     (注：用户 Stop() 已先把 CurrentTime 归零，差值 = TotalTime，不会误判)
    ///   - 其他 → 仅 Stopped 状态（如:从中段 Pause 后再 Stop 的边缘场景）
    /// </summary>
    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            RaiseOnUIThread(PlaybackError, e.Exception.Message);
            SetState(PlayState.Stopped);
            return;
        }

        // 自然播完判定：播放头距 TotalTime 不超过 200ms
        var reader = _reader;
        bool naturalEnd = reader != null
            && (reader.TotalTime - reader.CurrentTime) <= TimeSpan.FromMilliseconds(200);

        if (naturalEnd)
        {
            RaiseOnUIThread(TrackEnded);
            // 状态仍设为 Stopped；VM 的 TrackEnded handler 决定是否随即 Play 下一首
        }

        SetState(PlayState.Stopped);
    }
```

- [ ] **Step 5: 编译验证**

```bash
dotnet build UmaPlayer.csproj -c Debug -nologo --verbosity quiet
```

Expected: `0 个警告 0 个错误`

- [ ] **Step 6: 提交**

```bash
git add Services/IPlaybackService.cs Services/NAudioPlaybackService.cs
git commit -m "feat(playback): add TrackEnded event for natural-end detection

OnPlaybackStopped 中用 200ms 容差区分自然播完与用户 Stop。
偿还 COUPLING.md 债 #2, 为 Phase 2 自动下一首铺路。"
```

---

### Task 3: 新增 RepeatMode 枚举

**Files:**
- Create: `Models/RepeatMode.cs`

- [ ] **Step 1: 创建枚举文件**

新建 `Models/RepeatMode.cs`：

```csharp
namespace UmaPlayer.Models;

/// <summary>
/// 循环播放模式：
///   Off  —— 不循环，列表播完即停
///   List —— 列表循环，最后一首播完跳回第一首
///   One  —— 单曲循环，当前曲反复播（仅自动触发生效；用户手动 Next 仍跳走）
/// </summary>
public enum RepeatMode { Off, List, One }
```

- [ ] **Step 2: 编译验证**

```bash
dotnet build UmaPlayer.csproj -c Debug -nologo --verbosity quiet
```

Expected: `0 个警告 0 个错误`

- [ ] **Step 3: 提交**

```bash
git add Models/RepeatMode.cs
git commit -m "feat(models): add RepeatMode enum (Off/List/One)"
```

---

### Task 4: 新增值转换器（RepeatMode 图标 + Bool 高亮画刷）

**Files:**
- Create: `Converters/RepeatModeToIconConverter.cs`
- Create: `Converters/BoolToAccentBrushConverter.cs`

- [ ] **Step 1: 创建 RepeatModeToIconConverter**

新建 `Converters/RepeatModeToIconConverter.cs`：

```csharp
using System.Globalization;
using System.Windows.Data;
using UmaPlayer.Models;

namespace UmaPlayer.Converters;

/// <summary>
/// 将 RepeatMode 转换为循环按钮图标：
///   Off  → ⇄  (不循环)
///   List → 🔁 (列表循环)
///   One  → 🔂 (单曲循环)
/// </summary>
[ValueConversion(typeof(RepeatMode), typeof(string))]
public sealed class RepeatModeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            RepeatMode.List => "\U0001F501", // 🔁
            RepeatMode.One  => "\U0001F502", // 🔂
            _               => "⇄",     // ⇄
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 2: 创建 BoolToAccentBrushConverter**

新建 `Converters/BoolToAccentBrushConverter.cs`：

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace UmaPlayer.Converters;

/// <summary>
/// 将 bool 映射为画刷资源：
///   true  → AccentPrimary（强调色，提示「已激活」）
///   false → ForegroundSecondary（次要色，提示「未激活」）
/// 用于 Shuffle 按钮的 on/off 颜色切换；循环按钮也可复用（RepeatMode != Off → true）。
/// </summary>
[ValueConversion(typeof(bool), typeof(Brush))]
public sealed class BoolToAccentBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isActive = value is bool b && b;
        var key = isActive ? "AccentPrimary" : "ForegroundSecondary";
        return (Brush)Application.Current.FindResource(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 3: 编译验证**

```bash
dotnet build UmaPlayer.csproj -c Debug -nologo --verbosity quiet
```

Expected: `0 个警告 0 个错误`

- [ ] **Step 4: 提交**

```bash
git add Converters/RepeatModeToIconConverter.cs Converters/BoolToAccentBrushConverter.cs
git commit -m "feat(converters): add RepeatModeToIcon + BoolToAccentBrush

为 PlaylistView 工具栏的循环/随机按钮提供视觉反馈。"
```

---

### Task 5: MainViewModel 加队列字段与 Queue Helper 方法

**Files:**
- Modify: `ViewModels/MainViewModel.cs`

- [ ] **Step 1: 加 using**

在 `ViewModels/MainViewModel.cs` 顶部 using 块中追加：

```csharp
using System.Collections.ObjectModel;
```

最终 using 块应包含（保留现有所有 using，下列只是确保 ObservableCollection 可用）：

```csharp
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using UmaPlayer.Configuration;
using UmaPlayer.Models;
using UmaPlayer.Services;
```

- [ ] **Step 2: 加 Phase 2 字段**

在 `MainViewModel` 类内、`_volumeBeforeMute` 字段之后、`public string VolumeIcon =>` 之前，插入一段 `#region Phase 2 — Playlist Queue`：

```csharp
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
```

- [ ] **Step 3: 编译验证**

```bash
dotnet build UmaPlayer.csproj -c Debug -nologo --verbosity quiet
```

Expected: `0 个警告 0 个错误`

- [ ] **Step 4: 提交**

```bash
git add ViewModels/MainViewModel.cs
git commit -m "feat(vm): add Phase 2 queue fields (Queue, CurrentIndex, Shuffle, Repeat)

仅声明字段与派生属性, 命令和事件订阅在后续 task 加入。"
```

---

### Task 6: MainViewModel 加 CalculateNextIndex 与 PlayTrackAtAsync 核心算法

**Files:**
- Modify: `ViewModels/MainViewModel.cs`

- [ ] **Step 1: 在 Phase 2 region 内追加核心算法方法**

在 Task 5 创建的 `#endregion` 之前插入两个 private 方法：

```csharp
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

        CurrentIndex = index;
        _shuffleHistory.Add(index); // 不管是否 Shuffle 都登记，便于切换时无缝

        try
        {
            // 读元数据并回写到 Queue[index]（占位 Track → 完整 Track）
            var meta = await ReadTrackMetadataAsync(Queue[index].FilePath);
            Queue[index] = meta; // ObservableCollection.set[i] 触发 Replace, UI 自动刷新

            await _player.LoadAsync(meta);
            _player.Play();
        }
        catch
        {
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
```

- [ ] **Step 2: 编译验证**

```bash
dotnet build UmaPlayer.csproj -c Debug -nologo --verbosity quiet
```

Expected: `0 个警告 0 个错误`

- [ ] **Step 3: 提交**

```bash
git add ViewModels/MainViewModel.cs
git commit -m "feat(vm): add CalculateNext/Prev + PlayTrackAtAsync algorithms

PlayTrackAtAsync 含损坏文件保护（最多连跳 3 首）。
CalculateNextIndex 处理 Shuffle × Repeat 全部组合; RepeatOne 由调用方单独处理。"
```

---

### Task 7: MainViewModel 加 Phase 2 命令

**Files:**
- Modify: `ViewModels/MainViewModel.cs`

- [ ] **Step 1: 在 PlayTrackAtAsync 方法之后、`#endregion` 之前追加全部命令**

```csharp
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
            Queue.Add(CreateFallbackTrack(path));
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
            _player.Stop();
            CurrentIndex = -1;
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
        _player.Stop();
        Queue.Clear();
        CurrentIndex = -1;
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
```

- [ ] **Step 2: 添加 HandleTrackEnded 事件处理方法**

紧接上一步追加：

```csharp
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
```

- [ ] **Step 3: 在构造函数订阅 TrackEnded 事件**

找到现有构造函数中订阅事件的块：

```csharp
        _player.PositionChanged += HandlePositionChanged;
        _player.StateChanged += HandleStateChanged;
        _player.DurationChanged += HandleDurationChanged;
        _player.TrackChanged += HandleTrackChanged;
        _player.PlaybackError += HandlePlaybackError;
```

在最后一行 `_player.PlaybackError += HandlePlaybackError;` 之后追加：

```csharp
        _player.TrackEnded += HandleTrackEnded;
```

并同步更新 `CleanupAsync` 方法的解订阅块。找到现有：

```csharp
        _player.PositionChanged -= HandlePositionChanged;
        _player.StateChanged -= HandleStateChanged;
        _player.DurationChanged -= HandleDurationChanged;
        _player.TrackChanged -= HandleTrackChanged;
        _player.PlaybackError -= HandlePlaybackError;
```

在最后一行之后追加：

```csharp
        _player.TrackEnded -= HandleTrackEnded;
```

- [ ] **Step 4: 修复 HasCurrentTrack 的 CanExecute 联动**

`NextTrackCommand` / `PrevTrackCommand` 用 `CanExecute = nameof(HasCurrentTrack)`。CommunityToolkit 不会自动监听 `Queue.Count` 变化。

需在 Task 5 的 `[ObservableProperty] int _currentIndex` 已经有 `[NotifyPropertyChangedFor(nameof(HasCurrentTrack))]`，足够覆盖 `CurrentIndex` 变化。

但 `Queue.Count` 变化不会触发 `HasCurrentTrack` 通知。补救：在 ctor 末尾、`Initialize();` 之前订阅 `Queue.CollectionChanged`：

找到 `Initialize();` 行（在构造函数末尾），在其**之前**插入：

```csharp
        // 队列变化时强制刷新 Next/Prev 命令可用性
        Queue.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasCurrentTrack));
            NextTrackCommand.NotifyCanExecuteChanged();
            PrevTrackCommand.NotifyCanExecuteChanged();
        };
```

- [ ] **Step 5: 编译验证**

```bash
dotnet build UmaPlayer.csproj -c Debug -nologo --verbosity quiet
```

Expected: `0 个警告 0 个错误`

如果报 `NextTrackCommand` / `PrevTrackCommand` 找不到 `NotifyCanExecuteChanged`，确认 Task 7 Step 1 中两个命令的 `[RelayCommand(CanExecute = nameof(HasCurrentTrack))]` 写对了（带 CanExecute 才会生成支持该方法的 IRelayCommand）。

- [ ] **Step 6: 提交**

```bash
git add ViewModels/MainViewModel.cs
git commit -m "feat(vm): add Phase 2 commands and TrackEnded auto-advance

新增命令: AddToQueue, RemoveTrack, ClearQueue, PlayTrackAt,
         NextTrack, PrevTrack, ToggleShuffle, CycleRepeat
新增事件订阅: HandleTrackEnded → 自动推进下一首
RemoveTrack 含 _shuffleHistory 索引修正; ClearQueue 全局重置。
Next/Prev CanExecute 联动 Queue.CollectionChanged。"
```

---

### Task 8: 重构旧的 OpenFilesAsync 命令（保持 PlayerBar 📂 按钮可用）

**Files:**
- Modify: `ViewModels/MainViewModel.cs`

**说明：** 现有 `PlayerBar.xaml` 有个 📂 按钮绑 `OpenFilesCommand`。Phase 2 添加 `AddToQueueCommand` 后，📂 按钮**改为入队 + 立即播放首项**的语义，保持向下兼容、不再"替换当前播放曲"的旧行为，避免和 PlaylistView 的 [+ 添加] 重复。

- [ ] **Step 1: 替换 OpenFilesAsync 方法体**

找到现有：

```csharp
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
```

完整替换为：

```csharp
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
            Queue.Add(CreateFallbackTrack(path));
        }

        _shuffleHistory.Clear();
        await PlayTrackAtAsync(firstNewIndex);
    }
```

- [ ] **Step 2: 编译验证**

```bash
dotnet build UmaPlayer.csproj -c Debug -nologo --verbosity quiet
```

Expected: `0 个警告 0 个错误`

- [ ] **Step 3: 提交**

```bash
git add ViewModels/MainViewModel.cs
git commit -m "refactor(vm): OpenFiles command now appends to queue + auto-plays

PlayerBar 上的 📂 按钮语义升级: 多文件入队 + 从第一首新加项开始播,
而不是替换当前曲。与 PlaylistView 的 [+ 添加] 区分:
  - [+ 添加]: 仅入队, 不影响当前播放
  - 📂:      入队 + 立即播放首项"
```

---

### Task 9: 创建 PlaylistView UserControl（XAML 部分）

**Files:**
- Create: `Views/Controls/PlaylistView.xaml`

- [ ] **Step 1: 创建 XAML 文件**

新建 `Views/Controls/PlaylistView.xaml`：

```xml
<!--
    PlaylistView —— Phase 2 队列 UI 控件。
    布局: 2 行 Grid
      Row 0 (Auto) : 工具栏（添加/清空 + 随机/循环开关）
      Row 1 (*)    : ListBox 显示队列，每项含 ▶/空 + 文件名 + ×

    DataContext = MainViewModel（由 MainWindow.xaml 注入）
-->
<UserControl x:Class="UmaPlayer.Views.Controls.PlaylistView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:converters="clr-namespace:UmaPlayer.Converters">
    <UserControl.Resources>
        <converters:RepeatModeToIconConverter x:Key="RepeatModeToIcon"/>
        <converters:BoolToAccentBrushConverter x:Key="BoolToAccentBrush"/>
    </UserControl.Resources>

    <Grid Margin="16,0,16,16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- Row 0: 工具栏 -->
        <Grid Grid.Row="0" Margin="0,0,0,8">
            <!-- 左侧：添加 / 清空 -->
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Left">
                <Button Command="{Binding AddToQueueCommand}" Padding="12,4">
                    <TextBlock Text="+ 添加" FontSize="12"/>
                </Button>
                <Button Command="{Binding ClearQueueCommand}" Padding="12,4" Margin="8,0,0,0">
                    <TextBlock Text="清空" FontSize="12"/>
                </Button>
            </StackPanel>

            <!-- 右侧：随机 / 循环 -->
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                <Button Command="{Binding ToggleShuffleCommand}" Width="32" Height="32"
                        Background="Transparent" BorderThickness="0"
                        ToolTip="随机播放">
                    <TextBlock Text="&#x1F500;" FontSize="14"
                               Foreground="{Binding ShuffleEnabled, Converter={StaticResource BoolToAccentBrush}}"/>
                </Button>
                <Button Command="{Binding CycleRepeatCommand}" Width="32" Height="32"
                        Background="Transparent" BorderThickness="0" Margin="4,0,0,0"
                        ToolTip="循环模式 (Off / List / One)">
                    <TextBlock Text="{Binding RepeatMode, Converter={StaticResource RepeatModeToIcon}}"
                               FontSize="14"
                               Foreground="{Binding RepeatActive, Converter={StaticResource BoolToAccentBrush}}"/>
                </Button>
            </StackPanel>
        </Grid>

        <!-- Row 1: 队列列表 -->
        <Border Grid.Row="1" Background="{StaticResource BackgroundSecondary}" CornerRadius="4">
            <ListBox x:Name="QueueList"
                     ItemsSource="{Binding Queue}"
                     SelectedItem="{Binding SelectedTrack, Mode=TwoWay}"
                     Background="Transparent" BorderThickness="0"
                     Foreground="{StaticResource ForegroundPrimary}"
                     ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                     MouseDoubleClick="QueueList_MouseDoubleClick"
                     KeyDown="QueueList_KeyDown">
                <ListBox.ItemContainerStyle>
                    <Style TargetType="ListBoxItem">
                        <Setter Property="Padding" Value="0"/>
                        <Setter Property="Background" Value="Transparent"/>
                        <Setter Property="Template">
                            <Setter.Value>
                                <ControlTemplate TargetType="ListBoxItem">
                                    <Border x:Name="bd" Background="{TemplateBinding Background}"
                                            Padding="8,4">
                                        <ContentPresenter/>
                                    </Border>
                                    <ControlTemplate.Triggers>
                                        <Trigger Property="IsMouseOver" Value="True">
                                            <Setter TargetName="bd" Property="Background"
                                                    Value="{StaticResource BackgroundTertiary}"/>
                                        </Trigger>
                                        <Trigger Property="IsSelected" Value="True">
                                            <Setter TargetName="bd" Property="Background"
                                                    Value="{StaticResource AccentPressed}"/>
                                        </Trigger>
                                    </ControlTemplate.Triggers>
                                </ControlTemplate>
                            </Setter.Value>
                        </Setter>
                    </Style>
                </ListBox.ItemContainerStyle>
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="20"/>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>
                            <!-- ▶ 标记: 由 code-behind 在 CurrentIndex 变化时切换；MVP 简化为空白占位 -->
                            <TextBlock Grid.Column="0" Text="" FontSize="11"
                                       Foreground="{StaticResource AccentPrimary}"
                                       VerticalAlignment="Center"/>
                            <TextBlock Grid.Column="1" Text="{Binding Title}"
                                       FontSize="12" TextTrimming="CharacterEllipsis"
                                       VerticalAlignment="Center"/>
                            <Button Grid.Column="2" Content="×"
                                    Width="20" Height="20" Padding="0"
                                    Background="Transparent" BorderThickness="0"
                                    FontSize="14"
                                    Foreground="{StaticResource ForegroundSecondary}"
                                    Tag="{Binding}"
                                    Click="RemoveButton_Click"
                                    ToolTip="移除"/>
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
        </Border>
    </Grid>
</UserControl>
```

**关于 ▶ 当前曲标记：** 为简化 MVP，DataTemplate 中的 ▶ 列保留占位空 TextBlock，**Task 10 在 code-behind 通过监听 `CurrentIndex` 变化遍历更新**。这种做法不优雅但避免引入 MultiBinding 或 IndexConverter，保持 MVP 简洁。

- [ ] **Step 2: 编译验证**

```bash
dotnet build UmaPlayer.csproj -c Debug -nologo --verbosity quiet
```

Expected: `0 个警告 0 个错误`

注意：此时 XAML 引用的事件处理器尚未实现，但 WPF 编译器允许 .xaml.cs 在下一 task 提供。如果报 partial class 缺失，**先临时跳过这一步的 build 验证**，让 Task 10 一起编译。

如果 build 报错涉及 `MissingMethodException`，请直接进入 Task 10。

- [ ] **Step 3: 暂不提交（待 Task 10 一起）**

Task 9 的 XAML 依赖 Task 10 的 code-behind 才能编译通过。两个 task 合并为一个 commit。

---

### Task 10: 创建 PlaylistView code-behind（事件转发 + 当前曲高亮）

**Files:**
- Create: `Views/Controls/PlaylistView.xaml.cs`

- [ ] **Step 1: 创建 code-behind 文件**

新建 `Views/Controls/PlaylistView.xaml.cs`：

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UmaPlayer.Models;
using UmaPlayer.ViewModels;

namespace UmaPlayer.Views.Controls;

/// <summary>
/// 播放队列 UserControl 的 code-behind。
///
/// 职责：
///   1) 双击 ListBox 项 → PlayTrackAtCommand(index)
///   2) Delete 键 → RemoveTrackCommand(SelectedIndex)
///   3) × 按钮 → RemoveTrackCommand(对应行 index)
///   4) 监听 VM.CurrentIndex 变化，刷新行首 ▶ 标记与文字颜色
/// </summary>
public partial class PlaylistView : UserControl
{
    private MainViewModel? _vm;

    public PlaylistView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 取消旧订阅
        if (_vm != null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = e.NewValue as MainViewModel;

        if (_vm != null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            // 初次绑定时刷新一次
            RefreshCurrentIndicator();
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentIndex))
            RefreshCurrentIndicator();
    }

    /// <summary>
    /// 遍历 ListBox 所有可见项，根据 VM.CurrentIndex 设置 ▶ 标记与文字颜色。
    /// 通过 ItemContainerGenerator + VisualTree 直接修改，避开 MultiBinding/Converter 复杂度。
    /// </summary>
    private void RefreshCurrentIndicator()
    {
        if (_vm == null) return;

        QueueList.UpdateLayout(); // 确保 container 已生成
        for (int i = 0; i < QueueList.Items.Count; i++)
        {
            var container = QueueList.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
            if (container == null) continue;

            var marker = FindChildByOrder<TextBlock>(container, 0); // ▶ 列
            var title  = FindChildByOrder<TextBlock>(container, 1); // 文件名列
            if (marker == null || title == null) continue;

            bool isCurrent = (i == _vm.CurrentIndex);
            marker.Text = isCurrent ? "▶" : ""; // ▶
            title.Foreground = isCurrent
                ? (Brush)Application.Current.FindResource("AccentPrimary")
                : (Brush)Application.Current.FindResource("ForegroundPrimary");
        }
    }

    /// <summary>
    /// 按"出现顺序"在 VisualTree 中找第 N 个 T 类型的子元素。
    /// 0 = ▶ 列, 1 = 文件名列（与 XAML 中 DataTemplate 的 TextBlock 顺序对应）。
    /// </summary>
    private static T? FindChildByOrder<T>(DependencyObject parent, int n) where T : DependencyObject
    {
        int count = 0;
        return Walk(parent);

        T? Walk(DependencyObject p)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(p); i++)
            {
                var c = VisualTreeHelper.GetChild(p, i);
                if (c is T match)
                {
                    if (count == n) return match;
                    count++;
                }
                var deeper = Walk(c);
                if (deeper != null) return deeper;
            }
            return null;
        }
    }

    // —— 事件转发 ——

    /// <summary>双击列表项 → 播放该项。空白区双击不触发。</summary>
    private void QueueList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm == null) return;
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item == null) return;

        int index = QueueList.ItemContainerGenerator.IndexFromContainer(item);
        if (index < 0) return;

        _vm.PlayTrackAtCommand.Execute(index);
        e.Handled = true;
    }

    /// <summary>Delete 键 → 删除选中项。</summary>
    private void QueueList_KeyDown(object sender, KeyEventArgs e)
    {
        if (_vm == null) return;
        if (e.Key != Key.Delete) return;
        if (QueueList.SelectedIndex < 0) return;

        _vm.RemoveTrackCommand.Execute(QueueList.SelectedIndex);
        e.Handled = true;
    }

    /// <summary>× 按钮 → 删除对应行。Tag 已绑定 DataContext (Track)。</summary>
    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is not Button btn) return;
        if (btn.Tag is not Track track) return;

        int index = _vm.Queue.IndexOf(track);
        if (index < 0) return;

        _vm.RemoveTrackCommand.Execute(index);
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? obj) where T : DependencyObject
    {
        while (obj != null)
        {
            if (obj is T match) return match;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }
}
```

- [ ] **Step 2: 编译验证**

```bash
dotnet build UmaPlayer.csproj -c Debug -nologo --verbosity quiet
```

Expected: `0 个警告 0 个错误`

- [ ] **Step 3: 提交 PlaylistView 整体（XAML + code-behind 一起）**

```bash
git add Views/Controls/PlaylistView.xaml Views/Controls/PlaylistView.xaml.cs
git commit -m "feat(view): add PlaylistView UserControl (queue UI)

XAML 布局: 工具栏 ([+ 添加][清空] | 🔀 ↻) + ListBox。
Code-behind 职责:
  - 双击/Delete/× 按钮 → 转发到 VM 命令
  - 监听 CurrentIndex 变化, 刷新 ▶ 行首标记与文字颜色
  - 通过 ItemContainerGenerator 直接操作可见 container,
    避免引入 MultiBinding/IndexConverter

工具栏中: Shuffle 用 BoolToAccentBrush 高亮,
        循环按钮图标随 RepeatMode 动态变化。"
```

---

### Task 11: MainWindow 加入 PlaylistView 并改两行布局

**Files:**
- Modify: `Views/MainWindow.xaml`

- [ ] **Step 1: 替换整个 MainWindow.xaml**

```xml
<!--
    主窗口布局 —— Phase 2 改为两行:
      Row 0 (Auto): PlayerBar (现有, 不动)
      Row 1 (*)   : PlaylistView (新)
    默认 Height 从 450 提升至 650 以容纳队列。
    Closing 事件由 code-behind 处理: 保存窗口几何 + 触发 VM 清理。
-->
<Window x:Class="UmaPlayer.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="clr-namespace:UmaPlayer.Views.Controls"
        Title="UmaPlayer"
        Width="800" Height="650"
        MinWidth="600" MinHeight="500"
        Background="{StaticResource BackgroundPrimary}"
        Foreground="{StaticResource ForegroundPrimary}"
        WindowStartupLocation="Manual"
        Closing="Window_Closing">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <controls:PlayerBar    Grid.Row="0" DataContext="{Binding}"/>
        <controls:PlaylistView Grid.Row="1" DataContext="{Binding}"/>
    </Grid>
</Window>
```

- [ ] **Step 2: 编译验证**

```bash
dotnet build UmaPlayer.csproj -c Debug -nologo --verbosity quiet
```

Expected: `0 个警告 0 个错误`

- [ ] **Step 3: 提交**

```bash
git add Views/MainWindow.xaml
git commit -m "feat(view): integrate PlaylistView into MainWindow

布局改两行 Grid: PlayerBar 上, PlaylistView 下。
默认 Height 450 → 650 以容纳队列。
加 MinWidth/MinHeight 防过小窗口下挤压。"
```

---

### Task 12: MainWindow code-behind 添加旧高度迁移逻辑

**Files:**
- Modify: `Views/MainWindow.xaml.cs`

- [ ] **Step 1: 在窗口几何恢复块中加入一次性迁移**

找到现有 try 块：

```csharp
        try
        {
            var settings = _persistence.LoadAsync().GetAwaiter().GetResult();
            Left = settings.WindowLeft;
            Top = settings.WindowTop;
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
            EnsureVisible();
        }
```

完整替换为：

```csharp
        try
        {
            var settings = _persistence.LoadAsync().GetAwaiter().GetResult();
            Left = settings.WindowLeft;
            Top = settings.WindowTop;
            Width = settings.WindowWidth;

            // Phase 2 一次性迁移: Phase 1 持久化的高度可能 < 500 (PlaylistView 不可见)
            // 检测并提升到 650, 让用户首次看到完整 UI。
            Height = settings.WindowHeight < 500 ? 650 : settings.WindowHeight;

            EnsureVisible();
        }
```

- [ ] **Step 2: 编译验证**

```bash
dotnet build UmaPlayer.csproj -c Debug -nologo --verbosity quiet
```

Expected: `0 个警告 0 个错误`

- [ ] **Step 3: 提交**

```bash
git add Views/MainWindow.xaml.cs
git commit -m "feat(view): one-time migration for window height < 500

Phase 1 用户持久化的 WindowHeight 可能 < 500, 此时 PlaylistView 几乎不可见。
启动时检测并提升到 650, 用户下次关闭会自然保存新值, 迁移完成。"
```

---

### Task 13: 手动冒烟测试 — 启动应用确认基本工作

**Files:** 无（运行性验证）

- [ ] **Step 1: 启动应用**

```bash
dotnet run --project UmaPlayer.csproj
```

Expected:
- 窗口打开，高度 650
- 上方 PlayerBar 完整（封面、进度条、控制按钮、音量）
- 下方 PlaylistView：[+ 添加] [清空] 在左，🔀 ⇄ 在右
- 中间灰色 ListBox 区域空白

如果窗口尺寸异常、PlaylistView 不显示，回到 Task 9 / 11 检查 XAML。

- [ ] **Step 2: 冒烟测试 — 加入并播放**

手动操作：
1. 点 [+ 添加] → 文件对话框 → **多选** 2-3 个 mp3
2. 文件应出现在队列中
3. **双击**第 1 首 → 应开始播放，行首出现 ▶，文字变紫
4. 等播完或按 Stop → 应自动切到第 2 首（如果 RepeatList 关闭，列表播完停止）

如果双击无反应，检查 Task 10 的 `QueueList_MouseDoubleClick`。
如果自动推进无反应，检查 Task 2 (TrackEnded) + Task 7 (HandleTrackEnded)。

- [ ] **Step 3: 不 commit，仅验证基础回路畅通**

完整验收清单留给 Task 14。

---

### Task 14: 手动验收清单（完整覆盖 Spec §11）

**Files:** 无（验收记录）

逐项手动执行并打勾。建议准备 5+ 个真实音频文件 + 1 个故意损坏的文件（如把 .txt 改名 .mp3）。

#### 基础队列
- [ ] [+ 添加] 单击 → 文件对话框可多选 → 所选文件按顺序加入队列底部
- [ ] 加入 5 个文件后队列显示 5 行
- [ ] 双击第 3 行 → 第 3 首立即播放，行首出现 ▶ 标记
- [ ] [清空] → 队列空，播放停止，PlayerBar 显示 "No track loaded"

#### 单项删除
- [ ] 点击单项右侧 × → 该项移除
- [ ] 选中某项按 Delete 键 → 该项移除
- [ ] 删除当前播放曲 → 播放停止
- [ ] 删除非当前曲（之前/之后）→ 当前曲继续播

#### 自动推进 — 顺序模式
- [ ] 顺序 + RepeatOff：第 N 首播完 → 第 N+1 首
- [ ] 顺序 + RepeatOff：最后一首播完 → 停止
- [ ] 顺序 + RepeatList：最后一首播完 → 跳回第一首
- [ ] 顺序 + RepeatOne：当前曲反复播

#### 自动推进 — 随机模式
- [ ] Shuffle ON + RepeatOff：所有曲随机播完一遍后停止
- [ ] Shuffle ON + RepeatList：全播完后重置随机历史继续随机
- [ ] Shuffle ON + RepeatOne：随机选一首后反复播该首

#### 手动控制
- [ ] Next 按钮在 RepeatOne 下也跳下一首
- [ ] Queue 为空时 Next/Prev 按钮置灰
- [ ] PlayerBar 的 Play/Pause/Stop/Seek/Volume 全部正常工作

#### 容错
- [ ] 加入一个损坏文件 + 几个正常文件，播到损坏文件时自动跳过到下一首
- [ ] 连续 3 个损坏文件 → 停止，不无限循环
- [ ] 切换 Shuffle 不报错

#### 窗口
- [ ] 启动时窗口高 650
- [ ] 如果有旧 settings.json（高 < 500），启动自动提升到 650

#### Phase 1 回归
- [ ] PlayerBar 上的 📂 按钮：多选文件 → 全部入队 + 第一新加项开始播
- [ ] 关闭窗口再打开，窗口位置/大小/音量保留
- [ ] 进度条拖动、单击跳转正常

**若任一项失败：**
- 记录失败的复现步骤
- 回到对应 Task 重新审视
- 修复后重跑该项与下游受影响项

**若全部通过：**

```bash
git commit --allow-empty -m "test: manual acceptance pass for Phase 2 playlist queue

完整执行 spec §11 验收清单, 全部通过。"
```

---

### Task 15: 更新项目文档

**Files:**
- Modify: `docs/PROJECT.md`
- Modify: `docs/COUPLING.md`

- [ ] **Step 1: 更新 PROJECT.md §1.1（"已实现"清单）**

在 `docs/PROJECT.md` 的 §1.1 表格末尾追加一行：

```markdown
| 播放列表 | 内存队列：多选入队、单项删除、上/下一首、自动推进、随机/循环模式（关闭即丢） |
```

- [ ] **Step 2: 更新 PROJECT.md §1.2（"未实现"清单）**

把以下两行从 §1.2 移除（它们的部分能力已在 Phase 2 实现）：

```markdown
- 播放列表管理（创建 / 保存 / 加载 / 编辑）
- 上一首 / 下一首、播放模式（顺序 / 随机 / 单曲循环）—— 依赖播放列表
```

替换为：

```markdown
- 多个命名播放列表（创建 / 保存 / 加载 / 切换）—— 当前仅支持单个内存队列
- 播放队列持久化（关闭即丢）
- 拖拽入队 / 队列内拖拽重排序
- M3U / PLS 等播放列表格式导入导出
```

- [ ] **Step 3: 更新 PROJECT.md §3 目录结构**

在 `Models/` 块加入 RepeatMode：

```
├── Models/
│   ├── Track.cs                 # 不可变 record：音轨信息（含封面字节数组）
│   ├── PlayState.cs             # enum: Stopped / Playing / Paused
│   ├── RepeatMode.cs            # enum: Off / List / One  (Phase 2)
│   └── AudioDeviceInfo.cs       # 预留：设备信息
```

在 `Views/Controls/` 块加入 PlaylistView：

```
│   └── Controls/
│       ├── PlayerBar.xaml(.cs)  # 全功能播放栏（封面/信息/进度/控制/音量）
│       └── PlaylistView.xaml(.cs)  # 播放队列（Phase 2）
```

在 `Converters/` 块加入新转换器：

```
├── Converters/
│   ├── PlayStateToIconConverter.cs       # ▶/⏸ 图标
│   ├── TimeSpanToStringConverter.cs      # 0:00 / 0:00:00
│   ├── RepeatModeToIconConverter.cs      # ⇄ / 🔁 / 🔂 (Phase 2)
│   └── BoolToAccentBrushConverter.cs     # 强调色/次要色画刷 (Phase 2)
```

- [ ] **Step 4: 更新 COUPLING.md：标记债 #2 已偿**

在 `docs/COUPLING.md` §3 的"债 #2 — IPlaybackService 缺自然播完信号"小节标题旁添加 ✅ 已偿，并在该节末尾追加：

```markdown
**✅ Phase 2 已偿还**（commit: 见 `git log --grep TrackEnded`）—— 加入 `event Action? TrackEnded`，
通过 200ms 容差判定"自然播完"。
```

- [ ] **Step 5: 更新 COUPLING.md §6（Phase 2 启动检查清单 → Phase 3 启动检查清单）**

把 §6 的标题从 `## 6. Phase 2 启动检查清单` 改为：

```markdown
## 6. Phase 3 启动检查清单
```

并把内容中的"播放列表"相关步骤标记为已完成。具体把现有 §6 整节替换为：

```markdown
## 6. Phase 3 启动检查清单

> Phase 2（播放队列）已完成。下一阶段（如多命名播放列表 / 队列持久化）启动时按以下顺序：

1. ☐ **VM 拆分**（债 #1 关联）—— `MainViewModel` 已达 ~400 行，到拆分阈值
   - 拆 `PlayerViewModel`（仅 transport）+ `PlaylistViewModel`（队列 + 模式）
   - `MainViewModel` 作为 facade 持有两者
2. ☐ **`PlayerBar` / `PlaylistView` 去硬转型** —— 配合 VM 拆分；命令通过 `DependencyProperty` 或 XAML `{Binding}` 暴露
3. ☐ **抽 `ITrackMetadataReader`**（债 #4）—— 多列表 / 文件夹扫描需要批量元数据
4. ☐ **解决 settings 合并纪律**（债 #3）—— 加入队列持久化前必做
5. ☐ **队列持久化** —— `%LocalAppData%\UmaPlayer\queue.json`
6. ☐ **多命名播放列表（L3）** —— 真正的"播放列表管理"
7. ☐ **拖拽支持** —— 入队 + 重排序

**预估总工作量：** 10~14 小时（不含 L3 多命名列表本身的功能开发）
```

- [ ] **Step 6: 编译验证（文档不影响编译，但跑一遍确认未误改源码）**

```bash
dotnet build UmaPlayer.csproj -c Debug -nologo --verbosity quiet
```

Expected: `0 个警告 0 个错误`

- [ ] **Step 7: 提交**

```bash
git add docs/PROJECT.md docs/COUPLING.md
git commit -m "docs: update PROJECT.md and COUPLING.md for Phase 2 completion

PROJECT.md:
  - §1.1 加入播放列表能力
  - §1.2 移除已实现项, 补充 Phase 3 未实现项
  - §3 目录结构加入 RepeatMode / PlaylistView / 新转换器

COUPLING.md:
  - 债 #2 (TrackEnded) 标记已偿还
  - §6 Phase 2 启动清单 → Phase 3 启动清单"
```

---

## 完成标志

执行完 Task 1~15 后：

✅ `git log --oneline` 显示 13~14 个新 commit（Phase 2 范围）
✅ `dotnet build` 0 警告 0 错误
✅ Task 14 验收清单全部打勾
✅ `docs/PROJECT.md` 与 `docs/COUPLING.md` 反映 Phase 2 状态

进入 Phase 3 之前，**先合上本计划**，参考 `docs/COUPLING.md` §6 决定下一步。

---

## Self-Review 检查（计划作者自查）

### 1. Spec 覆盖检查

| Spec 要求 | 对应 Task |
|-----------|-----------|
| §3.1 多选添加 | Task 1 (接口) + Task 7 (AddToQueue 命令) + Task 8 (📂 命令升级) |
| §3.1 单项删除（× / Delete） | Task 7 (RemoveTrack 命令) + Task 10 (事件转发) |
| §3.1 整体清空 | Task 7 (ClearQueue 命令) + Task 9 (按钮) |
| §3.1 双击播放 | Task 7 (PlayTrackAt 命令) + Task 10 (双击事件) |
| §3.1 上/下一首 | Task 7 (NextTrack/PrevTrack) |
| §3.1 自动推进 | Task 2 (TrackEnded 事件) + Task 7 (HandleTrackEnded) |
| §3.1 随机模式 | Task 5 (字段) + Task 6 (CalculateNextIndex) + Task 7 (ToggleShuffle) |
| §3.1 循环模式 | Task 3 (枚举) + Task 7 (CycleRepeat) + Task 6 (CalculateNextIndex) |
| §3.1 损坏跳过（连跳 3） | Task 6 (PlayTrackAtAsync skipCount) |
| §3.1 当前曲高亮 | Task 10 (RefreshCurrentIndicator) |
| §3.1 列表只显示文件名 | Task 7 (AddToQueue 用 CreateFallbackTrack) + Task 6 (播放时才读元数据) |
| §6 取舍 a (入队不读元数据) | Task 7 AddToQueue + Task 6 PlayTrackAtAsync |
| §6 取舍 b (200ms 容差) | Task 2 OnPlaybackStopped |
| §6 取舍 c (RepeatOne 仅自动) | Task 7 HandleTrackEnded vs NextTrack 分别处理 |
| §6 取舍 d (VM 不拆分) | 全部新增字段加到 MainViewModel |
| §7 错误处理（队列空 Next/Prev 置灰） | Task 7 CanExecute=HasCurrentTrack + Step 4 通知联动 |
| §7 删除当前曲停止播放 | Task 7 RemoveTrack 逻辑 |
| §7 切换 Shuffle 清空历史 | Task 7 ToggleShuffle 逻辑 |
| §8 UI 规范（两行布局、高度 650） | Task 11 |
| §8 旧高度迁移 | Task 12 |
| §9 债 #2 偿还 | Task 2 |
| §9 多选支持 | Task 1 |
| §10 文件清单（新增 + 修改） | Task 1~12 全覆盖 |
| §11 手动验收清单 | Task 14 |

无遗漏。

### 2. 占位符扫描

- 无 "TBD" / "TODO 后续补"
- 无 "添加适当的错误处理" 类模糊措辞
- 无 "类似 Task N" 引用（所有代码完整给出）
- 每个 step 含完整代码块或确切命令

### 3. 类型一致性检查

- `RepeatMode.Off / List / One` —— 在 Task 3 定义，Task 4/5/6/7 一致使用
- `CalculateNextIndex(int? failedIndex = null)` —— Task 6 定义，Task 7 调用签名匹配
- `PlayTrackAtAsync(int index, int skipCount = 0)` —— Task 6 定义，Task 6 自身递归调用 + Task 7 调用一致
- `_shuffleHistory` —— `HashSet<int>` 全程一致
- `Queue` —— `ObservableCollection<Track>` 全程一致
- `HasCurrentTrack` —— Task 5 定义，Task 7 用作 CanExecute
- `event Action? TrackEnded` —— Task 2 定义（接口 + 实现），Task 7 订阅签名一致

无类型不匹配。

---

**计划完成，已包含所有 Spec 要求与可执行细节。**
