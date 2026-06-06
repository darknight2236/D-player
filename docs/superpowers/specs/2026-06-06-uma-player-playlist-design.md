# UmaPlayer Phase 2 — 播放列表（当前队列）设计

**日期：** 2026-06-06
**状态：** 待审批
**对应分支：** `master`
**前置文档：** [`docs/PROJECT.md`](../../PROJECT.md) · [`docs/COUPLING.md`](../../COUPLING.md)

---

## 1. 概述

Phase 2 为 UmaPlayer 加入**最小可用的播放队列功能**，让用户能"一次选多首歌、连续播放"。

- **范围：** L1 — 单一内存队列，关闭即丢
- **不做：** 多列表管理、M3U 导入导出、文件夹扫描、拖拽重排序、拖拽入队、自动去重、队列持久化、VM 拆分、元数据读取抽象
- **工时估算：** 4~5 小时
- **顺手清理的技术债：** 2 项（见 §9）

> **核心边界：** 本 spec 严格只做 §3 列出的能力。任何"顺便加一下"的请求都属于 scope creep，应记入 `docs/COUPLING.md` 留给 Phase 3。

---

## 2. 用户故事

1. 我点"添加"，从文件对话框选 5 个 mp3，5 首歌按选择顺序加入队列底部。
2. 我双击队列里第 3 首，它立即开始播放。
3. 第 3 首播完，第 4 首自动开始（顺序模式 + 不循环时）。
4. 我打开"随机"模式，下一首从未播过的曲目中随机选。
5. 我打开"列表循环"，最后一首播完后从第一首重新开始。
6. 我打开"单曲循环"，当前曲反复播。
7. 我选中队列里某项，按 Delete 键，该项从队列移除（若是当前播放曲，则停止播放）。
8. 我按"清空"按钮，队列清空，播放停止。
9. 我加入一个损坏的 mp3，播放到它时自动跳过到下一首。
10. 关闭程序、再次启动：队列为空（不持久化）。

---

## 3. 功能范围

### 3.1 做（MVP）

| 类别 | 能力 |
|------|------|
| 队列管理 | 多选添加文件、单项删除（× 按钮 / Delete 键）、整体清空 |
| 播放控制 | 双击播放、上一首、下一首、当前曲高亮显示 |
| 自动推进 | 一首播完根据模式决定下一首；损坏文件自动跳过（最多连跳 3 首） |
| 随机模式 | 开 / 关；开启时从"未播过"集合中随机选 |
| 循环模式 | Off / List / One 三态循环 |
| 列表显示 | 文件名 + 时长占位（"--:--"，因不读元数据） |

### 3.2 明确不做（写下来防止 scope creep）

- ❌ 队列持久化（关闭即丢）
- ❌ 多个命名播放列表（侧栏切换）
- ❌ M3U / PLS / CUE 等格式的导入导出
- ❌ 拖拽文件入队
- ❌ 队列内拖拽重排序
- ❌ 自动去重
- ❌ 入队时批量读元数据（仅在播放该曲时读 → 见 §6 取舍 b）
- ❌ 搜索 / 过滤 / 排序
- ❌ ViewModel 拆分（PlayerVM / PlaylistVM 留给 Phase 3）
- ❌ ITrackMetadataReader 抽取（留给 Phase 3）

---

## 4. 架构

### 4.1 总览（既有 + 新增）

```
现有 (Phase 1)                       新增 (Phase 2)
┌─────────────────────────────┐
│ MainWindow                  │
│ ┌─────────────────────────┐ │
│ │ PlayerBar               │ │   完全不动
│ │ (现有，零修改)           │ │
│ └─────────────────────────┘ │
│ ┌─────────────────────────┐ │
│ │ PlaylistView (新)        │ │ ← 新 UserControl
│ │  ListBox + 工具按钮      │ │   绑同一个 MainViewModel
│ └─────────────────────────┘ │
└─────────────────────────────┘

MainViewModel
├─ Phase 1 字段/命令 (不动)
└─ Phase 2 新增字段:
   ├─ ObservableCollection<Track> Queue
   ├─ int CurrentIndex          (-1 = 队列为空 / 未选)
   ├─ Track? SelectedTrack      (UI 选中状态)
   ├─ bool ShuffleEnabled
   ├─ RepeatMode RepeatMode     (Off / List / One)
   ├─ HashSet<int> _shuffleHistory  (随机模式下"已播过"索引集合)
   └─ Phase 2 新增命令:
      ├─ AddToQueueCommand       (文件对话框 → 多文件入队)
      ├─ RemoveTrackCommand(int) (按索引移除)
      ├─ ClearQueueCommand
      ├─ PlayTrackAtCommand(int) (双击列表项触发)
      ├─ NextTrackCommand
      ├─ PrevTrackCommand
      ├─ ToggleShuffleCommand
      └─ CycleRepeatCommand      (Off → List → One → Off)

IPlaybackService (扩展)
└─ + event Action TrackEnded   ← 仅"自然播完"触发，用户 Stop 不触发

NAudioPlaybackService (扩展)
└─ OnPlaybackStopped 内增加判定:
   if (e.Exception == null && Position 接近 Duration - 200ms)
       raise TrackEnded
   else
       raise StateChanged(Stopped) (与现有行为一致)

Win32FileDialogService (扩展)
└─ OpenFiles 改 Multiselect=true
```

### 4.2 新增类型

#### `Models/RepeatMode.cs`
```csharp
public enum RepeatMode { Off, List, One }
```

#### `Views/Controls/PlaylistView.xaml` (+`.xaml.cs`)
新 UserControl，包含：
- 顶部工具栏：`[+ 添加]` `[清空]` `🔀 随机` `↻ 循环 (动态图标)`
- 主体：`ListBox`，`ItemsSource={Binding Queue}`，`SelectedItem={Binding SelectedTrack}`
- 单项模板：`▶/空 │ 文件名 │ [×]`
- `MouseDoubleClick` / `KeyDown(Delete)` → 调用 VM 命令

---

## 5. 数据流：关键场景

### 场景 A：多文件入队

```
用户点 [+ 添加]
  → AddToQueueCommand
    → _fileDialog.OpenFiles(filter, multiselect=true)
       returns IReadOnlyList<string> paths
    → foreach path in paths:
        Queue.Add(new Track(
            FilePath: path,
            Title:    Path.GetFileNameWithoutExtension(path),
            Artist=null, Album=null, ..., Duration: Zero))
    → (不触发播放；若 Queue 之前为空，CurrentIndex 保持 -1)
```

**取舍：** 入队时不读元数据，避免一次选 100 个文件等几秒。代价是列表只显示文件名。详见 §6。

### 场景 B：双击播放

```
PlaylistView.ListBox.MouseDoubleClick
  → e.OriginalSource 沿 VisualTree 找 ListBoxItem，取其 DataContext 索引
  → PlayTrackAtCommand(index)
    → CurrentIndex = index
    → _shuffleHistory.Clear() 然后 Add(index)
    → var meta = await ReadTrackMetadataAsync(Queue[index].FilePath)
    → Queue[index] = meta   (用读到的完整元数据替换占位 Track)
    → await _player.LoadAsync(meta)
    → _player.Play()
```

**注意：** `ObservableCollection.set[i]` 会触发 `CollectionChanged(Replace)`，ListBox 自动刷新该行。

### 场景 C：曲目自然播完 → 自动下一首

```
NAudioPlaybackService.OnPlaybackStopped(args)
  ↓ 判定:
  │   bool naturalEnd =
  │       args.Exception == null
  │    && _reader != null
  │    && (_reader.TotalTime - _reader.CurrentTime) <= 200ms
  │
  │   if (naturalEnd)
  │       RaiseOnUIThread(TrackEnded, ()=>{})  ← 不再发 StateChanged(Stopped)
  │   else
  │       SetState(PlayState.Stopped)           ← 原有行为
  ↓
MainViewModel.HandleTrackEnded()
  ↓ var next = CalculateNextIndex()
  ↓ if (next == -1) → _player.Stop() (实际已 Stop，仅同步 PlayState)
  │ else            → PlayTrackAtAsync(next, skipCount: 0)
  ↓
PlayTrackAtAsync 内部:
  try { ... 见场景 B ... }
  catch {
      // 损坏文件保护：递归跳下一首，但 skipCount 累加，>=3 则停止
      if (skipCount >= 3) { _player.Stop(); return; }
      var next = CalculateNextIndex(failedIndex: index)
      if (next == -1) { _player.Stop(); return; }
      await PlayTrackAtAsync(next, skipCount + 1)
  }
```

### 场景 D：CalculateNextIndex 算法

```
public int CalculateNextIndex(int? failedIndex = null)
{
    if (Queue.Count == 0) return -1;

    // 用户手动按 Next 时也走这里，模式优先级:
    //   RepeatOne 仅在「自动播完」时生效，手动 Next 当作 RepeatOff 处理
    //   (即用户按 Next 永远跳走，不重播)
    // 但本方法不区分场景；区分逻辑在 VM 调用处:
    //   HandleTrackEnded       → 若 RepeatOne，直接 return CurrentIndex (不调用此方法)
    //   NextTrackCommand 用户  → 直接调用此方法

    if (ShuffleEnabled)
    {
        // 候选 = 所有索引 - 已播过 - 失败过
        var candidates = Enumerable.Range(0, Queue.Count)
            .Except(_shuffleHistory)
            .Where(i => i != failedIndex)
            .ToList();

        if (candidates.Count == 0)
        {
            if (RepeatMode == RepeatMode.List)
            {
                _shuffleHistory.Clear();
                candidates = Enumerable.Range(0, Queue.Count)
                                       .Where(i => i != failedIndex).ToList();
                if (candidates.Count == 0) return -1;
            }
            else return -1;  // 全播完且不循环
        }
        return candidates[_random.Next(candidates.Count)];
    }
    else
    {
        var next = CurrentIndex + 1;
        if (next < Queue.Count) return next;
        return RepeatMode == RepeatMode.List ? 0 : -1;
    }
}
```

### 场景 E：删除单项

```
RemoveTrackCommand(int index)
  ↓ bool isCurrent = (index == CurrentIndex)
  ↓ Queue.RemoveAt(index)
  ↓ // 修正 CurrentIndex
  │ if (isCurrent) {
  │     _player.Stop()
  │     CurrentIndex = -1
  │ } else if (index < CurrentIndex) {
  │     CurrentIndex--  // 上方项被删，自身下移
  │ }
  ↓ // 修正 _shuffleHistory: 删除该索引，并对大于它的索引 -1
  │ _shuffleHistory = new HashSet<int>(
  │     _shuffleHistory.Where(i => i != index)
  │                    .Select(i => i > index ? i - 1 : i))
```

---

## 6. 关键设计取舍

### 取舍 a：入队不读元数据（明确推迟到 Phase 3）

**决策：** 入队仅存 `FilePath` + `文件名(无扩展名)` 作为 Title，其他字段空。

**理由：**
- 一次选 100 文件 × ATL 解析 50ms ≈ 5 秒卡顿，UX 差
- 列表只显示文件名，对 MVP 可接受
- 实际播放该曲时（双击 / 自动推进）才读元数据，结果回写到 `Queue[i]`

**未来升级路径**（Phase 3 写入计划即可，无需现在考虑）：
1. 抽 `ITrackMetadataReader` 接口（COUPLING.md 债 #4）
2. `AddToQueueCommand` 入队后启动 `Task.Run` 批量读，渐进式更新 `Queue[i]`
3. 加 LRU 元数据缓存避免重复 IO

### 取舍 b：TrackEnded 用 200ms 容差判定

**决策：** `NAudioPlaybackService.OnPlaybackStopped` 中判断 `(TotalTime - CurrentTime) <= 200ms` → 视为自然播完。

**理由：**
- WASAPI 缓冲区是 100ms，实际 `CurrentTime` 在 Stopped 时通常停在 `TotalTime - 30~80ms`
- 200ms 给足余量，避免短文件/慢机器漏判
- 用户手动 `Stop()` 时 `_reader.CurrentTime = TimeSpan.Zero` 已被先置零（见现有代码 line 102），所以差值 = TotalTime - 0 = TotalTime，远大于 200ms → 不会误判

**风险：** 如果曲目本身 < 200ms（极短音效），用户手动 Stop 时可能误判为自然播完。Phase 2 不处理 —— 音乐文件几乎不可能 < 200ms。

### 取舍 c：RepeatOne 仅自动触发，手动 Next 不重播

**决策：** 用户按 Next 按钮，即使在 RepeatOne 模式下，也跳到下一首。

**理由：** 与 foobar2000 / Spotify 一致；用户主动操作 > 模式设定。

### 取舍 d：单 MainViewModel 不拆分

**决策：** Phase 2 不拆 PlayerVM / PlaylistVM，所有新字段/命令直接加到 `MainViewModel`。

**理由：**
- 拆分需修改 `PlayerBar.xaml.cs` 的 `as MainViewModel` 硬转型，工作量增 ~50%
- MVP 阶段单 VM 行数预计 ~400 行，可控
- 留给 Phase 3 "加多列表" 时一起做（届时拆分有实际需求驱动）

---

## 7. 错误处理与边界情况

| 场景 | 处理 |
|------|------|
| 队列为空，按 Next/Prev | 命令 CanExecute=false，按钮置灰 |
| 删除当前播放曲 | 停止播放，CurrentIndex=-1 |
| 删除当前曲之前的项 | CurrentIndex 自减，播放不中断 |
| 删除当前曲之后的项 | CurrentIndex 不变，播放不中断 |
| 清空队列 | 停止播放，CurrentIndex=-1，清空 `_shuffleHistory` |
| 文件不存在/损坏 | `LoadAsync` 抛出 → 跳过该曲推进下一首 |
| 自动推进连续遇损坏 | 最多连跳 3 首，之后停止（避免无限循环） |
| 切换 Shuffle 状态 | 清空 `_shuffleHistory`（防止状态混乱） |
| 修改 Queue（添加/删除） | 不动 `_shuffleHistory` 索引语义除非必要（删项时修正） |
| 单曲循环 + 用户按 Next | 跳下一首，不重播当前 |
| `PlaybackError` 事件 | 维持现状（VM 中 TODO 注释），不在 Phase 2 加 toast |
| 加入路径包含中文/空格 | 由 `MediaFoundationReader` 自然处理，不特殊处理 |

---

## 8. UI 规范

### 8.1 MainWindow 布局变更

```xml
<!-- 修改后的 MainWindow.xaml 主体 -->
<Window ... Height="650" MinHeight="500">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />  <!-- PlayerBar -->
            <RowDefinition Height="*" />     <!-- PlaylistView -->
        </Grid.RowDefinitions>
        <controls:PlayerBar Grid.Row="0" />
        <controls:PlaylistView Grid.Row="1" />
    </Grid>
</Window>
```

- 默认窗口高度：450 → 650
- 持久化的 `WindowHeight` 若为 Phase 1 旧值（<500），启动时自动提升到 650（一次性迁移）

### 8.2 PlaylistView 布局

```
┌──────────────────────────────────────────────┐
│  [+ 添加]  [清空]              🔀  ⇄ ↻      │  ← 工具栏
├──────────────────────────────────────────────┤
│  ▶  song1.mp3                       --:--  ×│  ← 当前播放，▶ 图标
│     song2.flac                      --:--  ×│
│     song3.wav  (选中蓝底)            --:--  ×│  ← 选中
│     song4.mp3                       --:--  ×│
│                                              │
└──────────────────────────────────────────────┘
```

**按钮图标：**
- 随机：`🔀` (off 灰色 / on 紫色)
- 循环：动态文字
  - Off：`⇄` (灰色)
  - List：`🔁` (紫色)
  - One：`🔂` (紫色)

**色彩遵循现有主题：** `BackgroundPrimary` 底，`AccentPrimary` 高亮，`ForegroundPrimary` 文字。

### 8.3 当前曲高亮策略

- 当前曲行：`Foreground=AccentPrimary` + 行首 ▶ 图标
- 选中项（与当前曲独立）：ListBox 默认蓝底（在 `Themes/Controls.xaml` 中可加 Style 调色，可选）

---

## 9. 顺手清理的技术债

参见 [`docs/COUPLING.md`](../../COUPLING.md) §3。

### 债 #2 — `IPlaybackService` 加 `TrackEnded` 事件 ✅ 做

```csharp
// Services/IPlaybackService.cs 增加:
event Action? TrackEnded;  // 仅"自然播完"触发，用户 Stop / 异常不触发
```

实现见 §5 场景 C。

### `Win32FileDialogService` 多选支持 ✅ 做

```csharp
// 改为:
public IReadOnlyList<string> OpenFiles(string filter, bool multiselect = false)
{
    var dialog = new OpenFileDialog { Filter = filter, Multiselect = multiselect };
    return dialog.ShowDialog() == true ? dialog.FileNames.ToList().AsReadOnly() : Array.Empty<string>();
}

// IFileDialogService 接口同步加 bool multiselect = false 参数（默认值保持向下兼容）
```

### 不做的债（保持现状）

- ❌ 债 #1 (BitmapImage 抽取)
- ❌ 债 #3 (settings.json 合并纪律) — Phase 2 不持久化队列，本债不会恶化
- ❌ 债 #4 (ITrackMetadataReader 抽取)
- ❌ VM 拆分
- ❌ PlayerBar 去硬转型

---

## 10. 文件清单

### 新增

| 文件 | 用途 | 估算行数 |
|------|------|----------|
| `Models/RepeatMode.cs` | 枚举 Off/List/One | ~5 |
| `Views/Controls/PlaylistView.xaml` | 队列 UI | ~80 |
| `Views/Controls/PlaylistView.xaml.cs` | 双击/Delete 事件转发 | ~40 |
| `Converters/RepeatModeToIconConverter.cs` | 模式 → 图标字符串 | ~20 |
| `Converters/BoolToAccentBrushConverter.cs`（可选） | Shuffle on/off → 颜色 | ~20 |

### 修改

| 文件 | 改动概述 |
|------|----------|
| `Services/IPlaybackService.cs` | +`event Action TrackEnded` |
| `Services/NAudioPlaybackService.cs` | `OnPlaybackStopped` 增加"自然播完"判定分支 |
| `Services/IFileDialogService.cs` | `OpenFiles` 增 `bool multiselect = false` 参数 |
| `Services/Win32FileDialogService.cs` | 透传 multiselect 到 `OpenFileDialog.Multiselect` |
| `ViewModels/MainViewModel.cs` | 加 §4.1 列出的所有新字段/命令；订阅新 TrackEnded 事件 |
| `Views/MainWindow.xaml` | 改为两行 Grid；高度 450 → 650 |
| `Views/MainWindow.xaml.cs` | 启动时若 settings.WindowHeight < 500 提升到 650 |
| `docs/PROJECT.md` | 同步更新结构图与"已实现"清单 |
| `docs/COUPLING.md` | 标注债 #2 已偿还，更新 Phase 3 检查清单 |

---

## 11. 手动验收清单

实现完成后须人工逐项打勾：

**基础队列：**
- [ ] [+ 添加] 单击 → 文件对话框可多选 → 所选文件按顺序加入队列底部
- [ ] 加入 5 个文件后，队列显示 5 行
- [ ] 双击第 3 行 → 第 3 首立即播放，行首出现 ▶ 标记
- [ ] [清空] → 队列空，播放停止，PlayerBar 显示 "No track loaded"

**单项删除：**
- [ ] 点击单项右侧 × → 该项移除
- [ ] 选中某项按 Delete 键 → 该项移除
- [ ] 删除当前播放曲 → 播放停止
- [ ] 删除非当前曲（无论之前/之后）→ 当前曲继续播

**自动推进：**
- [ ] 顺序 + RepeatOff：第 N 首播完 → 第 N+1 首
- [ ] 顺序 + RepeatList：最后一首播完 → 跳回第一首
- [ ] 顺序 + RepeatOne：当前曲反复播
- [ ] Shuffle ON + RepeatOff：所有曲随机播完一遍后停止
- [ ] Shuffle ON + RepeatList：全播完后重置随机历史，继续随机
- [ ] Shuffle ON + RepeatOne：随机选一首后反复播该首

**手动控制：**
- [ ] Next 按钮在 RepeatOne 下也跳下一首
- [ ] Queue 为空时 Next/Prev 按钮置灰
- [ ] PlayerBar 的现有 Play/Pause/Stop/Seek/Volume 全部不受影响

**容错：**
- [ ] 加入一个不存在的路径（手动改文件名 / 删文件后入队）→ 播到它时跳过
- [ ] 连续 3 个损坏文件 → 第 3 个后停止，不无限循环
- [ ] 切换 Shuffle 时不报错

**窗口：**
- [ ] 启动时窗口高 650
- [ ] 上次保存的窗口高 < 500 时启动自动提升到 650（一次性迁移）

---

## 12. 风险与不确定性

| 风险 | 缓解 |
|------|------|
| `MainViewModel` 增长到 ~400 行，可读性下降 | 用 `#region` 分组 Phase 1 / Phase 2 字段；接受为"待 Phase 3 拆分"的状态 |
| `Queue.RemoveAt` 与 `_shuffleHistory` 索引同步出 bug | §5 场景 E 给出修正算法；验收清单覆盖删除时的场景 |
| TrackEnded 200ms 阈值在某些设备/格式下不触发 | 阈值可调；先用 200ms，若实测漏判调大到 500ms |
| 双击 ListBox 空白区也触发 PlayTrackAt | code-behind 通过 `e.OriginalSource → ListBoxItem` 查找过滤；空白返回 |
| `ObservableCollection` 替换元素（场景 B 用 Queue[i]=meta）UI 不刷新 | `ObservableCollection.set[i]` 会 raise `Replace`；ListBox 支持；不会有问题 |

---

## 13. 后续衔接

完成 Phase 2 后，下一阶段优先级（更新 `docs/COUPLING.md` Phase 3 检查清单）：

1. **VM 拆分** —— `MainViewModel` 达到 ~400 行，已到拆分阈值
2. **`ITrackMetadataReader` 抽取** —— 入队批量读元数据需要
3. **队列持久化** —— 关闭恢复，但需先解决 settings 合并纪律（债 #3）
4. **多命名播放列表（L3）** —— 真正的"播放列表管理"
5. **拖拽支持** —— 入队 + 重排序

本 spec 完成后即关闭 Phase 2，不在本周期内追加上述任一项。
