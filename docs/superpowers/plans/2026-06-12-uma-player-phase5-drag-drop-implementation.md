# UmaPlayer Phase 5 — 拖拽支持 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给 PlaylistView 加 3 个拖拽能力：外部音频文件拖入入队；队列内项拖拽重排（含多选）；拖拽过程中显示插入线 + 边框高亮。同时偿还 COUPLING.md §5 那条 in-flight `RemoveTrack`/`MoveTracks` 的 `_playToken` 残留债。

**Architecture:** View 层负责所有 DragDrop 事件、命中测试、文件过滤、Adorner 绘制；VM 层仅暴露纯数据命令（`DropExternalFiles(paths)` / `MoveTracks(args)`）。重排算法用对象身份（Track record 引用相等）回找 `CurrentIndex` 与 `_shuffleHistory`，不做索引算术。外部拖入与内部重排通过 DataObject 格式区分（`FileDrop` vs `"UmaPlayer.QueueItems"`），互斥处理。

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm 8.x, `System.Windows.DragDrop` (BCL), `System.Windows.Documents.Adorner`。验证方式：构建 + manual acceptance pass（与 Phase 1-4 节奏一致；无 xUnit 项目）。

**Spec 引用：** [`docs/superpowers/specs/2026-06-12-uma-player-phase5-drag-drop-design.md`](../specs/2026-06-12-uma-player-phase5-drag-drop-design.md)

---

## 文件结构

| 文件 | 操作 | 责任 |
|------|------|------|
| `Models/MoveTracksArgs.cs` | 新增 | 重排命令的参数 record（SourceIndices + TargetIndex） |
| `Views/Controls/DragDropExtensions.cs` | 新增 | `IsDragOver` attached DependencyProperty + `AudioExtensions` 静态白名单 |
| `Views/Controls/DropInsertionAdorner.cs` | 新增 | 在 ListBox AdornerLayer 上画 1px 插入线 |
| `ViewModels/PlaylistViewModel.cs` | 改 | 新增 `DropExternalFiles` / `MoveTracks` 命令；`RemoveTrack` 入口加 `_playToken++` |
| `Views/Controls/PlaylistView.xaml` | 改 | 根 Border 加 `AllowDrop` + `IsDragOver` 触发器；ListBox 加 `AllowDrop` + 5 个事件挂接 |
| `Views/Controls/PlaylistView.xaml.cs` | 改 | 拖拽启动（PreviewMouseLeftButtonDown + MouseMove）、DragOver/Drop/DragLeave 处理、`ComputeInsertIndex` 命中测试、Adorner 生命周期 |
| `docs/PROJECT.md` | 改 | Phase 5 状态 + 新模块描述 |
| `docs/COUPLING.md` | 改 | 勾选 §6 检查清单第 7 项 + 标记 §5 残留债已偿 |

工作分支：`feature/phase5-drag-drop`（已存在，spec 提交于 `e10ae0d`）

---

## 验证方式说明

- 本项目 **没有 xUnit / 自动化测试项目**（详见 spec §7.3）。每个任务用"构建成功 + 必要时手工触发场景"作为通过条件。
- 最终一次性 manual acceptance pass 在 Task 8 集中跑（覆盖 spec §7.1 全部 10 个场景 + §7.2 三项 Phase 4 回归），并对应一个独立 commit（与 Phase 2/3/4 节奏一致：`a81b144 test: Phase 4 manual acceptance pass`）。
- 构建命令：`dotnet build UmaPlayer.sln -c Debug --nologo -v quiet`，期望 `Build succeeded. 0 Warning(s) 0 Error(s)`。
- 启动命令：`dotnet run --project UmaPlayer.csproj`。

---

## Task 1：MoveTracksArgs record

**Files:**
- Create: `Models/MoveTracksArgs.cs`

- [ ] **Step 1: 创建 `Models/MoveTracksArgs.cs`**

```csharp
namespace UmaPlayer.Models;

/// <summary>
/// 队列内拖拽重排命令的参数（Phase 5）。
///
/// 由 View 层（PlaylistView.xaml.cs）的 Drop handler 构造，传给
/// PlaylistViewModel.MoveTracksCommand。
///
/// 不变量（由 View 层保证）：
///   - SourceIndices 升序无重复
///   - 每项 ∈ [0, Queue.Count)
///   - TargetIndex ∈ [0, Queue.Count]，i 表示插到 i 之前；Count 表示末尾
///
/// 用 record 与 AppSettings / QueueState 风格保持一致。
/// </summary>
public sealed record MoveTracksArgs(
    IReadOnlyList<int> SourceIndices,
    int TargetIndex);
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build UmaPlayer.sln -c Debug --nologo -v quiet`
Expected: `已成功生成。 0 个警告 0 个错误`

- [ ] **Step 3: 提交**

```bash
git add Models/MoveTracksArgs.cs
git commit -m "feat(models): add MoveTracksArgs record (Phase 5 reorder command param)"
```

---

## Task 2：PlaylistViewModel.DropExternalFiles 命令

**Files:**
- Modify: `ViewModels/PlaylistViewModel.cs`（在现有 `AddToQueue` 命令之后插入新命令；与 `AddToQueue` 同语义但入口为 OS DragDrop）

- [ ] **Step 1: 在 `AddToQueueCommand` 之后（约第 256 行后，紧接 `RemoveTrack` 之前）插入新命令**

```csharp
    /// <summary>
    /// 外部文件拖入入队（Phase 5）。
    /// 与 AddToQueue 同语义：仅占位入队，不读元数据，不自动播放。
    /// 与 AddToQueue 区别：入口是 OS DragDrop（V 层已过滤白名单后缀）而非文件对话框。
    ///
    /// View 层契约：传入的 paths 已经过 .mp3/.wma/.flac/.aac/.wav 后缀过滤；
    /// 本命令不再二次过滤，避免双重职责。
    /// </summary>
    [RelayCommand]
    private void DropExternalFiles(IReadOnlyList<string> paths)
    {
        if (paths is null || paths.Count == 0) return;

        foreach (var path in paths)
        {
            Queue.Add(_metadataReader.CreateFallback(path));
        }
    }
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build UmaPlayer.sln -c Debug --nologo -v quiet`
Expected: `已成功生成。 0 个警告 0 个错误`（命令源生成器会自动产出 `DropExternalFilesCommand` 属性）

- [ ] **Step 3: 提交**

```bash
git add ViewModels/PlaylistViewModel.cs
git commit -m "feat(vm): add PlaylistViewModel.DropExternalFiles command"
```

---

## Task 3：PlaylistViewModel.MoveTracks 命令 + RemoveTrack 加 `_playToken++`

**Files:**
- Modify: `ViewModels/PlaylistViewModel.cs`

- [ ] **Step 1: 在 `RemoveTrack(int index)` 方法体最顶端加 `_playToken++`（COUPLING.md §5 残留债偿还）**

把 `RemoveTrack` 方法（约第 260 行）的开头改成：

```csharp
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

        // ... 后续代码保持不变 ...
```

注意：仅在 `if (index < 0 ...) return;` **之后**自增，避免无效调用浪费 token；其余代码不动。

- [ ] **Step 2: 在 `using` 区域加 `using UmaPlayer.Models;`（如未引用）+ 文件顶部确认有 `using System.Linq;`（已有）**

`MoveTracksArgs` 在 `UmaPlayer.Models` 命名空间。检查文件顶部 5 行附近的 `using` 列表，确认包含：
```
using UmaPlayer.Models;
using System.Linq;
```
若已经存在，跳过；否则补上。

- [ ] **Step 3: 在 `AddToQueueCommand` 与 `DropExternalFilesCommand`（Task 2 已加）之后，紧接 `RemoveTrack` 之前插入 `MoveTracks` 命令**

```csharp
    /// <summary>
    /// 队列内拖拽重排（Phase 5）。
    ///
    /// 算法用对象身份（Track 是 record，引用相等）回找 CurrentIndex 与
    /// _shuffleHistory；不用索引算术，避免多源多目标插入时的前移/后移混合错误。
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
        var historyObjs = new HashSet<Track>();
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

        // 7. 重建 CurrentIndex（按对象身份回找）
        CurrentIndex = (currentTrackObj != null) ? Queue.IndexOf(currentTrackObj) : -1;

        // 8. 重建 _shuffleHistory（按对象身份回找）
        _shuffleHistory.Clear();
        for (int i = 0; i < Queue.Count; i++)
        {
            if (historyObjs.Contains(Queue[i])) _shuffleHistory.Add(i);
        }
    }
```

- [ ] **Step 4: 构建验证**

Run: `dotnet build UmaPlayer.sln -c Debug --nologo -v quiet`
Expected: `已成功生成。 0 个警告 0 个错误`

- [ ] **Step 5: 提交**

```bash
git add ViewModels/PlaylistViewModel.cs
git commit -m "feat(vm): add PlaylistViewModel.MoveTracks command + _playToken bump in RemoveTrack

MoveTracks remaps CurrentIndex and _shuffleHistory by object identity
(Track is a record, so reference equality survives Queue reorder).
Playback is not interrupted: NAudio is unaware of the reorder; only
CurrentIndex follows the moved Track's record reference.

RemoveTrack also bumps _playToken now, closing the in-flight race
recorded in COUPLING.md §5 (PlayTrackAtAsync metadata read could write
Queue[i] = meta to the wrong slot if user deletes a non-current track
during the await window)."
```

---

## Task 4：DragDropExtensions（attached property + 后缀白名单）

**Files:**
- Create: `Views/Controls/DragDropExtensions.cs`

- [ ] **Step 1: 创建 `Views/Controls/DragDropExtensions.cs`**

```csharp
using System.IO;
using System.Windows;

namespace UmaPlayer.Views.Controls;

/// <summary>
/// 拖拽相关的 attached DependencyProperty 与静态辅助（Phase 5）。
///
/// IsDragOver: 由 PlaylistView code-behind 在 DragEnter/DragLeave 切换，
/// XAML 用 Style.Trigger 高亮根 Border 的 BorderBrush。
///
/// AudioExtensions: 与 IFileDialogService 在 OpenFiles 中使用的过滤器
/// "*.mp3;*.wma;*.flac;*.aac;*.wav" 严格对齐，单一来源，避免漂移。
/// </summary>
public static class DragDropExtensions
{
    /// <summary>支持的音频后缀白名单（小写，含点）。</summary>
    public static readonly IReadOnlyList<string> AudioExtensions = new[]
    {
        ".mp3", ".wma", ".flac", ".aac", ".wav"
    };

    /// <summary>过滤一组路径，仅保留后缀在白名单中的（大小写不敏感）。文件夹/缺失文件会被自动剔除。</summary>
    public static IReadOnlyList<string> FilterAudioPaths(IEnumerable<string>? paths)
    {
        if (paths is null) return Array.Empty<string>();

        var result = new List<string>();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            // 文件夹一般 Path.GetExtension 返回 ""，自动被白名单排除
            var ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) continue;
            foreach (var allowed in AudioExtensions)
            {
                if (string.Equals(ext, allowed, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(path);
                    break;
                }
            }
        }
        return result;
    }

    // —— IsDragOver attached property ——

    public static readonly DependencyProperty IsDragOverProperty =
        DependencyProperty.RegisterAttached(
            "IsDragOver",
            typeof(bool),
            typeof(DragDropExtensions),
            new PropertyMetadata(false));

    public static void SetIsDragOver(DependencyObject element, bool value) =>
        element.SetValue(IsDragOverProperty, value);

    public static bool GetIsDragOver(DependencyObject element) =>
        (bool)element.GetValue(IsDragOverProperty);
}
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build UmaPlayer.sln -c Debug --nologo -v quiet`
Expected: `已成功生成。 0 个警告 0 个错误`

- [ ] **Step 3: 提交**

```bash
git add Views/Controls/DragDropExtensions.cs
git commit -m "feat(view): add DragDropExtensions (IsDragOver attached prop + audio suffix filter)"
```

---

## Task 5：DropInsertionAdorner（1px 插入线）

**Files:**
- Create: `Views/Controls/DropInsertionAdorner.cs`

- [ ] **Step 1: 创建 `Views/Controls/DropInsertionAdorner.cs`**

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace UmaPlayer.Views.Controls;

/// <summary>
/// 队列内拖拽重排时，在 ListBox AdornerLayer 上画 1px 插入线（Phase 5）。
///
/// 颜色 = AccentPrimary（紫色，与主题一致），左右 4px 留白。
/// 位置由 _insertIndex 决定：
///   - insertIndex == Queue.Count → 画在最后一项底部
///   - 否则                       → 画在 Queue[insertIndex] 项顶部
///
/// 生命周期由 PlaylistView code-behind 集中管理 —— DragOver 时 Update + AdornerLayer.Add，
/// DragLeave/Drop/QueryContinueDrag 取消时 Hide + AdornerLayer.Remove。
/// </summary>
public sealed class DropInsertionAdorner : Adorner
{
    private readonly ListBox _listBox;
    private int _insertIndex;
    private readonly Pen _pen;

    public DropInsertionAdorner(ListBox listBox) : base(listBox)
    {
        _listBox = listBox;
        IsHitTestVisible = false; // 不拦截鼠标事件
        var brush = (Brush)Application.Current.FindResource("AccentPrimary");
        _pen = new Pen(brush, 2.0);
        _pen.Freeze();
    }

    /// <summary>更新插入位置；触发 InvalidateVisual 重画。</summary>
    public void Update(int insertIndex)
    {
        if (_insertIndex == insertIndex) return;
        _insertIndex = insertIndex;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_listBox.Items.Count == 0) return;

        double y;
        if (_insertIndex >= _listBox.Items.Count)
        {
            // 最后一项底部
            var lastIdx = _listBox.Items.Count - 1;
            if (_listBox.ItemContainerGenerator.ContainerFromIndex(lastIdx) is not ListBoxItem last) return;
            var rect = GetContainerRect(last);
            y = rect.Bottom;
        }
        else
        {
            if (_listBox.ItemContainerGenerator.ContainerFromIndex(_insertIndex) is not ListBoxItem container) return;
            var rect = GetContainerRect(container);
            y = rect.Top;
        }

        double left = 4;
        double right = _listBox.ActualWidth - 4;
        drawingContext.DrawLine(_pen, new Point(left, y), new Point(right, y));
    }

    private Rect GetContainerRect(ListBoxItem item)
    {
        // item 在 ListBox 坐标系下的 bounds
        var transform = item.TransformToAncestor(_listBox);
        var topLeft = transform.Transform(new Point(0, 0));
        return new Rect(topLeft, new Size(item.ActualWidth, item.ActualHeight));
    }
}
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build UmaPlayer.sln -c Debug --nologo -v quiet`
Expected: `已成功生成。 0 个警告 0 个错误`

- [ ] **Step 3: 提交**

```bash
git add Views/Controls/DropInsertionAdorner.cs
git commit -m "feat(view): add DropInsertionAdorner (1px accent-color insertion line)"
```

---

## Task 6：PlaylistView.xaml 加 AllowDrop + IsDragOver 触发器

**Files:**
- Modify: `Views/Controls/PlaylistView.xaml`

- [ ] **Step 1: 在 `<UserControl ...>` 根标签添加 `xmlns:local` 命名空间**

把 `<UserControl x:Class="UmaPlayer.Views.Controls.PlaylistView" ...>` 修改为包含一个新 xmlns：

```xml
<UserControl x:Class="UmaPlayer.Views.Controls.PlaylistView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:converters="clr-namespace:UmaPlayer.Converters"
             xmlns:local="clr-namespace:UmaPlayer.Views.Controls">
```

- [ ] **Step 2: 用根 Border 包裹整个 Grid，承载 IsDragOver 高亮**

把 `<Grid Margin="16,0,16,16">` 改为外层包一个 Border：

```xml
    <Border BorderThickness="2" BorderBrush="Transparent"
            AllowDrop="True"
            DragEnter="Root_DragEnter"
            DragOver="Root_DragOver"
            DragLeave="Root_DragLeave"
            Drop="Root_Drop">
        <Border.Style>
            <Style TargetType="Border">
                <Style.Triggers>
                    <Trigger Property="local:DragDropExtensions.IsDragOver" Value="True">
                        <Setter Property="BorderBrush" Value="{StaticResource AccentPrimary}"/>
                    </Trigger>
                </Style.Triggers>
            </Style>
        </Border.Style>
        <Grid Margin="16,0,16,16">
            <!-- 原有 Grid 内容保持不变 ... -->
        </Grid>
    </Border>
```

把当前 `Views/Controls/PlaylistView.xaml` 中第 18 行起的整段 `<Grid Margin="16,0,16,16"> ... </Grid>` 包进上述 `<Border>...<Grid>...</Grid></Border>` 中。最外层 `<UserControl>` 的根 child 由 Grid 改成 Border。

- [ ] **Step 3: 给 ListBox 加 `AllowDrop` + 5 个事件挂接**

将现有 `<ListBox x:Name="QueueList" ...>` 标签（第 62-69 行附近）改为：

```xml
            <ListBox x:Name="QueueList"
                     ItemsSource="{Binding Queue}"
                     SelectedItem="{Binding SelectedTrack, Mode=TwoWay}"
                     SelectionMode="Extended"
                     AllowDrop="True"
                     Background="Transparent" BorderThickness="0"
                     Foreground="{StaticResource ForegroundPrimary}"
                     ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                     MouseDoubleClick="QueueList_MouseDoubleClick"
                     KeyDown="QueueList_KeyDown"
                     PreviewMouseLeftButtonDown="QueueList_PreviewMouseLeftButtonDown"
                     PreviewMouseMove="QueueList_PreviewMouseMove"
                     PreviewMouseLeftButtonUp="QueueList_PreviewMouseLeftButtonUp"
                     DragOver="QueueList_DragOver"
                     DragLeave="QueueList_DragLeave"
                     Drop="QueueList_Drop">
```

新增点：`SelectionMode="Extended"`（多选）、`AllowDrop="True"`、6 个事件 hook。**保留** `MouseDoubleClick` 与 `KeyDown` 两个原有 hook。

- [ ] **Step 4: 构建验证（仅 XAML，预期 code-behind 缺方法会报编译错 —— 跳过此步直接到 Task 7）**

XAML 引用的事件 handler 会在 Task 7 加上；本任务只做 XAML 改动。**先不构建**，否则会因 handler 缺失报错。

- [ ] **Step 5: 提交**

```bash
git add Views/Controls/PlaylistView.xaml
git commit -m "feat(view): PlaylistView XAML adds AllowDrop, IsDragOver border trigger, multi-select

Note: code-behind handlers wired up in next task; build will fail until Task 7."
```

---

## Task 7：PlaylistView.xaml.cs 实现拖拽 handler 全套

**Files:**
- Modify: `Views/Controls/PlaylistView.xaml.cs`

- [ ] **Step 1: 在文件顶部 `using` 列表中加：**

确保以下 using 都在（按字母序，去重）：

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using UmaPlayer.Models;
using UmaPlayer.ViewModels;
```

- [ ] **Step 2: 在类内部增加拖拽相关字段**

在类 `PlaylistView` 内（紧跟 `private PlaylistViewModel? _vm;` 字段下方）增加：

```csharp
    /// <summary>内部拖拽自定义 DataObject 格式名（用于区分外部 FileDrop）。</summary>
    private const string QueueItemsFormat = "UmaPlayer.QueueItems";

    /// <summary>PreviewMouseLeftButtonDown 时记录的起点；MouseMove 用于阈值判定。</summary>
    private Point? _dragStartPoint;

    /// <summary>当前 ListBox AdornerLayer 上的插入线 Adorner；同一时刻最多 1 个。</summary>
    private DropInsertionAdorner? _currentAdorner;
```

- [ ] **Step 3: 在 `RemoveButton_Click` 之后追加拖拽启动 handler 与外部拖入 handler**

```csharp
    // —— Phase 5：拖拽启动（PreviewMouseLeftButton* + MouseMove） ——

    private void QueueList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 仅记录起点；实际启动在 MouseMove 阈值后。
        // 不抢 ListBox 默认选中行为 —— 不 Handled。
        _dragStartPoint = e.GetPosition(QueueList);
    }

    private void QueueList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 鼠标抬起即清除起点，避免松开后再移动还会触发拖拽
        _dragStartPoint = null;
    }

    private void QueueList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_vm is null) return;
        if (_dragStartPoint is null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { _dragStartPoint = null; return; }

        var current = e.GetPosition(QueueList);
        var dx = Math.Abs(current.X - _dragStartPoint.Value.X);
        var dy = Math.Abs(current.Y - _dragStartPoint.Value.Y);
        if (dx < SystemParameters.MinimumHorizontalDragDistance &&
            dy < SystemParameters.MinimumVerticalDragDistance)
            return;

        // 只有当拖动起点落在某个 ListBoxItem 上时才启动（避免空白区拖出空选）
        var sourceItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (sourceItem is null) { _dragStartPoint = null; return; }

        // SelectedItems 顺序未必升序；按 Queue 索引升序排列
        var indices = QueueList.SelectedItems
            .Cast<Track>()
            .Select(t => _vm.Queue.IndexOf(t))
            .Where(i => i >= 0)
            .Distinct()
            .OrderBy(i => i)
            .ToList();
        if (indices.Count == 0) { _dragStartPoint = null; return; }

        _dragStartPoint = null; // 启动拖拽即消费起点
        var data = new DataObject(QueueItemsFormat, indices);
        // DoDragDrop 是同步 modal —— 期间 UI 线程被 OLE 阻塞，但 NAudio 在另一线程推流不停
        DragDrop.DoDragDrop(QueueList, data, DragDropEffects.Move);

        // 拖拽结束（无论 Drop / Esc / Leave）后清理 Adorner
        HideAdorner();
    }

    // —— Phase 5：DragOver / Drop（区分内部重排 vs 外部 FileDrop） ——

    private void QueueList_DragOver(object sender, DragEventArgs e)
    {
        if (_vm is null) { e.Effects = DragDropEffects.None; e.Handled = true; return; }

        if (e.Data.GetDataPresent(QueueItemsFormat))
        {
            // 内部重排
            int insertIdx = ComputeInsertIndex(e.GetPosition(QueueList));
            ShowAdorner(insertIdx);
            e.Effects = DragDropEffects.Move;
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            // 外部文件
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            var audio = DragDropExtensions.FilterAudioPaths(paths);
            e.Effects = audio.Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void QueueList_DragLeave(object sender, DragEventArgs e)
    {
        // 拖出 ListBox 边界即隐藏插入线（外部高亮由 Root_DragLeave 处理）
        HideAdorner();
    }

    private void QueueList_Drop(object sender, DragEventArgs e)
    {
        if (_vm is null) { e.Handled = true; return; }
        try
        {
            if (e.Data.GetDataPresent(QueueItemsFormat))
            {
                var sources = e.Data.GetData(QueueItemsFormat) as IReadOnlyList<int>;
                if (sources is null || sources.Count == 0) return;
                int target = ComputeInsertIndex(e.GetPosition(QueueList));
                _vm.MoveTracksCommand.Execute(new MoveTracksArgs(sources, target));
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                var audio = DragDropExtensions.FilterAudioPaths(paths);
                if (audio.Count > 0)
                {
                    _vm.DropExternalFilesCommand.Execute(audio);
                }
            }
        }
        finally
        {
            HideAdorner();
            e.Handled = true;
        }
    }

    // —— Phase 5：根 Border 高亮（仅外部文件拖入触发；内部重排不亮整框） ——

    private void Root_DragEnter(object sender, DragEventArgs e)
    {
        if (sender is not DependencyObject dep) return;
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            !e.Data.GetDataPresent(QueueItemsFormat))
        {
            DragDropExtensions.SetIsDragOver(dep, true);
        }
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        // DragEnter 已处理高亮；此处仅设 Effects 防止默认拒绝
        if (e.Data.GetDataPresent(QueueItemsFormat))
        {
            // 内部重排不在 Root 处理 Effects，让 ListBox 的 DragOver 决定
            return;
        }
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            var audio = DragDropExtensions.FilterAudioPaths(paths);
            e.Effects = audio.Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }
    }

    private void Root_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is DependencyObject dep)
            DragDropExtensions.SetIsDragOver(dep, false);
    }

    private void Root_Drop(object sender, DragEventArgs e)
    {
        // 实际 Drop 由 ListBox_Drop 处理；此处仅清高亮
        if (sender is DependencyObject dep)
            DragDropExtensions.SetIsDragOver(dep, false);
    }

    // —— Phase 5：插入位置命中测试 ——

    /// <summary>
    /// 把 ListBox 坐标系下的鼠标点映射到插入索引 ∈ [0, Queue.Count]。
    /// 命中某项 → 鼠标在上半部 → 该项之前；下半部 → 该项之后。
    /// 无命中 → Queue.Count（末尾）。
    /// </summary>
    private int ComputeInsertIndex(Point posInListBox)
    {
        for (int i = 0; i < QueueList.Items.Count; i++)
        {
            if (QueueList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem container) continue;
            var transform = container.TransformToAncestor(QueueList);
            var topLeft = transform.Transform(new Point(0, 0));
            var rect = new Rect(topLeft, new Size(container.ActualWidth, container.ActualHeight));
            if (posInListBox.Y >= rect.Top && posInListBox.Y < rect.Bottom)
            {
                return posInListBox.Y < rect.Top + rect.Height / 2 ? i : i + 1;
            }
        }
        return QueueList.Items.Count; // 鼠标在所有项之下 → 末尾
    }

    // —— Phase 5：Adorner 生命周期 ——

    private void ShowAdorner(int insertIndex)
    {
        var layer = AdornerLayer.GetAdornerLayer(QueueList);
        if (layer is null) return;
        if (_currentAdorner is null)
        {
            _currentAdorner = new DropInsertionAdorner(QueueList);
            layer.Add(_currentAdorner);
        }
        _currentAdorner.Update(insertIndex);
    }

    private void HideAdorner()
    {
        if (_currentAdorner is null) return;
        var layer = AdornerLayer.GetAdornerLayer(QueueList);
        layer?.Remove(_currentAdorner);
        _currentAdorner = null;
    }
```

- [ ] **Step 4: 构建验证**

Run: `dotnet build UmaPlayer.sln -c Debug --nologo -v quiet`
Expected: `已成功生成。 0 个警告 0 个错误`

- [ ] **Step 5: 启动并冒烟**

Run: `dotnet run --project UmaPlayer.csproj`
冒烟项（不算正式验收，仅确认拖拽路径不崩）：
- 从资源管理器拖一个 .mp3 进 PlaylistView → 边框高亮 → 末尾入队
- 队列内拖第 1 项到第 3 项后 → 出现紫色 1px 插入线 → 松开后顺序变化

- [ ] **Step 6: 提交**

```bash
git add Views/Controls/PlaylistView.xaml.cs
git commit -m "feat(view): wire up PlaylistView drag-drop handlers (Phase 5)

External FileDrop -> DropExternalFilesCommand (audio suffix filter).
Internal QueueItems format -> MoveTracksCommand (1px insertion adorner).
4px threshold start; selectedItems sorted by Queue index ascending.
Root border IsDragOver attached state highlights only on external file
drag (internal reorder leaves the border alone)."
```

---

## Task 8：手动验收（10 个场景 + 3 个回归）

**Files:**
- 仅创建 commit（无文件改动），与 Phase 4 acceptance pass 风格一致。

清空 queue.json 以确保起点干净：

```bash
rm -f "$LOCALAPPDATA/UmaPlayer/queue.json"
```

按 spec §7.1 清单逐项验证：

- [ ] **Scenario 1：** 资源管理器拖 1 个 .mp3 → 末尾入队，不播放，▶ 不亮起
- [ ] **Scenario 2：** 资源管理器拖 5 个文件（4 mp3 + 1 .txt）→ 仅 4 个音频入队
- [ ] **Scenario 3：** 资源管理器拖 1 个文件夹 → 边框不高亮（光标显示禁止），松开后队列无变化
- [ ] **Scenario 4：** 队列里单选第 3 项拖到第 1 项前 → 出现插入线 → 松开后该曲到位置 0；CurrentIndex 跟随，播放不中断
- [ ] **Scenario 5：** 队列里 Ctrl+点选第 1、3 项拖到第 5 项前 → 聚成连续块到位置 3，块内顺序保留 [1→3, 3→4]，原 2 滑到 1，原 4 滑到 2
- [ ] **Scenario 6：** 拖动当前正在播放的曲到末尾 → 音频继续播，▶ 标记跟到末尾，Position 不归零
- [ ] **Scenario 7：** 拖动时按 Esc → 队列无变化，Adorner 立即消失
- [ ] **Scenario 8：** 单选拖到原位置 → 队列无变化（幂等）
- [ ] **Scenario 9：** Shuffle 开 + 已播过两首 → 重排后再 Next，已播过两首仍不会被随机到（_shuffleHistory 跟随重映射）
- [ ] **Scenario 10：** 拖入 + 重排后关窗 → 重启 → queue.json 与启动后队列均反映新顺序

回归（spec §7.2）：

- [ ] **Regression A：** 启动恢复有当前曲（CurrentIndex≥0）→ 首次按 ▶ 触发 PlayCurrent，不卡死
- [ ] **Regression B：** 关闭后查看 queue.json，包含完整状态（cancel-and-close 仍生效）
- [ ] **Regression C：** ▶ 按钮、Slider、Shuffle/Repeat 图标外观正常（无 Aero 白底回退）

- [ ] **Step 1: 全部 13 项通过后提交空 commit**

```bash
git commit --allow-empty -m "test: Phase 5 manual acceptance pass

All 10 drag-drop scenarios + 3 Phase 4 regressions verified passing:
- External FileDrop with mixed/folder/audio-only inputs
- Internal single + multi-select reorder, Esc cancel, drop-on-self idempotency
- CurrentIndex + _shuffleHistory follow reorder via object identity
- Phase 4 invariants (PlayCurrent, queue.json write, theme styles) intact"
```

---

## Task 9：文档更新（PROJECT.md + COUPLING.md）

**Files:**
- Modify: `docs/PROJECT.md`
- Modify: `docs/COUPLING.md`

- [ ] **Step 1: PROJECT.md — 文档头日期 + 阶段**

把 `docs/PROJECT.md` 第 5 行的：
```
> 文档日期：2026/06/12 · 对应分支：`master` · 当前阶段：**Phase 4 完成**（队列持久化）
```
替换为：
```
> 文档日期：2026/06/12 · 对应分支：`master` · 当前阶段：**Phase 5 完成**（拖拽支持）
```

- [ ] **Step 2: PROJECT.md — §1 项目简介尾段**

把第 11 行（"**UmaPlayer** 是一款..." 那段）末尾改为：
```
... Phase 4 加入队列持久化（关闭时写 `queue.json`，启动时恢复列表 + Shuffle/Repeat 模式 + CurrentIndex）。Phase 5 加入拖拽支持（外部音频文件拖入入队、队列内项拖拽重排、多选拖拽、1px 插入线 + 边框高亮）。可视化、库扫描、多命名播放列表等放在 Phase 6+。
```

- [ ] **Step 3: PROJECT.md — §1.1 关键特性表追加 1 行**

在"队列持久化"行下方追加：
```
| 拖拽支持 | 外部音频文件拖入入队、队列内单/多选拖拽重排、紫色 1px 插入线（Phase 5） |
```

- [ ] **Step 4: PROJECT.md — §3 目录结构两处增补**

在 `├── Models/` 块的 `QueueState.cs` 下追加：
```
│   ├── MoveTracksArgs.cs        # 重排命令参数 record (Phase 5)
```

在 `├── Views/Controls/` 块尾部（`PlaylistView.xaml(.cs)` 之后）追加：
```
│       ├── DragDropExtensions.cs   # IsDragOver attached prop + 音频后缀白名单 (Phase 5)
│       └── DropInsertionAdorner.cs # 1px 插入线 Adorner (Phase 5)
```

在 `└── docs/` 块的 specs/ 与 plans/ 下方各追加 Phase 5 一项：
```
        │   └── 2026-06-12-uma-player-phase5-drag-drop-design.md         # Phase 5 设计
```
和
```
            └── 2026-06-12-uma-player-phase5-drag-drop-implementation.md # Phase 5 计划
```

（具体行用现有 Phase 4 那行作为锚点替换；保持表格对齐。）

- [ ] **Step 5: PROJECT.md — §4.3 关键设计决策追加第 12 项**

在 `11. **Cancel-and-close 关闭模式（Phase 4）**：...` 段落之后追加：

```
12. **拖拽职责分层（Phase 5）**：所有 DragDrop 事件、命中测试、文件后缀过滤、Adorner 绘制都在 View 层（PlaylistView.xaml.cs / DragDropExtensions / DropInsertionAdorner）；VM 仅暴露纯数据命令 `DropExternalFilesCommand(IReadOnlyList<string>)` 与 `MoveTracksCommand(MoveTracksArgs)`，不依赖 WPF DragDrop 原语。重排时 `CurrentIndex` 与 `_shuffleHistory` 用对象身份（Track record 引用相等）回找新位置 —— 不做索引算术，避免多源多目标插入时前移/后移混合错误，且让"拖动当前曲"天然不中断播放（NAudio 不知道 Queue 重排，currentTrackObj 仍是同一个 record）。外部 FileDrop 与内部重排通过 DataObject 格式区分（`FileDrop` vs `"UmaPlayer.QueueItems"`），DragOver / Drop 内先判内部再判外部。
```

- [ ] **Step 6: PROJECT.md — §5.5 Views 段 PlaylistView 子项追加拖拽说明**

在 `- **`PlaylistView`** *(UserControl, Phase 2)*：...` 行之后追加一行（保持原有缩进）：

```
  - **拖拽（Phase 5）**：根 Border 持 `local:DragDropExtensions.IsDragOver` 触发器，外部 FileDrop 时整框高亮紫色边框；ListBox 内部拖拽通过 `PreviewMouseLeftButtonDown` + 4px 阈值启动 `DragDrop.DoDragDrop(... QueueItemsFormat ...)`，DragOver 期间在 AdornerLayer 上画紫色 1px 插入线。`SelectionMode="Extended"` 启用多选，拖出的源索引按 Queue 升序整理后传给 VM
```

- [ ] **Step 7: PROJECT.md — §9 已知约束追加 1 项**

在 `- **WPF inline Style 必须 `BasedOn`...` 那段之后追加：

```
- **OLE DragDrop 模态期间 UI 线程阻塞**（Phase 5）：`DragDrop.DoDragDrop` 是同步 OLE modal 调用，期间 UI 线程被卡住，进度条不刷新（NAudio 在另一线程继续推流，音频不停）。这是 WPF 的固有行为，用户操作上感知不到（拖拽期间本来就不需要看进度）；不要尝试在 UI 线程外启动 DragDrop —— OLE 拒绝。
```

- [ ] **Step 8: PROJECT.md — §10 历史与参考追加 Phase 5 设计/计划链接 + 里程碑**

在 §10 设计稿列表末尾追加：
```
  - [`docs/superpowers/specs/2026-06-12-uma-player-phase5-drag-drop-design.md`](./superpowers/specs/2026-06-12-uma-player-phase5-drag-drop-design.md) — Phase 5 拖拽支持设计
```

在实现计划列表末尾追加：
```
  - [`docs/superpowers/plans/2026-06-12-uma-player-phase5-drag-drop-implementation.md`](./superpowers/plans/2026-06-12-uma-player-phase5-drag-drop-implementation.md) — Phase 5
```

在主要里程碑提交末尾（Phase 4 那一组之后）追加：
```
  - **Phase 5**（feature/phase5-drag-drop → master）
    - 见 `git log --oneline feature/phase5-drag-drop` —— 9 commits（spec/plan/QA × 1 + impl × 7）
```

- [ ] **Step 9: COUPLING.md — 头部日期 + 阶段**

把 `docs/COUPLING.md` 第 3 行的：
```
> 创建日期：2026/06/06 · 更新日期：2026/06/12 · 对应分支：`master` · 对应阶段：**Phase 4 完成**（队列持久化）
```
替换为（更新日期保持 2026/06/12，本任务在同一天）：
```
> 创建日期：2026/06/06 · 更新日期：2026/06/12 · 对应分支：`master` · 对应阶段：**Phase 5 完成**（拖拽支持）
```

把第 5 行尾部说明改为：
```
> **本文档的用途：** 不是行动清单，是**风险登记册**。Phase 2 偿还债 #2；Phase 3 偿还债 #3/#4；Phase 4 加入队列持久化；Phase 5 加入拖拽 + 偿还 §5 残留 in-flight `RemoveTrack`/`MoveTracks` 债。剩余债与后续工作详见 §6。
```

- [ ] **Step 10: COUPLING.md — TL;DR 表更新 Phase 5 行**

把 `| Phase 5 是否会变痛 | ⚠️ **多命名播放列表会改 queue.json schema** | ...` 行替换为：
```
| Phase 6 是否会变痛 | ⚠️ **多命名播放列表会改 queue.json schema** | 当前 schema 仅一个队列；多列表需 schema v2 + 迁移 |
```

- [ ] **Step 11: COUPLING.md — §1 健康度结尾段追加 Phase 5 结论**

在 `✅ Phase 4 队列持久化沿用相同模式：...` 行下方追加：
```
✅ Phase 5 拖拽支持：所有 WPF DragDrop 事件全部在 View 层；VM 新增 `DropExternalFilesCommand` / `MoveTracksCommand` 仍是纯数据命令，未引入新跨域抽象。`MoveTracks` 重排算法用对象身份回找 CurrentIndex 与 _shuffleHistory，与 RemoveTrack 既有的"对象身份重映射"模式同一抽象层
```

把 `**结论：** Phase 4 后约 ~1700 行代码...` 改为：
```
**结论：** Phase 5 后约 ~1900 行代码（含拖拽相关 ~200 行 View / ~100 行 VM 命令）。MainViewModel 仍维持 44 行 Strict Facade；继续加功能（多命名播放列表 / 库扫描）不会再触碰核心架构。
```

- [ ] **Step 12: COUPLING.md — §2 依赖图新增行**

在 | `PlaylistViewModel` | ... | 行末增加 `MoveTracksArgs`：
```
| `PlaylistViewModel` | `IPlaybackService`, `IFileDialogService`, `ITrackMetadataReader`, `IQueuePersistence` | `Track`、`RepeatMode`、`QueueState`、`MoveTracksArgs`、`File.Exists` |
```

新增一行：
```
| `PlaylistView` (Phase 5) | — | `DragDropExtensions`、`DropInsertionAdorner`、`AdornerLayer`、`DragDrop`、`MoveTracksArgs`；订阅 ListBox 6 个拖拽事件 + `PropertyChanged` / `Queue.CollectionChanged` |
```
（替换原 PlaylistView 行）

- [ ] **Step 13: COUPLING.md — §5 隐式契约表把 in-flight RemoveTrack 那条标记为已偿，并新增 Phase 5 项**

把 `| `PlaylistViewModel.PlayTrackAtAsync` 期间 `RemoveTrack` 非当前曲未自增 `_playToken` | 代码审查发现，未修复（继承自 Phase 2） | ...Phase 5 重构时一并修 |` 那行替换为：
```
| `PlaylistViewModel.PlayTrackAtAsync` 期间 `RemoveTrack` 非当前曲未自增 `_playToken` | ✅ Phase 5 已偿（commit 见 git log）—— RemoveTrack 入口加 `_playToken++`；MoveTracks 同样纪律 | in-flight 元数据写到错位的 race 已关闭 |
```

在 `| **Phase 4 新增** | | |` 区块下方追加 `| **Phase 5 新增** | | |` 与 4 条新契约：

```
| **Phase 5 新增** | | |
| 内部拖拽 DataObject 格式名固定为 `"UmaPlayer.QueueItems"` | `PlaylistView.xaml.cs:QueueItemsFormat` 常量 + `MoveTracks*` handler | 重命名常量会让外部 FileDrop 与内部重排冲突；DragOver / Drop 区分依赖此格式名 |
| `MoveTracksArgs.SourceIndices` 必须升序无重复，目标索引 ∈ [0, Queue.Count] | View 层 DoDragDrop 前 `OrderBy(i => i).Distinct()`；VM 入口防御式跳过越界 | 违反时 `Queue.RemoveAt(i)` 倒序删可能跨界，OnIndexOutOfRange 静默吞 |
| `CurrentIndex` 与 `_shuffleHistory` 在 MoveTracks 内用 **对象身份**（Track record 引用相等）重映射 | 实现注释 + spec §4 算法描述 | 改用索引算术会在多源多目标交错移动时算错；且会破坏"拖动当前曲不中断播放"的不变量 |
| `DropInsertionAdorner` 生命周期由 PlaylistView code-behind 集中管理 | `_currentAdorner` 字段 + `ShowAdorner/HideAdorner` 配对 | DragOver / Drop / DragLeave / OLE 取消任一路径漏调 `HideAdorner` 都会让 1px 紫线残留在 ListBox 上 |
```

- [ ] **Step 14: COUPLING.md — §6 启动检查清单更新到 Phase 6**

把第 6 行起的整段标题与段落改为：
```
## 6. Phase 6 启动检查清单

> Phase 5（拖拽支持）已完成并准备合并到 master。下一阶段（如多命名播放列表 / 库扫描）启动时按以下顺序：
>
> **更新（Phase 5 完成）：** 项 7（拖拽）已完成；§5 in-flight RemoveTrack 残留债已偿。后续 Phase 6+ 仍待办：6、8。

1. ✅ **VM 拆分**（Phase 3 完成，commit `54edf9a`）
2. ✅ **`PlayerBar` / `PlaylistView` 去硬转型**（Phase 3 完成）
3. ✅ **抽 `ITrackMetadataReader`**（Phase 3 完成，commit `c9cd1bd`）
4. ✅ **解决 settings 合并纪律**（Phase 3 完成，commit `fadae44`）
5. ✅ **队列持久化**（Phase 4 完成）
6. ☐ **多命名播放列表（L3）** —— 真正的"播放列表管理"；queue.json 需 schema v2 + 迁移；UI 侧需 Tab 或侧栏
7. ✅ **拖拽支持**（Phase 5 完成）—— 外部入队 + 内部重排 + 多选 + 1px 插入线 Adorner
8. ☐ **偿还债 #1 (`BitmapImage`)** —— 想给 `PlayerViewModel` 写单元测试时一并做

**Phase 5 实际工作量：** 9 commits（spec + plan + 7 impl + acceptance + docs）≈ 半个工作日（subagent-driven，单 session 完成）

**Phase 6+ 候选范围预估：** 多命名播放列表 ~10h+；库扫描 ~12h+。
```

- [ ] **Step 15: COUPLING.md — §7 不要做的事追加 3 项**

在末尾追加：
```
- ❌ **在 `MoveTracks` 里改用索引算术替代对象身份重映射**（Phase 5）—— 多源多目标交错时前移/后移混合会算错；且会破坏"拖动当前曲不中断播放"的不变量
- ❌ **在 PlaylistView 之外定义 `QueueItemsFormat` 常量字符串硬编码**（Phase 5）—— 字符串名漂移会让外部 FileDrop 与内部重排互判错路径
- ❌ **让 VM 直接消费 WPF `DragEventArgs`/`DataObject`**（Phase 5）—— View 层负责拖拽机制；VM 只接收已过滤好的 paths / 索引
```

- [ ] **Step 16: COUPLING.md — §8 参考链接追加 Phase 5**

在 specs/ 列表末尾追加：
```
  - [`docs/superpowers/specs/2026-06-12-uma-player-phase5-drag-drop-design.md`](./superpowers/specs/2026-06-12-uma-player-phase5-drag-drop-design.md) — Phase 5
```
在 plans/ 列表末尾追加：
```
  - [`docs/superpowers/plans/2026-06-12-uma-player-phase5-drag-drop-implementation.md`](./superpowers/plans/2026-06-12-uma-player-phase5-drag-drop-implementation.md) — Phase 5
```

- [ ] **Step 17: 构建验证（确认文档改动未触发任何代码错）**

Run: `dotnet build UmaPlayer.sln -c Debug --nologo -v quiet`
Expected: `已成功生成。 0 个警告 0 个错误`

- [ ] **Step 18: 提交**

```bash
git add docs/PROJECT.md docs/COUPLING.md
git commit -m "docs: update PROJECT.md and COUPLING.md to Phase 5 state

- PROJECT.md: bump phase to Phase 5, add MoveTracksArgs +
  DragDropExtensions + DropInsertionAdorner to module map, document
  drag-drop layer responsibility split, add OLE DragDrop UI-thread-block
  gotcha to known constraints, add Phase 5 milestone marker
- COUPLING.md: mark §5 in-flight RemoveTrack debt paid, add 4 Phase 5
  implicit contracts (QueueItemsFormat constant, SourceIndices invariants,
  object-identity remap, Adorner lifecycle), bump checklist to Phase 6"
```

---

## 完成标志

- 9 个 task 各自 commit（plus spec commit `e10ae0d`，共 10 commits）
- `dotnet build` 0 警告 0 错误
- 全部 10 个验收场景 + 3 个回归通过
- `master` 分支可通过 `git checkout master && git merge --no-ff feature/phase5-drag-drop` 合并

---

## 自检（Self-Review）

**Spec 覆盖：**
- §1 范围（外部入队 + 重排 + 多选）→ Task 2/3/6/7 全覆盖
- §2 架构（View/VM 分层）→ Task 4/5（View 工具）+ Task 2/3（VM 命令）
- §3 数据流（FileDrop vs QueueItemsFormat 区分）→ Task 7 `QueueList_DragOver` / `QueueList_Drop`
- §4 重排算法（对象身份回找）→ Task 3 完整代码
- §5 错误处理与边界 → Task 8 验收脚本逐项覆盖
- §6 UI 反馈（高亮 + 插入线）→ Task 5 Adorner + Task 6 IsDragOver 触发器 + Task 7 Root_DragEnter
- §7 测试策略 → Task 8 完整 13 项手动验收
- §8 契合现有架构（PlayerVM 不动、Strict Facade 不动、queue.json 已有路径）→ 计划全程未触 PlayerVM/MainViewModel
- §9 风险（OLE 阻塞、Adorner DPI、大量文件、in-flight race）→ §9 风险 1 与 4 已纳入计划；2 与 3 验收阶段如有再处理（spec 已声明）

**Placeholder 扫：** 无 TBD/TODO/"add appropriate"。每段代码都贴齐。

**类型一致性：**
- `MoveTracksArgs(IReadOnlyList<int> SourceIndices, int TargetIndex)` 在 Task 1 定义、Task 3 消费、Task 7 构造，签名一致
- `DropExternalFilesCommand` 接收 `IReadOnlyList<string>`，Task 2 与 Task 7 一致
- `QueueItemsFormat` 字符串 `"UmaPlayer.QueueItems"` 在 Task 7 单点定义为 `private const`
- `_currentAdorner` / `ShowAdorner` / `HideAdorner` 在 Task 7 定义并自洽配对
- `DragDropExtensions.FilterAudioPaths` 与 `IsDragOverProperty` 在 Task 4 定义、Task 6/7 消费

无问题。
