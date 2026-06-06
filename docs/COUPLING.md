# UmaPlayer 耦合分析与重构备忘

> 创建日期：2026/06/06 · 对应分支：`master` · 对应阶段：**Phase 2 完成**（播放队列）
>
> **本文档的用途：** 不是行动清单，是**风险登记册**。Phase 2 已偿还债 #2 + 新增 1 项；Phase 3 启动时按 §6 顺序处理剩余债。

---

## TL;DR

| 维度 | 评级 | 备注 |
|------|------|------|
| 整体耦合度 | **中** | Phase 2 后 `MainViewModel` ~520 行，达到拆分阈值 |
| 是否需要立即重构 | ⚠️ **Phase 3 启动前必做 VM 拆分** | 见 §6 #1 |
| Phase 3 是否会变痛 | ⚠️ **多列表/拖拽会** | 主要在 VM 单体性 + settings 合并纪律 |
| 已识别"待还的债" | 4 项（#2 已偿 ✅，新增 1 项隐式契约 #5） | 见 §3 |
| 已识别"过度抽象" | 2 项 | 见 §4 |

---

## 1. 当前架构为什么是健康的

✅ DI 容器集中注册（`ServiceCollectionExtensions.AddUmaPlayerServices`），无 Service Locator 反模式
✅ 依赖方向正确：`View → VM → Service → Model`，Service 从不反向引用 VM/UI
✅ 所有跨边界依赖**都走接口**：`IPlaybackService` / `IFileDialogService` / `ISettingsPersistence`
✅ 无 `static` 单例、无全局可变状态
✅ Layer 边界清晰（`Models/` / `Services/` / `ViewModels/` / `Views/` 物理隔离）
✅ Phase 2 新增（`PlaylistView` / Phase 2 命令 / 推进算法）全部沿用既有模式，未引入新抽象层

**结论：** Phase 2 后约 ~1100 行代码（含队列 UI），架构投入产出比仍然健康。继续往里加功能不会立刻碰壁，但**单 VM 体量已临阈值**，Phase 3 前需拆。

---

## 2. 依赖图（事实陈述）

| 消费方 | 依赖的抽象 | 依赖的具体类型 |
|--------|------------|----------------|
| `App` | `MainViewModel`, `ISettingsPersistence` | `Views.MainWindow`, `ServiceProvider` |
| `ServiceCollectionExtensions` | — | 5 个 Service 实现 + `MainViewModel`（注册绑定） |
| `MainViewModel` | `IPlaybackService`, `IFileDialogService`, `ISettingsPersistence`, `IOptions<AppSettings>` | ⚠️ `ATL.Track`、`BitmapImage`（WPF）、`Track`、`PlayState`、`RepeatMode` |
| `MainWindow` | `MainViewModel`, `ISettingsPersistence` | `Window`, `SystemParameters` |
| `PlayerBar` | — | ⚠️ `MainViewModel`（通过 `DataContext as MainViewModel` 硬转型） |
| `PlaylistView` | — | ⚠️ `MainViewModel`（同样硬转型；订阅 `PropertyChanged` / `Queue.CollectionChanged`） |
| `NAudioPlaybackService` | `IPlaybackService` | `MediaFoundationReader`, `WasapiOut`, `VolumeSampleProvider` |
| `Win32FileDialogService` | `IFileDialogService` | `Microsoft.Win32.OpenFileDialog` |
| `JsonSettingsPersistence` | `ISettingsPersistence` | `File`, `JsonSerializer`, `Environment.SpecialFolder` |

> ⚠️ 标记的就是 §3 列出的"已结疤的伤口"。

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

### 债 #3 — settings.json 双写者无合并纪律

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

### 债 #4 — 元数据读取硬编码 `ATL.Track`

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
| `Window_Closing` 是 `async void`，WPF 不会等 await 完成 | `_vm.CleanupAsync()` 可能未完成进程就退出 | `App.OnExit` 接着 dispose，靠 `DisposePlayback` 幂等性救场 |
| `Volume` setter 跨线程写 `VolumeSampleProvider.Volume` | UI 线程写，WASAPI render 线程读，无同步原语 | 实践无问题；如改非原子类型会爆 |
| **Phase 2 新增** | | |
| `Stop()` vs `Unload()` 语义差异 | `IPlaybackService` 两个独立方法 + 各自 XML 注释 | 用 `Stop()` 替代 `Unload()` 会让"清空队列后按 Play"重播刚才那首；用 `Unload()` 替代 `Stop()` 会让 `Pause→恢复` 失效 |
| `PlayTrackAtAsync` 必须自增 `_playToken` 后再 `await` | 注释 + `if (myToken != _playToken) return` 守卫 | 任何新增的 `await` 后忘记校验 token 都会留下重入窗口 |
| `PlaylistView.RefreshCurrentIndicator` 用 DataTemplate 列序定位 ▶ TextBlock | `FindChildByOrder<TextBlock>` | 在 `DataTemplate` 里加列会**静默错位** |

**建议：** 这些不需要立即修，但**每次改相关代码时去注释里复习一遍**。

---

## 6. Phase 3 启动检查清单

> Phase 2（播放队列）已完成并合并到 master（`8efc109`）。下一阶段（如多命名播放列表 / 队列持久化）启动时按以下顺序：

1. ☐ **VM 拆分**（债 #1 关联）—— `MainViewModel` 现 ~520 行，**已超阈值**
   - 拆 `PlayerViewModel`（仅 transport：Play/Pause/Stop/Seek/Volume）
   - 拆 `PlaylistViewModel`（队列 + Shuffle/Repeat + Next/Prev + 推进算法）
   - `MainViewModel` 作为 facade 持有两者；事件路由：`IPlaybackService.TrackEnded` 由 PlaylistVM 订阅
2. ☐ **`PlayerBar` / `PlaylistView` 去硬转型** —— 配合 VM 拆分；命令通过 `DependencyProperty` 或 XAML `{Binding}` 暴露；`PlaylistView` 的 `PropertyChanged` 订阅改成绑定到 `CurrentIndex` 触发的附加行为
3. ☐ **抽 `ITrackMetadataReader`**（债 #4）—— 多列表 / 文件夹扫描需要批量元数据；当前 `ReadTrackMetadataAsync` 是 VM 内 static
4. ☐ **解决 settings 合并纪律**（债 #3）—— 加入队列持久化前必做；引入 `UpdateAsync(Func<>)` 把读-改-写封进锁内
5. ☐ **队列持久化** —— `%LocalAppData%\UmaPlayer\queue.json`；考虑与 settings.json 分离以减小写盘压力
6. ☐ **多命名播放列表（L3）** —— 真正的"播放列表管理"；UI 侧需引入 Tab 或侧栏
7. ☐ **拖拽支持** —— 外部文件拖入入队 + 队列内拖拽重排序

**预估总工作量：** 10~14 小时（不含 L3 多命名列表本身的功能开发）

---

## 7. 不要做的事 🚫

- ❌ 现在为了"更解耦"而拆分 VM —— **除非**开始 Phase 3（届时是 §6 #1 必做项）
- ❌ 现在删 `IAudioDeviceManager` / `IAudioOutputFactory` —— 删了 Phase 3 还要写回来
- ❌ 现在引入 MediatR / EventAggregator —— 6 个事件直连完全够用
- ❌ 现在为 `MainViewModel` 写单测 —— 先把 §3 的 BitmapImage 和 ATL 抽掉再说
- ❌ 用 `IPlaybackService.Stop()` 代替 `Unload()` 来"清空当前曲" —— 会重现已修复的 Bug（清队列后按 Play 重播刚才那首）

---

## 8. 参考

- 完整代码导读：[`docs/PROJECT.md`](./PROJECT.md)
- 原始设计稿：
  - [`docs/superpowers/specs/2026-04-23-uma-player-design.md`](./superpowers/specs/2026-04-23-uma-player-design.md) — Phase 1
  - [`docs/superpowers/specs/2026-06-06-uma-player-playlist-design.md`](./superpowers/specs/2026-06-06-uma-player-playlist-design.md) — Phase 2
- 原始实现计划：
  - [`docs/superpowers/plans/2026-04-24-uma-player-implementation.md`](./superpowers/plans/2026-04-24-uma-player-implementation.md) — Phase 1
  - [`docs/superpowers/plans/2026-06-06-uma-player-playlist-implementation.md`](./superpowers/plans/2026-06-06-uma-player-playlist-implementation.md) — Phase 2
