# UmaPlayer 耦合分析与重构备忘

> 创建日期：2026/06/06 · 更新日期：2026/06/12 · 对应分支：`master` · 对应阶段：**Phase 4 完成**（队列持久化）
>
> **本文档的用途：** 不是行动清单，是**风险登记册**。Phase 2 偿还债 #2；Phase 3 偿还债 #3/#4 + 完成 VM 拆分 + View 去硬转型；Phase 4 加入队列持久化（无新还债，仅功能增量 + 2 个 WPF 隐式契约）。剩余债与后续工作详见 §6。

---

## TL;DR

| 维度 | 评级 | 备注 |
|------|------|------|
| 整体耦合度 | **低** | Phase 3 后 MainViewModel 仅 44 行（Strict Facade）；Phase 4 仅给 PlaylistViewModel 加了 `IQueuePersistence` 一个新依赖 |
| 是否需要立即重构 | ✅ 无 | Phase 3 完成所有结构性改造；Phase 4 沿用既有模式 |
| Phase 5 是否会变痛 | ⚠️ **多命名播放列表会改 queue.json schema** | 当前 schema 仅一个队列；多列表需 schema v2 + 迁移 |
| 已识别"待还的债" | 1 项剩余（#1 BitmapImage；#2/#3/#4 ✅ 已偿） | 见 §3 |
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

**结论：** Phase 4 后约 ~1700 行代码（含 `QueueState` + `IQueuePersistence` + `JsonQueuePersistence` + PlaylistVM 的 ~140 行恢复/快照逻辑）。MainViewModel 仍维持 44 行 Strict Facade；继续加功能（多命名播放列表 / 拖拽）不会再触碰核心架构。

---

## 2. 依赖图（事实陈述）

| 消费方 | 依赖的抽象 | 依赖的具体类型 |
|--------|------------|----------------|
| `App` | `MainViewModel`, `ISettingsPersistence`, `IQueuePersistence` | `Views.MainWindow`, `ServiceProvider` |
| `ServiceCollectionExtensions` | — | 6 个 Service 实现 + `MainViewModel`（注册绑定） |
| `MainViewModel` (Facade) | `PlayerViewModel`, `PlaylistViewModel`, `IPlaybackService` | — |
| `PlayerViewModel` | `IPlaybackService`, `ISettingsPersistence`, `IOptions<AppSettings>` | ⚠️ `BitmapImage`（WPF，债 #1） |
| `PlaylistViewModel` | `IPlaybackService`, `IFileDialogService`, `ITrackMetadataReader`, `IQueuePersistence` | `Track`、`RepeatMode`、`QueueState`、`File.Exists` |
| `MainWindow` | `MainViewModel`, `ISettingsPersistence`, `IQueuePersistence` | `Window`, `SystemParameters` |
| `PlayerBar` | — | `PlayerViewModel`（`DataContext as PlayerViewModel`，3 处）；跨级访问 `Playlist.<Cmd>`（含 Phase 4 ▶ DataTrigger 的 `PlayCurrentCommand`） |
| `PlaylistView` | — | `PlaylistViewModel`（`DataContext as PlaylistViewModel`）；订阅 `PropertyChanged` / `Queue.CollectionChanged` |
| `NAudioPlaybackService` | `IPlaybackService` | `MediaFoundationReader`, `WasapiOut`, `VolumeSampleProvider` |
| `Win32FileDialogService` | `IFileDialogService` | `Microsoft.Win32.OpenFileDialog` |
| `JsonSettingsPersistence` | `ISettingsPersistence` | `File`, `JsonSerializer`, `Environment.SpecialFolder` |
| `JsonQueuePersistence` (Phase 4) | `IQueuePersistence` | `File`, `JsonSerializer`, `Environment.SpecialFolder` |
| `AtlMetadataReader` | `ITrackMetadataReader` | `ATL.Track`（封装隔离） |

> ⚠️ 标记的是 §3 仍未偿的债（仅剩 #1 BitmapImage）。

---

## 3. 已识别的 4 个"待还的债"

### 债 #1 — VM 持有 `BitmapImage`（WPF 类型泄漏）

**位置：** `ViewModels/MainViewModel.cs:38-39`、`CreateAlbumArtImage`

```csharp
[ObservableProperty] private BitmapImage? _albumArtImage;  // ← System.Windows.Media.Imaging
```

**影响：**
- VM 不再能跨 UI 框架复用（如未来想出 MAUI / Avalonia 版本）
- VM 不可在无 WPF 上下文的单元测试项目中实例化

**触发时机：** 想为 VM 写单测时；想把核心逻辑独立成跨平台库时

**预修方案（约 15 分钟，等触发时再做）：**
1. VM 改为暴露 `byte[]? AlbumArtBytes`
2. 新建 `Converters/BytesToBitmapImageConverter.cs`
3. XAML 绑定加 `Converter={StaticResource BytesToBitmap}`

**Phase 3 复盘：** 仍未偿。Phase 3 已把 VM 拆为 Player/Playlist，单元测试场景变得更可能（PlayerViewModel 只依赖 transport 服务）；下次想给 `PlayerViewModel` 写单元测试时一并还。

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
| `PlaylistViewModel.PlayTrackAtAsync` 期间 `RemoveTrack` 非当前曲未自增 `_playToken` | 代码审查发现，未修复（继承自 Phase 2） | 用户在 in-flight 元数据读取期间删除非当前曲，可能让 `Queue[index] = meta` 写到错位（或越界）；触发窗口极窄（几 ms），最坏 silent IndexOutOfRange 被吞。Phase 5 重构时一并修 |
| **Phase 4 新增** | | |
| `MainWindow.Window_Closing` 必须用 cancel-and-close 模式 | `MainWindow.xaml.cs:Window_Closing` 注释 + `_isClosing` 标志 | `async void` 多 await 时第一个 await yield 后 WPF 立即继续关闭流程，`ShutdownMode.OnLastWindowClose` 触发 `Application.Shutdown → Dispatcher.InvokeShutdown`，后续 await 续延 post 到死 dispatcher 上**永不运行**。Phase 4 加 queue.json 写盘后从 2 个 await 涨到 4 个，settings 还能写但 queue 永远不更新（commit `9bae7ce` 用此模式修复）。任何新增 await 都要遵守此纪律 |
| WPF inline `Style` 必须 `BasedOn="{StaticResource {x:Type X}}"` | `PlayerBar.xaml` ▶ 按钮 inline Style 注释 | `<X.Style><Style TargetType="X">` 没有 `BasedOn` 会完全替换 `Themes/Controls.xaml` 中的隐式 Style，回退到 OS 原生外观（Button 白底灰框、Slider 灰色等）。Phase 4 ▶ 按钮加 DataTrigger 时漏 BasedOn → 按钮变白底（commit `ca66fa9` 修复）。Code review checklist：看到 inline Style 就检查 BasedOn |
| `IQueuePersistence.LoadAsync` 隐式契约：绝不抛 | `JsonQueuePersistence.LoadAsync` catch-all + 注释 | 任何异常逃出会让 `PlaylistViewModel` 构造抛 → 应用启动崩溃。文件不存在/JSON 损坏/版本不匹配/反序列化得 null 全部走静默 fallback |
| `PlaylistViewModel.SnapshotState()` 必须纯读、无副作用 | 方法 XML 注释 | `MainWindow.Window_Closing` 在 `CleanupAsync` 之后调它，假定不会改 Queue/CurrentIndex/Shuffle/Repeat。若未来加副作用会让人意外 |
| `LoadFromDisk` 同步段不可 await | `PlaylistViewModel.LoadFromDisk` 注释（用 `.GetAwaiter().GetResult()`） | DI 容器构造 VM 时若死锁 UI sync ctx 会让窗口永不显示。`JsonQueuePersistence` 内部已 `ConfigureAwait(false)`，UI 线程同步等待 worker pool 任务回调时不会死锁 |

**建议：** 这些不需要立即修，但**每次改相关代码时去注释里复习一遍**。

---

## 6. Phase 5 启动检查清单

> Phase 4（队列持久化）已完成并准备合并到 master。下一阶段（如多命名播放列表 / 拖拽支持）启动时按以下顺序：
>
> **更新（Phase 4 完成）：** 项 5（队列持久化）已完成。后续 Phase 5+ 仍待办：6、7。

1. ✅ **VM 拆分**（Phase 3 完成，commit `54edf9a`）—— MainViewModel 643→44 行 Strict Facade；PlayerVM + PlaylistVM 互不持引用
2. ✅ **`PlayerBar` / `PlaylistView` 去硬转型**（Phase 3 完成）—— DataContext 切到子 VM；跨域命令用 `RelativeSource AncestorType=Window`
3. ✅ **抽 `ITrackMetadataReader`**（Phase 3 完成，commit `c9cd1bd`）—— `AtlMetadataReader` 封装 z440.atl.core
4. ✅ **解决 settings 合并纪律**（Phase 3 完成，commit `fadae44`）—— `UpdateAsync(Func<>)` 把读-改-写封进锁内
5. ✅ **队列持久化**（Phase 4 完成）—— `%LocalAppData%\UmaPlayer\queue.json` 与 settings.json 分离；`IQueuePersistence` + `JsonQueuePersistence` + `QueueState` schema v1
6. ☐ **多命名播放列表（L3）** —— 真正的"播放列表管理"；UI 侧需引入 Tab 或侧栏；queue.json 需 schema v2 + 迁移
7. ☐ **拖拽支持** —— 外部文件拖入入队 + 队列内拖拽重排序
8. ☐ **偿还债 #1 (`BitmapImage`)** —— 想给 `PlayerViewModel` 写单元测试时一并做

**Phase 4 实际工作量：** 10 commits（含 2 个 hotfix：BasedOn + cancel-and-close）+ 1 个验收 + 1 个 docs ≈ 半个工作日（subagent-driven，单 session 完成）

**Phase 5+ 候选范围预估：** 拖拽支持 ~4h；多命名播放列表 ~10h+。

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

---

## 8. 参考

- 完整代码导读：[`docs/PROJECT.md`](./PROJECT.md)
- 原始设计稿：
  - [`docs/superpowers/specs/2026-04-23-uma-player-design.md`](./superpowers/specs/2026-04-23-uma-player-design.md) — Phase 1
  - [`docs/superpowers/specs/2026-06-06-uma-player-playlist-design.md`](./superpowers/specs/2026-06-06-uma-player-playlist-design.md) — Phase 2
  - [`docs/superpowers/specs/2026-06-07-uma-player-phase3-design.md`](./superpowers/specs/2026-06-07-uma-player-phase3-design.md) — Phase 3
  - [`docs/superpowers/specs/2026-06-12-uma-player-phase4-queue-persistence-design.md`](./superpowers/specs/2026-06-12-uma-player-phase4-queue-persistence-design.md) — Phase 4
- 原始实现计划：
  - [`docs/superpowers/plans/2026-04-24-uma-player-implementation.md`](./superpowers/plans/2026-04-24-uma-player-implementation.md) — Phase 1
  - [`docs/superpowers/plans/2026-06-06-uma-player-playlist-implementation.md`](./superpowers/plans/2026-06-06-uma-player-playlist-implementation.md) — Phase 2
  - [`docs/superpowers/plans/2026-06-07-uma-player-phase3-implementation.md`](./superpowers/plans/2026-06-07-uma-player-phase3-implementation.md) — Phase 3
  - [`docs/superpowers/plans/2026-06-12-uma-player-phase4-queue-persistence-implementation.md`](./superpowers/plans/2026-06-12-uma-player-phase4-queue-persistence-implementation.md) — Phase 4
