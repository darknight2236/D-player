# UmaPlayer Phase 5 · 拖拽支持设计

> 日期：2026/06/12 · 分支：`feature/phase5-drag-drop` · 前置：Phase 4 完成（队列持久化）

## 1. 目标

给 `PlaylistView` 加 3 个拖拽能力：

1. **外部文件拖入入队** —— 从资源管理器拖音频文件到 PlaylistView，松开后批量追加到队列末尾，不自动播放。
2. **队列内项拖拽重排** —— ListBox 内按住拖动一项到新位置，松开后重排，`CurrentIndex` 与 `_shuffleHistory` 同步重映射，不中断播放。
3. **多项同时拖拽** —— ListBox 多选后整体拖动，目标位置作为连续块插入；块内顺序保持原列表顺序。

**顺带债务清算（来自 COUPLING.md §5）：** 在 `RemoveTrack` 与新增的 `MoveTracks` 入口处自增 `_playToken`，关上 in-flight `RemoveTrack`/`MoveTracks` 让 `Queue[index] = meta` 写到错位的 race。

**不在 Phase 5 范围：**
- 拖出队列到外部（用户价值低）
- 文件夹递归扫描（属于"音乐库扫描"半个项目，超出范围）
- 多命名播放列表 / Tab UI（独立 Phase）

## 2. 架构与组件分配

拖拽逻辑全部在 **View 层**；VM 层仅暴露纯数据命令（接收已过滤的字符串数组 / 已算好的源索引 + 目标索引）。这样保证 PlaylistViewModel 仍可单测，不依赖 WPF DragDrop 事件。

```
PlaylistView.xaml              [DragOver 高亮通过 IsDragOver 触发器]
  ├── 根 Border (AllowDrop=True): 包裹整个控件，提供高亮边框 + IsDragOver 状态接收点
  └── ListBox  (AllowDrop=True, x:Name="QueueList")
        ├── 列表项: Border 包裹, 用作 hit-test target
        └── DropInsertionAdorner 在 AdornerLayer 上画 1px 插入线
        // DragEnter/DragOver/DragLeave 事件会冒泡到根 Border，故根 Border 上挂 IsDragOver attached state；
        // 实际的数据 Drop 由 ListBox 处理（外部文件入队 + 内部重排）。

PlaylistView.xaml.cs           [新增 ~150 行]
  ├── ListBox_PreviewMouseLeftButtonDown / MouseMove  → 启动内部拖拽（4px 阈值）
  ├── ListBox_DragOver / DragLeave / Drop             → 处理重排 + 外部入队
  ├── DropInsertionAdorner                             → 1px 插入线绘制
  ├── ComputeInsertIndex(Point)                        → 命中测试 + ½ 高度判定
  └── FilterAudioPaths(string[])                       → 后缀过滤 .mp3/.wma/.flac/.aac/.wav

PlaylistViewModel.cs           [新增 2 命令 + 修改 1 命令]
  ├── [RelayCommand] DropExternalFiles(IReadOnlyList<string>)   → 入队（追加），与 AddToQueue 同语义
  ├── [RelayCommand] MoveTracks(MoveTracksArgs)                  → 重排 + CurrentIndex/history 重映射
  └── RemoveTrack: 入口加 _playToken++（顺带还债）

Models/MoveTracksArgs.cs       [新增, 简单 record]
  public sealed record MoveTracksArgs(
      IReadOnlyList<int> SourceIndices,   // 升序无重复
      int TargetIndex);                    // ∈ [0, Queue.Count]
```

**关键边界：**
- View 层负责 DragDrop 事件、命中测试、插入点计算、文件过滤、Adorner 绘制
- VM 层仅接收"已过滤好的字符串数组"、"已算好的源索引集合 + 目标索引"
- 重映射算法（CurrentIndex 跟随、`_shuffleHistory` 跟随）放在 VM —— 跟现有 `RemoveTrack` 的修正逻辑同一抽象层

## 3. 数据流

### 3.1 路径 A · 外部文件拖入

```
资源管理器拖动文件
  → ListBox.DragOver
      e.Data.GetDataPresent(DataFormats.FileDrop) ?
        ├── true  → e.Effects = DragDropEffects.Copy + 设置 IsDragOver=true（高亮边框）
        └── false → e.Effects = DragDropEffects.None
  → ListBox.Drop
      var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
      var audioPaths = FilterAudioPaths(paths);   // 仅留 .mp3/.wma/.flac/.aac/.wav
      if (audioPaths.Count == 0) return;          // 静默无操作
      DataContext.DropExternalFilesCommand.Execute(audioPaths);
  → PlaylistViewModel.DropExternalFiles(paths)
      foreach (var path in paths)
          Queue.Add(_metadataReader.CreateFallback(path));
      // 不触发播放 —— 与 AddToQueue 完全等价（仅入口不同）
```

### 3.2 路径 B · 队列内重排

```
PreviewMouseLeftButtonDown 记录 _dragStartPoint + _dragSourceItem
  → MouseMove
      if 鼠标已按下 && |当前点 - _dragStartPoint| > SystemParameters.MinimumHorizontalDragDistance(4px):
          // QueueList.SelectedItems 返回选中顺序；必须按索引升序排列后再传入 VM
          var sourceIndices = QueueList.SelectedItems
              .Cast<Track>().Select(t => Queue.IndexOf(t)).OrderBy(i => i).ToList();
          DragDrop.DoDragDrop(QueueList, new DataObject("UmaPlayer.QueueItems", sourceIndices), Move)
  → ListBox.DragOver（自身拖自身）
      if e.Data.GetDataPresent("UmaPlayer.QueueItems"):
          var insertIdx = ComputeInsertIndex(e.GetPosition(QueueList))
          DropInsertionAdorner.Update(insertIdx)         // 画 1px 插入线
          e.Effects = DragDropEffects.Move
  → ListBox.Drop
      var sourceIndices = (IReadOnlyList<int>)e.Data.GetData("UmaPlayer.QueueItems");
      var targetIndex = ComputeInsertIndex(e.GetPosition(QueueList));
      DataContext.MoveTracksCommand.Execute(new MoveTracksArgs(sourceIndices, targetIndex));
      DropInsertionAdorner.Hide();
  → PlaylistViewModel.MoveTracks(args)
      [详见 §4]
```

### 3.3 ComputeInsertIndex 命中测试

```
foreach (item container in ListBox)
    var rect = container 的 bounds（在 ListBox 坐标系）
    if rect.Top <= y < rect.Bottom:
        return y < rect.Top + rect.Height/2 ? itemIndex : itemIndex + 1;
return Queue.Count;  // 鼠标在所有项之下 → 末尾
```

### 3.4 两条拖拽路径共存

外部拖入 vs 内部重排：通过 `e.Data.GetDataPresent` 区分 —— `FileDrop`（外部）vs `"UmaPlayer.QueueItems"`（内部）。两者互斥；DragOver / Drop 内先判内部再判外部。

## 4. MoveTracks 重排算法

**输入：**
- `sourceIndices`：升序无重复，每项 ∈ [0, Queue.Count)
- `targetIndex` ∈ [0, Queue.Count]，i 表示插到 i 之前；Count 表示末尾

**输出：** Queue 重排，`CurrentIndex` 与 `_shuffleHistory` 重映射后保留同一个语义对象（"当前播放曲" / "已播过曲"）

**算法：**
```
1. _playToken++                                   // 顶替 in-flight
2. 缓存被移动的实际对象：
   var moving = sourceIndices.Select(i => Queue[i]).ToList()
3. 缓存"将要保留的当前曲对象"：
   var currentTrackObj = (CurrentIndex >= 0) ? Queue[CurrentIndex] : null
4. 缓存"将要保留的 history 对象集合"：
   var historyObjs = _shuffleHistory.Select(i => Queue[i]).ToHashSet()
5. 修正 targetIndex（删源位置后, 目标位置可能往前缩）：
   var adjustedTarget = targetIndex - sourceIndices.Count(i => i < targetIndex)
6. 倒序删除源位置：
   foreach (i in sourceIndices.OrderByDescending(x => x)) Queue.RemoveAt(i)
7. 在 adjustedTarget 处插入 moving（保持原顺序）：
   for k in 0..moving.Count: Queue.Insert(adjustedTarget + k, moving[k])
8. 重建 CurrentIndex（按对象身份回找）：
   CurrentIndex = (currentTrackObj != null) ? Queue.IndexOf(currentTrackObj) : -1
9. 重建 _shuffleHistory（按对象身份回找）：
   _shuffleHistory.Clear()
   for i in 0..Queue.Count: if historyObjs.Contains(Queue[i]) _shuffleHistory.Add(i)
```

**为什么用对象身份而不是索引算术？**
- ObservableCollection 没有原生 Move(many → one) 操作；多源多目标的索引算术（前移/后移混合）容易出错。
- Track 是 record，引用相等；插入 Queue 后引用不变。
- O(n) 代价可接受 —— 队列规模 < 几千项。

**幂等性：**
- 拖到原位置（targetIndex 等于源索引或紧邻）时算法幂等。`adjustedTarget` 等于源原位置时，删 + 插回原处；CurrentIndex 与 history 用对象身份回找，结果不变。

**当前曲在拖动集中：**
- `currentTrackObj` 仍指向同一个 record；步骤 8 重建后 `CurrentIndex` 指向新位置，**不中断播放**（NAudio 不知道 Queue 重排，继续推流）。

## 5. 错误处理与边界条件

### 5.1 外部拖入

| 场景 | 行为 |
|---|---|
| 拖入混合内容（音频 + 非音频） | 仅入队音频，其他静默丢弃 |
| 拖入全部非音频 | DragOver 阶段设 `Effects=None`，光标显示禁止图标；Drop 不触发 |
| 拖入文件夹 | 视为非音频路径丢弃（用 `Path.GetExtension` 严格匹配白名单） |
| 拖入指向已删除/网络断连的路径 | 入队为占位 Track，`PlayTrackAtAsync` 时 catch 兜底跳过 —— 与 `AddToQueue` 行为对称 |
| 重复路径 | 不去重，与 `AddToQueue` 一致 |

### 5.2 队列内重排

| 场景 | 行为 |
|---|---|
| 拖到原位置（targetIndex 在 sourceIndices 中或紧邻） | 算法幂等，无可见变化（也不重置 `_shuffleHistory`） |
| 拖到 ListBox 空白区下方 | `ComputeInsertIndex` 返回 `Queue.Count`（末尾），插到队尾 |
| 拖动期间用户按 Esc | DragDrop API 自动取消，`Drop` 不触发，Adorner 在 `DragLeave` / `QueryContinueDrag` 清理 |
| 拖动期间窗口失焦 | 同 Esc，DragDrop 由 OLE 回滚 |
| 单选拖到自己 | sourceIndices.Count==1 且 adjustedTarget == sourceIndex → 算法幂等 |
| 多选不连续（Ctrl+点选 0,2,5） | sourceIndices = [0,2,5]，移动后聚成连续块插到 targetIndex；中间被跳过的 1,3,4 平滑收缩 |
| 拖动时 in-flight 元数据读取 | `_playToken++` 顶替；旧调用 `await` 后看 token 不匹配 silent return；不会把元数据写到错位 |

### 5.3 Adorner 清理纪律

- `DragLeave`、`Drop`、`QueryContinueDrag` 取消时都必须清理 Adorner，否则 ListBox 上残留 1px 线
- 用一个 `_currentInsertionAdorner` 字段集中管理，`Hide()` 处理 `AdornerLayer.Remove + null`

### 5.4 拖拽与现有快捷键共存

- 拖拽与 Delete 键互不干扰：`PreviewMouseLeftButtonDown` + `MouseMove` 超过 4px 才启动 DragDrop；Delete 处理 `KeyDown` 仍然走原有逻辑。

### 5.5 Shuffle 历史处理

- 队列内拖拽重排后 `_shuffleHistory` 跟随重排重映射（按对象身份回找），保证已播过状态不丢。

## 6. UI 反馈

### 6.1 外部拖入高亮

`PlaylistView.xaml` 根 Grid 加 `IsDragOver` 触发器（DataContext 持有的 view-model 不直接管这个，View 自己用 code-behind 设 attached state 或 `DependencyProperty`）：

```xml
<Style TargetType="Border" x:Key="DropTargetBorder">
    <Setter Property="BorderBrush" Value="Transparent"/>
    <Setter Property="BorderThickness" Value="2"/>
    <Style.Triggers>
        <Trigger Property="local:DragDropExtensions.IsDragOver" Value="True">
            <Setter Property="BorderBrush" Value="{StaticResource AccentBrush}"/>
        </Trigger>
    </Style.Triggers>
</Style>
```

`DragDropExtensions.IsDragOver` 是一个 attached DependencyProperty，由 code-behind 在 `DragEnter` / `DragLeave` 期间切换。

### 6.2 重排插入线 Adorner

`DropInsertionAdorner` 类（继承 `Adorner`）：
- 构造接收 `ListBox` 作为 AdornedElement
- 内部字段 `_insertIndex`（int）
- `OnRender(DrawingContext)` 计算插入线 Y 坐标：若 `_insertIndex == Queue.Count`，画在最后一项底部；否则画在 `Queue[insertIndex]` 项顶部
- 颜色：`AccentBrush`（紫色，与主题一致），高度 1px，覆盖 ListBox 全宽（缩进左右 4px 留白）

## 7. 测试策略

### 7.1 手动验收脚本（10 个场景）

1. 资源管理器拖 1 个 mp3 → 末尾入队，不播放
2. 资源管理器拖 5 个文件（含 1 个 .txt）→ 仅 4 个音频入队
3. 资源管理器拖 1 个文件夹 → 无变化（光标显示禁止）
4. 队列里单选第 3 项拖到第 1 项前 → CurrentIndex 跟随，播放不中断
5. 队列里 Ctrl+多选第 1、3 项拖到第 5 项前 → 聚成连续块到位置 3（删 0,2 后调整）
6. 拖动当前正在播放的曲到末尾 → 音频继续播，▶ 标记跟到末尾
7. 拖动时按 Esc → 队列无变化，Adorner 消失
8. 拖到原位 → 队列无变化
9. Shuffle 开 + 已播过 [0,2] → 重排后 `_shuffleHistory` 仍指向那两首原对象
10. 重启验证：拖入 + 重排后关窗，重启 queue.json 反映新顺序

### 7.2 回归项（Phase 4 不能被破坏）

- 启动恢复 + 首次 ▶ 触发 PlayCurrent
- 关闭写 queue.json（cancel-and-close 仍生效）
- ▶ 按钮 BasedOn 主题样式（按钮不变白底）

### 7.3 自动化测试

不引入。Phase 5 不立 unit-test 项目，重排算法手工 trace 几个 case 即可（与现有 PlaylistViewModel 测试缺位状况一致 —— 偿还债 #1 时再统一补）。

## 8. 与现有架构的契合

### 8.1 不破坏的不变量

- **PlayerVM / PlaylistVM 互不持引用**（COUPLING.md §5）：拖拽功能完全在 PlaylistVM 域内，无需触碰 PlayerVM。
- **MainViewModel Strict Facade**：不暴露新成员，仍是 `Player` / `Playlist` / `CleanupAsync`。
- **DI 注册顺序**（PlayerVM 先于 PlaylistVM）：本期不动注册顺序。

### 8.2 沿用既有模式

- **DropExternalFiles** 走 `_metadataReader.CreateFallback` + `Queue.Add`，与 `AddToQueue` 同一条入队管线（仅入口不同 —— 一个是文件对话框，一个是 OS DragDrop）。
- **MoveTracks** 的 `_shuffleHistory` 重映射沿用 `RemoveTrack` 已有的对象身份重映射风格，未引入新抽象。

### 8.3 持久化

- 重排后队列内存状态变化即可；queue.json 在 `Window_Closing` 时写入新状态（已有路径）。
- 无需修改 `QueueState` schema、无需 `IQueuePersistence` 改动。

## 9. 风险登记

| 风险 | 说明 | 缓解 |
|---|---|---|
| OLE DragDrop 模态阻塞 UI 线程 | `DragDrop.DoDragDrop` 是同步 OLE 调用，期间 UI 线程被 modal 卡住，PollPositionAsync 回调照常入队（事件驱动）但 UI 渲染暂停 | 用户期望行为；NAudio 在另一线程推流，音频不停 |
| Adorner 在多显示器/DPI 切换下错位 | WPF Adorner 用 device-independent units，DPI 变化时通常会跟随；老 NVIDIA 驱动有少数 bug 报告 | 不主动处理；若验收时发现再加 `LayoutUpdated` 重算 |
| 拖入大量文件（如 1000 首）卡顿 | `Queue.Add` 在循环内每次都触发 CollectionChanged | YAGNI：< 100 首是常见规模；若验收阶段卡顿明显，再上 `Queue` 的批量插入扩展 |
| Phase 4 残留 race（PlayTrackAtAsync 期间 Move） | 步骤 1 `_playToken++` 已覆盖；同时也修了 RemoveTrack 同样的债 | 已纳入设计 |

## 10. 参考

- 完整代码导读：[`docs/PROJECT.md`](../../PROJECT.md)
- 风险登记册：[`docs/COUPLING.md`](../../COUPLING.md) §5（in-flight RemoveTrack 残留债）
- Phase 4 队列持久化设计：[`2026-06-12-uma-player-phase4-queue-persistence-design.md`](./2026-06-12-uma-player-phase4-queue-persistence-design.md)
