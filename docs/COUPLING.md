# D-player 耦合分析与重构备忘

> 创建日期：2026-06-06 · 更新日期：2026-10-08（代码基线 `9657f95`） · 对应分支：`master` · 对应阶段：**Phase 21 已交付、验收闭合（2026-10-08，23 项全部通过，逐项结果见 `docs/PHASE21-ACCEPTANCE.md`）—— WinUI 壳视觉与交互打磨：三栏 IA + 令牌层 + 六态 + 键盘/无障碍 + 9 项动效；起窗崩溃 0xC0000005 已修复并验证（`bba9ad4`，根因：对象初始化器在构造函数体之后执行）** · Phase 20 已交付（`D-player.Core` 抽取 + WinUI 3 第二壳切片 + 门禁改走 `D-player.slnf`）；WPF 壳行为与外观零变化；两壳去留的决策门已拍板：续投 WinUI · 上一阶段：Phase 20 完成
>
> **本文档的用途：** 不是行动清单，是**风险登记册**。Phase 2 偿还债 #2；Phase 3 偿还债 #3/#4 + 完成 VM 拆分 + View 去硬转型；Phase 4 加入队列持久化（无新还债，仅功能增量 + 2 个 WPF 隐式契约）；Phase 5 加入拖拽支持 + 偿还旧债 #5（in-flight RemoveTrack 重入），新增 5 个 WPF 隐式契约；Phase 6 加入多命名歌单 + xUnit 骨架 + debt #1 部分偿还；Phase 7 完成 debt #1 完整偿还（VM 层无 WPF 类型）；Phase 8 建立 ViewModel 单元测试体系；Phase 9 sidebar 歌单拖拽重排；Phase 10 文件夹绑定歌单 + AudioConstants 层级修正；Phase 11 设置对话框；Phase 12 UI 重构 + 全局 Shuffle/Repeat + TrackInfoView；Phase 13 音频可视化（SampleAggregator FFT + SpectrumView，无新架构债，仅新增跨线程封送等隐式契约）；Phase 14 均衡器（EqualizerSampleProvider 10 段图形 EQ 中间件 + EqualizerDialog，无新架构债，仅给 IPlaybackService 加 1 属性、 0 新 DI 服务、 0 新 ViewModel，新增线程安全/Nyquist 旁路/ComboBox 首项自选等隐式契约）。所有技术债已清零。详见 §6。Phase 15 耦合健康度审计完成：结论为耦合低/健康、无需解耦（详见[审计报告](./superpowers/specs/2026-09-12-d-player-phase15-coupling-audit-report.md)）。Phase 16 图标矢量化（emoji/字形图标 → `Themes/Icons.xaml` 统一描边矢量 Geometry 集，转换器返回 Geometry，▶ 标记 TextBlock→Path；纯表现层，0 新依赖）。Phase 17 UI 深度深色定制（无边框 WindowChrome + 自绘 TitleBar 应用于主窗与 3 个对话框、ComboBox/CheckBox/ScrollBar/ToolTip/ContextMenu 深色隐式样式；纯表现层，0 新依赖）。Phase 18 播放列表文件导入导出（新增 `Services/PlaylistFiles` 门面模块 + `IPlaylistFileService` 1 个新 Singleton DI 服务；两个 VM 各加 1 个依赖 + 可 await 公开方法；追加路径复用既有 `DropExternalFiles` 不加新元数据依赖；无新架构债，新增 7 条隐式契约，详见 §5）。Phase 19 依赖迁移（NAudio 收窄到 `NAudio.Core` + `NAudio.Wasapi`、输出经 `WasapiPlayerBuilder` 建链、测试栈迁到 xunit.v3 + 单一 MTP runner；**无新增架构债**，仅新增 1 条测试栈隐式契约，详见 §5）。Phase 20 WinUI 3 第二 UI 壳（共享层物理抽成 WPF-free 的 `D-player.Core` 类库；`IFileDialogService` 的实现与用户数据目录 `DPlayerDataPaths` 改由各壳注入；门禁改走 `D-player.slnf`；新建 `D-player.WinUI` 切片壳。**架构债仍是 0 项，但新增 9 条跨壳隐式契约**（Core 不得引 WPF / 两壳共用一份 VM-Service / 同一手势共用 Core 公开入口 / 数据目录由壳注入 / 每壳自注册自己的对话框 / 门禁走筛选器且 WinUI 刻意在外 / 仓库根级新工程必须进 glob 排除集 / Mica 依赖根背景 Transparent / 每壳各自 await `CleanupAsync` 且不同步 Dispose 容器），详见 §5 与 §7）。

---

## TL;DR

| 维度 | 评级 | 备注 |
|------|------|------|
| 整体耦合度 | **低** | Phase 3 后 MainViewModel 仅 44 行（Strict Facade）；Phase 4 仅给 PlaylistViewModel 加 `IQueuePersistence` 一个新依赖；Phase 5 加拖拽完全在 PlaylistVM 域内完成（2 个新 RelayCommand，0 新依赖；View 层 +2 文件）；Phase 6 多命名歌单 + Phase 7 偿还债 #1 后 VM 层无 WPF 类型泄漏；Phase 13 频谱仅给 IPlaybackService 加 1 事件 + 1 属性，0 新 DI 依赖；Phase 14 均衡器仅给 IPlaybackService 加 1 属性（EqualizerConfig），0 新 DI 服务、 0 新 ViewModel；**Phase 15 审计确认**（M1–M6 客观度量）：0 环 / 0 层级违规 / IPlaybackService=20 成员 / 无 >600 LOC 多职责文件 / 2 stub 已注册未消费；Phase 16/17 均为纯表现层（0 新依赖 / 0 新 DI / 0 新 ViewModel）；Phase 18 播放列表文件导入导出仅新增 1 个无状态门面服务（`IPlaylistFileService` Singleton）+ 1 个文案格式化器（Phase 18 在 View 层，Phase 20 搬进 `D-player.Core/ViewModels`），两个 VM 各加 1 个依赖，追加路径复用既有 `DropExternalFiles`；**Phase 20 是纯结构调整**（搬家 + 依赖注入改道），0 新依赖方向变化、0 环、0 层级违规，代价换成"两壳并存"的 9 条跨壳纪律（§5 末尾那 9 行）而不是新的耦合边 |
| 是否需要立即重构 | ✅ 无 | Phase 3 完成所有结构性改造；Phase 4/5/6/7 沿用既有模式；Phase 15 审计裁决：D4 无必修项（0 环 / 0 违规）、D1 IPlaybackService 宽度可接受（观察项）、D5 PlaylistViewModel 652 LOC 单职责 cohesive（观察项，不拆分） |
| 已识别"待还的债" | 0 项剩余（#1/#2/#3/#4/#5 ✅ 全部已偿） | 见 §3 |
| 已识别"过度抽象" | 2 项 | 见 §4 |

---

## 1. 当前架构为什么是健康的

✅ DI 容器集中注册（`D-player.Core/Extensions/ServiceCollectionExtensions.cs` 的 `AddDPlayerCore(IConfiguration, DPlayerDataPaths)`，Phase 20 前叫 `Extensions/ServiceCollectionExtensions.cs` 的 `AddDPlayerServices`），无 Service Locator 反模式；UI 专属服务（`IFileDialogService`）刻意**不在**这张表里，由各壳自己注册
✅ 依赖方向正确：`View → VM → Service → Model`，Service 从不反向引用 VM/UI
✅ 所有跨边界依赖**都走接口**：`IPlaybackService` / `IFileDialogService` / `ISettingsPersistence`
✅ 无 `static` 单例、无全局可变状态
✅ Layer 边界清晰（`Models/` / `Services/` / `ViewModels/` / `Views/` 物理隔离）
✅ Phase 2 新增（`PlaylistView` / Phase 2 命令 / 推进算法）全部沿用既有模式，未引入新抽象层
✅ Phase 3 拆分：MainViewModel 收敛为 Strict Facade（44 行）；PlayerVM/PlaylistVM 互不持引用，仅共享 IPlaybackService Singleton；View 跨域命令用 RelativeSource AncestorType=Window 跨级绑定
✅ Phase 4 队列持久化沿用相同模式：`IQueuePersistence` 接口 + `JsonQueuePersistence` 实现 + Singleton 注册；与 `JsonSettingsPersistence` 文件隔离、锁隔离；PlaylistViewModel 读盘、MainWindow 写盘，无 VM 间耦合
✅ Phase 5 拖拽功能完全在 PlaylistVM 域内：DragDrop 事件 / 命中测试 / 文件过滤 / Adorner 绘制全在 View 层；VM 仅暴露 2 个纯数据 RelayCommand（`DropExternalFiles(paths)` / `MoveTracks(args)`），无 `DataObject` / `DragEventArgs` / `AdornerLayer` 渗透；MainViewModel Facade 维持 ~44 行不变
✅ Phase 10 文件夹绑定歌单：`ILibraryScannerService` + `ILibraryCache` 接口 + 实现注入 PlaylistsViewModel；`AudioConstants` 从 View 层提取到 Models 层消除层级违规；启动后台自动增量同步
✅ Phase 13 音频可视化：`SampleAggregator` 作为 `ISampleProvider` 透明中间件插入播放链，`IPlaybackService` 仅增 `SpectrumDataAvailable` 事件 + `SpectrumConfig` 属性；VM 层零新依赖（PlayerViewModel 已持有 IPlaybackService）；SpectrumView 纯 code-behind 绘制不进 VM —— 印证“加可视化不触碰核心架构”的判断
✅ Phase 14 均衡器：`EqualizerSampleProvider` 同样作为 `ISampleProvider` 透明中间件插入播放链（在 SampleAggregator 之前），`IPlaybackService` 仅增 `EqualizerConfig` 1 个属性（镜像 `SpectrumConfig`）；**0 新 DI 服务**（EQ provider 在 `NAudioPlaybackService.LoadAsync` 内按曲创建，同 `SampleAggregator`）、**0 新 ViewModel**（方案 A：`EqualizerDialog` 直写 `IPlaybackService` + `ISettingsPersistence`，PlayerViewModel 仅持 `EqualizerEnabled` 供按钮高亮）—— 同 Phase 13 印证“加功能不触碰核心架构”的判断
✅ Phase 16 图标矢量化：`Themes/Icons.xaml` 资源字典 + 转换器签名 string→Geometry + ▶ 标记 TextBlock→Path；View 层局部重构，VM 层零新依赖（反而移除 `VolumeIcon`），App.xaml 仅多合并一个字典
✅ Phase 17 UI 深度深色定制：`Views/Controls/TitleBar` 新 UserControl + 4 个 Window 的 WindowChrome 配置 + Controls.xaml 深色模板扩充；0 新 DI 服务、0 新 ViewModel、0 业务逻辑变更
✅ Phase 18 播放列表文件导入导出：新增 `Services/PlaylistFiles` 门面模块（`IPlaylistFileService` Singleton，无状态），依赖方向仍单向（VM → 服务 → Models；View → VM）；`PlaylistViewModel` / `PlaylistsViewModel` 各注入 1 个依赖、各暴露可 await 公开方法而非新命令；追加路径复用既有 `DropExternalFiles`（不为导入引入 `ILibraryScannerService`）；导入报告文案由 View 层 `PlaylistImportReportFormatter` 组装（四个入口共用），守住"VM 不拼展示文案"分层纪律（唯一例外：导出错误文案 VM 直传，单一分支、单一调用方，设计稿 §7.5）；后缀白名单单一来源（`PlaylistFileFormats.Extensions`，View 层 `DragDropExtensions` 只代理）

✅ Phase 19 依赖迁移：`NAudio` meta 包收窄为 `NAudio.Core` + `NAudio.Wasapi`、输出经 `WasapiPlayerBuilder` 建 `WasapiPlayer`（仍按 `IWavePlayer` 持有，契约一字未改）、测试栈迁到 xunit.v3 + 单一 MTP runner；0 新依赖方向变化
✅ Phase 20 抽出 `D-player.Core`：共享层（Models/Services/ViewModels/Configuration/Extensions）物理搬进类库，依赖方向**完全不变**（仍是 `View → VM → Service → Model`，壳 → Core），只是把"层边界"从目录约定升级成工程边界；两处真实 WPF 泄漏被清掉（`PlaylistViewModel.SortedView` 删除、`Win32FileDialogService` 搬回 WPF 壳）。第二壳 `D-player.WinUI` 只引用 Core，**不引用 WPF 壳**，两壳之间没有任何直接边 —— 这是"加一套 UI 而不引入新耦合"的结构前提。测试总数从 161 → 172 → **180**，逐阶段可核（Phase 20 各 Task 的增减见 §5 末尾与 PROJECT.md §3）

**结论：** Phase 14（均衡器）已落地，再次验证了“Phase 10 后继续加功能不会再触碰核心架构”的判断 —— EQ 功能仅给 `IPlaybackService` 加 1 个属性（`EqualizerConfig`），0 新 DI 服务，0 新 ViewModel，0 新债（与 Phase 13 频谱同构：透明 ISampleProvider 中间件 + 按曲在 LoadAsync 建链）。Phase 16/17 进一步验证：两阶段均为纯表现层（View/Themes 资源与控件），未触碰 VM/Service/Model 分层，0 新依赖、0 新债。

---

## 2. 依赖图（事实陈述）

| 消费方 | 依赖的抽象 | 依赖的具体类型 |
|--------|------------|----------------|
| `App`（WPF 壳 `App.xaml.cs`） | `MainViewModel`, `ISettingsPersistence` | `Views.MainWindow`, `ServiceProvider`, `DPlayerDataPaths`（`{ FolderName = "D-player" }`）, `LegacyDataMigration.MigrateIfNeeded(paths)`（启动一次性数据目录迁移，更名 UmaPlayer→D-player，**只属 WPF 壳**）, `Win32FileDialogService` 注册 |
| `D-player.WinUI/App`（Phase 20 第二壳） | `MainViewModel` | `DPlayerDataPaths`（`{ FolderName = "D-player-winui" }`）, `WinUiFileDialogService` 注册, `DPlayer.WinUI.MainWindow`；**刻意不同步 Dispose `ServiceProvider`** |
| `D-player.WinUI/MainWindow`（Phase 20） | `MainViewModel`（构造函数注入）, `PlaylistsViewModel.HandleDoubleClickPlay`, `PlaylistViewModel.PlayCurrentCommand`/`Queue`, `PlayerViewModel.*`, `MainViewModel.CleanupAsync` | `NavigationView` / `ListView` / `MicaBackdrop` / `AppWindow.Closing`；自己实现 `INotifyPropertyChanged` 供 `x:Bind` 投影；**不**引用 WPF 壳的任何类型 |
| `ServiceCollectionExtensions`（`D-player.Core/Extensions/`） | — | 9 个 Service 实现 + `MainViewModel` + `PlaylistsViewModel` + `Func<Playlist, PlaylistVM>`（注册绑定）+ 由壳传入的 `DPlayerDataPaths`；**不注册 `IFileDialogService`**（Phase 20 起由各壳自行注册） |
| `MainViewModel` (Facade) | `PlayerViewModel`, `PlaylistsViewModel`, `IPlaybackService`, `IPlaylistService` | — |
| `PlayerViewModel` | `IPlaybackService`, `ISettingsPersistence`, `IOptions<AppSettings>` | — |
| `PlaylistViewModel` | `IPlaybackService`, `IFileDialogService`, `ITrackMetadataReader`, `IPlaylistFileService`（Phase 18） | `Track`、`RepeatMode`、`QueueState`、`MoveTracksArgs`、`PlaylistImportReport`、`File.Exists` |
| `PlaylistsViewModel` | `Func<Playlist, PlaylistViewModel>`, `IPlaybackService`, `ILibraryScannerService`, `ILibraryCache`, `IPlaylistFileService`（Phase 18） | `Playlist`、`PlaylistViewModel`、`LibraryDiff`、`PlaylistImportReport` |
| `MainWindow` | `MainViewModel`, `ISettingsPersistence` | `Window`, `SystemParameters` |
| `SettingsDialog` | `ISettingsPersistence`, `IPlaybackService`（Phase 13）, `PlayerViewModel?`（可选，保存后同步） | `Window`, `App.GetService<>()` |
| `EqualizerDialog`（Phase 14） | `ISettingsPersistence`, `IPlaybackService`, `PlayerViewModel?`（可选，保存后同步 EqualizerEnabled） | `Window`, `App.GetService<>()`；直写 `_playbackService.EqualizerConfig` 实时预览 + `EqualizerConfig.Create` / `EqualizerPresets` |
| `TitleBar`（Phase 17） | — | `SystemCommands` / `WindowChrome` / `SystemParameters` / `Window.GetWindow(this)`；作用于宿主 Window 的 chrome 行为 |
| `PlayerBar` | — | `PlayerViewModel`（`DataContext as PlayerViewModel`，3 处）；跨级访问 `Playlist.<Cmd>`（含 Phase 4 ▶ DataTrigger 的 `PlayCurrentCommand`） |
| `PlaylistView` | — | `PlaylistViewModel`（`DataContext as PlaylistViewModel`）；订阅 `PropertyChanged` / `Queue.CollectionChanged`；Phase 5 直接消费 `DragDropExtensions` / `DropInsertionAdorner` / `MoveTracksArgs`，但全部走 RelayCommand 与 VM 通信；Phase 6 双击路由走 `App.GetService<PlaylistsViewModel>().HandleDoubleClickPlay`；Phase 18 消费 `PlaylistImportUi` / `DragDropExtensions.FilterPlaylistPaths` + await VM 公开方法（导入/导出，非命令），导出错误经 `ConfirmDialog.ShowError` |
| `PlaylistsSidebarView` | — | `PlaylistsViewModel`（`DataContext as PlaylistsViewModel`）；订阅 `PropertyChanged` / `Playlists.CollectionChanged`；消费 `PromptDialog`；Phase 18 消费 `PlaylistImportUi` / `DragDropExtensions.FilterPlaylistPaths` + await `ImportPlaylistFileAsync`（导入按钮与列表文件拖拽） |
| `NAudioPlaybackService` | `IPlaybackService` | `MediaFoundationReader`, `WasapiPlayer`（Phase 19：经 `WasapiPlayerBuilder` 构造，仍按 `IWavePlayer` 持有与调用）, `VolumeSampleProvider`, `SampleAggregator`（Phase 13）, `EqualizerSampleProvider`（Phase 14，在 `LoadAsync` 内按曲创建） |
| `Win32FileDialogService`（Phase 20 起在 **WPF 壳**的 `D-player/Services/`，命名空间仍是 `DPlayer.Services`） | `IFileDialogService` | `Microsoft.Win32.OpenFileDialog` / `OpenFolderDialog` / `SaveFileDialog`（Phase 18 导出） |
| `JsonSettingsPersistence` | `ISettingsPersistence` | `File`, `JsonSerializer`, `Environment.SpecialFolder` |
| `JsonPlaylistService` (Phase 6) | `IPlaylistService` | `File`, `JsonSerializer`, `Environment.SpecialFolder` |
| `LibraryScannerService` (Phase 10) | `ILibraryScannerService` | `Directory.EnumerateFiles`, `AudioConstants.Extensions` |
| `JsonLibraryCache` (Phase 10) | `ILibraryCache` | `File`, `JsonSerializer`, `Environment.SpecialFolder` |
| `PlaylistFileService` (Phase 18) | `IPlaylistFileService` | `File`, `Path`, `Encoding`（CodePages/GBK 经 internal `PlaylistFileEncoding`）, `AudioConstants.AudioExtensions`；内部编排 `M3uParser` / `PlsParser` / `M3u8Writer`（均 internal，不出模块） |
| `AtlMetadataReader` | `ITrackMetadataReader` | `ATL.Track`（封装隔离） |
| `PlayStateToIconConverter` / `RepeatModeToIconConverter` / `BoolToVolumeIconConverter`（Phase 16） | — | `Application.Current.FindResource` 查 `Themes/Icons.xaml` 的 `Icon.*` Geometry（依赖 App.xaml 已合并该字典） |

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
| `IAudioOutputFactory` | `StubAudioOutputFactory` | 注册了，**无人消费**（`NAudioPlaybackService` 在 `LoadAsync` 内直接经 `WasapiPlayerBuilder` 建输出，不经此工厂；工厂返回 `WasapiPlayer`，构造参数与播放链一致（共享 + 事件同步 + 100ms），至今无调用点） |

**判断：** 这是 YAGNI 违规。但成本几乎为零（4 个空文件 + 2 行注册）。

**建议：** 暂不删 —— 删了 Phase 3（多设备支持）还要重写。但**别再增加这类预留接口**，等真正需求落地再抽。

**Phase 15 审计再确认（D2）：** 保留这 2 个 stub —— 多设备/输出模式仍在路线图上（PROJECT.md §1.2），删了将来还要写回来。但**别再增加这类预留接口**，等真正需求落地再抽。

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
| `PlaylistView.RefreshCurrentIndicator` 用 `x:Name` 定位 ▶ 标记（Phase 16 起为 `Path`） | `FindChildByName<Path>(container, "PART_Marker")` + 切 `Visibility` | 不再依赖列序；Phase 16 标记由 TextBlock 改 Path（实心三角），改名会失效但不会错位 |
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
| `PlaylistsSidebarView.RefreshActiveMarker` 用 `x:Name` 定位活跃标记（Phase 16 起为 `Path`） | `FindChildByName<Path>(container, "PART_SidebarMarker")` + 切 `Visibility` | Phase 16 移除 `FindChildByOrder<TextBlock>(container,0)`（序数定位），改 x:Name 定位，消除 DataTemplate 加列静默错位隐患 |
| 删除当前播放歌单会停止播放 | `PlaylistsViewModel.RemovePlaylist` | Phase 12 改为：调 `_player.Stop()` + `_player.Unload()` + 清空 `CurrentPlaylistId`，▶ 标记消失。`PlaylistsViewModel` 新增 `IPlaybackService` 依赖 |
| **Phase 10 新增** | | |
| `ILibraryCache.LoadAsync` 隐式契约：绝不抛 | `JsonLibraryCache.LoadAsync` catch-all + 注释 | 与 `IPlaylistService.LoadAsync` 同隐式契约。任何异常逃出会让 `RescanSinglePlaylistAsync` 抛 → 应用启动崩溃或手动刷新失败。文件不存在/JSON 损坏/反序列化得 null 全部走静默 fallback 到空字典 |
| `LibraryScannerService` 不依赖 Views 层 | `Models.AudioConstants` 提取 + `Services/LibraryScannerService.cs` | Phase 10 前音频后缀白名单在 `DragDropExtensions`（View 层）；`LibraryScannerService` 需要用同一白名单但不能反向依赖 Views。已提取到 `Models.AudioConstants` 消除层级违规。Code review：新增扫描相关逻辑时确保不引用 `Views.Controls` 命名空间 |
| **Phase 11 新增** | | |
| SettingsDialog.Show 在 Loaded 中 async 读盘 | `SettingsDialog.xaml.cs:OnLoaded` | 与 MainWindow_Loaded 同模式（async void + try/catch）；文件极小（几百字节） |
| **Phase 12 新增** | | |
| Shuffle/Repeat 是全局设置（PlaylistsViewModel 持有） | `PlaylistsViewModel.ShuffleEnabled/RepeatMode` + `PlaylistViewModel.Container` | `PlaylistViewModel` 通过 `Container` 属性读取全局状态；`Container` 由 `HookPlaylistVm` 设置，null 时 fallback 到 false/Off |
| `PlaylistView.RefreshCurrentIndicator` 改用 `FindChildByName` | `PART_Marker` / `PART_Title` x:Name（Phase 16 起 `PART_Marker` 为 `Path`） | 不再依赖视觉树列序；DataTemplate 加列不再导致 ▶ 错位 |
| 表头排序物理重排 Queue | `PlaylistViewModel.SortBy` | `Queue.Clear()` + `Queue.Add()` 重排后 `CurrentIndex` 跟踪当前播放曲新位置 |
| `ApplyCachedMetadataSync` 旧缓存回读 TrackNumber | `PlaylistsViewModel.ApplyCachedMetadataSync` | 缓存条目 `TrackNumber` 为 null 时调 `ReadAsync` 补全，保证 # 列持久化 |
| GridSplitter DragDelta 实时限制列宽 | `MainWindow.xaml.cs` | 侧边栏/曲目信息列宽不超过窗口宽度一半；`HorizontalChange` 方向判断左右不同 |
| **Phase 13 新增** | | |
| 频谱事件必须经 UI 线程封送 | `NAudioPlaybackService.OnSpectrumDataReady` + `_syncContext.Post` | `SampleAggregator.SpectrumDataReady` 在 NAudio 音频渲染线程触发；不封送直接广播会让 `PlayerViewModel.HandleSpectrumData` 跨线程触碰绑定属性，抛 `InvalidOperationException` |
| `PlayerViewModel.HandleSpectrumData` 不得重复 32-bar 映射 | `PlayerViewModel.HandleSpectrumData` 注释 | `SampleAggregator` 已完成 FFT→对数分桶→32 bars；VM 只做灵敏度增益 + 指数平滑。二次映射会双重压缩频谱（曾有 double-log binning bug，commit `51a04c6` 修） |
| `SpectrumConfig` 是 record（引用类型），mock 需预设实例 | `PlayerViewModelSpectrumTests` 构造函数注释 | NSubstitute 对 record 属性默认返回 null；`OnSpectrumEnabledChanged` 读 `_player.SpectrumConfig with {...}` 会 NRE。测试须 `_player.SpectrumConfig.Returns(new SpectrumConfig())` |
| `SettingsDialog.Save_Click` 不得 `ConfigureAwait(false)` | `SettingsDialog.xaml.cs:Save_Click` | await 后需直接写 `PlayerViewModel` 属性同步频谱设置；`ConfigureAwait(false)` 会把续延扔到 threadpool，跨线程写 VM 观察属性触发绑定更新会异常（commit `a76d92a` 修） |
| `SpectrumView` 用 `CompositionTarget.Rendering` 必须在 `Unloaded` 解绑 | `SpectrumView.xaml.cs` 构造函数 | 全局渲染事件持有控件引用；不解绑会导致控件无法回收（内存泄漏） |
| **Phase 14 新增** | | |
| `EqualizerSampleProvider.Read`（音频线程）与 `Update`（UI 线程）共用 buffer 粒度 `lock`；`Update` 必须用 `SetPeakingEq` 就地重算（保留 x1/x2/y1/y2 状态） | `Services/EqualizerSampleProvider.cs` | 无锁会撕裂系数；重建 `BiQuadFilter` 会清空延迟线导致拖动爆音 |
| 立体声必须每声道独立 `BiQuadFilter?[channel][band]` | `EqualizerSampleProvider._filters[channel][band]` | 左右共享滤波器实例会串扰滤波状态 |
| EQ 插入点必须在 `SampleAggregator` 之前 | `NAudioPlaybackService.LoadAsync` | 移到其后会让频谱与 EQ 后听感脱钩 |
| `IPlaybackService.EqualizerConfig` setter 语义对齐 `SpectrumConfig`：存字段 + `_equalizer?.Update` | `NAudioPlaybackService.EqualizerConfig` | 链未建时仅存字段待 `LoadAsync` 拾取（不会丢更新） |
| 给 `IPlaybackService` 加成员必须同步更新 `PlaylistsViewModel` 内的手写 `NullPlaybackService` 空对象 | `PlaylistsViewModel.NullPlaybackService`（该接口的第二个生产实现者，NSubstitute 只覆盖测试替身） | 否则 CS0535 编译失败 |
| `EqualizerDialog.Save_Click` 不得 `ConfigureAwait(false)` | `Views/Dialogs/EqualizerDialog.xaml.cs:Save_Click` | 保存后需在 UI 线程写 `PlayerViewModel.EqualizerEnabled`（同 SettingsDialog Phase 13 契约） |
| WPF ComboBox 向空集合添加首项会自动选中 index 0 并触发一次 `SelectionChanged`；`EqualizerDialog.OnLoaded` 填充预设下拉必须在 `_suppress` 窗口内进行 | `EqualizerDialog.xaml.cs:OnLoaded` | 否则打开对话框即误 push 一次 Flat/禁用配置，扰动正在播放的 EQ（Phase 14 code review 拦下的 Critical） |
| `EqualizerDialog` 的取消回滚基准 `_initialConfig` 取“打开瞬间的实时 `_playbackService.EqualizerConfig`”，非二次读盘 | `EqualizerDialog.xaml.cs:OnLoaded` / `OnClosed` | 读盘失败会把回滚基准误置为禁用平直 |
| `EqualizerConfig` 是 record，mock 需预设实例 | `PlayerViewModelEqualizerTests` 构造函数 | NSubstitute 对 record 属性默认返回 null；`OnEqualizerEnabledChanged` 读 `_player.EqualizerConfig with {...}` 会 NRE。测试须 `_player.EqualizerConfig.Returns(new EqualizerConfig())`（同 `SpectrumConfig` 契约） |
| PlayerBar EQ 竖直滑块模板必须自定义（`EqualizerDialog` 的 `EqBandSlider`） | `EqualizerDialog.xaml` `EqBandSlider` | Controls.xaml 隐式 Slider 模板横向专用（Height=20 + 填充条 VerticalAlignment=Center），竖直滑块直接用会渲染错位 |

| **Phase 16 新增** | | |
| 应用图标均为 `Themes/Icons.xaml` 矢量 `Geometry`（`Icon.*`），活跃态经 `BoolToAccentBrush` 着 `Path.Stroke` | `Themes/Icons.xaml` + 各 View 的 `Path Style=IconPath` | 不再依赖系统 emoji 字体；改图标须同步 Icons.xaml 键与使用处 |
| ▶ 当前/活跃标记为 `Path`（`PART_Marker` / `PART_SidebarMarker`），code-behind 切 `Visibility` | `PlaylistView.xaml.cs` / `PlaylistsSidebarView.xaml.cs` | 标记元素类型由 TextBlock 改 Path；`FindChildByName` 泛型须为 `Path` |
| VM 层无 emoji/图标类型（`VolumeIcon` 已移除） | `PlayerViewModel` | 音量图标改由 View 层 `BoolToVolumeIconConverter` 提供；勿在 VM 重新引入 emoji/Geometry |

| **Phase 17 新增** | | |
| 自绘标题栏按钮必须 `shell:WindowChrome.IsHitTestVisibleInChrome=True` | `Views/Controls/TitleBar.xaml` | 否则 caption 高度（32px）内的点击被拖动吞掉；新增/修改标题栏按钮必查 |
| 最大化内容边距用「WorkArea 偏移 + WindowResizeBorderThickness」常量式计算，不读窗口实际边界 | `TitleBar.xaml.cs:ApplyWindowState` | 读实际边界会因布局时序让 right/bottom 偏大留空（commit `d86694b`）；边距加在 `Window.Content` 根元素上，还原时清零 |
| ComboBox 深色模板由 ToggleButton 承载点击（`ClickMode=Press`），`ContentPresenter` 设 `IsHitTestVisible=False` | `Themes/Controls.xaml:ComboBoxTemplate` | 点击必须落在 toggle 表面才能开合下拉（commit `dd435bd`） |
| 无边框窗（WindowStyle=None + WindowChrome）必须保留 `CaptionHeight=32` | `MainWindow.xaml` + `SettingsDialog`/`EqualizerDialog`/`PromptDialog` XAML | OS 经 caption 负责拖动/双击最大化/Aero Snap；不要手写 DragMove 或移除 WindowChrome |

| **Bug 修复新增（seek / clear，2026-10-05）** | | |
| 单击进度条跳转必须用 `AddHandler(PreviewMouseLeftButtonDownEvent, ..., handledEventsToo: true)` 代码挂接，不能用 XAML 属性 | `PlayerBar.xaml.cs` 构造函数 | 隐式 Slider 样式开启 `IsMoveToPointEnabled`（Phase 13 `781d35d`）后，Slider 类处理器按轨道时先置 `e.Handled=true`；XAML 附加的实例处理器（不接收已处理事件）被静默跳过 → 单击跳转整体失效。SeekBar 的 Value 是 OneWay，类处理器的本地改值不再回传 VM（音量滑块 TwoWay 故不受影响） |
| `PlaylistViewModel.UnloadCurrentTrack` 必须先检查 `IsActivePlaylist` 再操作 `_player` | `PlaylistViewModel.UnloadCurrentTrack` | `IPlaybackService` 是全局单例：清空/删除非播放中歌单的曲目不得停掉正在播放的歌。`_playToken++` 与 `CurrentIndex=-1` 仍无条件执行 |

| **Phase 18 新增** | | |
| 拖拽双轨白名单：`FilterAudioPaths` 与 `FilterPlaylistPaths` 互不重叠，Drop 处理器必须两个都查 | `Views/Controls/DragDropExtensions` + `PlaylistView` / `PlaylistsSidebarView` 的 DragOver/Drop 处理器 | 只查音频白名单会让 `.m3u/.m3u8/.pls` 被静默丢弃（回到 Phase 18 之前的行为——用户看不到任何反馈）；混合拖入（音频 + 列表文件）时两条管道都要跑 |
| CodePages provider 注册点在 `PlaylistFileEncoding` 的**静态构造函数**里 | `Services/PlaylistFiles/PlaylistFileEncoding.cs`（`Encoding.GetEncoding(936)` 只出现在该类内部） | 静态构造函数保证注册永远早于本类任何解码调用——这是类型不变量，不依赖 App 启动顺序（`RegisterProvider` 幂等，csproj 无需加包：net10.0 框架隐含，显式引用触发 NU1510）。**不要**把 GBK 解码搬到别的类型里——搬走就等于把注册时机重新变成一条口头约定 |
| Import 不抛 / Export 抛 | `IPlaylistFileService` 接口 XML 注释 + `PlaylistFileService` 实现 | `ImportAsync` 吞掉 IO/权限/路径异常返回空结果（由 View 的"没有可导入的条目"报告兜住）；`ExportAsync` 必须让异常冒到 VM 转错误文案。把读侧改成会抛 = 崩溃面扩大；把写侧包成不抛 = 用户丢失导出失败反馈 |
| `#EXTINF` / PLS `Title=` / `Length=` 刻意忽略 | `M3uParser` / `PlsParser` XML 注释 | 标题与时长只信音频文件（ATL 读取结果）；要用列表文件的元数据得先定"两个真相来源谁优先"的规则，本阶段刻意不做 |
| 导入报告文案由**格式化器**组装（`PlaylistImportReportFormatter`，Phase 18 在 `Views/Controls`，Phase 20 搬进 `D-player.Core/ViewModels` 以便两壳复用），导出错误文案是 VM 直传的单一分支例外（设计稿 §7.5） | `D-player.Core/ViewModels/PlaylistImportReportFormatter` + `PlaylistViewModel.ExportPlaylistFileAsync` 的 `$"导出失败：{ex.Message}"` | 四个入口（侧边栏按钮/侧边栏拖拽/工具栏按钮/列表区拖拽）共用同一份文案规则，VM 只返回结构化 `PlaylistImportReport`；导出错误是单分支直传、恰好一个调用方，抽 formatter 属于过度仪式。在 VM 里给导入拼中文句子 = 破坏该纪律 |
| `SourceFolder = null` 是导入歌单的身份标记 | `PlaylistsViewModel.ImportPlaylistFileAsync` 的 `Playlist` seed | 设成非 null 会被当作文件夹绑定歌单，触发 library cache 读写与"刷新文件夹"按钮；导入歌单重启后必须走 `LoadMetadataForNormalPlaylistSync` 普通加载路径 |
| `DropExternalFilesCommand` 的"paths 已过滤"契约新增调用方 | `PlaylistViewModel.ImportPlaylistFileAsync`（原本只有 View 拖拽入口） | VM 信任入参已过滤、不二次过滤；导入路径天然满足（过滤在服务层 `Classify` 完成）。任何新增调用方必须自己保证路径已过滤 |

| **Bug 修复新增（播放链并发 / 曲尾停止，2026-10-06）** | | |
| 播放链生命周期（`LoadAsync` 重建 / `Unload` / `Dispose` / 传输命令）统一受 `_chainGate` 串行化；持锁期间不得 await | `NAudioPlaybackService` 全部取锁点（`LoadAsync` 的 lambda 体、`Play`/`Pause`/`Stop`/`Seek`/`Unload`/`Dispose`） | 缺串行化时，重叠的 `LoadAsync`（或并发 `Unload`）会释放前一次正在使用或构建的链 —— `_reader` / `_volumeProvider` / `_wavePlayer` 三个共享字段被交错读写，异常从链内部抛出并冒进 `async void` 事件处理器（进程崩溃）。**NAudio 3 的 guarded dispose 不使这条失效**（它只保护 NAudio 自己的对象）。commit `cb5ca1a`，Phase 19 迁移到 `WasapiPlayer` 后重述 |
| `OnPlaybackStopped`（播放线程回调）**不得**取 `_chainGate` | `NAudioPlaybackService.OnPlaybackStopped` | 持锁方可能正阻塞在输出类的 `Stop()` / `Dispose()` 上等待播放线程退出（`NAudio.Wasapi 3.1.0` 随包 XML 文档只把 `Dispose` 标为 "Stops playback (blocking) and releases all resources"，`Stop` 仅 "Stop playback and flush buffers" —— 阻塞面按两者计，别只盯着 `Stop()`）；在回调里取同一把锁会立即死锁。该方法对 `_reader` 的读取因此是防御式的（并发拆除中的 reader 会抛）。**失效模式：本仓未配置任何逐条测试超时，这条纪律被改坏时表现为跑测试挂起（stall）而非失败** |
| `TrackEnded` 只代表"曲目自然播完"：任何主动停止（`Stop()` / `DisposePlayback()`）都必须**先**置 `_stopRequested = true`，`Play()` 开新播放会话时清零 | `NAudioPlaybackService._stopRequested`（`OnPlaybackStopped` 命中即提前返回） | 用户停止与自然播完都只表现为一次 `PlaybackStopped`，输出类不告知发起方；仅凭"播放头距 TotalTime ≤ 200ms"无法区分，漏置位 = 曲尾 200ms 内按停止被误判为播完并自动推进下一首。该不确定性不随实现（同步/异步派发、`WasapiOut`/`WasapiPlayer`）变化。**回归证据只有一条落在本标记上**：`NAudioPlaybackServiceStopSemanticsTests.Stop_WhenPlayheadIsAtTrackEnd_DoesNotRaiseTrackEnded`；同类的 `Unload_WhenPlayheadIsAtTrackEnd_…` 走 `DisposePlayback`，而它先解订阅 `PlaybackStopped` 再停止（`NAudioPlaybackService.cs:388-390`），回调根本不触发，故那条用例钉住的是"解订阅早于停止"的顺序，不是本标记 |
| **Phase 19 新增（依赖迁移后的测试栈契约）** | | |
| 测试栈 = xunit.v3（`OutputType=Exe`）+ **单一 runner（MTP）的两个入口**；音频集成测试受并行度约束 | `Tests/D-player.Tests.csproj` + 仓库根 `global.json` + `Tests/Services/NAudioPlaybackService*Tests.cs` | 测试工程是可执行程序，`dotnet run --project Tests/D-player.Tests.csproj` 与 `dotnet test D-player.slnf` 跑的是同一个 Microsoft.Testing.Platform runner（Phase 19 当时写的是 `dotnet test D-player.sln`——解决方案筛选器是 Phase 20 才出现的；门禁现在且只现在 `D-player.slnf`，用 `.sln` 跑测试会把 WinUI 一起拖进 restore/构建）。`dotnet test` 的路由机制是仓库根 `global.json` 的 `{"test":{"runner":"Microsoft.Testing.Platform"}}`（.NET 10 SDK 的原生 opt-in）——**删掉 `global.json` 就破坏 `dotnet test`**；csproj 的 `TestingPlatformDotnetTestSupport` 是 .NET 9 及更早版本的路由开关，在本 SDK 上不参与执行路径，`xunit.runner.visualstudio` / `Microsoft.NET.Test.Sdk` 同样只是为 IDE 测试发现（测试浏览器）的兼容性而保留 —— **该能力在本阶段未验证过**（验收只跑命令行两条路径），也不在跑测试的路径上。两条命令都不能加 `--nologo`（MTP 不识别该参数：一条测试都不跑，摘要却显示 `成功: 0`，退出码 5）。5 条音频测试真实占用 WASAPI 设备：xunit v3 默认并行实测稳定（10 次 MTP + 3 次 `dotnet test` 全绿），故既未加 `[Collection("AudioDevice")]` 也未全局禁用并行——新增音频测试若出现设备争用，按设计稿 §5.3 的顺序收紧（先集合串行，再全局禁用并行） |
| 输出工厂 `StubAudioOutputFactory` 返回 `WasapiPlayer`（共享 + 事件同步 + 100ms，与播放链同参）；仓库 `#pragma warning disable CS0618` 数为 **0** | `Services/StubAudioOutputFactory.cs`、`Services/IAudioOutputFactory.cs` | 该工厂只注册、无消费方；Phase 19 收尾经用户拍板把它从 legacy 的 `WasapiOut` 迁到 `WasapiPlayer`（零契约改动：`WasapiPlayer` 实现 `IWavePlayer`，契约一字未改），顺带退休仓库最后一处过时 API 抑制。详见设计稿文末勘误与 §7 对应条目 |
| **断点续播（2026-10-06）** | | |
| 断点续播的写入时机（暂停/停止/切歌**立即**写、播放中由 `PositionChanged` 驱动但**最多 30 秒一次**、关闭时 `CleanupAsync` 兜底写）与恢复语义（**只就位不出声**；文件缺失/不在歌单则跳过并提示一次） | `PlayerViewModel.SaveLastPlayedState` / `TrySaveLastPlayedState` / `CleanupAsync`、`MainViewModel.RestoreLastPlayedAsync`、`PlaylistsViewModel.FindTrackByPath`、`Views/MainWindow.xaml.cs`（提示） | 每次 `PositionChanged` 无条件写盘 ≈ 30Hz 读改写，会持续打盘并与 `ISettingsPersistence` 的锁内读改写互相排队；恢复路径**不得**调用 `Play()`（启动主动出声违背"本地优先、不打扰"，`▶` 才出声）；无当前曲目时不得写（清空队列/卸载后旧断点仍有效）；恢复成功后必须立即回写一次，否则"启动后马上退出"会把位置退回 0（`Tests/ViewModels/PlaybackResumeTests.cs` 11 条钉住以上各点） |

| **Phase 20 新增（两壳并存后的跨壳契约）** | | |
| Core 必须 WPF-free | `D-player.Core/**/*.cs` 不得出现 `System.Windows.*` / `PresentationFramework` / `ICollectionView` / `CollectionViewSource`（grep 自检见 PROJECT.md §9） | 一旦 Core 引到 WPF 类型，第二壳就被拖回 WPF，Phase 20 的全部收益（一份逻辑两套 UI）作废，且这类污染很容易在"顺手用个 `ICollectionView` 视图"时重新长回来（`SortedView` 就是这么被删掉的） |
| 两壳共用**同一份** VM/Service，不许复制实现 | `D-player.Core` 是唯一的功能真相源；壳里只放 UI 与该壳专属服务 | 任何"壳侧自己实现一遍"的改动（排序、推进算法、持久化、双击语义）都会在两壳之间造出静默行为漂移，而本项目现在只有 WPF 侧有真机回归习惯 |
| 同一手势必须走 Core 里**同一个**公开入口 | 双击：`Views/Controls/PlaylistView.xaml.cs` 与 `D-player.WinUI/MainWindow.xaml.cs` 都调 `PlaylistsViewModel.HandleDoubleClickPlay(target, index)` | 实施中曾在 Core 新增 `PlaylistViewModel.PlayIndexAsync(int)` 让 WinUI 直进 `PlayTrackAtAsync`，绕开 `PlayTrackAt` 开头的 `_shuffleHistory.Clear()` → 随机模式下两壳双击语义分叉（WinUI 持续蚕食未播池，`RepeatMode.Off` 时可返回 −1 而 WPF 继续播）。该 API 已删除。**并行 API 本身就是漂移源**；由 `PlaylistsViewModelTests` 的越界事实 + "共享入口派出了播放调用"的事实（断言的是 `_player.Received(1).LoadAsync(...)` / `Play()` 这类**派发**，不是真的听见声音——是否出声仍属需用户真机确认，见对比材料）+ "两壳同一入口"这条纪律共同守护 |
| 用户数据目录由壳注入 | `AddDPlayerCore(IConfiguration, DPlayerDataPaths)` + 各壳的 `FolderName`（WPF `D-player` / WinUI `D-player-winui`） | 三个持久化服务的 `SemaphoreSlim` 只在单进程内生效，跨进程无协调：共用目录 → 两壳互相覆盖对方的 queue/settings。`LegacyDataMigration` 只由 WPF 壳调用：跑两遍会把同一份旧数据搬进两个目录，此后两边互相看不见（回归由 `Tests/Configuration/DPlayerDataPathsTests.cs` 的壳路径钉桩托住，**但那只钉 Core 侧的组合逻辑，钉不住壳传错名字**） |
| 每壳自己注册 `IFileDialogService` | WPF `App.xaml.cs` → `Win32FileDialogService`；WinUI `D-player.WinUI/App.xaml.cs` → `WinUiFileDialogService`（切片期空实现） | Core 的 `PlaylistViewModel` 工厂在**被调用时**才 `GetRequiredService<IFileDialogService>()`：漏注册时 `GetRequiredService<MainViewModel>()` 仍成功，**第一次构造歌单才炸**。`Tests/Extensions/AddDPlayerCoreTests.cs` 钉住 Core 图（含"真的构造一个歌单 VM"这一步），但按构造它钉不到壳侧那条注册 —— 缺这条防护的原因是 WinUI 不在门禁里（见下） |
| 门禁走 `D-player.slnf`（两条，每次改动必跑）；WinUI 不进门禁，由一条**有触发条件的壳侧检查**补位 | 门禁一 `dotnet build D-player.slnf -c Debug --nologo -v q` + 门禁二 `dotnet test D-player.slnf -c Debug -v q`；壳侧检查 `dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q`，**触发条件 = `D-player.Core` 的公开面一变就跑**（构造参数 / `IFileDialogService` 新成员 / `HandleDoubleClickPlay` 元数 / `ViewedPlaylist` setter 等：这类改动下两条门禁全绿而壳已编译不过），改到 WinUI 自身时同样跑；一键跑 `powershell -File tools/verify-gates.ps1 -Fast|-Full` | 隔离的是 Windows App SDK 的成本（一次 restore 9 个子包、首次约 8.1 分钟、self-contained 输出目录巨大）。**代价必须一起记**：WinUI 的 XAML 编译、`NavigationView` 左栏逻辑、`AppWindow.Closing` 落盘都没有任何自动回归，只有"手跑的单壳构建 + 真机手测"两道保护。**词汇纪律（本波立的）**：把手跑命令叫成"门禁"就等于宣称它会自动跑——此前的"门禁三件套（含单壳构建）"与"门禁三"两个说法已作废，因为它们把一条需要人记得跑的检查写成了必然发生的事实，而 README 当时的触发条件（"整解构建只在改到 WinUI 时跑"）方向也是反的：真正的暴露面是 **Core 公开面变化**。被排除的代价不贵——增量单壳构建实测 9.31 秒（工作实例：A5 的"死 using"整理，`.slnf` 门禁对 WinUI 完全无感） |
| 仓库根级新工程必须进 `D-player.csproj` 的 glob 排除集 | `Tests/**`、`D-player.Core/**`、`D-player.WinUI/**` 三组 `Compile/Page/ApplicationDefinition/Resource/None/EmbeddedResource Remove` | WPF SDK 生成的 `*_wpftmp.csproj` 会从仓库根重新 glob；漏掉就是重复编译（AssemblyInfo 重复 / xunit 引用缺失）或 `MC3074`/`CS0234`（WPF 侧没有 `Microsoft.UI.Xaml`）。这一条在计划里原本不存在，是 Task 1 评审拦下来的（计划勘误见实现计划 Task 1 Step 3 的原地标注） |
| WinUI 的 Mica 只在**根背景透明**时可见 | `D-player.WinUI/MainWindow.xaml` 根 `Grid Background="Transparent"` + `MainWindow.xaml.cs` 的 `SystemBackdrop = new MicaBackdrop()`；三行像素对照写在该文件顶部注释 | 往根 Grid 或任何铺满容器加不透明背景刷 = 材质"看起来消失"（肉眼与截图都像没挂上）。反向陷阱：`DWMWA_SYSTEMBACKDROP_TYPE` 对组合器挂载的 backdrop **不是判据**（本项目实测恒为 0），拿它做断言曾经把已生效的 Mica 误判成"未挂载" |
| 每壳各自负责关闭落盘，且**不得**同步 Dispose 容器 | WPF `Views/MainWindow.xaml.cs:Window_Closing`（cancel-and-close，Phase 4）；WinUI `D-player.WinUI/MainWindow.xaml.cs:AppWindow_Closing` → `await MainViewModel.CleanupAsync()` → `Close()` | 少了这一步，最终断点位置只能靠 30 秒节流或"先暂停"才落盘，WASAPI 设备也不释放。同步 `Dispose(ServiceProvider)` 会抛（`MainViewModel` 只实现 `IAsyncDisposable`）—— WPF 壳 `App.OnExit` 那条是**已知既有缺陷**（数据已落盘，表现为退出码非 0），Phase 20 刻意没有把它复制进第二壳；两处该一起修，属 Phase 20 之后的独立决策 |

| **Phase 21 新增（WinUI 视觉层契约）** | | |
| 令牌三档时长 + 颜色只走主题资源 + 六态齐备 | `D-player.WinUI/Theme/Tokens.xaml`（间距 4/8/12/16/24、圆角 4/8/12、字号 12/13.5/15/20、结构尺寸）+ `Theme/Motion.cs`（`MotionTokens.Fast`=100ms / `Normal`=150ms / `Slow`=200ms + `StandardEasing`）+ `Theme/Styles.xaml`（六态容器模板：default/hover/pressed/selected/focus/disabled）；所有颜色引用 `{ThemeResource …}`，壳内无写死 `#RRGGBB` | 新增第四个时长或 >250ms 动画违反令牌纪律；壳内出现硬编码色值违反颜色契约 |
| 折叠与阈值契约 | NavRail 200↔48px、InfoPanel 260↔0px；`<960px` 窗口宽度自动收起 InfoPanel（8px 迟滞：收起阈值 960px、展开阈值 968px）；手动覆盖优先，直到窗口宽度穿越回 threshold+8px 才重新接管 | 无迟滞会在阈值边界抖动；手动覆盖被自动逻辑覆盖会让用户失去控制感 |
| 无跨层 `x:Bind` 链 | `{x:Bind Playlists.ViewedPlaylist.Queue}` 这类两层以上链式绑定会让 XamlCompiler 报 WMC9999 崩溃；只绑一层或在 code-behind 赋值 | 跨层 `x:Bind` 链触发 XamlCompiler 内部错误，构建直接失败 |
| 动效纪律：单一驱动源 + 防叠加 | 所有动画使用 `MotionTokens.Fast/Normal/Slow`；无循环；无 >250ms；动画启动前先 `_storyboard?.Stop()` + `Completed` 回调用 `ReferenceEquals` 守卫防止旧回调干扰 | 不做防叠加会导致连续快速操作（如折叠 5 次）动画堆积、抽搐、终态不对 |

**建议：** 这些不需要立即修，但**每次改相关代码时去注释里复习一遍**。

> **Phase 15 审计核对（M7）：** §5 全部契约与代码一致，无新增未登记契约。
>
> **Phase 20 审计复跑（最终修复波把 scan root 补到四个）：** `tools/coupling-audit/Invoke-CouplingAudit.ps1` 现在同时扫 `D-player.Core/<层>`、`D-player.WinUI/<层>`、`D-player/<层>` 与仓库根（**四个 scan root**；第四个是本波补的——Phase 20 把 WPF 壳的 `Services` 挪进仓库根下的 `D-player/` 之后，`D-player\Services\Win32FileDialogService.cs` 静默掉出了扫描集，而它在 Phase 20 之前是被收集的），DI 注册表也从 Core 下解析。复跑：`0 cycles` / **`0 violations`** / 无新增未登记契约。加第四个根**没有改动任何读数**：实测收文件数 73 → 74（增量正是那一个文件），完整输出逐字相同——那个文件里没有 `using DPlayer.*`，命名空间节点 `DPlayer.Services` 本就在集合里，M6 按规则排除接口实现类，40 行也够不到 M5 的 600 阈值。所以这条改的是**口径**：补的是一个收集回归，不是新发现的耦合。
>
> **读数纪律（比上面那个数字重要）**：`WinUI->Services : 1` **不是"新壳只带来一条层边"的测量结果，而是收集规则的产物**。`DPlayer.WinUI` 命名空间下的两个根级文件（`D-player.WinUI/App.xaml.cs`、`D-player.WinUI/MainWindow.xaml.cs`）从来不在扫描集里，而它们实际 import 的是 `DPlayer.Models` / `DPlayer.ViewModels` / `DPlayer.Configuration` / `DPlayer.Extensions` / `DPlayer.Services`——收进来至少要再多 4 条方向合法的层边。于是**真实层边数未知且高于 1**，报出的 1 只是下界；同理 `DPlayer.Services` 的 fan-in "6 → 7" 也只是"被收集的那部分"的读数。**盲区两面都得写明**：仓库根自己的根级 `App.xaml.cs`（命名空间 `DPlayer`，正是审计里的 `Root` 层）**是**被收集的（`Root->Configuration` / `Root->Extensions` / `Root->Services` / `Root->ViewModels` 四条层边就来自它），而 WinUI 壳根级那两个文件**不是**——差别不在"要不要收根级文件"，而在四个 scan root 里只有 `$RepoRoot` 这一个额外收了根级 `*.cs`。补齐它要改的是收集规则本身（M1/M3/M5 的口径会一起变，Phase 15 的基线要重算），属独立决策，不在本波。M6 仍只报 `IAudioDeviceManager`、`IAudioOutputFactory` 两个已登记的预留 Stub（§4）。M5 的两个 >600 LOC 文件现在显示为 `D-player.Core\ViewModels\PlaylistViewModel.cs`(721) 与 `…PlaylistsViewModel.cs`(661 —— 本波给 `HandleDoubleClickPlay` 的 XML 注释加了三行，commit `398de93`) —— 与 Phase 15 D5 观察项同源（单职责 cohesive，不拆分），搬家只改了路径。**审计脚本在 Phase 20 的搬迁后曾一度跑不出结果**（M2 的 BFS 撞到"被引用但未被扫描到"的命名空间 → `ContainsKey(null)` 抛；M6 读的注册表文件也已不存在），这是 P-5 裁定的直接后果，不是新发现的耦合。

---

## 6. 启动检查清单（Phase 15 完成）

> Phase 15（耦合健康度审计）已完成。所有结构性改造与债务偿还已清零；Phase 11/12/13/14 均为功能增量，未触碰核心架构（Phase 13/14 同构：透明 ISampleProvider 中间件 + 仅给 IPlaybackService 加 1 属性 + 0 新 DI 服务 + 0 新 ViewModel）。Phase 15 以脚本度量 + 人工裁决确认耦合低/健康、无需解耦。Phase 16/17 为纯表现层重构（图标矢量化 + 深色定制），0 新依赖、0 新债。

1. ✅ **VM 拆分**（Phase 3 完成，commit `54edf9a`）—— MainViewModel 643→44 行 Strict Facade；PlayerVM + PlaylistVM 互不持引用
2. ✅ **`PlayerBar` / `PlaylistView` 去硬转型**（Phase 3 完成）—— DataContext 切到子 VM；跨域命令用 `RelativeSource AncestorType=Window`
3. ✅ **抽 `ITrackMetadataReader`**（Phase 3 完成，commit `c9cd1bd`）—— `AtlMetadataReader` 封装 z440.atl.core
4. ✅ **解决 settings 合并纪律**（Phase 3 完成，commit `fadae44`）—— `UpdateAsync(Func<>)` 把读-改-写封进锁内
5. ✅ **队列持久化**（Phase 4 完成）—— `%LocalAppData%\D-player\queue.json` 与 settings.json 分离
6. ✅ **多命名播放列表**（Phase 6 完成）—— `Playlist` record + `IPlaylistService` + `PlaylistsViewModel` 容器 + sidebar UI + v1→v2 迁移
7. ✅ **拖拽支持**（Phase 5 完成）—— 外部文件拖入入队 + 队列内拖拽重排（含多选）+ 视觉反馈；同步偿还旧债 #5
8. ✅ **偿还债 #1 (`BitmapImage`)** —— Phase 7 已完成：`AlbumArtImage` → `AlbumArtBytes` (byte[])，VM 层无 WPF 类型
9. ✅ **图标矢量化**（Phase 16 完成）—— `Themes/Icons.xaml` 矢量图标集 + 转换器返回 Geometry + ▶ 标记改 Path；VM 层移除 `VolumeIcon`
10. ✅ **UI 深度深色定制**（Phase 17 完成）—— 无边框 WindowChrome + 自绘 TitleBar（主窗三键 / 对话框仅关闭键）+ ComboBox/CheckBox/ScrollBar/ToolTip/ContextMenu 深色隐式样式
11. ✅ **播放列表文件导入导出**（Phase 18 完成）—— `Services/PlaylistFiles` 门面模块（`IPlaylistFileService`：编码探测 → 解析 → 归一化 → 过滤计数，读侧不抛/写侧抛）+ 双入口分层（侧边栏新建歌单 / 工具栏追加当前）+ 拖拽按落点分流 + `.m3u8` 导出（绝对路径 + `#EXTINF`）

**Phase 11+ 候选范围：**
- [x] PlayerViewModel 单元测试（Phase 8 完成，15 个测试）
- [x] PlaylistViewModel 单元测试（Phase 8 完成，16 个测试）
- [x] PlaylistsViewModel 单元测试（Phase 8 完成，17 个测试）
- [x] sidebar 拖拽重排歌单顺序（Phase 9 完成）
- [x] 文件夹绑定歌单（Phase 10 完成：扫描 + 增量同步 + 元数据缓存）
- [x] 设置对话框（Phase 11）—— 模态 Window，PlayerBar ⚙ 按钮 + Ctrl+, 快捷键；不加 SettingsViewModel（YAGNI）
- [x] UI 界面重构（Phase 12）—— PlayerBar 移底 + 圆形播放键 + PlaylistView 优化 + Sidebar 图标 + 色板微调
- [x] Phase 12 持续优化 —— 全局 Shuffle/Repeat + TrackInfoView + #列 + 表头排序 + 导入文件夹到当前歌单 + 多项 UI 修复
- [x] 音频可视化（Phase 13 完成）—— SampleAggregator FFT（8192 点 + 汉宁窗 + 50% 重叠 + 对数分组 20Hz–16kHz + RMS/gamma）+ SpectrumView 32 柱 60fps + 4 色主题 + 灵敏度/平滑度/启用配置 + settings.json 持久化 + TrackInfoView 底部集成
- [x] 均衡器（Phase 14 完成）—— EqualizerSampleProvider 10 段图形 EQ（ISO 倍频程 31Hz–16kHz ±12dB 峰值滤波 Q≈1.1 + preamp，插在 SampleAggregator 之前→频谱反映 EQ 后信号）+ 9 个内置预设 + Custom + 实时就地 SetPeakingEq 重算（防爆音）+ EqualizerDialog 竖直滑块对话框 + PlayerBar 🎚 启用态高亮按钮 + settings.json 持久化
- [x] 耦合健康度审计（Phase 15 完成）—— M1–M6 脚本度量 + M7/D1–D5 裁决；结论：耦合低、无需解耦（D1 观察项、D5 观察项）
- [x] 图标矢量化（Phase 16 完成）—— Icons.xaml 矢量 Geometry 集 + IconPath 样式；转换器 string→Geometry；▶ 标记与 sidebar 活跃标记改 Path（Fill=AccentPrimary 实心）
- [x] UI 深度深色定制（Phase 17 完成）—— 自定义无边框标题栏（TitleBar + WindowChrome，含最大化常量边距）+ ComboBox/CheckBox/ScrollBar/ToolTip/ContextMenu/MenuItem 深色模板
- [x] 播放列表文件导入导出（Phase 18 完成）—— 导入 M3U/M3U8/PLS（相对路径解析 + UTF-8/UTF-16(BOM)/GBK 编码探测 + URL/后缀/存在性过滤计数报告）+ 导出 M3U8（绝对路径 + `#EXTINF`，UTF-8 无 BOM + CRLF）；无新 NuGet 包（CodePages 在 net10.0 框架隐含）
- [x] WinUI 3 第二 UI 壳 + 决策门材料（Phase 20 **已交付**）—— `D-player.Core` 抽取（WPF-free）+ 数据目录由壳注入 + 门禁改走 `D-player.slnf` + `D-player.WinUI` 第一条纵向切片（真机起窗、Mica、双击走共享入口、关闭落盘）。**0 新增架构债，新增 9 条跨壳契约（§5 末尾的 9 行）**；"续投还是停止"的决策门**已拍板：续投 WinUI（2026-10-07）**
- [x] WinUI 壳视觉与交互打磨（Phase 21 **已交付**）—— 三栏 IA（NavRail/TrackList/InfoPanel/PlayerBar）+ 令牌层（间距/圆角/字号/时长）+ 六态样式 + 键盘/无障碍 + 9 项动效；新增 4 条视觉层契约（§5 末尾）+ 6 条 ❌ 纪律（§7）。**23 项验收全部通过、验收闭合（2026-10-08，逐项结果见 `docs/PHASE21-ACCEPTANCE.md`）；起窗崩溃（exit 0xC0000005）已修复并验证（`bba9ad4`）**

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
- ❌ **为 Phase 11 设置对话框加 SettingsViewModel** —— 仅 DefaultVolume 可编辑，对话框直接读写 ISettingsPersistence；Phase 12 音频设置有复杂交互时再加（Phase 13 频谱设置已加入对话框，仍直接读写 + 同步 PlayerViewModel，未引入 SettingsViewModel）
- ❌ **在 `PlayerViewModel.HandleSpectrumData` 里重做 FFT→bar 映射**（Phase 13）—— `SampleAggregator` 已输出 32 bars；VM 只做灵敏度增益 + 指数平滑。二次映射会 double-log 压缩频谱（commit `51a04c6`）
- ❌ **让 `SampleAggregator.SpectrumDataReady` 直接更新 UI 绑定属性**（Phase 13）—— 该事件在音频线程触发，必须经 `NAudioPlaybackService` 的 `_syncContext.Post` 封送到 UI 线程
- ❌ **在 `SettingsDialog.Save_Click` 用 `ConfigureAwait(false)`**（Phase 13）—— await 后要直接写 `PlayerViewModel` 属性同步频谱设置，离开 UI 线程会异常（commit `a76d92a`）
- ❌ **忘记在 `SpectrumView.Unloaded` 解绑 `CompositionTarget.Rendering`**（Phase 13）—— 全局渲染事件会持有控件引用导致内存泄漏
- ❌ **让 `EqualizerSampleProvider.Update` 重建 `BiQuadFilter`（而非 `SetPeakingEq` 就地改）**（Phase 14）—— 重建会清空 x1/x2/y1/y2 延迟线，拖动滑块时爆音
- ❌ **在 `NAudioPlaybackService` 里绕过 `_chainGate` 直接碰 `_wavePlayer` / `_reader` / 播放链字段**（新加传输命令也不例外）—— 会退回到"释放另一个正在使用或正在构建的链 → 异常冒进 async void"的崩溃面（与 §5 的播放链闸门条目同口径：崩溃面在我们自己的三个共享字段上，不在输出类内部）
- ❌ **在输出类的 `PlaybackStopped` 回调（播放线程）里取 `_chainGate`**（Phase 19 起输出类为 `WasapiPlayer`）—— 持锁方可能正阻塞在输出类的 `Stop()` / `Dispose()` 上等待播放线程退出（`Dispose` 被随包 XML 文档明确标为 blocking；口径与 §5 播放链闸门条目一致），取锁即死锁
- ❌ **新增任何"主动停止播放"的路径时忘记置 `_stopRequested`** —— 曲尾 200ms 内的用户停止会被误报为自然播完，停止后自动推进下一首
- ❌ **给 `IPlaybackService` 加成员却漏改 `PlaylistsViewModel` 内的手写 `NullPlaybackService`**（Phase 14）—— 它是该接口的第二个生产实现者，漏改会 CS0535 编译失败
- ❌ **在 `EqualizerDialog.OnLoaded` 未抑制就填充预设下拉**（Phase 14）—— WPF ComboBox 向空集合添加首项会自动选中 index 0 并触发 `SelectionChanged`，打开即误 push 一次 Flat/禁用配置扰动播放中的 EQ
- ❌ **重新引入 emoji/字形图标**（Phase 16）—— 全应用统一走 `Themes/Icons.xaml` 矢量 `Path`；VM 层不得再出现图标字符串/Geometry（`VolumeIcon` 已移除）
- ❌ **从 `App.xaml` 移除 `Icons.xaml` 合并、或加图标不同步字典**（Phase 16）—— 转换器经 `Application.Current.FindResource` 取 Geometry，缺失会运行时抛异常
- ❌ **在 WindowStyle=None 窗口上漏配 WindowChrome / 漏给标题栏按钮 `IsHitTestVisibleInChrome=True`**（Phase 17）—— 拖动/双击最大化/点击行为损坏
- ❌ **把最大化边距改回"读窗口实际边界"实现**（Phase 17）—— 布局时序导致 right/bottom 留空（commit `d86694b`；用 WorkArea 偏移 + 隐藏边框厚度的常量式边距）
- ❌ **在 SeekBar 上用 XAML 属性挂接 `PreviewMouseLeftButtonDown`**（2026-10-05 修复）—— `IsMoveToPointEnabled` 的 Slider 类处理器会先消费事件，XAML 实例处理器被静默跳过导致单击跳转失效；必须 `handledEventsToo: true`（见 §5）
- ❌ **让 `UnloadCurrentTrack`（经 `ClearQueue`/`RemoveTrack`）无条件操作全局 `IPlaybackService`**（2026-10-05 修复）—— 只有 `IsActivePlaylist` 歌单才有权 `Unload()`，否则清空另一歌单会误停当前播放
- ❌ **为导入给 `PlaylistViewModel` 注入 `ILibraryScannerService`**（Phase 18）—— 追加路径复用既有 `DropExternalFiles`（服务层已过滤 + 逐个读元数据入队）；引入扫描服务会让歌单级 VM 背上容器级依赖，违反双 VM 互不持引用的既有拓扑
- ❌ **把导出入口放到侧边栏**（Phase 18）—— 侧边栏按钮作用于"选中项"，导出语义是"当前查看的歌单"（`ViewedPlaylist`），两个指针在键盘导航下可能不同步，放侧边栏会产生"到底导出哪个"的歧义
- ❌ **重新引入 `#pragma warning disable CS0618`（或任何抑制手段）来压 NAudio 的过时警告** —— 依赖已全部迁到 3.x 新 API，仓库当前 pragma 数为 **0**（`StubAudioOutputFactory` 已于 2026-10-06 收尾迁到 `WasapiPlayer`，经用户拍板；此前那条"不要顺手迁"的条目随迁移完成作废）。要压警告说明又用回了 legacy 类（`WasapiOut` 等），正确做法是改用 `WasapiPlayerBuilder → WasapiPlayer`（见 §5 输出工厂条目）
- ❌ **在 `PositionChanged` 回写里无条件写盘，或在断点恢复路径里调用 `Play()`**（2026-10-06）—— 前者每帧一次"读盘→改→写盘"（≈30Hz）持续打盘并与 `ISettingsPersistence` 的锁竞争；后者让应用**启动即出声**，违背断点续播"只就位、`▶` 才出声"的既定语义（契约见 §5 断点续播条目）
- ❌ **在 `D-player.Core` 里引用 WPF（`System.Windows.*` / `PresentationFramework` / `ICollectionView` / `CollectionViewSource`）**（Phase 20）—— Core 一旦被 UI 类型污染，"一份逻辑两套壳"就只剩一份壳能用；`SortedView` 之所以删掉而不是加 `#if`，就是为了不给这条留下缺口。要视图请回到壳里做
- ❌ **给 WinUI 壳做 UmaPlayer → D-player 的数据迁移**（Phase 20）—— `LegacyDataMigration` 只属 WPF 壳；跑两遍会把同一份旧数据搬进 `D-player` 与 `D-player-winui` 两个目录，此后两边互相看不见，用户以为"数据丢了"
- ❌ **让两个壳共用同一个数据目录**（Phase 20）—— 三个持久化服务的锁都是进程内的，跨进程没有任何协调；共用目录 = 后关的那个覆盖前一个的队列与音量快照
- ❌ **把 `D-player.WinUI` 加进 `D-player.slnf`**（Phase 20 的隔离决策）—— 那会让每次改共享层都付一次 Windows App SDK 的构建/restore 成本（首 restore 9 个子包、约 8.1 分钟、自包含输出巨大）。要纳入必须先单独拍板并接受成本；当前状态是"WinUI 无自动回归"，这条已知代价记在 §5 与对比材料里，不要靠"临时加进去又拿出来"来回摇摆
- ❌ **为壳新增一个"看起来一样"的 Core 并行 API**（Phase 20）—— `PlayIndexAsync` 的教训：它绕开 `PlayTrackAt` 里的 `_shuffleHistory.Clear()`，于是两壳同一个手势语义分叉。跨壳复用请走已有公开入口（如 `PlaylistsViewModel.HandleDoubleClickPlay`）；确实缺入口时，先让**两个壳都走新入口**再删旧的，别留两份
- ❌ **在壳的关闭路径上同步 `Dispose(ServiceProvider)`**（Phase 20）—— `MainViewModel` 只实现 `IAsyncDisposable`，同步 Dispose 抛 `InvalidOperationException`；WPF `App.OnExit` 那条是既有缺陷（要修请单独决策并**两壳一起**改，别把它复制进新壳）
- ❌ **用 `DWMWA_SYSTEMBACKDROP_TYPE` 断言 WinUI 的 Mica 是否生效**（Phase 20）—— 组合器挂载的 backdrop 不写这个 DWM 属性（实测恒为 0），照它判定会把已生效的材质判成"未挂载"。有效判据是"透明表面后的像素是否随窗外内容变化"
- ❌ **往 WinUI 根 Grid 或铺满容器上加不透明背景刷**（Phase 20）—— Mica 会整片被盖住，视觉退化成实色深色；这条约定和像素对照就在 `MainWindow.xaml` 顶部注释里
- ❌ **新增仓库根级工程却不补 `D-player.csproj` 的 glob 排除集**（Phase 20）—— 门禁会以重复类型或 `MC3074`/`CS0234` 炸掉；排除集与 `.slnf` 的隔离是一对，改一个要看另一个
- ❌ **在 WinUI 壳内写死十六进制色值 `#RRGGBB`**（Phase 21）—— 颜色必须走 `{ThemeResource …}` 引用 Fluent 主题资源；写死色值会让深色/浅色主题切换失效，且违反令牌化纪律（B5 清单项）
- ❌ **引入第四个动效时长或 >250ms 动画**（Phase 21）—— 令牌层只允许三档（Fast 100ms / Normal 150ms / Slow 200ms）；新增时长或超长动画会让动效节奏失控，违反"丰富但不烦人"的设计目标
- ❌ **在 WinUI 侧使用跨层 `x:Bind` 链**（Phase 21）—— `{x:Bind Playlists.ViewedPlaylist.Queue}` 这类两层以上的链式绑定会让 XamlCompiler 报 WMC9999 崩溃；只绑一层或在 code-behind 赋值
- ❌ **把折叠状态写进 `settings.json`**（Phase 21）—— 折叠/展开的默认态是展开（左栏/右栏），不持久化用户手动折叠状态；持久化需要新增 `settings.json` 字段，属功能面契约，本阶段（纯视觉打磨）刻意不做
- ❌ **在 WinUI 侧新增功能项**（Phase 21）—— 本阶段只做视觉与交互打磨，不加任何功能（排序、拖拽、频谱、EQ、设置、导入导出等全部留给后续功能阶段）
- ❌ **构造函数体不得解引用由对象初始化器赋值的成员**（Phase 21 起窗崩溃 `7324677` 引入、`bba9ad4` 修复）—— 对象初始化器在 ctor 体**返回之后**才执行：`new PlayerBar(_vm.Player) { Playlists = _vm.Playlists }` 的 ctor 体末尾已在订阅 `Playlists!.PropertyChanged`，此刻仍为 null → 起窗即 NRE（exit 0xC0000005）；`required` 只约束调用点、管不了 ctor 体内的时序，`!` 还压掉了本会报警的编译器警告。绑定源一律走构造参数，并在 `InitializeComponent()` 之前赋值（NavRail/TrackList/InfoPanel/PlayerBar 同形）

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
  - [`docs/superpowers/specs/2026-09-12-d-player-phase15-coupling-audit-design.md`](./superpowers/specs/2026-09-12-d-player-phase15-coupling-audit-design.md) — Phase 15 设计规格
  - [`docs/superpowers/specs/2026-09-12-d-player-phase15-coupling-audit-report.md`](./superpowers/specs/2026-09-12-d-player-phase15-coupling-audit-report.md) — Phase 15 审计报告
  - [`docs/superpowers/specs/2026-09-13-d-player-phase16-icon-refactor-design.md`](./superpowers/specs/2026-09-13-d-player-phase16-icon-refactor-design.md) — Phase 16 图标矢量化设计规格
  - [`docs/superpowers/specs/2026-09-13-d-player-phase17-ui-dark-theming-design.md`](./superpowers/specs/2026-09-13-d-player-phase17-ui-dark-theming-design.md) — Phase 17 UI 深度深色定制设计规格
  - [`docs/superpowers/specs/2026-10-05-d-player-phase18-playlist-file-io-design.md`](./superpowers/specs/2026-10-05-d-player-phase18-playlist-file-io-design.md) — Phase 18 播放列表文件导入导出设计规格
- 原始实现计划：
  - [`docs/superpowers/plans/2026-04-24-uma-player-implementation.md`](./superpowers/plans/2026-04-24-uma-player-implementation.md) — Phase 1
  - [`docs/superpowers/plans/2026-06-06-uma-player-playlist-implementation.md`](./superpowers/plans/2026-06-06-uma-player-playlist-implementation.md) — Phase 2
  - [`docs/superpowers/plans/2026-06-07-uma-player-phase3-implementation.md`](./superpowers/plans/2026-06-07-uma-player-phase3-implementation.md) — Phase 3
  - [`docs/superpowers/plans/2026-06-12-uma-player-phase4-queue-persistence-implementation.md`](./superpowers/plans/2026-06-12-uma-player-phase4-queue-persistence-implementation.md) — Phase 4
  - [`docs/superpowers/plans/2026-06-12-uma-player-phase5-drag-drop-implementation.md`](./superpowers/plans/2026-06-12-uma-player-phase5-drag-drop-implementation.md) — Phase 5
  - [`docs/superpowers/plans/2026-06-13-uma-player-phase6-named-playlists.md`](./superpowers/plans/2026-06-13-uma-player-phase6-named-playlists.md) — Phase 6
  - [`docs/superpowers/plans/2026-09-12-d-player-phase15-coupling-audit-implementation.md`](./superpowers/plans/2026-09-12-d-player-phase15-coupling-audit-implementation.md) — Phase 15 实现计划
  - [`docs/superpowers/plans/2026-09-13-d-player-phase16-icon-refactor-implementation.md`](./superpowers/plans/2026-09-13-d-player-phase16-icon-refactor-implementation.md) — Phase 16 实现计划
  - [`docs/superpowers/plans/2026-09-13-d-player-phase17-ui-dark-theming-implementation.md`](./superpowers/plans/2026-09-13-d-player-phase17-ui-dark-theming-implementation.md) — Phase 17 实现计划
  - [`docs/superpowers/plans/2026-10-05-d-player-phase18-playlist-file-io-implementation.md`](./superpowers/plans/2026-10-05-d-player-phase18-playlist-file-io-implementation.md) — Phase 18 实现计划
