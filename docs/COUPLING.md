# UmaPlayer 耦合分析与重构备忘

> 创建日期：2026/06/06 · 更新日期：2026/06/14 · 对应分支：`master` · 对应阶段：**Phase 10 完成**（文件夹绑定歌单）
>
> **本文档的用途：** 不是行动清单，是**风险登记册**。Phase 2 偿还债 #2；Phase 3 偿还债 #3/#4 + 完成 VM 拆分 + View 去硬转型；Phase 4 加入队列持久化（无新还债，仅功能增量 + 2 个 WPF 隐式契约）；Phase 5 加入拖拽支持 + 偿还旧债 #5（in-flight RemoveTrack 重入），新增 5 个 WPF 隐式契约；Phase 6 加入多命名歌单 + xUnit 骨架 + debt #1 部分偿还；Phase 7 完成 debt #1 完整偿还（VM 层无 WPF 类型）；Phase 8 建立 ViewModel 单元测试体系；Phase 9 sidebar 歌单拖拽重排；Phase 10 文件夹绑定歌单 + AudioConstants 层级修正。所有技术债已清零。详见 §6。

---

## TL;DR

| 维度 | 评级 | 备注 |
|------|------|------|
| 整体耦合度 | **低** | Phase 3 后 MainViewModel 仅 44 行（Strict Facade）；Phase 4 仅给 PlaylistViewModel 加 `IQueuePersistence` 一个新依赖；Phase 5 加拖拽完全在 PlaylistVM 域内完成（2 个新 RelayCommand，0 新依赖；View 层 +2 文件）；Phase 6 多命名歌单 + Phase 7 偿还债 #1 后 VM 层无 WPF 类型泄漏 |
| 是否需要立即重构 | ✅ 无 | Phase 3 完成所有结构性改造；Phase 4/5/6/7 沿用既有模式 |
| 已识别"待还的债" | 0 项剩余（#1/#2/#3/#4/#5 ✅ 全部已偿） | 见 §3 |
| 已识别"过度抽象" | 2 项 | 见 §4 |

---

## 1. 当前架构为什么是健康的

✅ DI 容器集中注册（`ServiceCollectionExtensions.AddUmaPlayerServices`），无 Service Locator 反模式
✅ 依赖方向正确：`View → VM → Service → Model`，Service 从不反向引用 VM/UI
✅ 所有跨边界依赖**都走接口**：`IPlaybackService` / `IFileDialogService` / `ISettingsPersistence`
✅ 无 `static` 单例、无全局可变状态
✅ Layer 边界清晰（`Models/` / `Services/` / `ViewModels/` / `Views/` 物理隔离）
✅ Phase 2 新增（`PlaylistView` / Phase 2 命令 / 推进算法）全部沿用既有模式，未引入新抽象层
✅ Phase 3 拆分：MainViewModel 收敛为 Strict Facade（44 行）；PlayerVM/PlaylistVM 互不持引用，仅共享 IPlaybackService Singleton；View 跨域命令用 RelativeSource AncestorType=Window 跨级绑定
✅ Phase 4 队列持久化沿用相同模式：`IQueuePersistence` 接口 + `JsonQueuePersistence` 实现 + Singleton 注册；与 `JsonSettingsPersistence` 文件隔离、锁隔离；PlaylistViewModel 读盘、MainWindow 写盘，无 VM 间耦合
✅ Phase 5 拖拽功能完全在 PlaylistVM 域内：DragDrop 事件 / 命中测试 / 文件过滤 / Adorner 绘制全在 View 层；VM 仅暴露 2 个纯数据 RelayCommand（`DropExternalFiles(paths)` / `MoveTracks(args)`），无 `DataObject` / `DragEventArgs` / `AdornerLayer` 渗透；MainViewModel Facade 维持 ~44 行不变
✅ Phase 10 文件夹绑定歌单：`ILibraryScannerService` + `ILibraryCache` 接口 + 实现注入 PlaylistsViewModel；`AudioConstants` 从 View 层提取到 Models 层消除层级违规；启动后台自动增量同步

**结论：** Phase 10 后继续加功能（可视化）不会再触碰核心架构。

---

## 2. 依赖图（事实陈述）

| 消费方 | 依赖的抽象 | 依赖的具体类型 |
|--------|------------|----------------|
| `App` | `MainViewModel`, `ISettingsPersistence`, `IPlaylistService` | `Views.MainWindow`, `ServiceProvider` |
| `ServiceCollectionExtensions` | — | 8 个 Service 实现 + `MainViewModel` + `PlaylistsViewModel` + `Func<Playlist, PlaylistVM>`（注册绑定） |
| `MainViewModel` (Facade) | `PlayerViewModel`, `PlaylistsViewModel`, `IPlaybackService`, `IPlaylistService` | — |
| `PlayerViewModel` | `IPlaybackService`, `ISettingsPersistence`, `IOptions<AppSettings>` | — |
| `PlaylistViewModel` | `IPlaybackService`, `IFileDialogService`, `ITrackMetadataReader` | `Track`、`RepeatMode`、`QueueState`、`MoveTracksArgs`、`File.Exists` |
| `PlaylistsViewModel` | `Func<Playlist, PlaylistViewModel>`, `ILibraryScannerService`, `ILibraryCache` | `Playlist`、`PlaylistViewModel`、`LibraryDiff` |
| `MainWindow` | `MainViewModel`, `ISettingsPersistence` | `Window`, `SystemParameters` |
| `PlayerBar` | — | `PlayerViewModel`（`DataContext as PlayerViewModel`，3 处）；跨级访问 `Playlist.<Cmd>`（含 Phase 4 ▶ DataTrigger 的 `PlayCurrentCommand`） |
| `PlaylistView` | — | `PlaylistViewModel`（`DataContext as PlaylistViewModel`）；订阅 `PropertyChanged` / `Queue.CollectionChanged`；Phase 5 直接消费 `DragDropExtensions` / `DropInsertionAdorner` / `MoveTracksArgs`，但全部走 RelayCommand 与 VM 通信；Phase 6 双击路由走 `App.GetService<PlaylistsViewModel>().HandleDoubleClickPlay` |
| `PlaylistsSidebarView` | — | `PlaylistsViewModel`（`DataContext as PlaylistsViewModel`）；订阅 `PropertyChanged` / `Playlists.CollectionChanged`；消费 `PromptDialog` |
| `NAudioPlaybackService` | `IPlaybackService` | `MediaFoundationReader`, `WasapiOut`, `VolumeSampleProvider` |
| `Win32FileDialogService` | `IFileDialogService` | `Microsoft.Win32.OpenFileDialog` |
| `JsonSettingsPersistence` | `ISettingsPersistence` | `File`, `JsonSerializer`, `Environment.SpecialFolder` |
| `JsonPlaylistService` (Phase 6) | `IPlaylistService` | `File`, `JsonSerializer`, `Environment.SpecialFolder` |
| `LibraryScannerService` (Phase 10) | `ILibraryScannerService` | `Directory.EnumerateFiles`, `AudioConstants.Extensions` |
| `JsonLibraryCache` (Phase 10) | `ILibraryCache` | `File`, `JsonSerializer`, `Environment.SpecialFolder` |
| `AtlMetadataReader` | `ITrackMetadataReader` | `ATL.Track`（封装隔离） |

---

## 3. 已识别的 5 个"待还的债"

### 债 #1 — VM 持有 `BitmapImage`（WPF 类型泄漏）✅ 已偿（Phase 7）

**位置：** `ViewModels/PlayerViewModel.cs` — 原 `AlbumArtImage` 字段 + `CreateAlbumArtImage` 方法

**Phase 6 部分偿还：** 交付 `Converters/BytesToBitmapImageConverter.cs`（byte[] → Frozen BitmapImage），注册为 App.xaml 全局资源。

**Phase 7 完整偿还：**
- `PlayerViewModel.AlbumArtImage` (BitmapImage) → `AlbumArtBytes` (byte[])
- 删除 `CreateAlbumArtImage` 方法 + `System.IO` / `System.Windows.Media.Imaging` using
- `PlayerBar.xaml` 绑定改为 `{Binding AlbumArtBytes, Converter={StaticResource BytesToBitmapImage}}`
- `HandleTrackChanged` 直接赋 `track?.AlbumArt`（零拷贝）

**收益：** PlayerViewModel 不再依赖任何 WPF 类型，可在无 WPF 上下文的 xUnit 测试中实例化。

---

### 债 #2 — `IPlaybackService` 缺"自然播完"信号 ✅ 已偿

**位置：** `Services/NAudioPlaybackService.cs:OnPlaybackStopped`

```csharp
private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
{
    if (e.Exception != null) RaiseOnUIThread(PlaybackError, e.Exception.Message);
    SetState(PlayState.Stopped);  // ← 用户 Stop 和 自然播完 没区分
}
```

**影响：** 加播放列表后无法实现"曲终自动下一首"—— `PlayState.Stopped` 不携带原因。

**触发时机：** Phase 2 加播放列表的**第一天**就会撞上。

**预修方案（约 30 分钟）：** 给 `IPlaybackService` 加 `event Action TrackEnded`，仅在"非用户主动停止 + 无异常 + 播放头已到 Duration" 时触发。

**✅ Phase 2 已偿还**（commit: 见 `git log --grep TrackEnded`）—— 加入 `event Action? TrackEnded`，
通过 200ms 容差判定"自然播完"。

---

### 债 #3 — settings.json 双写者无合并纪律 ✅ 已偿（Phase 3）

**位置：**
- 写入者 A：`MainViewModel.OnVolumeChanged` → `_persistence.SaveAsync(_settings)`（fire-and-forget，**不重读**）
- 写入者 B：`MainWindow.Window_Closing` → 重读 + 合并 + 保存（正确做法）

**影响（理论 race）：**
```
T0  MainWindow 写入 { Volume=0.5, WindowLeft=200 }
T1  VM 内存中的 _settings 仍是 { Volume=0.5, WindowLeft=100 }  ← 旧值
T2  用户拖音量 → VM 写入 { Volume=0.6, WindowLeft=100 }  ← WindowLeft 被复活
```

`JsonSettingsPersistence` 的 `SemaphoreSlim` 只保护**单次读写原子**，不保护**读-改-写的合并**。

**触发时机：** 当前可能性低（用户拖音量时通常不会同时关窗口），但字段增多后概率上升。

**预修方案（约 1 小时）：** 两种思路任选其一
1. **细化接口：** `ISettingsPersistence.UpdateAsync(Func<AppSettings, AppSettings> mutator)`，把"读-改-写"封装在锁内
2. **改用 KV 存储：** 每个字段独立 set/get，无合并问题（更彻底，但要重写持久化层）

---

### 债 #4 — 元数据读取硬编码 `ATL.Track` ✅ 已偿（Phase 3）

**位置：** `ViewModels/MainViewModel.cs:ReadTrackMetadataAsync`

```csharp
var atlTrack = new ATL.Track(filePath);  // ← 直接 new，无抽象
```

**影响：**
- 想换标签库（如 TagLib#）要改 VM
- 加播放列表后想批量读元数据，逻辑只能复制粘贴

**触发时机：** Phase 2 加播放列表 / 音乐库扫描时；想换底层标签库时

**预修方案（约 45 分钟）：**
```csharp
public interface ITrackMetadataReader
{
    Task<Track> ReadAsync(string filePath);
    Task<IReadOnlyList<Track>> ReadBatchAsync(IEnumerable<string> filePaths);
}

public sealed class AtlMetadataReader : ITrackMetadataReader { ... }
```
注入到 VM，删掉 VM 中的两个 static 辅助方法。

---

### 债 #5 — `RemoveTrack` 期间不顶替 `_playToken` ✅ 已偿（Phase 5）

**位置：** `ViewModels/PlaylistViewModel.cs:RemoveTrack`（Phase 2 引入，Phase 3/4 沿用未修）

```csharp
[RelayCommand]
private void RemoveTrack(int index)
{
    if (index < 0 || index >= Queue.Count) return;
    // ❌ 没有 _playToken++ —— in-flight PlayTrackAtAsync 还在 await 元数据，
    //    继续后会用旧 index 写 Queue[index] = meta，索引语义已变（错位甚至越界）
    Queue.RemoveAt(index);
    ...
}
```

**影响：** 用户在 `PlayTrackAtAsync` 元数据读取期间（几 ms 窗口）删除非当前曲，可能让随后 `Queue[index] = meta` 写到错位（被删项之后的某项被覆盖）；最坏越界抛 `IndexOutOfRangeException` 被静默吞。触发概率极低但语义脏。

**Phase 5 偿还：** `RemoveTrack` 入口加 `_playToken++`；新增的 `MoveTracks` 同样在入口加 `_playToken++`（拖拽重排会让所有被移动项的索引变化，旧 in-flight 必须中止）。两处 commit `170bca8`。

---

## 4. 已识别的"过度抽象"（pre-emptive abstraction）

| 接口 | 实现 | 状态 |
|------|------|------|
| `IAudioDeviceManager` | `StubAudioDeviceManager` | 注册了，**无人消费** |
| `IAudioOutputFactory` | `StubAudioOutputFactory` | 注册了，**无人消费**（`NAudioPlaybackService` 自己 `new WasapiOut`） |

**判断：** 这是 YAGNI 违规。但成本几乎为零（4 个空文件 + 2 行注册）。

**建议：** 暂不删 —— 删了 Phase 3（多设备支持）还要重写。但**别再增加这类预留接口**，等真正需求落地再抽。

---

## 5. 隐式契约（注释里有，类型系统里没有）

这些"潜规则"目前靠注释和惯例维持，是未来重构时最容易踩的坑：

| 契约 | 位置 | 风险 |
|------|------|------|
| `NAudioPlaybackService` 必须在 UI 线程构造 | 注释 + `SynchronizationContext.Current ?? new()` 容错 | 后台线程解析会**静默失效**（事件不到 UI） |
| `Themes/Controls.xaml` 中 Slider 模板的 `PART_Track` 命名 | `PlayerBar.xaml.cs:FindName("PART_Track")` | 重命名会**静默禁用**单击跳转 |
| `DurationChanged` 必须在 `TrackChanged` 之前触发 | VM 隐式依赖此顺序计算 `PositionNormalized` | 颠倒顺序首帧 UI 异常 |
| `_isInitializing` 标志的生命周期 | VM 构造期间防止 `OnVolumeChanged` 写盘 | 若 ctor 之外有人写 `Volume` 会破坏不变量 |
| `Window_Closing` 是 `async void`，WPF 不会等 await 完成 | ✅ Phase 4 改用 cancel-and-close（commit `9bae7ce`）—— 首次 `e.Cancel=true` + `_isClosing` 标志，跑完异步链再 `Close()` | 已不再依赖"靠 DisposePlayback 幂等性救场"的降级方案 |
| `Volume` setter 跨线程写 `VolumeSampleProvider.Volume` | UI 线程写，WASAPI render 线程读，无同步原语 | 实践无问题；如改非原子类型会爆 |
| **Phase 2 新增** | | |
| `Stop()` vs `Unload()` 语义差异 | `IPlaybackService` 两个独立方法 + 各自 XML 注释 | 用 `Stop()` 替代 `Unload()` 会让"清空队列后按 Play"重播刚才那首；用 `Unload()` 替代 `Stop()` 会让 `Pause→恢复` 失效 |
| `PlayTrackAtAsync` 必须自增 `_playToken` 后再 `await` | 注释 + `if (myToken != _playToken) return` 守卫 | 任何新增的 `await` 后忘记校验 token 都会留下重入窗口 |
| `PlaylistView.RefreshCurrentIndicator` 用 DataTemplate 列序定位 ▶ TextBlock | `FindChildByOrder<TextBlock>` | 在 `DataTemplate` 里加列会**静默错位** |
| **Phase 3 新增** | | |
| `PlayerVM` / `PlaylistVM` 互不持引用 | 注释 + spec §2.1 | 任何一方加入对另一方的字段引用都会让 MainViewModel Facade 退化为转发层；spec 明确此为硬规则 |
| DI 注册顺序：PlayerVM **先于** PlaylistVM | `Extensions/ServiceCollectionExtensions.cs` 注释 | PlayerVM 在 ctor 中订阅 5 个 transport 事件；若 PlaylistVM 先构造，它的 TrackEnded 订阅会先收到事件，可能让 PlayerVM 错过开头几次 PositionChanged（实践上 LoadAsync 还没开始，未观察到，但显式顺序更稳） |
| `UpdateAsync` mutator 内不可读 WPF DP | `MainWindow.xaml.cs:Window_Closing` 注释 | `.ConfigureAwait(false)` 把闭包扔到 threadpool；DP 读取必须先在 UI 线程捕获到局部变量再 await。违反时 `Left/Top/Width/ActualHeight` 抛 `InvalidOperationException`，被外层 catch 静默吞掉 → 窗口几何不保存 |
| `IPlaybackService.TrackChanged` 携带 `Track?`（可为 null） | 接口 XML 注释 + `NAudioPlaybackService.Unload()` 实现 | `Unload()` 广播 `TrackChanged(null)` 通知 VM 清屏；VM 端 handler 必须 null-safe（已用 `?.AlbumArt`） |
| `PlaylistViewModel.PlayTrackAtAsync` 期间 `RemoveTrack` 非当前曲未自增 `_playToken` | ✅ Phase 5 已偿（commit `170bca8`）—— `RemoveTrack` 与新增的 `MoveTracks` 入口都加 `_playToken++` | 历史遗留，已不再是隐式契约（debt #5 关闭） |
| **Phase 4 新增** | | |
| `MainWindow.Window_Closing` 必须用 cancel-and-close 模式 | `MainWindow.xaml.cs:Window_Closing` 注释 + `_isClosing` 标志 | `async void` 多 await 时第一个 await yield 后 WPF 立即继续关闭流程，`ShutdownMode.OnLastWindowClose` 触发 `Application.Shutdown → Dispatcher.InvokeShutdown`，后续 await 续延 post 到死 dispatcher 上**永不运行**。Phase 4 加 queue.json 写盘后从 2 个 await 涨到 4 个，settings 还能写但 queue 永远不更新（commit `9bae7ce` 用此模式修复）。任何新增 await 都要遵守此纪律 |
| WPF inline `Style` 必须 `BasedOn="{StaticResource {x:Type X}}"` | `PlayerBar.xaml` ▶ 按钮 inline Style 注释 | `<X.Style><Style TargetType="X">` 没有 `BasedOn` 会完全替换 `Themes/Controls.xaml` 中的隐式 Style，回退到 OS 原生外观（Button 白底灰框、Slider 灰色等）。Phase 4 ▶ 按钮加 DataTrigger 时漏 BasedOn → 按钮变白底（commit `ca66fa9` 修复）。Code review checklist：看到 inline Style 就检查 BasedOn |
| `IQueuePersistence.LoadAsync` 隐式契约：绝不抛 | `JsonQueuePersistence.LoadAsync` catch-all + 注释 | 任何异常逃出会让 `PlaylistViewModel` 构造抛 → 应用启动崩溃。文件不存在/JSON 损坏/版本不匹配/反序列化得 null 全部走静默 fallback |
| `PlaylistViewModel.SnapshotState()` 必须纯读、无副作用 | 方法 XML 注释 | `MainWindow.Window_Closing` 在 `CleanupAsync` 之后调它，假定不会改 Queue/CurrentIndex/Shuffle/Repeat。若未来加副作用会让人意外 |
| `LoadFromDisk` 同步段不可 await | `PlaylistViewModel.LoadFromDisk` 注释（用 `.GetAwaiter().GetResult()`） | DI 容器构造 VM 时若死锁 UI sync ctx 会让窗口永不显示。`JsonQueuePersistence` 内部已 `ConfigureAwait(false)`，UI 线程同步等待 worker pool 任务回调时不会死锁 |
| **Phase 5 新增** | | |
| 拖拽 View/VM 边界：DragEventArgs / DataObject / AdornerLayer 不得渗入 VM | `PlaylistViewModel.DropExternalFiles(IReadOnlyList<string>)` / `MoveTracks(MoveTracksArgs)` 接口签名 + 注释 | 让 VM 仍可单测、保持 PlayerVM/PlaylistVM 不持引用的拓扑。任何把 OLE 类型/事件参数下推到 VM 的修改都倒退此契约 |
| `MoveTracks` 必须用对象身份（`ReferenceEquals` / `ReferenceEqualityComparer.Instance`）回找 CurrentIndex 与 _shuffleHistory | `PlaylistViewModel.MoveTracks` 注释 | Track 是 `sealed record`，结构相等会让 `Queue.IndexOf(currentTrackObj)` 在出现重复占位时返回首个等价匹配而非原始那一个 → ▶ 跟到错的曲、Shuffle 历史塌陷。code-behind 多选拖拽收集源索引也同此规则（`QueueList_PreviewMouseMove` 用 `ReferenceEqualityComparer` HashSet 扫一遍 Queue） |
| WPF DP 优先级 `local > trigger setter > style setter`：要被 trigger 改的属性默认值必须放进 Style.Setter | `PlaylistView.xaml` `QueueListBorder` 注释 + `wpf-local-value-defeats-style-trigger` memory | 写成元素的 local attribute 会让 trigger 永远赢不了，触发器静默失效（Phase 5 拖拽边框高亮初次落地踩了，commit `214d595` 修） |
| WPF DragDrop 是冒泡 RoutedEvent；子元素 `Handled=true` 后父 handler 不再触发 → 清理逻辑必须在两条路径都做 | `PlaylistView.xaml.cs` `QueueList_Drop` finally + `Root_Drop` 注释 | 假定事件会冒泡上来清 Adorner / IsDragOver 会让高亮卡死（commit `a80afdd` 修） |
| WPF ListBox `PreviewMouseLeftButtonDown` 不消费事件时仍会触发自身的塌选；多选拖拽必须主动拦 | `PlaylistView.xaml.cs:QueueList_PreviewMouseLeftButtonDown` `_pendingSingleSelectItem` 状态机注释 | "Ctrl+多选 → 在已选项上点 → 默认行为塌为单选" 在 PreviewMouseMove 启动 DoDragDrop 之前就发生，多选拖拽静默退化为单项拖；commit `af51dde` 用"按下时拦 + MouseUp 补单选"的状态机修复 |
| `Root_DragEnter` 高亮前必须 `FilterAudioPaths` 检查 | `PlaylistView.xaml.cs:Root_DragEnter` 注释 | 仅看 `FileDrop` 存在就亮，会让文件夹/全非音频也亮（光标已显示禁止但边框还紫，视觉冲突）；commit `7af6bec` 修 |
| **Phase 6 新增** | | |
| `IPlaylistService.LoadAsync` 隐式契约：绝不抛 | `JsonPlaylistService.LoadAsync` catch-all + 注释 | 任何异常逃出会让 `PlaylistsViewModel.Hydrate` 抛 → 应用启动崩溃。文件不存在/JSON 损坏/版本不匹配/反序列化得 null 全部走静默 fallback；v1→v2 迁移失败也吞掉，下次重迁（幂等） |
| `PlaylistsViewModel.StateChanged` 不因 `IsActivePlaylist` 设值触发 | `PlaylistsViewModel.OnPlaylistVmPropertyChanged` 注释 | `RecomputeIsActiveFlags` 批量设 `IsActivePlaylist` 会触发 `PropertyChanged`；若 `StateChanged` 不过滤会 echo 回 save → 无意义写盘 |
| `PlaylistView.RefreshCurrentIndicator` 必须检查 `IsActivePlaylist` | `PlaylistView.xaml.cs:RefreshCurrentIndicator` 注释 | 用户切到非播放歌单查看时，`CurrentIndex` 仍是该歌单的本地光标；不 guard 会让 ▶ 在非播放歌单上点亮（视觉与音频脱钩） |
| `PlaylistView.QueueList_MouseDoubleClick` 走 `PlaylistsViewModel.HandleDoubleClickPlay` | `PlaylistView.xaml.cs:QueueList_MouseDoubleClick` 注释 | 直接调 `_vm.PlayTrackAtCommand` 不会切 `CurrentPlaylistId` → 跨歌单双击时 sidebar ▶ 标记不移动、`IsActivePlaylist` 不更新 |
| `PlaylistsSidebarView.RefreshActiveMarker` 用 `FindChildByOrder<TextBlock>(container, 0)` 定位 ▶ | `PlaylistsSidebarView.xaml.cs:RefreshActiveMarker` 注释 | 在 `DataTemplate` 里加列会**静默错位**（与 PlaylistView 同规则） |
| 删除当前播放歌单不自动停止音频 | `PlaylistsViewModel.RemovePlaylist` 注释 | 有意的 MVP 简化：删除歌单仅切指针 + unhook TrackEnded，当前曲自然播完即停；不调 `_player.Unload()` 避免 jarring UX。spec 说"让 MainViewModel 处理音频停"但 MainVM 无此 handler —— Phase 7 可评估是否加 stop-on-delete |
| **Phase 10 新增** | | |
| `ILibraryCache.LoadAsync` 隐式契约：绝不抛 | `JsonLibraryCache.LoadAsync` catch-all + 注释 | 与 `IPlaylistService.LoadAsync` 同隐式契约。任何异常逃出会让 `RescanSinglePlaylistAsync` 抛 → 应用启动崩溃或手动刷新失败。文件不存在/JSON 损坏/反序列化得 null 全部走静默 fallback 到空字典 |
| `LibraryScannerService` 不依赖 Views 层 | `Models.AudioConstants` 提取 + `Services/LibraryScannerService.cs` | Phase 10 前音频后缀白名单在 `DragDropExtensions`（View 层）；`LibraryScannerService` 需要用同一白名单但不能反向依赖 Views。已提取到 `Models.AudioConstants` 消除层级违规。Code review：新增扫描相关逻辑时确保不引用 `Views.Controls` 命名空间 |

**建议：** 这些不需要立即修，但**每次改相关代码时去注释里复习一遍**。

---

## 6. 启动检查清单（Phase 10 完成）

> Phase 10（文件夹绑定歌单）已完成。所有结构性改造与债务偿还已清零。

1. ✅ **VM 拆分**（Phase 3 完成，commit `54edf9a`）—— MainViewModel 643→44 行 Strict Facade；PlayerVM + PlaylistVM 互不持引用
2. ✅ **`PlayerBar` / `PlaylistView` 去硬转型**（Phase 3 完成）—— DataContext 切到子 VM；跨域命令用 `RelativeSource AncestorType=Window`
3. ✅ **抽 `ITrackMetadataReader`**（Phase 3 完成，commit `c9cd1bd`）—— `AtlMetadataReader` 封装 z440.atl.core
4. ✅ **解决 settings 合并纪律**（Phase 3 完成，commit `fadae44`）—— `UpdateAsync(Func<>)` 把读-改-写封进锁内
5. ✅ **队列持久化**（Phase 4 完成）—— `%LocalAppData%\UmaPlayer\queue.json` 与 settings.json 分离
6. ✅ **多命名播放列表**（Phase 6 完成）—— `Playlist` record + `IPlaylistService` + `PlaylistsViewModel` 容器 + sidebar UI + v1→v2 迁移
7. ✅ **拖拽支持**（Phase 5 完成）—— 外部文件拖入入队 + 队列内拖拽重排（含多选）+ 视觉反馈；同步偿还旧债 #5
8. ✅ **偿还债 #1 (`BitmapImage`)** —— Phase 7 已完成：`AlbumArtImage` → `AlbumArtBytes` (byte[])，VM 层无 WPF 类型

**Phase 11+ 候选范围：**
- [x] PlayerViewModel 单元测试（Phase 8 完成，15 个测试）
- [x] PlaylistViewModel 单元测试（Phase 8 完成，16 个测试）
- [x] PlaylistsViewModel 单元测试（Phase 8 完成，17 个测试）
- [x] sidebar 拖拽重排歌单顺序（Phase 9 完成）
- [x] 文件夹绑定歌单（Phase 10 完成：扫描 + 增量同步 + 元数据缓存）
- [ ] 可视化 ~6h+

---

## 7. 不要做的事 🚫

- ❌ 现在删 `IAudioDeviceManager` / `IAudioOutputFactory` —— 删了未来加多设备支持还要写回来
- ❌ 现在引入 MediatR / EventAggregator —— 6 个事件直连完全够用；两个子 VM 通过 `IPlaybackService` 间接协作已足够解耦
- ❌ 现在为 `PlayerViewModel` 写单测 —— 先把债 #1（`BitmapImage`）抽掉再说
- ❌ 用 `IPlaybackService.Stop()` 代替 `Unload()` 来"清空当前曲" —— 会重现已修复的 Bug（清队列后按 Play 重播刚才那首）
- ❌ 让 `PlayerViewModel` 与 `PlaylistViewModel` 互相持有引用 —— 直接违反 spec §2.1 硬规则，会让 MainViewModel Facade 倒退为转发层
- ❌ 在 `UpdateAsync` mutator 闭包内读 WPF DependencyProperty —— 见 §5 隐式契约
- ❌ **在 `Window_Closing` 内继续堆 await 而不用 cancel-and-close 模式**（Phase 4）—— 静默丢失后续写盘
- ❌ **写 inline `<Style TargetType="X">` 而不加 `BasedOn="{StaticResource {x:Type X}}"`**（Phase 4）—— 控件回退到 OS 原生外观
- ❌ **让 `IQueuePersistence.LoadAsync` 抛异常**（Phase 4）—— 应用启动崩溃；任何异常都该被 catch-all 捕获并 fallback 到 `new QueueState()`
- ❌ **把 `BorderBrush`/`Background` 等可能被 Style.Trigger 改的 DP 写成元素的 local attribute**（Phase 5）—— DP 优先级 `local > trigger setter`，触发器永远赢不了；默认值要放进 Style.Setter（commit `214d595`）
- ❌ **在 `MoveTracks` 或类似重排逻辑用 `Queue.IndexOf` / 默认 `HashSet<Track>` 找回原 Track 实例**（Phase 5）—— Track record 结构相等会塌陷重复占位；必须用 `ReferenceEquals` + `ReferenceEqualityComparer.Instance`
- ❌ **假定子元素 `Drop` 设 `Handled=true` 后父元素的清理代码会冒泡执行**（Phase 5）—— 不会；清 Adorner / IsDragOver 必须在每条 Drop 路径自己显式做（commit `a80afdd`）
- ❌ **把 OLE 类型（DataObject、DragEventArgs、AdornerLayer）下推到 VM**（Phase 5）—— View/VM 边界破坏；VM 失去单测能力，PlayerVM/PlaylistVM 拓扑也会被牵连
- ❌ **在 `Root_DragEnter` 仅看 `FileDrop` 存在就高亮**（Phase 5）—— 文件夹/非音频也会亮，与光标禁止图标冲突；高亮前必须 `FilterAudioPaths` 检查（commit `7af6bec`）
- ❌ **在 Services 层引用 `Views.Controls.DragDropExtensions`**（Phase 10）—— 音频后缀白名单已提取到 `Models.AudioConstants`；扫描服务必须用 `AudioConstants.Extensions` 而非 View 层的代理属性，否则破坏依赖方向

---

## 8. 参考

- 完整代码导读：[`docs/PROJECT.md`](./PROJECT.md)
- 原始设计稿：
  - [`docs/superpowers/specs/2026-04-23-uma-player-design.md`](./superpowers/specs/2026-04-23-uma-player-design.md) — Phase 1
  - [`docs/superpowers/specs/2026-06-06-uma-player-playlist-design.md`](./superpowers/specs/2026-06-06-uma-player-playlist-design.md) — Phase 2
  - [`docs/superpowers/specs/2026-06-07-uma-player-phase3-design.md`](./superpowers/specs/2026-06-07-uma-player-phase3-design.md) — Phase 3
  - [`docs/superpowers/specs/2026-06-12-uma-player-phase4-queue-persistence-design.md`](./superpowers/specs/2026-06-12-uma-player-phase4-queue-persistence-design.md) — Phase 4
  - [`docs/superpowers/specs/2026-06-12-uma-player-phase5-drag-drop-design.md`](./superpowers/specs/2026-06-12-uma-player-phase5-drag-drop-design.md) — Phase 5
  - [`docs/superpowers/specs/2026-06-13-uma-player-phase6-named-playlists-design.md`](./superpowers/specs/2026-06-13-uma-player-phase6-named-playlists-design.md) — Phase 6
- 原始实现计划：
  - [`docs/superpowers/plans/2026-04-24-uma-player-implementation.md`](./superpowers/plans/2026-04-24-uma-player-implementation.md) — Phase 1
  - [`docs/superpowers/plans/2026-06-06-uma-player-playlist-implementation.md`](./superpowers/plans/2026-06-06-uma-player-playlist-implementation.md) — Phase 2
  - [`docs/superpowers/plans/2026-06-07-uma-player-phase3-implementation.md`](./superpowers/plans/2026-06-07-uma-player-phase3-implementation.md) — Phase 3
  - [`docs/superpowers/plans/2026-06-12-uma-player-phase4-queue-persistence-implementation.md`](./superpowers/plans/2026-06-12-uma-player-phase4-queue-persistence-implementation.md) — Phase 4
  - [`docs/superpowers/plans/2026-06-12-uma-player-phase5-drag-drop-implementation.md`](./superpowers/plans/2026-06-12-uma-player-phase5-drag-drop-implementation.md) — Phase 5
  - [`docs/superpowers/plans/2026-06-13-uma-player-phase6-named-playlists.md`](./superpowers/plans/2026-06-13-uma-player-phase6-named-playlists.md) — Phase 6
