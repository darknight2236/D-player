# D-player

> 一个轻量级、本地优先的 Windows 音乐播放器（.NET 10 + NAudio）。
> 共享层 `D-player.Core`（WPF-free）之上跑两套 UI 壳：**WPF 壳**（完整功能，日常使用的那一个）与 **WinUI 3 壳**（Phase 21 视觉打磨 + Phase 22 功能补齐：异步文件对话框 + 真实 picker、导入/导出、歌单增删改与 ▶ 标记、上一首/下一首/随机/循环/音量/静音、基础设置、窗口几何与本地日志；Phase 23 已实现 32 柱频谱与四项设置、10 段 EQ（含 Pre、预设、预览/取消/保存），**实现完成 / 验收待补（2026-10-28）**，见 [`docs/PHASE23-ACCEPTANCE.md`](docs/PHASE23-ACCEPTANCE.md)；仍缺拖拽 / 排序 / 文件夹绑定管理（TrackList ▶ 与 InfoPanel 采样率已于 2026-10-24 补齐）。**Phase 22 历史验收已完成**（验收 23 项全部 PASS（含用户 2026-10-24 确认的 C4/D3 与 D1 轮转），步骤见 [`docs/PHASE22-ACCEPTANCE.md`](docs/PHASE22-ACCEPTANCE.md)）；Phase 20 的去留结论与 Phase 21/22 的历史证据见 [`docs/PHASE20-COMPARISON.md`](docs/PHASE20-COMPARISON.md)）。
> 灵感来源于 foobar2000。原名 UmaPlayer。

---

## 特性

> 下列是 **WPF 壳**的完整能力。WinUI 3 壳 Phase 22 已具备导入/导出、歌单管理（新建/重命名/删除 + ▶ 标记）、完整播放控制（上一首/下一首/随机/循环/音量/静音）、基础设置、窗口几何与日志；Phase 23 频谱与 EQ 实现已有但未全验收；仍缺**拖拽 / 表头排序 / 文件夹绑定歌单的管理与刷新**；TrackList 的 ▶ 当前曲标记与 InfoPanel 采样率已在 2026-10-24 补齐，逐项列在 [`docs/PHASE20-COMPARISON.md`](docs/PHASE20-COMPARISON.md) 第 3 节（第 2 节保留 Phase 20 的历史快照）。

- **播放**：播放 / 暂停 / 上一首 / 下一首 / 随机 / 循环；拖拽 + 单击跳转进度条（≈30 Hz 节流刷新）；音量滑块 + 一键静音。
- **格式**：MP3 / WMA / FLAC / AAC / WAV（Windows Media Foundation 原生解码）。
- **元数据**：标题 / 艺术家 / 专辑 / 流派 / 年份 / 采样率 / 曲目号 / 内嵌封面（z440.atl.core）。
- **队列与歌单**：内存播放队列（多选入队、删除、清空、自动推进、表头排序）；多命名歌单（Spotify 式「查看 vs 播放」双指针）；文件夹绑定歌单（递归扫描 + 后台增量同步 + 元数据缓存）。
- **播放列表文件**：导入 .m3u / .m3u8 / .pls（相对路径按列表所在目录解析；UTF-8 / UTF-16(BOM) / GBK 编码探测，中文路径不乱码；URL/后缀/存在性严格过滤 + 跳过计数报告）；导出 .m3u8（绝对路径 + `#EXTINF`）。侧边栏按钮/拖到侧边栏 = 新建歌单，工具栏"导入列表"/拖到列表区 = 追加当前歌单。
- **拖拽**：外部音频文件拖入入队；队列内单/多选拖拽重排（插入线 Adorner + 边框高亮）。
- **持久化**：窗口几何、音量、队列、歌单、频谱与 EQ 设置均落盘，重启恢复。
- **音频可视化**：32 条垂直频谱柱（8192 点 FFT + 汉宁窗 + 50% 重叠 + 对数分组 20 Hz–16 kHz + RMS/gamma）；4 种颜色主题；灵敏度/平滑度可调。
- **均衡器**：10 段图形 EQ（ISO 倍频程 31 Hz–16 kHz，±12 dB 峰值滤波 + preamp）；9 个内置预设（Flat/Rock/Pop/Jazz/Classical/Dance/Bass Boost/Treble Boost/Vocal）+ 手动 Custom；拖动实时生效。
- **界面**：全量描边矢量图标（Themes/Icons.xaml，替代 emoji，活跃态 accent 着色）；无边框自定义标题栏（WindowChrome + 自绘 TitleBar）。
- **主题**：内置深色主题（深紫强调色）；ComboBox/CheckBox/ScrollBar/ToolTip/ContextMenu 深色化。

## 界面布局

WPF 壳：

```
┌─────────────────────────────────────────────────────────┐
│            标题栏 (TitleBar)   ─  □  ✕                   │
├────────────┬────────────────────────────┬──────────────┤
│  歌单侧栏   │          曲目队列           │   曲目信息    │
│ (Sidebar)  │      (PlaylistView)        │ (TrackInfo)  │
│            │                            │  + 频谱可视化  │
├────────────┴────────────────────────────┴──────────────┤
│          播放栏 (PlayerBar)  播放/随机/循环/音量/EQ/设置    │
└─────────────────────────────────────────────────────────┘
```

WinUI 3 壳（Phase 21 视觉打磨后）：自绘标题栏区 + 三栏 IA——左 NavRail（200px，可折叠至 48px）/ 中 TrackList（弹性宽度，36px 行高）/ 右 InfoPanel（260px，可折叠至 48px、折叠钮在栏头，`<960px` 窗口宽度自动收起）+ 底部 PlayerBar（64px）；设计令牌化（间距 4/8/12/16/24、圆角 4/8/12、字号 12/13.5/15/20、动效 100/150/200ms）；六态齐备（default/hover/pressed/selected/focus/disabled）；键盘与无障碍（Tab 顺序、方向键细步进、空格播放/暂停、UIA 名称）；9 项动效（列表悬停/选中、选择条、按钮缩放、折叠、焦点环、封面交叉淡入、播放/暂停图标、拇指缩放、空态）；**深色由壳强制**（`Application.RequestedTheme = Dark`）+ Mica 材质。Phase 22 补齐了导入/导出工具栏、歌单工具栏与 ▶ 标记、上一首/下一首/随机/循环/音量/静音/设置、窗口几何与本地日志；2026-10-24 的快赢切片又补了 TrackList 当前曲 ▶/标题强调色、行尾移除按钮、InfoPanel 采样率与两处日志出口；Phase 23（2026-10-28）已交付频谱与 EQ 实现，验收待补；拖拽、排序、文件夹绑定管理仍待后续阶段。

---

## 技术栈

| 层 | 选型 |
|----|------|
| 运行时 | .NET 10（共享层与 WPF 壳 `net10.0-windows`；WinUI 壳 `net10.0-windows10.0.19041.0` + x64） |
| UI 框架 | WPF（`UseWPF=true`，主壳）· WinUI 3 / Windows App SDK 2.5.1（第二壳，unpackaged + self-contained） |
| 共享层 | `D-player.Core` 类库（`net10.0-windows`，不开 `UseWPF`）：Models / Services / ViewModels / Configuration / DI 注册 |
| MVVM | [CommunityToolkit.Mvvm 8.x](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) |
| DI 容器 | `Microsoft.Extensions.DependencyInjection` 10.x |
| 配置 | `Microsoft.Extensions.Configuration.Json` + `IOptions<AppSettings>` |
| 音频引擎 | [NAudio 3.3.0](https://github.com/naudio/NAudio)（引用收窄为 `NAudio.Core` + `NAudio.Wasapi` 两个子包；3.3.0 起 `NAudio.Core` 额外带入传递依赖 `System.Numerics.Tensors` 9.0.0，输出目录为三个 DLL；`MediaFoundationReader` 解码 + `WasapiPlayer` WASAPI Shared 输出） |
| 元数据/标签 | [z440.atl.core 7.18](https://github.com/Zeugma440/atldotnet) |
| 测试 | xUnit v3（Microsoft.Testing.Platform）+ NSubstitute + Coverlet |

---

## 环境要求

- Windows 10 / 11（WinUI 壳需 Windows 10 2004（build 19041）及以上；self-contained 形态不需另装 Windows App SDK 运行时）
- .NET 10 SDK
- 构建 WinUI 壳需要 nuget.org 可达（首次 restore 会拉 Windows App SDK）

## 构建与运行

### 门禁（三道，口径只有一套，别混）

> **纪律：一条命令只有在你记得跑它的时候才会跑。** `-Fast` 跑门禁 1/2，`-Full` 再加门禁 3；
> 旧的"壳侧检查（不是门禁）"说法已作废——WinUI 单工程构建现在是门禁 3，由 `-Full` 执行。

- **门禁 1/2（每次改动，`-Fast`）**：走解决方案筛选器 `D-player.slnf`（Core + WPF 壳 + Tests）。
  用 `.slnf` 而不是 `D-player.sln`，是为了让日常构建/测试不把 WinUI 的 Windows App SDK 工具链拖进来。

```bash
dotnet restore D-player.slnf
dotnet build   D-player.slnf -c Debug --nologo -v q      # 门禁 1：0 警告 0 错误
dotnet test    D-player.slnf -c Debug -v q               # 门禁 2：186 通过 0 失败（切勿加 --nologo，见下）
dotnet run     --project D-player.csproj                 # 跑 WPF 壳
```

- **门禁 3（`-Full`，每个 Phase 22 任务必跑）**：WinUI 单工程构建，**0 警告 0 错误**。
  `D-player.slnf` 刻意不含 WinUI，所以改到 Core 公开面（构造函数参数、`IFileDialogService` 成员、
  `HandleDoubleClickPlay` 元数、`ViewedPlaylist` setter……）或 WinUI 自身时，门禁 1/2 照样全绿而壳可能已经编译不过；
  门禁 3 就是为这类改动存在的。首次 restore 之后是增量构建，实测约 **9 秒**。

```bash
dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q   # 门禁 3（-Full）
```

- **三条一起跑的脚本（推荐，会逐步 fail-fast）**：

```bash
powershell -File tools/verify-gates.ps1 -Fast   # = 门禁 1 + 门禁 2
powershell -File tools/verify-gates.ps1 -Full   # = 门禁 1 + 门禁 2 + 门禁 3
```

- **可选**：`dotnet build D-player.sln`（整解，四个工程）只在要确认 IDE「Build Solution」没被打挂时跑。

**这条保护不是抽象风险，是量出来的**：整支分支评审把 `MainWindow.xaml.cs` 里一条 `using` 报成"死引用可以清"、并以此驳回了台账里的那条 minor，而它的判断在事实上是反的。逐条删除实测（WASDK 2.5.1）：删 `Microsoft.UI.Composition.SystemBackdrops` → 仍 **0 警告 0 错误**（确实死，本波已删并留注释）；删 `Microsoft.UI.Xaml.Media` → **CS0246**，因为 `MicaBackdrop` 在 2.5.1 里就住在这个命名空间。要害不在于哪一条该删，而在于 **门禁 1/2 根本不编译 WinUI**：这类"看着像死引用"的整理，删错了只有门禁 3 才会发现——所以每个 Phase 22 任务与任何 Core 公开面/WinUI 改动都必须跑 `-Full`。

可执行文件输出于 `bin/Debug/net10.0-windows/D-player.exe`。

发布独立单文件：

```bash
dotnet publish D-player.csproj -c Release -r win-x64 \
    --self-contained false /p:PublishSingleFile=true
```

跑 WinUI 3 壳（第二壳；构建由门禁 3 覆盖，运行仍靠真机手测）：

```bash
dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q
dotnet run   --project D-player.WinUI/D-player.WinUI.csproj -c Debug
```

两条命令都不需要额外传 `-p:Platform=x64`（`<Platforms>` 只声明支持面、不设默认值，所以 csproj 里同时钉了 `<Platform>x64</Platform>`）。它的数据目录与 WPF 壳分开（`%LocalAppData%\D-player-winui\`）；Phase 22 起有导入/导出入口，首启不再只能看空状态。多歌单/测试数据的构造步骤仍保留在 [`docs/PHASE20-COMPARISON.md`](docs/PHASE20-COMPARISON.md) 第 2.1 节（该节同时是 2026-10-07 那次验收走查的执行记录）。

**要一次 restore 全部四个工程（含 WinUI）时才用 `dotnet restore D-player.sln`**：它会首次从 nuget.org 拉 `Microsoft.WindowsAppSDK`（2.5.1，带 9 个子包），实测约 8.1 分钟，且自包含输出目录很大。**日常请走上面的 `.slnf` 门禁 1/2**（构建约 2 秒级）；`tools/verify-gates.ps1 -Full` 会连门禁 3 一起跑（增量，秒级）。

## 测试

```bash
# xunit v3 的测试工程是可执行程序，runner 是 Microsoft.Testing.Platform（MTP）
dotnet run --project Tests/D-player.Tests.csproj -c Debug   # 直接跑可执行程序
dotnet test D-player.slnf -c Debug                          # 同一个 runner 的另一条命令（门禁 2）
```

两条命令跑的是**同一个 runner（MTP）**，不是两套 runner：`dotnet test` 之所以仍可用，靠的是仓库根 `global.json` 里的 `{"test":{"runner":"Microsoft.Testing.Platform"}}`（.NET 10 SDK 的原生 opt-in）——**删掉 `global.json` 这条命令就失败**。两条都**不要加 `--nologo`**：MTP 不认这个参数，加上后一条测试都不会跑，摘要却打印 `成功: 0`（易被当成全绿；实际是"运行了零个测试" + 退出码 5）；`--filter "FullyQualifiedName~X"` 照常可用。`--nologo` 只能用在 `dotnet build` 上。

当前共 **186** 个单元测试（Models / Services / ViewModels / Configuration 全覆盖 + DI 图解析（`Tests/Extensions/AddDPlayerCoreTests.cs`）+ 纯字符串函数 `PlaylistImportReportFormatter`——Phase 20 它已从 `Views/Controls` 搬进 `D-player.Core/ViewModels`，所以它不再是"View 层例外"；其余 View 层代码按项目惯例不做单测）。**这 186 条覆盖的是 Core + WPF + Tests：WinUI 壳一条都不覆盖**——门禁 3 只保证它能编译（0 警告），UI 行为仍靠真机手测（Phase 22 验收清单见 [`docs/PHASE22-ACCEPTANCE.md`](docs/PHASE22-ACCEPTANCE.md)）。逐文件分解见 [`docs/PROJECT.md`](docs/PROJECT.md) §3 的 Tests 目录树）。

---

## 项目结构

仓库根目录本身就是 WPF 壳的工程目录（`D-player.csproj` 在根上）：

```
D-player/                          # 仓库根 = WPF 壳工程
├── App.xaml(.cs)        # WPF 壳入口：UI 线程建 DI 容器、注册本壳的 IFileDialogService、加载主窗口
├── D-player.csproj / .sln         # WPF 壳工程 / 四工程全量解决方案（IDE 用，不是门禁）
├── D-player.slnf      # 门禁 1/2 的命令载体：Core + WPF 壳 + Tests；WinUI 刻意不进，构建由门禁 3（-Full）覆盖
├── global.json        # 测试 runner 路由（.NET 10 SDK 原生 opt-in）——不要删除：删掉后 `dotnet test D-player.slnf` 就失败
├── appsettings.json   # 构建时默认配置（两壳共用同一份，WinUI 侧链接复制）
├── Views/             # WPF XAML 视图、对话框（Settings/Equalizer/Prompt）、自定义控件
├── Converters/        # WPF 值转换器
├── Themes/            # WPF 深色主题资源字典（Colors / Fonts / Controls / Icons）
├── D-player/Services/ # 只属 WPF 壳的服务实现（Win32FileDialogService，包在 DPlayer.Services 命名空间下）
│
├── D-player.Core/                # 共享类库（net10.0-windows，不开 UseWPF；被两个壳引用）
│   ├── Models/ Configuration/    # 不可变 record 数据模型 + AppSettings + DPlayerDataPaths（数据目录由壳注入）
│   ├── Services/                 # 播放/持久化/元数据/扫描/缓存/播放列表文件读写（接口 + 实现，无 UI 类型）
│   ├── ViewModels/               # MainViewModel 门面 + Player/Playlist/Playlists 子 VM + 导入报告文案格式化器
│   └── Extensions/               # DI 注册扩展 AddDPlayerCore(IConfiguration, DPlayerDataPaths)
│
├── D-player.WinUI/               # WinUI 3 壳（第二壳，unpackaged + self-contained + x64；不进 .slnf，构建由门禁 3 覆盖）
│   ├── App.xaml(.cs)             # UI 线程建容器（数据目录 D-player-winui）+ 注册本壳的 IFileDialogService + 合并 Theme/ 资源字典 + AppLog 初始化/异常钩子
│   ├── MainWindow.xaml(.cs)      # 三栏 IA 主窗口：标题栏 + NavRail + TrackList + InfoPanel + PlayerBar；窗口几何、关闭守卫、空格守卫
│   ├── Theme/                    # 设计令牌与样式（Tokens.xaml / Motion.cs / Styles.xaml）
│   ├── Views/                    # NavRail / TrackList / InfoPanel / PlayerBar / SpectrumView + Dialogs/SettingsDialog、EqualizerDialog
│   └── Services/                 # 真实文件 picker、ContentDialog 宿主、D-player-winui 数据/日志路径、AppLog
│
├── Tests/               # xUnit v3 测试项目（可执行程序，跑 MTP；只引用 Core）
├── tools/               # 校验脚本：verify-gates.ps1（-Fast = 门禁 1/2，-Full = 再加门禁 3）+ coupling-audit/（Phase 15 耦合审计 M1–M6）
└── docs/                # 项目文档、耦合登记册、Phase 20 对比材料、Phase 22 验收清单、各阶段设计稿与实现计划
```

## 配置与数据

- **构建时默认值**：`appsettings.json` 的 `"Player"` 节（启动快照，两壳共用同一份文件）。
- **运行时数据目录由 UI 壳注入**（`DPlayerDataPaths`，两壳刻意分开——没有跨进程锁，共用目录会互相踩）：
  - WPF 壳：`%LocalAppData%\D-player\`（与更名前的落盘位置一致，老用户数据不搬家；UmaPlayer → D-player 的一次性迁移也只属这个壳）
  - WinUI 壳：`%LocalAppData%\D-player-winui\`（不做 UmaPlayer 迁移）
- 目录内文件（两壳同构）：
  - `settings.json` — 窗口几何、默认音量、频谱与 EQ 设置、断点续播位置
  - `queue.json` — 歌单与队列快照（Schema v3）
  - `library-cache.json` — 文件夹歌单的元数据缓存

## 架构概览

严格分层、单向依赖：`View → ViewModel → Service → Model`；跨边界一律走接口；DI 容器集中注册（无 Service Locator、无 static 单例）。

Phase 20 起分层落成物理边界：`D-player.Core` 持有 Models / Services / ViewModels / Configuration 与注册扩展 `AddDPlayerCore(IConfiguration, DPlayerDataPaths)`，**Core 里不得出现任何 WPF 类型**；每个壳在自己的入口里补注册 UI 相关服务（`IFileDialogService`）并传入本壳的数据目录名。

播放链（`NAudioPlaybackService`）：

```
MediaFoundationReader → EqualizerSampleProvider → SampleAggregator → VolumeSampleProvider → WasapiPlayer
                        （10 段 EQ，Phase 14）      （FFT 频谱，Phase 13）        （Shared + 事件同步 + 100ms，Phase 19）
```

核心抽象：`IPlaybackService`、`IPlaylistService`、`ISettingsPersistence`、`ITrackMetadataReader`、`ILibraryScannerService`、`ILibraryCache`、`IPlaylistFileService`（Phase 18 播放列表文件读写门面：导入绝不抛、导出抛给 VM 转文案）、`IFileDialogService`（接口在 Core，实现各壳自给）。

> 完整的模块详解、数据流、隐式契约登记册见 [`docs/PROJECT.md`](docs/PROJECT.md) 与 [`docs/COUPLING.md`](docs/COUPLING.md)。

---

## 开发阶段（Phase 1–23）

| Phase | 内容 |
|-------|------|
| 1 | 单曲播放骨架（NAudio + MVVM + DI + 深色主题） |
| 2 | 内存播放队列（自动推进、随机/循环） |
| 3 | ViewModel 重构 + 技术债清算 |
| 4 | 队列持久化（queue.json） |
| 5 | 拖拽支持（外部入队 + 队列内重排） |
| 6 | 多命名歌单（双指针模型）+ 测试骨架 |
| 7 | 偿还债 #1（VM 层去 WPF 类型） |
| 8 | ViewModel 单元测试体系 |
| 9 | 侧栏歌单拖拽重排 |
| 10 | 文件夹绑定歌单（扫描 + 增量同步 + 缓存） |
| 11 | 设置对话框 |
| 12 | UI 重构 + 全局 Shuffle/Repeat + 曲目信息面板 |
| 13 | 音频可视化（FFT 频谱） |
| 14 | 10 段图形均衡器 |
| 15 | 耦合健康度审计（tools/coupling-audit 脚本度量 + 裁决：无需解耦） |
| 16 | 图标矢量化（Icons.xaml 矢量图标集替换全部 emoji） |
| 17 | UI 深度深色定制（无边框自定义标题栏 + ComboBox/CheckBox/ScrollBar 等深色化） |
| 18 | 播放列表文件导入导出（M3U/M3U8/PLS 导入 + M3U8 导出） |
| 19 | 依赖迁移（NAudio 收窄为 Core + Wasapi、输出改经 `WasapiPlayerBuilder` 建 `WasapiPlayer`、测试栈迁到 xunit.v3；无产品行为变化） |
| 20 | WinUI 3 第二 UI 壳：抽出 WPF-free 的 `D-player.Core`、数据目录由壳注入、门禁改用 `D-player.slnf`、做出第一条纵向切片并交付对比材料（WPF 壳行为与外观零变化；**切片已交付，§2.1 那份验收走查已由用户 2026-10-07 在真机走完并在同日第二轮复验里收口——无待复验项**，唯一报出的"进度条单击定位"缺陷当晚已量出根因并修掉（结果与那处缺陷的实测机制记在对比材料 §2.2）；**决策门已拍板：续投 WinUI（2026-10-07，用户理由：切片只是基本骨架、不足以公平打分），六维评分延后至第二阶段之后**） |
| 21 | WinUI 壳视觉与交互打磨：三栏 IA（NavRail/TrackList/InfoPanel/PlayerBar）+ 设计令牌层（间距/圆角/字号/时长三档）+ 六态样式 + 键盘与无障碍 + 9 项动效；**23 项验收全部通过、验收闭合（2026-10-08），逐项结果见 `docs/PHASE21-ACCEPTANCE.md`；起窗崩溃（exit 0xC0000005）已在 `bba9ad4` 修复并验证——根因是对象初始化器在构造函数体之后执行（`Playlists` 订阅时仍为 null）** |
| 22 | WinUI 功能补齐（日常可用闭环）：异步文件对话框 + 真实 picker、导入/导出、歌单增删改与 ▶ 标记、上一首/下一首/随机/循环/音量/静音、基础设置、窗口几何持久化、本地日志；WinUI 构建纳入 `verify-gates.ps1 -Full` 的门禁 3。频谱/EQ/拖拽/排序/文件夹绑定管理留 Phase 23/24；**Phase 22 历史验收已完成**（验收 23 项全部 PASS（含用户 2026-10-24 确认的 C4/D3 与 D1 轮转），步骤见 `docs/PHASE22-ACCEPTANCE.md`） |

| 23 | WinUI 32 柱频谱与四项设置、暂停/停止回落及晚到数据守卫；10 段 EQ + Pre + 9 预设/Custom、实时预览、取消回滚、保存。**实现完成 / 验收待补（2026-10-28）**；[当前验收与缺口](docs/PHASE23-ACCEPTANCE.md)，不代表全面闭环。 |

每个阶段的设计稿与实现计划归档于 [`docs/superpowers/`](docs/superpowers/)（`specs/` 与 `plans/`）。Phase 20 的两壳对比材料与决策门记录位在 [`docs/PHASE20-COMPARISON.md`](docs/PHASE20-COMPARISON.md)；Phase 22 验收清单在 [`docs/PHASE22-ACCEPTANCE.md`](docs/PHASE22-ACCEPTANCE.md)。

---

## 已知限制

- 仅支持 Windows Media Foundation 原生解码的格式；OGG/Vorbis 需系统额外编解码器。
- 多设备 / 输出模式切换（WASAPI Exclusive / ASIO）接口已预留、尚未实现。
- 播放列表导出仅 M3U8（绝对路径）；不导出 PLS、不做相对路径导出。
- 列表文件里的网络流条目（http:// 等）导入时计入"格式不支持"跳过，不支持流媒体播放；`#EXTINF` / PLS `Title=` 元数据刻意忽略（标题与时长只信音频文件）。
- 按艺术家/专辑组织的音乐库视图尚未实现。
- **WinUI 3 壳已具备日常可用闭环（Phase 22），但不是与 WPF 完全等价**：Phase 22 交付了异步文件对话框 + 真实 picker、导入/导出（添加文件 / 递归导入文件夹 / M3U(M3U8)/PLS 追加 / M3U8 导出）、歌单新建/重命名/删除与 ▶ 活跃标记、上一首/下一首/随机/循环三态/音量/静音（64px 底栏）、基础设置（音量 + 只读音频输出 + 日志入口）、窗口几何持久化与屏外自愈、本地日志（`%LOCALAPPDATA%\D-player-winui\logs\app.log`，1 MiB 轮转）；验收已完成（验收 23 项全部 PASS（含用户 2026-10-24 确认的 C4/D3 与 D1 轮转），步骤见 [`docs/PHASE22-ACCEPTANCE.md`](docs/PHASE22-ACCEPTANCE.md)）。交付后快赢切片（2026-10-24）：TrackList ▶ 当前曲标记 + 标题强调色、行尾移除按钮、InfoPanel 采样率、`SettingsDialog`/`PlayerBar` 的 `Debug.WriteLine` 迁入 `AppLog`。2026-10-27 追加：折叠钮统一回各自栏头（右栏收起改 48px）、底栏去掉“歌名 — 艺术家”、滑块拖动提示显示时间/百分比、窄窗悬停静音钮弹纵向音量条。Phase 23 频谱/EQ 已实现但未全验收（鼠标拖动、听感、无曲目与完整 C 回归等见 [`docs/PHASE23-ACCEPTANCE.md`](docs/PHASE23-ACCEPTANCE.md)）。仍缺：拖拽（外部入队 / 队列内重排 / 歌单重排）、表头排序、文件夹绑定歌单的管理与刷新。WinUI 仍不在 `D-player.slnf` 里：门禁 1/2 覆盖不到它，只有 `-Full` 的门禁 3 会编译它；壳的行为没有单测，靠真机手测。已知问题见验收文档的「已知问题 / 交给用户」一节（含文件夹 picker 输入路径等效取消；NavRail 删除查看项后的选中问题在 2026-10-24 两次复现尝试中未重现，保留观察；Core 的 `TrackEnded` 抢播已于 2026-10-24 修复）。
