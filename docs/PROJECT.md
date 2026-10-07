# D-player 项目文档

> 一个轻量级、本地优先的 Windows 音乐播放器（.NET 10 + NAudio，WPF / WinUI 两套壳；共享层 `D-player.Core` 是 WPF-free 的）。
>
> 文档日期：2026/10/07（代码基线 `71613f2`，Phase 20 Task 4 收口） · 对应分支：`master` · 当前阶段：**Phase 20 切片已交付**（抽出 WPF-free 的 `D-player.Core`、WinUI 3 第二壳第一条纵向切片、门禁改走 `D-player.slnf`）；WPF 壳的行为与外观零变化；**验收已由用户 2026-10-07 在真机走完**（[`PHASE20-COMPARISON.md`](./PHASE20-COMPARISON.md) §2.2）：**除进度条单击定位是缺陷外全部通过**（点左半跳到开头、点右半跳到结尾；机制未量，只有一条标注为假设的解释）；**两壳去留的决策门仍未拍板**——六维评分表与结论仍等用户填 · 上一阶段：Phase 19 完成（NAudio 收窄到 Core+Wasapi + 输出经 `WasapiPlayerBuilder` 建链 + 测试栈迁到 xunit.v3，无产品行为变化） · **项目名：D-player（原 UmaPlayer；C# 命名空间 DPlayer）**

---

## 1. 项目简介

**D-player** 是一款面向 Windows 桌面的本地音乐播放器，灵感来源于 foobar2000 / Winamp。Phase 1 实现单曲播放骨架，Phase 2 加入内存播放队列（多选入队、自动推进、随机/循环模式）。Phase 3 重构 ViewModel 层（按职责拆分 + 抽象元数据读取 + 修正持久化合并纪律），偿还 4 项技术债。Phase 4 加入队列持久化（关闭时写 `queue.json`，启动时恢复列表 + Shuffle/Repeat 模式 + CurrentIndex）。Phase 5 加入拖拽支持（外部音频文件拖入入队、队列内项拖拽重排含多选、视觉反馈含边框高亮 + 插入线 Adorner），同时偿还 in-flight `RemoveTrack`/`MoveTracks` 的 `_playToken` 残留债。Phase 6 加入多命名歌单支持（Spotify 双指针模型：Viewed vs Current）、xUnit 测试骨架、BytesToBitmapImageConverter（Debt #1 部分偿还）。Phase 7 完成债务 #1 完整偿还（PlayerViewModel.BitmapImage → byte[]），VM 层不再依赖 WPF 类型。Phase 8 建立 ViewModel 单元测试体系（50 个测试覆盖 PlayerVM / PlaylistVM / PlaylistsVM）。Phase 9 加入 sidebar 歌单拖拽重排（复用 Phase 5 的 Adorner + 多选拖拽保护模式）。Phase 10 加入文件夹绑定歌单（指定文件夹递归扫描 → 创建/更新歌单，启动后台自动同步增删，手动刷新，JSON 元数据缓存），同时将音频后缀白名单从 View 层提取到 Models.AudioConstants 消除层级违规。Phase 11 添加设置对话框（默认音量滑块 + 音频输出灰色占位 + PlayerBar ⚙ 按钮 + Ctrl+, 快捷键）。Phase 12 UI 界面重构（PlayerBar 移到底部 + 圆形播放键 + PlaylistView 时长列/表头/行分隔线 + Sidebar 图标/选中态背景色 + 色板微调）。Phase 12 continued: 全局 Shuffle/Repeat（所有歌单共享）+ TrackInfoView 独立面板 + #列元数据 TrackNumber + 表头点击排序 + 导入文件夹改为添加到当前歌单 + 移除 Stop/OpenAndPlay 按钮 + Sidebar + 按钮直接新建歌单 + GridSplitter 列宽限制 + ViewBox 封面缩放 + 封面 ClipToBounds 圆角裁切。Phase 13 音频可视化（SampleAggregator FFT 频谱分析 + SpectrumView 自定义控件 + 32 条垂直频谱柱 + 4 种颜色主题 + 灵敏度/平滑度配置 + 设置持久化）。Phase 13 后续调优：FFT 尺寸 1024→2048→8192 提升低频分辨率、立体声先混单声道再加汉宁窗做 FFT、50% FFT 重叠提高更新率、对数频率分组 20Hz–16kHz + RMS + gamma 曲线、彩虹主题改为红→紫水平渐变、频谱移入 TrackInfoView 底部（高 120px）、全局 Slider 加 IsMoveToPointEnabled、SettingsDialog 保存留在 UI 线程即时同步 VM + 失败弹窗。Phase 14 均衡器（EqualizerSampleProvider 10 段图形 EQ 中间件 + EqualizerConfig/EqualizerPresets 数据模型 + 9 个内置预设 + 独立 EqualizerDialog 竖直滑块对话框 + PlayerBar 🎚 启用态高亮按钮 + 实时系数更新 + settings.json 持久化；EQ 插在 SampleAggregator 之前，频谱反映 EQ 后信号）。Phase 15 完成耦合健康度审计（`tools/coupling-audit` PowerShell 脚本 M1–M6 客观度量 + M7/D1–D5 人工裁决；结论：0 环 / 0 层级违规 / 无多职责文件，无需解耦，所有技术债清零）。Phase 16 图标矢量化（`Themes/Icons.xaml` 统一描边矢量图标集替换全部 emoji/字形图标；转换器返回 `Geometry`；▶ 标记 TextBlock→Path 实心三角；VM 层移除 `VolumeIcon` 守住"无 emoji"纪律）。Phase 17 UI 深度深色定制（无边框 WindowChrome + 自绘 TitleBar 应用于主窗与 3 个对话框、最大化常量式工作区边距；ComboBox/CheckBox/ScrollBar/ToolTip/ContextMenu/MenuItem 深色隐式样式；纯表现层，0 新依赖）。Phase 18 播放列表文件导入导出（新增 `Services/PlaylistFiles` 门面模块 + `IPlaylistFileService`：导入 `.m3u` / `.m3u8` / `.pls` —— 编码探测 UTF-8/UTF-16(BOM)/GBK 回退 + 相对路径归一化 + URL/后缀/存在性严格过滤计数；导出 `.m3u8` —— 绝对路径 + `#EXTINF`；双入口分层：侧边栏/拖到侧边栏 = 新建歌单，歌单工具栏/拖到列表区 = 追加当前歌单；导入报告文案由 View 层组装）。Phase 19 依赖迁移（把 `NAudio` meta 包收窄为真正用到的 `NAudio.Core` + `NAudio.Wasapi`、播放输出从 legacy `WasapiOut` 改由 `WasapiPlayerBuilder` 建立、测试栈从 xunit v2 迁到 xunit.v3（可执行程序 + 单一 MTP runner，`dotnet test` 经仓库根 `global.json` 到达同一个 runner）；无产品行为变化，无新架构债）。Phase 20 WinUI 3 第二 UI 壳（共享层 Models / Services / ViewModels / Configuration / DI 注册搬进 **WPF-free** 的类库 `D-player.Core`；`IFileDialogService` 的实现与用户数据目录 `DPlayerDataPaths` 改由各 UI 壳注入；`SortedView` 这个 WPF 类型泄漏被删除，列表直接绑 `Queue`；门禁改走解决方案筛选器 `D-player.slnf`（Core + WPF 壳 + Tests），新建的 `D-player.WinUI`（WinUI 3，unpackaged + self-contained，x64）刻意不进门禁；交付第一条纵向切片——自绘标题栏区 + NavigationView 歌单导航 + 曲目列表 + 播放器栏 + 真机可播 + 关闭落盘。**WPF 壳的行为与外观零变化**；两壳去留的决策门**仍未拍板**；§2.1 那份走查清单**已由用户 2026-10-07 在真机走完**，除"进度条单击定位"是一处缺陷外全部通过，逐项结果记在对比材料 §2.2，对比材料见 [`PHASE20-COMPARISON.md`](./PHASE20-COMPARISON.md)）。

### 1.1 关键特性（已实现）

> 下表是 **WPF 壳**的能力清单（完整功能的那一个）。WinUI 3 壳目前只有"歌单导航 + 曲目列表 + 播放器栏 + 真机可播 + 断点续播"这条纵向切片，缺项与逐项验证状态见 [`PHASE20-COMPARISON.md`](./PHASE20-COMPARISON.md)。

| 类别   | 能力                                                             |
|------|----------------------------------------------------------------|
| 文件导入 | Win32 OpenFileDialog 多文件选择                                     |
| 支持格式 | MP3 / WMA / FLAC / AAC / WAV（基于 Windows Media Foundation 原生解码） |
| 播放控制 | 播放 / 暂停 / 上一首 / 下一首 / 随机 / 循环                                  |
| 进度控制 | 拖拽 + 单击跳转的进度条；位置实时更新（≈30 Hz，节流）                                |
| 音量控制 | 0~1 线性滑块、一键静音/取消静音；通过 `VolumeSampleProvider` 实现                |
| 元数据  | 标题 / 艺术家 / 专辑 / 流派 / 年份 / 采样率 / 曲目号 / 内嵌封面（z440.atl.core）     |
| 主题   | 内置深色主题（深紫强调色）；Phase 17：无边框自定义标题栏 + ComboBox/CheckBox/ScrollBar/ToolTip/ContextMenu 深色隐式样式 |
| 持久化  | 窗口位置/尺寸、默认音量保存到 `%LocalAppData%\D-player\settings.json`       |
| 播放列表 | 内存队列：多选入队、单项删除、清空、上/下一首、自然播完自动推进、#列元数据 TrackNumber、表头点击排序 |
| 队列持久化 | 关闭时写 `%LocalAppData%\D-player\queue.json`；启动恢复列表 + CurrentIndex + Shuffle/Repeat（Phase 4） |
| 拖拽 | 外部音频文件拖入末尾入队（白名单 .mp3/.wma/.flac/.aac/.wav）；队列内单/多选拖拽重排（含 ▶ 当前曲跟随、Shuffle 历史按对象身份重映射）；插入线 Adorner + 圆角列表框边框高亮（Phase 5） |
| 多命名歌单 | 创建/删除/重命名多个独立歌单；Viewed vs Current 双指针（切查看不打断播放，双击才跨歌单切换音频）；全局 Shuffle/Repeat（所有歌单共享）；各歌单独立 CurrentIndex；v1→v2 schema 自动迁移（Phase 6） |
| 文件夹绑定歌单 | 指定文件夹扫描 → 创建歌单; 启动后台自动同步增删; 手动刷新; 元数据缓存; 导入文件夹添加到当前歌单 (Phase 10) |
| 设置 | 模态对话框：默认音量滑块；音频输出占位（Phase 12）；Ctrl+, 快捷键 (Phase 11) |
| 曲目信息面板 | 右侧独立 TrackInfoView：封面（ViewBox 自动缩放）+ 标题/艺术家/专辑/采样率；BackgroundSecondary 背景 (Phase 12 continued) |
| 音频可视化 | 32 条垂直频谱柱（8192 点 FFT + 汉宁窗 + 50% 重叠 + 对数分组 20Hz–16kHz + RMS/gamma）；4 种颜色主题（紫/蓝/绿/彩虹，彩虹为红→紫水平渐变）；灵敏度/平滑度配置；启用/禁用开关；置于 TrackInfoView 底部（高 120px）；设置持久化 (Phase 13) |
| 均衡器 | 10 段图形 EQ（ISO 倍频程 31Hz–16kHz ±12dB 峰值滤波 Q≈1.1 + preamp −12~+12dB）；9 个内置预设（Flat/Rock/Pop/Jazz/Classical/Dance/Bass Boost/Treble Boost/Vocal）+ 手动 Custom；实时生效（拖动即时听感）；启用开关（默认关，透明旁路）；独立 EqualizerDialog（PlayerBar 🎚 按钮打开，启用态高亮）；设置持久化 (Phase 14) |
| 图标 | 全量描边矢量图标集（Themes/Icons.xaml，24 个 `Icon.*`，Feather/Lucide 几何）；随机/循环/EQ 活跃态 accent 着色；▶ 标记实心三角 (Phase 16) |
| 窗口外观 | 无边框 WindowChrome + 自绘 TitleBar（最小化/最大化/关闭；对话框仅关闭按钮；最大化常量式工作区边距；OS 保留拖动/快照行为）(Phase 17) |
| 播放列表文件 | 导入 .m3u / .m3u8 / .pls（相对路径按列表所在目录解析；UTF-8/UTF-16(BOM)/GBK 编码探测；URL/后缀/存在性严格过滤 + 跳过计数报告）；导出 .m3u8（绝对路径 + `#EXTINF`）；侧边栏入口新建歌单、歌单工具栏入口追加当前歌单、拖拽按落点分流 (Phase 18) |

### 1.2 后续增量（未实现）

- 音乐库按艺术家/专辑组织（文件夹扫描已在 Phase 10 实现）
- OGG/Vorbis 支持（MF 不原生支持，需额外解码器）
- 多设备 / 输出模式切换（WASAPI Shared / Exclusive / ASIO）—— 接口已预留
- **WinUI 壳的功能补齐（Phase 20 决策门之后的工作，范围未定）**：频谱、拖拽（含插入线，WinUI 无 `AdornerLayer`）、文件/文件夹对话框（`IFileDialogService` 是同步接口而 WinUI picker 只有异步 API —— 直接同步等待会死锁，需先定跨壳契约怎么改）、EQ、设置、播放列表文件导入导出、音量、上一首/下一首、随机/循环、表头排序、列表 ▶ 当前曲标记与删除、窗口几何持久化、歌单增删/重命名/重排

---

## 2. 技术栈

| 层 | 选型 |
|----|------|
| 运行时 | .NET 10（共享层与 WPF 壳 `net10.0-windows`；WinUI 壳 `net10.0-windows10.0.19041.0`） |
| 共享层 | `D-player.Core` 类库（Phase 20，`net10.0-windows` 且**不开** `UseWPF`）：Models / Services / ViewModels / Configuration / Extensions；被两个 UI 壳引用，不得出现任何 WPF 类型 |
| UI 框架 | WPF（`UseWPF=true`，主壳，完整功能）· WinUI 3 / Windows App SDK **2.5.1**（Phase 20 第二壳，unpackaged + self-contained，`<Platforms>x64</Platforms>` + `<Platform>x64</Platform>`，第一条纵向切片） |
| MVVM | [CommunityToolkit.Mvvm 8.x](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) |
| DI 容器 | `Microsoft.Extensions.DependencyInjection` 10.x |
| 配置 | `Microsoft.Extensions.Configuration.Json` + `IOptions<AppSettings>` |
| 音频引擎 | [NAudio 3.1.0](https://github.com/naudio/NAudio)（Phase 19 把 meta 包 `NAudio` 收窄为实际用到的两个子包：`NAudio.Core` + `NAudio.Wasapi`；`NAudio.Wasapi 3.1.0` 的 nuspec 只依赖 `NAudio.Core`，输出目录里也只有这两个 DLL） |
| 音频解码 | `MediaFoundationReader` |
| 输出后端 | WASAPI Shared（`WasapiPlayer`：经 `WasapiPlayerBuilder` 建链 —— `WithSharedMode()` + `WithEventSync()` + `WithLatency(100)`，与迁移前的 `WasapiOut(Shared, 100)` 语义等价；Phase 19 起替代 legacy 的 `WasapiOut`，`WasapiPlayer` 实现 `IWavePlayer` 故服务侧字段类型未变） |
| 元数据/标签 | [z440.atl.core 7.18](https://github.com/Zeugma440/atldotnet)（`40338f5` 与 NAudio 同批升级；7.14–7.18 全是解析层修复，零 API 变更，故无代码改动） |

---

## 3. 目录结构

```
D-player/                        # 仓库根 = WPF 壳工程目录（D-player.csproj 就在根上）
├── App.xaml(.cs)                # WPF 壳入口：UI 线程建 DI 容器（AddDPlayerCore + 本壳的 Win32FileDialogService）、跑 LegacyDataMigration、加载主窗口
├── AssemblyInfo.cs              # ThemeInfo（资源字典位置）
├── D-player.csproj              # WPF 壳工程；ProjectReference 到 D-player.Core；三组兄弟目录的 glob 排除集（见 §9）
├── D-player.sln                 # 四个工程全在里面（IDE 用）—— **不是门禁**
├── D-player.slnf                # 门禁筛选器：D-player.Core + D-player.csproj + Tests（WinUI 刻意不进）
├── appsettings.json             # 启动默认配置（构建时复制到输出目录；WinUI 壳链接同一份，不复制第二份真相）
├── global.json                  # 测试 runner 路由：{"test":{"runner":"Microsoft.Testing.Platform"}}（.NET 10 SDK 原生 opt-in）。不要删除 —— 删掉后 `dotnet test D-player.slnf` 直接失败
│
├── D-player.Core/               # Phase 20 抽出的共享类库（net10.0-windows、不开 UseWPF，被两个 UI 壳引用；层目录下不得出现 WPF 类型）
│   ├── Configuration/
│   │   ├── AppSettings.cs       # 强类型配置 record（绑定到 "Player" section）
│   │   └── DPlayerDataPaths.cs  # 用户数据目录（Root + FolderName → Directory），由 UI 壳注入 (Phase 20)
│
│   ├── Models/
│   │   ├── Track.cs                 # 不可变 record：音轨信息（含封面字节数组）
│   │   ├── PlayState.cs             # enum: Stopped / Playing / Paused
│   │   ├── RepeatMode.cs            # enum: Off / List / One  (Phase 2)
│   │   ├── Playlist.cs              # 不可变 record: 歌单 (Id, Name, Items, CurrentIndex, ShuffleEnabled*, RepeatMode*, SourceFolder) (*=Phase 12 continued 写入时固定 false/Off, 实际全局状态在 QueueState 根级别) (Phase 6/10)
│   │   ├── QueueState.cs            # 不可变 record: queue.json schema v3 (Playlists + CurrentPlaylistId + ShuffleEnabled + RepeatMode + SourceFolder) (Phase 4/6/10/12 continued)
│   │   ├── MoveTracksArgs.cs        # 不可变 record: 队列内拖拽重排命令参数 (Phase 5)
│   │   ├── AudioConstants.cs         # 音频后缀白名单 (Phase 10, 从 DragDropExtensions 提取)
│   │   ├── LibraryCacheEntry.cs      # 缓存条目 record (Phase 10)
│   │   ├── LibraryDiff.cs            # 扫描增量同步 record (Phase 10)
│   │   ├── SpectrumConfig.cs        # 频谱分析配置 record (FftSize=8192/BarCount=32/Sensitivity/Smoothing) (Phase 13)
│   │   ├── EqualizerConfig.cs       # 均衡器运行时配置 record (Enabled/PreampDb/BandGainsDb[10]/Preset + Create 工厂) (Phase 14)
│   │   ├── EqualizerPresets.cs      # 均衡器频段常量 + 9 个内置预设 (ISO 倍频程中心频率/Q=1.1/±12dB) (Phase 14)
│   │   └── AudioDeviceInfo.cs       # 预留：设备信息
│
│   ├── Services/                    # 业务/基础设施服务（全部基于接口；Phase 20 起在 Core 里，实现一律不得依赖 UI 类型）
│   │   ├── IPlaybackService.cs      # 核心播放抽象
│   │   ├── NAudioPlaybackService.cs # NAudio 实现（WASAPI + MediaFoundation）
│   │   ├── SampleAggregator.cs      # ISampleProvider 透明中间件：FFT 频谱分析 (Phase 13)
│   │   ├── EqualizerSampleProvider.cs # ISampleProvider 透明中间件：10 段图形 EQ（每声道 BiQuadFilter 峰值滤波）(Phase 14)
│   │   ├── IFileDialogService.cs    # 文件/文件夹对话框抽象；**实现由各 UI 壳自己注册**（WPF: `D-player/Services/Win32FileDialogService.cs`；WinUI: 切片期空实现）(Phase 20)
│   │   ├── ISettingsPersistence.cs
│   │   ├── JsonSettingsPersistence.cs # 落点由 DPlayerDataPaths 决定：%LocalAppData%\D-player\settings.json（WPF 壳）(Phase 20 参数化)
│   │   ├── LegacyDataMigration.cs   # 启动一次性迁移旧数据目录 UmaPlayer → D-player；`MigrateIfNeeded(DPlayerDataPaths)`，只由 WPF 壳调用 (Phase 20 加参数)
│   │   ├── IPlaylistService.cs      # 多歌单持久化抽象 (Phase 6, 替换 IQueuePersistence)
│   │   ├── JsonPlaylistService.cs   # 落点由 DPlayerDataPaths 决定：%LocalAppData%\D-player\queue.json; 内置 v1→v2 迁移 (Phase 6/20)
│   │   ├── ILibraryScannerService.cs      # 库扫描抽象 (Phase 10)
│   │   ├── LibraryScannerService.cs       # 递归扫描 + Diff 实现 (Phase 10)
│   │   ├── ILibraryCache.cs               # 元数据缓存抽象 (Phase 10)
│   │   ├── JsonLibraryCache.cs            # JSON 缓存实现 (Phase 10)
│   │   ├── ITrackMetadataReader.cs # 元数据读取抽象 (Phase 3)
│   │   ├── AtlMetadataReader.cs    # 基于 z440.atl.core 的实现 (Phase 3)
│   │   ├── IAudioDeviceManager.cs   # 预留：设备枚举/切换
│   │   ├── StubAudioDeviceManager.cs# 占位实现，返回空集
│   │   ├── IAudioOutputFactory.cs   # 预留：输出后端工厂
│   │   ├── StubAudioOutputFactory.cs# 占位实现，固定返回 WASAPI Shared
│   │   └── PlaylistFiles/           # 播放列表文件读写门面模块 (Phase 18)
│   │       ├── PlaylistFileFormats.cs  # 后缀白名单 (.m3u/.m3u8/.pls) + IsPlaylistFile + 对话框过滤器字符串（后缀集合唯一来源）
│   │       ├── PlaylistFileEncoding.cs # 字节 → 文本：BOM 判定 → 严格 UTF-8 试解码 → GBK(936) 回退；静态构造函数注册 CodePages provider
│   │       ├── M3uParser.cs            # M3U/M3U8 解析：逐行取非 # 开头非空行（#EXTM3U/#EXTINF/注释一律忽略）
│   │       ├── PlsParser.cs            # PLS(INI) 解析：只取 File<N>= 值，按出现顺序（Title<N>/Length<N>/NumberOfEntries/Version 忽略）
│   │       ├── PlaylistImportResult.cs # 服务层导入结果 record（SuggestedName + AcceptedPaths + 三个互斥计数）
│   │       ├── IPlaylistFileService.cs # 门面接口：ImportAsync（绝不抛）/ ExportAsync（可抛 IOException）
│   │       ├── PlaylistFileService.cs  # 门面实现：编排 编码探测 → 解析 → 路径归一化 → 过滤计数；Singleton
│   │       └── M3u8Writer.cs           # Track 列表 → extended M3U8 文本（UTF-8 无 BOM、CRLF、绝对路径）
│   │
│   ├── ViewModels/
│   │   ├── MainViewModel.cs        # Strict Facade (~44 行)：仅暴露 Player/Playlists + InitializeAsync/CleanupAsync + debounce save (Phase 3/6)
│   │   ├── PlayerViewModel.cs      # Transport 子 VM：播放/暂停/进度/音量 (Phase 3)
│   │   ├── PlaylistViewModel.cs    # 队列子 VM：Queue/推进算法 + Id/Name/IsActivePlaylist + SortBy（物理重排 Queue，Phase 20 删掉 SortedView）+ ImportFolderToCurrent + ImportPlaylistFileAsync/ExportPlaylistFileAsync (Phase 3/6/12 continued/18/20)
│   │   ├── PlaylistImportReport.cs # 一次导入的结构化报告 record（只带数据不带文案）(Phase 18)
│   │   ├── PlaylistImportReportFormatter.cs # 导入报告 record → 中文文案（单个/多个/全跳过分支；四个入口共用的纯字符串函数）(Phase 18，Phase 20 从 Views/Controls 搬进 Core)
│   │   └── PlaylistsViewModel.cs   # 多歌单容器：ObservableCollection<PlaylistVM> + 全局 Shuffle/Repeat + Add/Remove/Rename + HandleDoubleClickPlay（两壳共用的唯一双击入口）+ ImportFolder/Rescan/Refresh + ImportPlaylistFileAsync (Phase 6/10/12 continued/18/20)
│   │
│   └── Extensions/
│       └── ServiceCollectionExtensions.cs # AddDPlayerCore(IConfiguration, DPlayerDataPaths)：共享服务与 VM 注册；**不含任何 UI 相关服务**（Phase 20 由 AddDPlayerServices 改名并拆出对话框注册）
│
├── Views/                       # ↓↓↓ 以下三个目录 + 根上的 App.xaml/AssemblyInfo.cs + D-player/Services/ 属于 WPF 壳 ↓↓↓
│   ├── MainWindow.xaml(.cs)     # 主窗口；3 行(TitleBar | 内容区 | PlayerBar)；内容区 5 列(Sidebar | Splitter | Playlist | Splitter | TrackInfo)；Phase 17 无边框 WindowChrome
│   ├── Dialogs/
│   │   ├── PromptDialog.xaml(.cs)    # 共享单输入对话框（新建/重命名歌单）(Phase 6)；Phase 17 无边框 + TitleBar；输入框深色样式
│   │   ├── ConfirmDialog.xaml(.cs)   # 主题化确认对话框（删歌单/清空/移除曲目；替代系统 MessageBox）；Phase 18 加 ShowInfo 单按钮信息模式
│   │   ├── SettingsDialog.xaml(.cs)  # 设置对话框（音量 + 音频输出占位 + 频谱可视化）(Phase 11/13)；Phase 17 无边框 + TitleBar
│   │   └── EqualizerDialog.xaml(.cs) # 均衡器对话框（11 根竖直滑块 + 预设下拉 + 启用开关 + 实时预览）(Phase 14)；Phase 17 无边框 + TitleBar
│   └── Controls/
│       ├── TitleBar.xaml(.cs)   # 自绘无边框标题栏（Title/ShowMaximize DP + SystemCommands + 最大化常量边距 + Max/Restore 图标切换）(Phase 17)
│       ├── PlayerBar.xaml(.cs)  # 播放栏（进度/控制/音量 + 随机/循环 + 均衡器/设置按钮；Phase 16 图标全部矢量 Path）
│       ├── PlaylistView.xaml(.cs)    # 播放队列（Phase 2 + Phase 5 拖拽 + Phase 6 IsActivePlaylist guard + #列/表头排序/导入文件夹；Phase 16 ▶ 标记改 Path；Phase 18 导入列表/导出列表按钮 + 拖拽分流播放列表文件）
│       ├── PlaylistsSidebarView.xaml(.cs) # 左侧歌单栏（+/-/导入 三个图标按钮、ListBox、双击重命名、▶ 标记、文件夹/扫描图标；Phase 18 拖入播放列表文件新建歌单）(Phase 6/10/12 continued/18)
│       ├── TrackInfoView.xaml(.cs)   # 右侧曲目信息面板（封面 ViewBox 缩放 + 标题/艺术家/专辑/采样率 + 底部 SpectrumView）(Phase 12 continued/13)
│       ├── SpectrumView.xaml(.cs)    # 频谱可视化控件：32 柱 Canvas + CompositionTarget.Rendering 60fps + 4 色主题 (Phase 13)
│       ├── DragDropExtensions.cs     # IsDragOver attached DP + 音频/播放列表后缀白名单与过滤（FilterAudioPaths / FilterPlaylistPaths）(Phase 5/18)
│       ├── PlaylistImportUi.cs       # 导入共用执行器：跑导入 → 聚合报告 → ConfirmDialog.ShowInfo → 兜住 async void 异常 (Phase 18)
│       └── DropInsertionAdorner.cs   # ListBox AdornerLayer 插入线绘制 (Phase 5)
│
├── Converters/
│   ├── PlayStateToIconConverter.cs       # ▶/⏸ → 矢量 Geometry (Phase 16)
│   ├── TimeSpanToStringConverter.cs      # 0:00 / 0:00:00
│   ├── RepeatModeToIconConverter.cs      # 循环三态 → 矢量 Geometry (Phase 2/16)
│   ├── BoolToVolumeIconConverter.cs      # 音量/静音 → 矢量 Geometry (Phase 16)
│   ├── BoolToAccentBrushConverter.cs     # 强调色/次要色画刷 (Phase 2)
│   └── BytesToBitmapImageConverter.cs    # byte[] → Frozen BitmapImage (Phase 6, 债务 #1 部分偿还)
│
├── Themes/                      # 深色主题资源字典（App.xaml 合并加载）
│   ├── Colors.xaml              # #1E1E2E 背景 + 深紫强调色板
│   ├── Fonts.xaml               # Segoe UI + Header/Body/Caption 文本样式
│   ├── Controls.xaml            # Window/Button/Slider + ComboBox/CheckBox/ScrollBar/ToolTip/ContextMenu 深色模板 (Phase 17)
│   └── Icons.xaml               # 矢量图标集：24 个 Icon.* Geometry + IconPath 样式 (Phase 16；Feather/Lucide 署名；Phase 18 加 Icon.Import/Icon.Export)
│
├── D-player/Services/
│   └── Win32FileDialogService.cs # Microsoft.Win32.OpenFileDialog / OpenFolderDialog / SaveFileDialog 封装（只属 WPF 壳，命名空间仍是 DPlayer.Services）(Phase 20 从 Core 搬回壳里)
│
├── D-player.WinUI/              # Phase 20 第二 UI 壳（WinUI 3 / Windows App SDK 2.5.1，unpackaged + self-contained，x64）；**不进门禁**，靠一条有触发条件、需要手跑的壳侧构建检查补位（见 §4.3 第 28 条）
│   ├── D-player.WinUI.csproj    # WindowsPackageType=None + WindowsAppSDKSelfContained=true + Platforms/Platform=x64；ProjectReference 到 Core；链接根上的 appsettings.json
│   ├── App.xaml(.cs)            # UI 线程建容器（AddDPlayerCore + 本壳的 WinUiFileDialogService，数据目录 D-player-winui）；刻意**不**同步 Dispose ServiceProvider
│   ├── MainWindow.xaml(.cs)     # 切片主窗口：自绘标题栏区 + NavigationView 歌单栏 + 曲目 ListView + 底部播放器栏；AppWindow.Closing → await CleanupAsync；Mica 依赖根 Grid Background=Transparent
│   └── Services/
│       └── WinUiFileDialogService.cs # 切片期空实现（三个方法各返回"用户取消"）；同步接口 vs WinUI 异步 picker 的死锁障碍写在类注释里
│
├── Tests/                       # xUnit v3 测试项目 (Phase 6+，共 **180** 个测试 = 172 个 [Fact] + 1 个 [Theory] 展开的 8 条 [InlineData]；下列逐文件括号计数之和即 180；Phase 20 起只引用 D-player.Core，不引用任何 UI 壳)
│   ├── D-player.Tests.csproj   # 测试项目文件 (xUnit v3 + NSubstitute + Coverlet；OutputType=Exe，跑 Microsoft.Testing.Platform)
│   ├── Smoke/
│   │   └── SmokeTests.cs                 # 冒烟测试：Track record 结构相等 (1)
│   ├── Configuration/
│   │   └── DPlayerDataPathsTests.cs      # 数据目录组合 + 两个壳各自注入的目录名钉桩 (4) (Phase 20)
│   ├── Extensions/
│   │   └── AddDPlayerCoreTests.cs        # DI 图解析：从容器取 MainViewModel 并真的构造一个歌单 VM（缺 IFileDialogService 注册就在这里抛）(1) (Phase 20)
│   ├── Models/
│   │   ├── EqualizerConfigTests.cs       # 均衡器配置 record (4) (Phase 14)
│   │   └── EqualizerPresetsTests.cs      # 均衡器预设 (5) (Phase 14)
│   ├── Services/
│   │   ├── LibraryScannerServiceTests.cs # 库扫描 (17) (Phase 10)
│   │   ├── JsonLibraryCacheTests.cs      # 元数据缓存 (5) (Phase 10)
│   │   ├── EqualizerSampleProviderTests.cs # 均衡器中间件（立体声独立 + Nyquist 旁路）(7) (Phase 14)
│   │   ├── NAudioPlaybackServiceConcurrencyTests.cs # 播放链并发/播完回归（真实占用 WASAPI 设备）(3) (Phase 18 后修复 / Phase 19)
│   │   ├── NAudioPlaybackServiceStopSemanticsTests.cs # 曲尾停止语义回归（真实占用 WASAPI 设备）(2) (Phase 18 后修复 / Phase 19)
│   │   ├── PlaylistFileEncodingTests.cs  # 编码探测（BOM / 严格 UTF-8 / GBK 回退 / 空输入）(6) (Phase 18)
│   │   ├── PlaylistFileParserTests.cs    # 后缀判定 + M3U/PLS 解析 (16) (Phase 18)
│   │   ├── PlaylistFileServiceTests.cs   # 导入管道（归一化/过滤计数/不抛）+ 导出与往返 (20) (Phase 18)
│   │   └── TestAudio.cs                  # 音频测试的公共夹具（非测试类，不计数）
│   └── ViewModels/
│       ├── PlaybackResumeTests.cs         # 断点续播（落盘时机 + 启动恢复 + 缺文件提示）(11) (2026-10-06)
│       ├── PlayerViewModelTests.cs        # Transport (15) (Phase 8)
│       ├── PlayerViewModelSpectrumTests.cs# 频谱 (5) (Phase 13)
│       ├── PlayerViewModelEqualizerTests.cs # 均衡器（启用态传播 + 构造期抑制写盘）(3) (Phase 14)
│       ├── PlaylistViewModelTests.cs      # 队列 + 追加导入/导出 + 表头排序物理重排 (23) (Phase 8/18/20)
│       ├── PlaylistImportReportFormatterTests.cs # 导入报告文案分支 (5) (Phase 18；Phase 20 随格式化器从 Tests/Views 搬来)
│       └── PlaylistsViewModelTests.cs     # 多歌单 + 容器级导入新建歌单 + 共用双击入口的越界/出声事实 (27) (Phase 8/12/18/20)
│
├── tools/
│   ├── verify-gates.ps1         # 一键跑校验：`-Fast` = 门禁一 + 门禁二（`.slnf` 构建 + 测试）；`-Full` = 再加 WinUI 单壳构建（**壳侧检查，不是门禁**，触发条件见 §8.2）。逐步 fail-fast：任一步非零退出、**或两个构建步骤在 MSBuild 安静日志里报出任一行警告**即中止（门禁口径是 0 警告 0 错误，而 `dotnet build` 只报警告时退出码仍是 0）；`dotnet test` 那一步不带 `--nologo`
│   └── coupling-audit/          # Phase 15 耦合审计脚本（Invoke-CouplingAudit.ps1，M1–M6 度量；层目录按**四个 scan root** 收集：`D-player.Core/<层>`、`D-player.WinUI/<层>`、`D-player/<层>`、仓库根 `<层>`，外加仓库根自己的根级 `*.cs`；DI 注册表也从 Core 下解析。盲区与读数口径见 COUPLING §5 末尾的"Phase 20 审计复跑"块）
│
└── docs/
    ├── PROJECT.md               # 本文档
    ├── COUPLING.md              # 耦合分析 / 风险登记册
    ├── PHASE20-COMPARISON.md    # Phase 20 两壳对比材料（功能等价核对表 + 六维评分表 + 决策门记录位）(Phase 20)
    └── superpowers/             # 设计稿 & 实现计划（历史归档）
        ├── specs/
        │   ├── 2026-04-23-uma-player-design.md                          # Phase 1 设计
        │   ├── 2026-06-06-uma-player-playlist-design.md                 # Phase 2 设计
        │   ├── 2026-06-07-uma-player-phase3-design.md                   # Phase 3 设计
        │   ├── 2026-06-12-uma-player-phase4-queue-persistence-design.md # Phase 4 设计
        │   ├── 2026-06-12-uma-player-phase5-drag-drop-design.md         # Phase 5 设计
        │   ├── 2026-06-13-uma-player-phase6-named-playlists-design.md   # Phase 6 设计
        │   ├── 2026-06-14-uma-player-phase10-library-scan-design.md     # Phase 10 设计
        │   ├── 2026-06-15-uma-player-phase11-settings-panel-design.md   # Phase 11 设计
        │   ├── 2026-06-22-uma-player-phase12-ui-refactor-design.md      # Phase 12 设计
        │   ├── 2026-06-24-uma-player-phase13-audio-visualization-design.md # Phase 13 设计
        │   ├── 2026-09-08-d-player-phase14-equalizer-design.md          # Phase 14 设计
        │   ├── 2026-09-12-d-player-phase15-coupling-audit-design.md     # Phase 15 审计设计
        │   ├── 2026-09-12-d-player-phase15-coupling-audit-report.md     # Phase 15 审计报告
        │   ├── 2026-09-13-d-player-phase16-icon-refactor-design.md      # Phase 16 设计
        │   ├── 2026-09-13-d-player-phase17-ui-dark-theming-design.md    # Phase 17 设计
        │   ├── 2026-10-05-d-player-phase18-playlist-file-io-design.md   # Phase 18 设计
        │   ├── 2026-10-06-d-player-phase19-dependency-migration-design.md # Phase 19 设计
        │   └── 2026-10-06-d-player-phase20-winui-shell-design.md        # Phase 20 设计（WinUI 第二壳 + 决策门）
        └── plans/
            ├── 2026-04-24-uma-player-implementation.md                          # Phase 1 计划
            ├── 2026-06-06-uma-player-playlist-implementation.md                 # Phase 2 计划
            ├── 2026-06-07-uma-player-phase3-implementation.md                   # Phase 3 计划
            ├── 2026-06-12-uma-player-phase4-queue-persistence-implementation.md # Phase 4 计划
            ├── 2026-06-12-uma-player-phase5-drag-drop-implementation.md         # Phase 5 计划
            ├── 2026-06-13-uma-player-phase6-named-playlists.md                  # Phase 6 计划
            ├── 2026-06-14-uma-player-phase10-library-scan.md                    # Phase 10 计划
            ├── 2026-06-15-uma-player-phase11-settings-panel.md                  # Phase 11 计划
            ├── 2026-06-22-uma-player-phase12-ui-refactor-implementation.md      # Phase 12 计划
            ├── 2026-06-24-uma-player-phase13-audio-visualization-implementation.md # Phase 13 计划
            ├── 2026-09-08-d-player-phase14-equalizer-implementation.md          # Phase 14 计划
            ├── 2026-09-12-d-player-phase15-coupling-audit-implementation.md     # Phase 15 计划
            ├── 2026-09-13-d-player-phase16-icon-refactor-implementation.md      # Phase 16 计划
            ├── 2026-09-13-d-player-phase17-ui-dark-theming-implementation.md    # Phase 17 计划
            ├── 2026-10-05-d-player-phase18-playlist-file-io-implementation.md   # Phase 18 计划
            ├── 2026-10-06-d-player-phase19-dependency-migration-implementation.md # Phase 19 计划
            └── 2026-10-06-d-player-phase20-winui-shell-implementation.md        # Phase 20 计划（含实施中被代码推翻处的原地标注）
```

---

## 4. 架构

### 4.1 高层架构（MVVM + DI）

```
   ┌──────────────────────────────────────────────────────┐
   │                       App.xaml.cs                    │
   │   1. 读取 appsettings.json                            │
   │   2. 构建 ServiceCollection (AddDPlayerCore)        │
   │   3. 解析 MainViewModel + ISettingsPersistence        │
   │      + IPlaylistService                               │
   │   4. new MainWindow(vm, persistence).Show()           │
   └──────────────┬─────────────────────────┬─────────────┘
                  │                         │
                  ▼                         ▼
   ┌──────────────────────┐    ┌──────────────────────────┐
   │     MainWindow       │    │  MainViewModel (Facade)  │
   │  (View, code-behind) │◀──▶│  ~130 行：持有子 VM       │
   │ - 窗口位置恢复/保存   │    │  + InitializeAsync()      │
   │ - 关闭写 queue.json  │    │  + CleanupAsync()         │
   │   (cancel-and-close) │    │  + debounce save          │
   │ - TitleBar + 5 列    │    └──────┬──────────┬─────────┘
   │ - PlayerBar 底部     │           │          │
   └──────────────────────┘           ▼          ▼
                              ┌─────────────┐ ┌──────────────────┐
                              │ PlayerVM    │ │ PlaylistsVM      │
                              │ Transport:  │ │ 多歌单容器:       │
                              │ Play/Pause/ │ │ 全局 Shuffle/    │
                              │ Position/Vol│ │ Repeat + 增删    │
                              │             │ │ 歌单 + 导入文件夹 │
                              └──────┬──────┘ └───────┬──────────┘
                                     │                │ 持有 N 个 PlaylistVM
                                     │  互不持引用     │
                                     ▼  仅共享 Service ▼
           ┌──────────────────────────┴──────────────────────────┐
           ▼                          ▼                          ▼
┌────────────────────┐  ┌────────────────────────┐  ┌──────────────────────┐
│ IPlaybackService   │  │ ITrackMetadataReader   │  │ ISettingsPersistence │
│ (NAudio impl)      │  │ (ATL impl) [Phase 3]   │  │ UpdateAsync(Func<>)  │
└────────────────────┘  └────────────────────────┘  └──────────────────────┘
┌────────────────────────┐  ┌──────────────────────┐  ┌──────────────────────┐
│ ILibraryScannerService │  │ ILibraryCache        │  │ IPlaylistService     │
│ (Recursive scan+Diff)  │  │ (JSON impl) [Ph10]   │  │ (JSON impl) [Ph6]    │
│ [Phase 10]             │  │                      │  │                      │
└────────────────────────┘  └──────────────────────┘  └──────────────────────┘

注：IFileDialogService 由 PlaylistViewModel（AddToQueue / ImportFolderToCurrent / Phase 18 导入导出对话框）+ PlaylistsViewModel（ImportFolderAsync / Phase 18 ImportPlaylistFileAsync）消费。
注：IPlaylistService 由 MainViewModel（启动读盘 + 关闭写盘 + debounce save）统一消费。
注：IPlaylistFileService (Phase 18) 由 PlaylistViewModel（追加导入 + M3U8 导出）与 PlaylistsViewModel（新建歌单导入）消费。
注（Phase 20）：从 `MainViewModel` 往下的整棵树住在 `D-player.Core` 里，两个 UI 壳共用同一份。**两壳各自的具体形态（谁在图顶端、每壳做哪三件事）以紧接其下的那段为准，本注不重述一遍。**

Phase 20 起这张图描述的是 **`D-player.Core` 内部的结构**，两个 UI 壳各占图顶端那个位置：
WPF 壳 = `App.xaml.cs` + `Views/MainWindow`；WinUI 壳 = `D-player.WinUI/App.xaml.cs` + `D-player.WinUI/MainWindow.xaml(.cs)`。
两壳都只做三件事：在 UI 线程 `AddDPlayerCore(configuration, dataPaths)` → 补注册本壳的 `IFileDialogService` → 把解析出来的 `MainViewModel` 交给自己的窗口。
VM/Service 这一整棵树**两壳共用同一份代码**，谁都不许往 Core 里塞 UI 类型。
```

### 4.2 服务生命周期

注册位置：`D-player.Core/Extensions/ServiceCollectionExtensions.cs`（`AddDPlayerCore(IConfiguration, DPlayerDataPaths)`，只注册共享层）；UI 相关服务（`IFileDialogService`）由**各壳在自己的入口处补注册**，所以 Core 的注册表里搜不到它。

| 服务 | 生命周期 | 说明 |
|------|----------|------|
| `IOptions<AppSettings>` | Singleton（框架） | 绑定 `appsettings.json` 的 `"Player"` 节，作为**启动默认快照** |
| `DPlayerDataPaths` | **Singleton** (Phase 20) | 用户数据目录（`Root` + `FolderName` → `Directory`）；**由各 UI 壳构造并注入**（WPF `"D-player"` / WinUI `"D-player-winui"`），三个持久化服务都依赖它 |
| `IPlaybackService` | **Singleton** | 持有 NAudio 设备资源，必须长生命周期 |
| `IFileDialogService` | Singleton（**注册在各壳里，不在 Core**） | 无状态；Core 只声明接口。WPF 壳注册 `Win32FileDialogService`，WinUI 壳注册切片期空实现 `WinUiFileDialogService` (Phase 20) |
| `ISettingsPersistence` | Singleton | 内部 `SemaphoreSlim` 并发互斥 |
| `IPlaylistService` | Singleton (Phase 6，替换 Phase 4 `IQueuePersistence`) | 独立 `SemaphoreSlim`，与 settings 文件锁互不影响；`LoadAsync` 绝不抛 |
| `ITrackMetadataReader` | Singleton (Phase 3) | 无状态，封装 z440.atl.core；`ReadAsync` 不抛 |
| `ILibraryScannerService` | **Singleton** (Phase 10) | 递归文件夹扫描 + Diff 计算；无状态 |
| `ILibraryCache` | **Singleton** (Phase 10) | JSON 元数据缓存 (`library-cache.json`)；内部 `SemaphoreSlim` |
| `IPlaylistFileService` | **Singleton** (Phase 18) | 播放列表文件读写门面；无状态；`ImportAsync` 绝不抛、`ExportAsync` 可抛（错误策略刻意不对称） |
| `IAudioDeviceManager` | Singleton（Stub） | 预留 |
| `IAudioOutputFactory` | Transient（Stub） | 预留；语义上由 `IPlaybackService` 创建即释放 —— 但**当前并无这条接线**：`NAudioPlaybackService` 在 `LoadAsync` 内直接经 `WasapiPlayerBuilder` 自建输出、不经此工厂，故该工厂注册后无任何消费方（见 §5.2 与 COUPLING.md §4） |
| `PlayerViewModel` | **Transient** (Phase 3) | Transport 子 VM；DI 中**必须先于** `PlaylistViewModel` 注册；Phase 13 订阅 `SpectrumDataAvailable`；Phase 14 仅持有 `EqualizerEnabled` observable（供 PlayerBar 🎚 按钮高亮） |
| `PlaylistViewModel` | **Transient**（经 `Func<Playlist, PlaylistViewModel>` 工厂） (Phase 3/6) | 队列子 VM；由 `PlaylistsViewModel` 用工厂按需创建，seed 为动态参数 |
| `PlaylistsViewModel` | **Singleton** (Phase 6) | 多歌单容器；WPF 壳的 View code-behind 经 `App.GetService<PlaylistsViewModel>()` 取用（双击跨歌单播放），WinUI 壳则经 `MainViewModel.Playlists` 拿到同一个实例 —— 两种取法都要求它必须单例 |
| `MainViewModel` | **Transient** | Strict Facade，构造时聚合两个子 VM |

> **Phase 14 注：** `EqualizerSampleProvider` **不是 DI 服务** —— 与 `SampleAggregator` 同样在 `NAudioPlaybackService.LoadAsync` 内按曲创建（每首新曲重建一条播放链），不注册进容器。`IPlaybackService` 仅新增 `EqualizerConfig` 属性（镜像 Phase 13 的 `SpectrumConfig`），0 新 DI 依赖、 0 新 ViewModel（方案 A：`EqualizerDialog` 直写 `IPlaybackService` + `ISettingsPersistence`）。

> **Phase 20 注（两壳并存带来的三条纪律）：** ① 容器必须在 **UI 线程**构造（`NAudioPlaybackService` 构造时捕获 `SynchronizationContext.Current`；WinUI 3 的 UI 线程上是 `DispatcherQueueSynchronizationContext`，实测可用，不需要壳自造上下文）。② 每壳必须**自己**注册 `IFileDialogService` —— Core 的 `PlaylistViewModel` 工厂是在**被调用时**才 `GetRequiredService<IFileDialogService>()`，漏注册不会在 `GetRequiredService<MainViewModel>()` 时炸，而是第一次构造歌单才炸；`Tests/Extensions/AddDPlayerCoreTests.cs` 钉住的是 **Core 图**，钉不住壳忘记自己那一条。③ 关闭时必须 `await MainViewModel.CleanupAsync()`（写最终断点位置 + 释放 WASAPI 设备）**之后再**放行关闭，且**不得**在 UI 收尾路径上同步 `Dispose` 容器 —— WPF 壳 `App.OnExit` 里那条同步 Dispose 是已知既有缺陷，WinUI 壳刻意没有把它复制过来。

### 4.3 关键设计决策

1. **配置双源**：启动只读默认值用 `IOptions<AppSettings>`；运行时可变状态（窗口、音量、最后播放路径）走 `ISettingsPersistence` 写入用户数据目录。两者通过 `record with` 不可变更新协同。

2. **位置事件节流**：`NAudioPlaybackService.PollPositionAsync` 以 ~30 Hz（33 ms）轮询，并通过 `PositionThrottle` 进一步节流，避免 UI 线程被淹没。

3. **UI 线程封送**：服务层捕获启动时的 `SynchronizationContext`（必然是 UI 线程，因为 `IPlaybackService` 由 `App.OnStartup` 间接解析），所有事件通过 `_syncContext.Post` 派发，VM 直接绑定即可。

4. **拖拽 Seek 防回跳**：`PlayerViewModel.IsSeeking` 标志在拖动期间抑制 `PositionChanged` → `Position` 写入，避免拖拽时滑块被服务回写"拽回去"。

5. **静音状态保留音量**：`ToggleMute` 把当前 `Volume` 存到 `_volumeBeforeMute`，置 `Volume=0`；取消静音恢复。拖动滑块若有非零值会自动取消静音。

6. **窗口可见性自愈**：`MainWindow.EnsureVisible()` 检查恢复的位置是否在虚拟屏内（防止外接屏拔掉后窗口飘到屏外），不在则回退到主屏居中。

7. **面向接口 + 占位实现**：`IAudioDeviceManager` / `IAudioOutputFactory` 已注册 Stub，便于后续替换为真实多设备/Exclusive/ASIO 实现而不动 VM。

8. **Strict VM Facade（Phase 3/6）**：`MainViewModel` 作为子 VM 容器（~130 行），暴露 `Player` + `Playlists` + `InitializeAsync` + `CleanupAsync`，集中 debounce save。`PlayerViewModel`（transport）与 `PlaylistViewModel`（队列）**互不持引用**，仅通过 `IPlaybackService` Singleton 间接协作（PlayerVM 订阅 transport 事件；PlaylistVM 单独订阅 `TrackEnded` 推进队列）。PlaylistVM 通过 `Container` 属性代理读取 PlaylistsVM 的全局 Shuffle/Repeat 状态。View 跨域命令（如 PlayerBar 上的 ⏮/⏭ 调用 PlaylistVM，🔀/🔁 调用 PlaylistsVM）通过 `{Binding DataContext.Playlists.<sub>.<cmd>, RelativeSource={RelativeSource AncestorType=Window}}` 跨级绑定到 MainWindow 的 DataContext 解决。

9. **持久化 read-modify-write 原子化（Phase 3）**：`ISettingsPersistence` 接口由 `SaveAsync(AppSettings)` 改为 `UpdateAsync(Func<AppSettings, AppSettings> mutator)`，把"读盘 → 应用 mutator → 写盘"整个序列封进 `SemaphoreSlim` 锁内，根治了 VM 写音量与 `Window_Closing` 写窗口尺寸的合并竞态（COUPLING.md 旧债 #3）。注意：`UpdateAsync` 内部 `.ConfigureAwait(false)`，故调用方若需要在 mutator 内读取 WPF DependencyProperty，必须在 await 之前先把值捕获到 UI 线程局部变量（见 `MainWindow.xaml.cs:Window_Closing`）。

10. **队列持久化与 settings 隔离（Phase 4）**：队列状态独立写到 `%LocalAppData%\D-player\queue.json`。**为什么不复用 settings.json**：(a) 队列条目数量级远大于 settings 字段，混在一起每次拖音量都会让队列 JSON 重新序列化；(b) settings 是高频更新（音量、窗口尺寸），queue 是低频快照（仅关闭时一次），写入节奏不同；(c) 模式失败隔离 —— queue.json 损坏不影响窗口/音量恢复。`IQueuePersistence` 故意比 `ISettingsPersistence` 简化：只有 `LoadAsync()` 与 `SaveAsync(QueueState)`，没有 `UpdateAsync` —— PlaylistViewModel 是唯一权威源（"读队列" = `SnapshotState()`），无需读-改-写原子化。

11. **Cancel-and-close 关闭模式（Phase 4）**：`MainWindow.Window_Closing` 是 `async void`；从 Phase 3 的 2 个 await（settings + CleanupAsync）涨到 Phase 4 的 4 个（+ snapshot + queue 写盘）后撞上致命 race —— 第一个 await yield 后 WPF 立即继续关闭流程，`ShutdownMode.OnLastWindowClose` 触发 `Application.Shutdown` → `Dispatcher.InvokeShutdown`，把后续 await 续延 post 到死 dispatcher 上永不运行（settings 通常能抢到，queue 永远丢）。修复：首次进入 `e.Cancel = true` 拦下，跑完所有异步工作后调 `Close()` 重新触发 Closing，第二次进入凭 `_isClosing` 标志直接 fall-through。这是 WPF `async void` Closing 的标准纪律 —— 任何新增 await 都该用此模式。

12. **拖拽 View/VM 边界（Phase 5）**：所有 OLE DragDrop 事件、命中测试（`ComputeInsertIndex`）、文件后缀过滤（`DragDropExtensions.FilterAudioPaths`）、Adorner 绘制（`DropInsertionAdorner`）都在 View 层；VM 仅暴露纯数据命令 —— `DropExternalFiles(IReadOnlyList<string>)` 和 `MoveTracks(MoveTracksArgs)`。VM 不感知 `DataObject` / `DragEventArgs` / `AdornerLayer`，仍可单测。

13. **重排算法用对象身份而非索引算术（Phase 5）**：`MoveTracks` 缓存被移动的 Track 引用 + 当前曲引用 + Shuffle 历史引用集合，删-插完成后用 `ReferenceEquals` 扫一遍 Queue 重建 `CurrentIndex` 和 `_shuffleHistory`。**不能用 `Queue.IndexOf`**：Track 是 `sealed record`（结构相等），多个 `CreateFallback("X.mp3")` 占位是结构相等但引用不同的对象，IndexOf 会返回首个结构等价匹配而非原始那一个，导致重排后 ▶ 跟到错的曲、Shuffle 历史塌陷。HashSet 同理需 `ReferenceEqualityComparer.Instance`。

14. **拖拽期间 in-flight 重入用 `_playToken++` 顶替（Phase 5）**：`PlaylistViewModel.RemoveTrack` 与新增的 `MoveTracks` 入口都自增 `_playToken`，关上 in-flight `PlayTrackAtAsync` 在 `await` 元数据期间 `Queue[index] = meta` 写到错位的窗口（COUPLING.md 旧债 #5）。`MoveTracks` 不调 `_player.Unload()` —— 重排不中断播放，NAudio 在另一线程继续推流，仅 ▶ 标记跟到新位置。

15. **GUID 主键, Name 仅展示（Phase 6）**：`Playlist.Id` 是 GUID 字符串，创建时一次性确定，不可变。`Name` 是显示名，可重命名、可重复，不破坏持久化绑定。

16. **Viewed vs Current 双指针（Phase 6）**：`ViewedPlaylist`（UI 选中，不持久化）与 `CurrentPlaylistId`（正在播放，持久化）解耦。用户切查看不打断播放，只有双击才跨歌单切换音频（Spotify 模型）。

17. **Schema v2 一次性自动迁移（Phase 6）**：`JsonPlaylistService.LoadAsync` 检测 v1 包成单条 "默认歌单"，立即写盘；迁移失败则吞掉，内存仍是 v2，下次重迁（幂等）。

18. **删除最后一个歌单自动重建（Phase 6）**：永远不存在 0 歌单状态；UI 不需要"空状态"分支。

19. **Debounce save 集中在 MainViewModel（Phase 6）**：子 VM 不感知存盘；`PlaylistsViewModel.StateChanged` → MainVM 500ms debounce → `SaveAsync`。`CleanupAsync` 同步 flush 一次。

21. **Phase 11 SettingsDialog 不加 SettingsViewModel（YAGNI）**：仅 DefaultVolume 可编辑，OutputMode/PreferredDeviceId 无消费者。对话框直接读写 `ISettingsPersistence`。Phase 12 音频设置有复杂交互（如切换输出模式需重载设备列表）时再引入 SettingsViewModel。

20. **debt #1 偿还（Phase 6/7）**：Phase 6 交付 `BytesToBitmapImageConverter`；Phase 7 完成数据流切换 —— `PlayerViewModel.AlbumArtBytes` 改为 `byte[]`，XAML 通过 Converter 转为 Frozen BitmapImage。VM 层不再依赖任何 WPF 类型。

22. **全局 Shuffle/Repeat（Phase 12 continued）**：Shuffle/Repeat 从 Playlist 级别提升到 PlaylistsViewModel 全局共享。原因：用户切换歌单时 Shuffle/Repeat 状态被重置（每歌单独立）的体验反直觉，且与主流播放器（Spotify / foobar2000）行为不一致。实现：PlaylistsViewModel 持有 `[ObservableProperty] ShuffleEnabled/RepeatMode`，PlaylistViewModel 通过 `Container` 属性代理读取；Playlist record 的同名字段保留但写入时固定 `false/Off`（向后兼容旧 queue.json）；QueueState 根级别新增 `ShuffleEnabled/RepeatMode` 持久化字段。

23. **图标矢量化（Phase 16）**：全部 emoji/字形图标替换为 `Themes/Icons.xaml` 描边矢量 `Geometry`（22 个 `Icon.*`，24×24 viewbox，Feather/Lucide 几何署名）。转换器（`PlayStateToIconConverter` / `RepeatModeToIconConverter` / 新增 `BoolToVolumeIconConverter`）返回 `Geometry` 而非字符串（经 `Application.Current.FindResource` 查资源）；▶ 标记由 TextBlock 改 `Path`（`Icon.PlayMarker` 实心三角 `Fill=AccentPrimary`，小尺寸下描边不清晰的例外）；活跃态经 `BoolToAccentBrushConverter` 着 `Path.Stroke`。动机：emoji 由系统字体渲染、彩色不可主题化、跨 Windows 版本不一致。VM 层移除 `VolumeIcon`（守住"VM 无 WPF 类型/无 emoji"纪律）。

24. **无边框 chrome + 自绘标题栏（Phase 17）**：`WindowStyle=None` + `WindowChrome(CaptionHeight=32, UseAeroCaptionButtons=False, GlassFrameThickness=0)` + 复用 `Views/Controls/TitleBar` UserControl；OS 仍负责拖动/双击最大化/Aero Snap（不做手写 DragMove）。自绘按钮必须 `shell:WindowChrome.IsHitTestVisibleInChrome=True`（否则点击被 caption 拖动吞掉）。最大化时给 `Window.Content` 根元素加**常量式工作区边距**（`WorkArea` 偏移 + `WindowResizeBorderThickness`，不读窗口实际边界 —— 布局时序会让 right/bottom 边距偏大留空）。应用：MainWindow `ShowMaximize=True`（三键）；3 个对话框 `ShowMaximize=False`（仅关闭键）。

25. **深色控件隐式样式（Phase 17）**：`Controls.xaml` 扩充 ComboBox（自绘 ToggleButton 可点击表面 `ClickMode=Press` + 深色 Popup + ComboBoxItem 悬停/选中态）、CheckBox（深色方框 + accent 勾）、ScrollBar（横竖双模板、仅 track+thumb 隐藏箭头）、ToolTip/ContextMenu/MenuItem/Separator 深色。纯资源字典改动，全局生效，0 代码路径变化。

26. **播放列表文件导入导出（Phase 18）**：新增门面服务 `IPlaylistFileService`（Singleton，无状态）集中「编码探测 → 格式解析 → 路径归一化 → URL/后缀/存在性过滤计数」，两个 VM 共用同一条管道。**错误策略刻意不对称**：`ImportAsync` 绝不抛（读侧对齐 `JsonPlaylistService.LoadAsync`，任何失败退化为空结果，由 View 的"没有可导入的条目"报告兜住）；`ExportAsync` 让 `IOException`/`UnauthorizedAccessException` 冒到 VM 转错误文案（写侧对齐设置/EQ 对话框的 try/catch + 错误框）。**双入口分层**沿用既有约定：容器级（侧边栏按钮/拖到侧边栏）= 新建歌单（`PlaylistsViewModel.ImportPlaylistFileAsync`，seed 的 `SourceFolder = null` → 普通歌单，不写 library cache）；歌单级（工具栏"导入列表"/拖到列表区）= 追加当前歌单（`PlaylistViewModel.ImportPlaylistFileAsync`，复用既有 `DropExternalFiles`，不新增元数据依赖）。VM 方法的 `presetPath` 参数是拖拽入口与对话框入口共用同一管道的接点（非空跳过对话框）。**GBK 解码不新增 NuGet 包**：`System.Text.Encoding.CodePages` 在 net10.0 框架隐含，显式 `PackageReference` 会触发 NU1510 警告破坏 0 警告门禁，`D-player.csproj` 未改动；CodePages provider 在 `PlaylistFileEncoding` 静态构造函数注册（`Encoding.GetEncoding(936)` 只出现在该类内部，"注册早于解码"是类型不变量而非启动顺序约定，`App.xaml.cs` 未改动）。**分层纪律**：VM 只返回结构化 `PlaylistImportReport`，中文文案由 `PlaylistImportReportFormatter` 组装（四个入口共用；Phase 18 时在 `Views/Controls`，Phase 20 搬进 `D-player.Core/ViewModels` 以便第二壳复用）；唯一例外是导出错误文案 `$"导出失败：{ex.Message}"` —— 单一分支直传、只有一个调用方（设计稿 §7.5）。

27. **共享层抽成 WPF-free 的 `D-player.Core`（Phase 20）**：Models / Services / ViewModels / Configuration / DI 注册整体搬进类库（`net10.0-windows`，**不开** `UseWPF`），两个 UI 壳各引用它。搬家只清掉两处真实的 WPF 泄漏：`PlaylistViewModel.SortedView`（`ICollectionView`）删除 —— `SortBy` 本来就把 `Queue` 物理重排，列表直接绑 `Queue` 即可，视图层没有任何东西需要"排序视图"这个中间概念；`Win32FileDialogService`（`Microsoft.Win32`）从 Core 搬回 WPF 壳，Core 只留 `IFileDialogService` 接口与消费点。**动机**：VM/Service 层早在 Phase 7 就已做到无 WPF 类型，物理隔离后第二壳才可能复用同一份逻辑而不引入 WPF 依赖。**WPF 侧行为零变化**是这一条的硬约束。

28. **门禁走解决方案筛选器 `D-player.slnf`；WinUI 有一条"有明确触发条件的壳侧检查"，它不是门禁（Phase 20）**：词汇只有三层，别再混——**① 门禁 = 两条，每次改动必跑**：`dotnet build D-player.slnf -c Debug --nologo -v q`（0 警告 0 错误）+ `dotnet test D-player.slnf -c Debug -v q`（180 通过 / 0 失败）；**② 壳侧检查 = WinUI 单工程构建** `dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q`（0/0），**没有任何自动化会跑它**，所以必须说清什么时候跑：**`D-player.Core` 的公开面一变就跑**——构造参数、`IFileDialogService` 的新成员、`HandleDoubleClickPlay` 的元数、`ViewedPlaylist` 的 setter 这类改动，让 `.slnf` 门禁照样全绿，而用户即将拿去对比的那只壳已经在编译错误里烂掉；改到 WinUI 本身时同样要跑；首次 restore 之后是增量构建，实测约 9 秒，没有理由省；**③ 整解 `D-player.sln` 构建 = 可选**，只为确认 IDE「Build Solution」没被打挂。**纪律：一条命令只有被记得跑才会跑——把需要手跑的命令叫成"门禁"，就等于宣称它会自动跑**（此前 README 写过"整解构建只在改到 WinUI 时跑"，触发条件指错了方向；本项目历史上还把 ② 叫过"门禁三"、把 ①② 合称"门禁三件套"，这些说法一并作废）。两条门禁 + 壳侧检查可由 `tools/verify-gates.ps1 -Fast | -Full` 一次跑完（逐步 fail-fast：任一步非零退出、**或两个构建步骤在 MSBuild 安静日志里报出任一行警告**即中止——口径是 **0 警告 0 错误**，而 `dotnet build` 只报警告时退出码仍是 0，光看退出码会把违约的构建印成 ok；`dotnet test` 那一步刻意不带 `--nologo`）。**筛选器只含 Core + WPF 壳 + Tests**，WinUI 刻意排除在外：Windows App SDK 一次 restore 拉 9 个子包、首次约 8.1 分钟、自包含输出目录很大，把它放进每次构建/测试会让"改一行共享层跑全量"的成本翻几十倍。代价也要写明白：**WinUI 的 XAML 编译、左栏（pane）逻辑、关闭落盘因此没有任何自动回归**，`Tests/Extensions/AddDPlayerCoreTests.cs` 只能钉住 Core 的 DI 图。工作实例见 §8.2 末尾与 `D-player.WinUI/MainWindow.xaml.cs` 顶部那段 using 探针注释（A5：一次"死引用整理"报错了对象，而 `.slnf` 门禁对 WinUI 完全无感，只有单壳构建会发现）。将来若决定续投 WinUI，"把它纳入门禁"仍是一条要单独拍板的决策项。

29. **用户数据目录由 UI 壳注入（Phase 20）**：`DPlayerDataPaths(Root, FolderName)`（`Directory` 是组合属性）取代此前硬编码在三个持久化服务里的 `Environment.SpecialFolder.LocalApplicationData` + `"D-player"`；`AddDPlayerCore(IConfiguration, DPlayerDataPaths)` 因此多了第二个参数。**为什么必须分开**：两壳的 `queue.json`/`settings.json` 写入者之间没有任何跨进程协调，共用目录会互相覆盖对方的队列与音量。WPF 壳继续用 `D-player`（老用户数据不搬家，`LegacyDataMigration` 的 UmaPlayer 迁移也继续只属它），WinUI 壳用 `D-player-winui`。

30. **两壳共用同一条双击入口，不新增并行 API（Phase 20）**：WinUI 的双击走 `PlaylistsViewModel.HandleDoubleClickPlay(target, index)` —— 与 WPF `Views/Controls/PlaylistView.xaml.cs` 完全同一个方法。实施中曾按计划在 Core 新增过 `PlaylistViewModel.PlayIndexAsync(int)`，随后被推翻并删除：它直进 `PlayTrackAtAsync`，绕开了 `PlayTrackAt` 开头的 `_shuffleHistory.Clear()`（"视为新会话"），于是随机模式下两壳的双击语义会静默分叉 —— 而这正是本阶段要对比的那个手势。留下的纪律：**跨壳的同一手势必须走 Core 里同一个公开入口**；宁可壳侧少几行，也不要复制一份"看起来一样"的实现。

31. **每壳各自负责关闭落盘，且不得同步 Dispose 容器（Phase 20）**：WPF 用 `Window_Closing` 的 cancel-and-close（Phase 4 起）；WinUI 用 `AppWindow.Closing` → `args.Cancel = true` → `await MainViewModel.CleanupAsync()` → `Close()`，复刻同一形状（`D-player.WinUI/MainWindow.xaml.cs`）。少了这一步，最终断点位置只能靠 30 秒节流或"先暂停"才落盘。同时 WinUI **不**在关闭路径上同步 `Dispose` 它的 `ServiceProvider`：`MainViewModel` 只实现 `IAsyncDisposable`，同步 Dispose 会抛（WPF 壳 `App.OnExit` 那条既有缺陷正是如此），本阶段刻意不把它复制到第二壳。

32. **WinUI 的 Mica 依赖"根背景 Transparent"这条约定（Phase 20）**：`Window.SystemBackdrop = new MicaBackdrop()` 挂上后，材质只在**没有不透明背景刷**的表面后面可见；根 `Grid` 必须是 `Background="Transparent"`，否则整片材质被页面底色盖住，肉眼与截图都像"材质没生效"。并且 `DWMWA_SYSTEMBACKDROP_TYPE` 对组合器挂载的 backdrop **不是有效探针**（本项目实测恒为 0）—— 判据只能是"透明表面后的像素是否随窗外内容变化"。实测数据与三行对照已写进 `D-player.WinUI/MainWindow.xaml` 顶部注释。

33. **仓库根的兄弟目录必须进根工程的 glob 排除集（Phase 20）**：`D-player.csproj` 在仓库根，SDK 默认 `**/*.cs` / `**/*.xaml` 会把 `D-player.Core/**`、`D-player.WinUI/**`（以及原有的 `Tests/**`）扫进 WPF 程序集，出现重复类型或不存在 `Microsoft.UI.Xaml` 导致的 MC3074/CS0234。每加一个仓库根级别的兄弟工程，就要给它补一组 `Compile/Page/ApplicationDefinition/Resource/None/EmbeddedResource Remove`。

---

## 5. 模块详解

> **Phase 20 起的路径说明**：下面 §5.1–§5.3（`Models` / `Services` / `ViewModels`）与 §4.2 的注册表都在 **`D-player.Core/`** 下；`Views` / `Converters` / `Themes` 在 WPF 壳（仓库根），`D-player.WinUI` 单独见 §5.7。小节标题沿用层名不带工程前缀。

### 5.1 `Models`

- **`Track`**：不可变 record。字段：`FilePath` / `Title` / `Artist` / `Album` / `Genre` / `Year` / `SampleRate` / `AlbumArt` / `Duration` / `TrackNumber`（元数据曲目号，Phase 12 continued 新增）。`Duration` 与 `SampleRate` 在加载时由 `NAudioPlaybackService.LoadAsync` 通过 `track with { Duration=..., SampleRate=... }` 补齐。`AlbumArt` 为原始字节数组，由 VM 转 `BitmapImage`（限 200px、`Freeze()` 跨线程安全）。**注意 record 的结构相等：** 两个 `CreateFallback("X.mp3")` 占位 Track 在结构上相等 —— 任何按相等性查找/去重的代码（`IndexOf` / 默认 `HashSet<Track>`）都会塌陷它们。Phase 5 重排算法因此改用引用身份（`ReferenceEquals` + `ReferenceEqualityComparer.Instance`）。
- **`PlayState`**：`Stopped / Playing / Paused`。
- **`RepeatMode`**：`Off / List / One`（Phase 2）。
- **`QueueState`**（Phase 4）：不可变 record；`SchemaVersion=3`（v2→v3 新增 `Playlist.SourceFolder`，无迁移：缺失字段反序列化为 null）/ `Playlists: IReadOnlyList<Playlist>` / `CurrentPlaylistId` / `ShuffleEnabled: bool` / `RepeatMode: RepeatMode`（Phase 12 continued：全局播放模式，所有歌单共享，从 Playlist 级别提升到 QueueState 根级别）。**只持久化路径与队列态**，不携带 Track 元数据或封面 —— 启动时由 `PlaylistsViewModel.Hydrate` 为每条路径创建占位 Track，用户首次播放时由 `PlayTrackAtAsync` 升级为完整元数据。
- **`MoveTracksArgs`**（Phase 5）：不可变 record；`SourceIndices: IReadOnlyList<int>`（升序无重复，每项 ∈ [0, Queue.Count)） / `TargetIndex: int`（∈ [0, Queue.Count]，i 表示插到 i 之前；Count 表示末尾）。由 View 层 Drop handler 构造，这些不变量由 View 保证（VM 信任入参，无校验代码）。
- **`LibraryCacheEntry`**（Phase 10）：不可变 record；`FilePath` / `Title` / `Artist` / `Album` / `Genre` / `Year` / `Duration` / `SampleRate` / `TrackNumber`（Phase 12 continued 新增，元数据曲目号）。不含 `AlbumArt` 字节——按需加载，保持缓存文件体积小。
- **`AudioDeviceInfo`**：`(Id, Name, IsDefault)`，目前仅类型存在。

### 5.2 `Services/NAudioPlaybackService`

| 成员 | 说明 |
|------|------|
| `LoadAsync(Track)` | 在 `Task.Run` 上：销毁旧播放链 → 新建 `MediaFoundationReader` → `ToSampleProvider` → **`EqualizerSampleProvider`（Phase 14 均衡器中间件）** → **`SampleAggregator`（Phase 13 频谱中间件）** → `VolumeSampleProvider` → `WasapiPlayer`（Phase 19：`new WasapiPlayerBuilder().WithSharedMode().WithEventSync().WithLatency(100).Build()`，与迁移前的 `WasapiOut(Shared, 100ms)` 语义等价；`WasapiPlayer` 无公开构造函数，只能经 builder 建立，之后仍按 `IWavePlayer` 使用）；触发 `DurationChanged` / `TrackChanged` · Phase 3：在 DurationChanged 之前先广播 PositionChanged(Zero)，防止切到时长更短的曲时旧 Position 与新 Duration 并存（"4:05 / 3:20" glitch） |
| `Play / Pause / Stop` | 委派给 `IWavePlayer`；`Stop` 同时将 `CurrentTime` 归零（保留底层资源，再 `Play()` 会重播同一首） |
| `Unload()` | **完全释放**底层 reader/wavePlayer，清掉 `_currentTrack`；之后 `Play()` 是 no-op。**Phase 3：同时广播 `TrackChanged(null) + DurationChanged(Zero) + PositionChanged(Zero)`** 让 VM 清屏（标题/封面/时长/进度全归零）。`PlaylistViewModel.UnloadCurrentTrack` 在清空队列/删当前曲时调用 |
| `Seek(TimeSpan)` | 写 `reader.CurrentTime` 后**主动广播 PositionChanged**（Phase 3：暂停态下 PollPositionAsync 已退出，否则进度条不刷新，看上去像"没跳转"）；通过 `ClampToDuration` 截到 [0, TotalTime] |
| `Volume { get; set; }` | `Math.Clamp(0..1)`；运行时写入 `VolumeSampleProvider.Volume` |
| `PollPositionAsync` | 仅在 `PlaybackState==Playing` 时循环；每 33ms 派发一次 `PositionChanged` · Phase 3：经 `ClampToDuration` 截断，避免解码器尾部浮点越界 |
| `OnPlaybackStopped` | 区分 (1) 异常 → `PlaybackError`；(2) 自然播完（距 `TotalTime` ≤ 200ms） → `TrackEnded`；(3) 用户 `Stop` → 仅 `Stopped` |
| `SpectrumConfig { get; set; }` | Phase 13：运行时可更新；setter 把 `Enabled` 传播到 in-flight `SampleAggregator`（禁用后 FFT 停转，不空耗 CPU） |
| `EqualizerConfig { get; set; }` | Phase 14：运行时可更新；setter 存字段 + `_equalizer?.Update(value)` 把配置实时下发到在链 EQ provider（语义对齐 `SpectrumConfig`；链未建时仅存字段待 `LoadAsync` 拾取） |
| `SpectrumDataAvailable` 事件 | Phase 13：`SampleAggregator.SpectrumDataReady`（音频线程）→ `OnSpectrumDataReady` 经 `_syncContext.Post` 封送到 UI 线程再广播 |
| `Dispose()` | 拆事件、停止、释放 reader/wavePlayer；`DisposePlayback` 先解绑并清空 `_sampleAggregator`（Phase 13），并置空 `_equalizer`（Phase 14，无事件订阅，随播放链释放） |

### 5.2a `Services/SampleAggregator` + `Models/SpectrumConfig`（Phase 13）

`SampleAggregator` 是实现 `ISampleProvider` 的**透明中间件**，插在 `MediaFoundationReader.ToSampleProvider()` 与 `VolumeSampleProvider` 之间：原样传递 PCM（`Read` 返回源读取数），同时截取样本做 FFT。

处理管线（每填满一个 FFT 缓冲区触发一次）：
1. **多声道混单声道**：立体声逐帧取各声道均值，避免声道相位干扰频谱
2. **汉宁窗**：FFT 前统一加窗（`0.5*(1-cos(2πj/(N-1)))`），重叠时窗口不错位
3. **FFT**：`NAudio.Dsp.FastFourierTransform.FFT`，`SpectrumConfig.FftSize`（当前 **8192**，历经 1024→2048→8192 提升低频分辨率）
4. **对数频率分组**：20Hz–16kHz 按对数均分到 `BarCount`（**32**）条柱（人耳对低频更敏感）
5. **RMS + dB + gamma**：每桶取均方根幅度 → 增益 8x → `20*log10` 映射到 -60~0dB → 归一化 [0,1] → `pow(x, 0.8)` gamma 增强对比
6. **50% 重叠**：保留缓冲区后半段，更新率翻倍，动画更平滑
7. **复制数组触发事件**：`SpectrumDataReady?.Invoke(_spectrumData.ToArray())`（复制防止订阅者篡改共享缓冲区）

`SpectrumConfig`（不可变 record）：`Enabled` / `BarCount=32` / `Sensitivity`（0.5~2.0）/ `Smoothing`（0.0~0.95）/ `FftSize=8192`。`Enabled` 运行时可切（`NAudioPlaybackService.SpectrumConfig` setter 传播到聚合器）。

**线程边界：** `SpectrumDataReady` 在 **NAudio 音频线程**触发；`NAudioPlaybackService.OnSpectrumDataReady` 负责封送到 UI 线程。灵敏度增益 + 指数平滑在 `PlayerViewModel.HandleSpectrumData`（UI 线程）做，**不在此重复 32-bar 映射**（聚合器已完成）。

### 5.2b `Services/EqualizerSampleProvider` + `Models/EqualizerConfig`（Phase 14）

`EqualizerSampleProvider` 是实现 `ISampleProvider` 的**透明中间件**，插在 `MediaFoundationReader.ToSampleProvider()` 与 `SampleAggregator` 之间（**在 SampleAggregator 之前** → Phase 13 频谱可视化反映 EQ 处理后的信号）。10 段图形均衡器：ISO 倍频程中心频率 31/62/125/250/500/1k/2k/4k/8k/16k Hz，每段 ±12 dB 峰值滤波（`BiQuadFilter.PeakingEQ`，Q≈1.1 约一个倍频程带宽）+ preamp（−12~+12 dB）。

关键实现点：
1. **插入点（LoadAsync 建链）**：`ToSampleProvider → EqualizerSampleProvider → SampleAggregator → VolumeSampleProvider → WasapiPlayer`（Phase 19 起链尾是 `WasapiPlayer`）；EQ 在频谱聚合器之前，故频谱与 EQ 后听感一致。
2. **每声道独立滤波**：`BiQuadFilter?[][] _filters`，索引 `[channel][band]`；立体声左右声道各持一组滤波器，避免共享实例串扰滤波状态（x1/x2/y1/y2 延迟线）。
3. **preamp 线性增益**：`EqualizerConfig.PreampLinearGain = 10^(PreampDb/20)`，在逐级滤波前对样本统一缩放（多段提升时留余量防削波）。
4. **`Update` 就地 `SetPeakingEq`**：运行时改配置**不重建** `BiQuadFilter`，而是对已有实例调 `SetPeakingEq(...)` 就地重算系数 —— 保留 x1/x2/y1/y2 状态（延迟线不清空），拖动滑块时无爆音/咔哒声。首次（slot 为 null）才 `PeakingEQ` 新建。
5. **Nyquist 旁路**：中心频率 ≥ 采样率一半（`sampleRate/2`）的频段置 null slot 旁路 —— PeakingEQ 在 ≥Nyquist 时不稳定（如 44.1kHz 采样下 16kHz 段接近奈奎斯特，低采样率素材则直接旁路）。
6. **buffer 粒度 `lock` 线程模型**：`Read`（NAudio 音频线程）与 `Update`（UI 线程）共用一把 `object _lock`，锁粒度为一次缓冲区处理，防止撕裂系数。`_enabled` 用 `volatile bool` 且在锁外读取（Read 热路径 bool 读原子，最坏一次陈旧缓冲区，无撕裂风险）。
7. **`Enabled=false` 透明旁路**：禁用时 `Read` 直接返回源读取数（零处理成本），原样透传 PCM —— 默认关闭保证不影响存量用户既有听感。

`EqualizerConfig`（不可变 record）：`Enabled`（默认 false）/ `PreampDb`（默认 0）/ `BandGainsDb`（`IReadOnlyList<double>`，10 段，默认全 0）/ `Preset`（默认 `Flat`）。派生 `PreampLinearGain`。静态工厂 `EqualizerConfig.Create(enabled, preampDb, bandGainsDb, preset)` 把 preamp 与各段增益 Clamp 到 [−12, 12] 并规整段数（不足补 0、超出截断），用于把持久化/外部输入安全转为运行时配置。**注意 record 相等语义：** `BandGainsDb` 是引用类型，record 合成的相等对该属性按引用比较（不逐段比较数值）；`EqualizerPresets.Match` 需值相等时用 `SequenceEqual` 自行按序列比较。

`EqualizerPresets`（纯静态数据类，无副作用，可脱离 UI 单测）：常量 `BandCount=10` / `MinGainDb=−12` / `MaxGainDb=12` / `Q=1.1f` / `Flat="Flat"` / `Custom="Custom"`；`CenterFrequencies`（10 个 ISO 倍频程中心频率，固定常量、不持久化）；`All`（9 个内置预设有序列表 Flat/Rock/Pop/Jazz/Classical/Dance/Bass Boost/Treble Boost/Vocal，不含 Custom）；`Names`（下拉项名，不含 Custom）；`TryGet(name, out gains)`（按名查曲线，返回副本）；`Match(gains)`（返回精确匹配的预设名，无匹配返回 `Custom`）。

**架构（方案 A）：** 无新 DI 服务、无新 ViewModel。`EqualizerSampleProvider` 在 `NAudioPlaybackService.LoadAsync` 内按曲创建（同 `SampleAggregator`）；`IPlaybackService` 仅增 `EqualizerConfig` 属性；`EqualizerDialog` 直写 `IPlaybackService`（实时预览）+ `ISettingsPersistence`（保存）；`PlayerViewModel` 仅持 `EqualizerEnabled` observable 供 PlayerBar 🎚 按钮高亮。

### 5.3 `Services/JsonSettingsPersistence`

- 路径：`%LocalAppData%\D-player\settings.json`
- 启动时若不存在则返回 `new AppSettings()`（默认值由 record 初始化器给出，**与 `appsettings.json` 不重复绑定**）
- **Phase 3 重构**：接口由 `SaveAsync(AppSettings)` 改为 `UpdateAsync(Func<AppSettings, AppSettings> mutator)`，把整段"读盘 → 应用 mutator → 写盘"封进 `SemaphoreSlim(1,1)` 临界区。调用方仅需提供 `s => s with { Field = newValue }`，再无合并竞态
- 读盘失败（损坏/权限）→ 以 `new AppSettings()` 为起点喂给 mutator，写盘照常；写盘失败则抛出（关闭流程调用方自行 catch）
- 私有 `ReadFromDiskNoLockAsync()`：调用方负责持锁；供 `LoadAsync` 与 `UpdateAsync` 共用

### 5.3a `Services/JsonPlaylistService`（Phase 6，替换 JsonQueuePersistence，Schema v3）

- 路径：`%LocalAppData%\D-player\queue.json`
- 与 settings 持久化结构对称：独立 `SemaphoreSlim(1,1)`、`WriteIndented=true`、`JsonStringEnumConverter`（让 `RepeatMode` 序列化成字符串而非整数，跨版本稳定且方便手动调试）
- 接口仅 `LoadAsync()` / `SaveAsync(QueueState)`，**故意没有 `UpdateAsync`** —— PlaylistsViewModel 是队列状态的唯一权威源，无需读-改-写合并
- `LoadAsync` 隐式契约：**绝不抛**（catch-all 静默 fallback 到 `new QueueState()`）。文件不存在/JSON 损坏/版本号不匹配/反序列化得 null 全部走同一回退分支；旧文件保留供用户排查。内置 v1→v2 一次性迁移（单条"默认歌单"）+ v2→v3 字段补充（`SourceFolder` 缺失即 null，无需迁移）
- `SaveAsync` 失败抛出，由 `MainWindow.Window_Closing` 自行 catch（与 settings 写盘失败行为对称：用户下次启动队列丢失，但不打扰关闭流程）

### 5.3b `Services/PlaylistFiles`（Phase 18，播放列表文件读写门面）

新目录，8 个文件；对外只暴露 `PlaylistFileFormats` / `IPlaylistFileService` / `PlaylistFileService` / `PlaylistImportResult`（public），编码与解析器全部 `internal`：

- **`PlaylistFileFormats`**：后缀白名单 `Extensions = [".m3u", ".m3u8", ".pls"]`（本主题唯一来源，`DragDropExtensions.PlaylistFileExtensions` 代理到这里）+ `IsPlaylistFile(path?)` 静态判定（View 层拖拽判定不该为一个后缀判断去 DI 取服务）+ 对话框过滤器字符串 `OpenFilter` / `SaveFilter`。
- **`PlaylistFileEncoding`**（internal static）：字节 → 文本。探测链：**BOM（EF BB BF → UTF-8；FF FE → UTF-16LE；FE FF → UTF-16BE）→ 无 BOM 用"非法字节即抛"的严格 UTF-8 试解码 → `DecoderFallbackException` 回退 GBK(936)**。传统 `.m3u` 在中文 Windows 上多为 ANSI/GBK，只按 UTF-8 读会让整表中文路径变成 U+FFFD 进而被"文件缺失"过滤掉。CodePages provider 在**静态构造函数**注册（`Encoding.RegisterProvider` 幂等）—— `Encoding.GetEncoding(936)` 只出现在本类内部，注册永远早于解码，不依赖 App 启动顺序；csproj **不需要**加 `System.Text.Encoding.CodePages` 包（net10.0 框架隐含，显式引用触发 NU1510）。
- **`M3uParser`**（internal static）：逐行取"非 `#` 开头的非空行"；`#EXTM3U` / `#EXTINF` / 注释一律忽略（标题与时长只信 ATL 从音频文件读到的结果）。
- **`PlsParser`**（internal static）：INI 风格，只取 `File<N>=` 的值（key 大小写不敏感），**按出现顺序**返回而非按序号排序；段头 / `#`、`;` 注释 / `Title<N>` / `Length<N>` / `NumberOfEntries` / `Version` 全部忽略。
- **`PlaylistImportResult`**：服务层结果 record —— `SuggestedName`（列表文件名去后缀；取不到名时 "导入的歌单"）/ `AcceptedPaths`（已归一化为绝对路径，保留原顺序与重复，**不去重**）/ `TotalEntries` / `SkippedMissing` / `SkippedUnsupported`（三个计数互斥，一个条目只进一个桶）。
- **`PlaylistFileService`**：门面实现（Singleton，无状态），`ImportAsync` 编排：读全部字节 → `PlaylistFileEncoding.Decode` → 按后缀选解析器（`.pls` → PlsParser，其余 → M3uParser）→ `Classify`（URL 前缀 `http://`/`https://`/`mms://`/`rtsp://` → unsupported；相对路径以**列表文件所在目录**为 base 经 `Path.GetFullPath` 归一化，同时消掉 `..\`；后缀判定**先于**存在性判定，白名单引用 `AudioConstants.AudioExtensions`；`File.Exists` 为假 → missing）。
- **`M3u8Writer`**（internal static）：`Track` 列表 → extended M3U8 文本。形状：首行 `#EXTM3U`，每条两行 `#EXTINF:{秒},{Artist - Title}` + 绝对路径；UTF-8 **无 BOM**、`\r\n` 行尾。秒 = `(int)Math.Round(Duration.TotalSeconds)`，未知（≤ 0）写 `-1`；Artist 为空只写 Title（不留悬空 `" - "`）；Title 也为空写文件名；标题里的 `,` 无需转义（`#EXTINF` 只按第一个逗号切分）。

**读侧不抛 / 写侧抛的不对称（接口契约）**：`ImportAsync` 把 IO/权限/非法路径异常全部吞掉退化为空结果（用户仍有"没有可导入的条目"反馈而不是崩溃）；`ExportAsync` 必须让异常冒到 VM 转成错误文案，由 View 用 `ConfirmDialog.ShowError` 弹出。**不要**把 `ExportAsync` 包成不抛。

### 5.4 `ViewModels/MainViewModel`（Strict Facade，~130 行）

Phase 6 后暴露四个公开成员：

```csharp
public PlayerViewModel Player { get; }
public PlaylistsViewModel Playlists { get; }
public Task InitializeAsync();
public Task CleanupAsync();
```

构造由 DI 注入 `(PlayerViewModel, PlaylistsViewModel, IPlaybackService, IPlaylistService)`。`InitializeAsync` 读 queue.json + Hydrate 容器 + 触发后台文件夹扫描。debounce save 500ms 集中在本类（`StateChanged` → `ScheduleSave` → `SaveAfterDelayAsync`）。

`CleanupAsync()` 顺序固定：解绑 `StateChanged` → 取消 debounce → flush in-flight save → 同步 `BuildSnapshot` + `SaveAsync` → 遍历所有 `PlaylistVM.Cleanup()` → `await Player.CleanupAsync()` → `_player.Dispose()`。**必须先解绑后 Dispose**，避免事件 handler 在底层资源销毁后被回调。

**架构不变量（COUPLING.md §5）：** `PlayerViewModel` 与 `PlaylistViewModel` **互不持引用**，仅共享 `IPlaybackService` Singleton；本 Facade 不暴露 Player/Playlists/InitializeAsync/CleanupAsync 之外的任何成员（否则倒退为"转发 Facade"反模式）。

### 5.4a `ViewModels/PlayerViewModel`（Transport 子 VM，~320 行）

源生成器属性：`_isSeeking`, `_position`, `_duration`, `_playState`, `_currentTrack`, `_albumArtBytes`（Phase 7：byte[]，非 BitmapImage）, `_volume`, `_isMuted`；**Phase 13 频谱**：`_spectrumData`(float[32]), `_spectrumEnabled`, `_spectrumSensitivity`, `_spectrumColorTheme`, `_spectrumSmoothing` + 私有 `_smoothedSpectrum`；**Phase 14 均衡器**：`_equalizerEnabled`（供 PlayerBar 🎚 按钮激活态高亮）。派生：`VolumeIcon`（🔇/🔊）、`SampleRateText`、`PositionNormalized`（0..1）、`SpectrumColorThemes`（["紫色","蓝色","绿色","彩虹"]）。

**Phase 10 新增成员：**
- `SourceFolder`（`string?`，构造时从 `Playlist` seed 传入）：文件夹绑定歌单的源路径；null 表示普通手动歌单
- `HasSourceFolder`（`bool`，派生）：sidebar DataTemplate 用，决定是否显示文件夹图标
- `IsScanning`（`[ObservableProperty] bool`）：由 `PlaylistsViewModel` 设置，指示后台扫描进行中
- `HasScanError`（`[ObservableProperty] bool`）：由 `PlaylistsViewModel` 设置，指示最近一次扫描失败

构造时订阅 `IPlaybackService` 的 **6 个事件**（`PositionChanged / StateChanged / DurationChanged / TrackChanged / PlaybackError` + **Phase 13 `SpectrumDataAvailable`**），并阻塞读盘加载持久化音量 + 频谱设置（`_isInitializing` 标志抑制初始化期的写盘）。**不订阅 `TrackEnded`**（那是 PlaylistViewModel 的职责）。

`[RelayCommand]`：`SeekStarted / SeekCompleted(normalized) / PlayPause / ToggleMute` / **`ToggleSpectrum`（Phase 13）**。

`partial void OnVolumeChanged(value)`：同步到 `_player.Volume` → 拖滑块到非零自动取消静音 → `_persistence.UpdateAsync(s => s with { DefaultVolume = value })`。

**Phase 13 频谱管线：**`HandleSpectrumData(float[] rawData)` —— 聚合器已完成 FFT→32-bar 映射，此处仅 `rawData.ToArray()` 复制 → 逐柱乘灵敏度增益（Clamp 0.5~2.0）→ 指数移动平均平滑（`_smoothedSpectrum[i]*smoothing + data[i]*(1-smoothing)`，smoothing Clamp 0~0.95）→ 写 `SpectrumData` 触发绑定；`SpectrumEnabled==false` 时直接 return（不更新）。四个 `OnSpectrum*Changed` 钩子调 `SaveSpectrumSettings()` 持久化；`OnSpectrumEnabledChanged` 额外把 `_player.SpectrumConfig with { Enabled=value }` 回写，禁用后 FFT 停转。

**Phase 14 均衡器：**`LoadEqualizerSettings(AppSettings)`（构造期 `Initialize()` 调用）—— 先设 `EqualizerEnabled` observable（构造期 `_isInitializing=true`，`OnEqualizerEnabledChanged` 不写盘），再把完整配置（preamp/10 段/预设）用 `EqualizerConfig.Create(...)` 下发到 `_player.EqualizerConfig`，首次播放即生效。`OnEqualizerEnabledChanged(value)`：`_player.EqualizerConfig = _player.EqualizerConfig with { Enabled=value }` 传播到播放链，构造期跳过写盘，否则 `UpdateAsync(s => s with { EqualizerEnabled=value })` 持久化。（EQ 的 preamp/各段/预设由 `EqualizerDialog` 直写 service + persistence，不经 VM；VM 仅持启用态供按钮高亮。）

`CleanupAsync()`：解绑 **6 个事件**（含 `SpectrumDataAvailable`）+ 用观察属性 `Volume`（**非**陈旧的 `_settings` 字段）持久化最后一次音量。**不 Dispose `IPlaybackService`**（PlaylistViewModel 还在用，Facade 层统一 Dispose）。

`HandleTrackChanged(Track? track)`：track 为 null 时把 `CurrentTrack` 和 `AlbumArtImage` 一起置 null（XAML 的 `FallbackValue='No track loaded'` 处理标题显示）。

### 5.4b `ViewModels/PlaylistViewModel`（队列子 VM，~650 行 / Phase 4 增加 LoadFromDisk + SnapshotState + PlayCurrent / Phase 10 增加 SourceFolder + IsScanning / Phase 12 continued 增加排序 + ImportFolderToCurrent / Phase 18 增加列表文件导入导出）

源生成器属性：`_currentIndex`（-1 表示未选）, `_selectedTrack`（UI 列表选中项，与播放无关）。集合：`ObservableCollection<Track> Queue`。私有：`HashSet<int> _shuffleHistory` / `Random _random` / `int _playToken`（重入哨兵）。派生：`HasCurrentTrack`。

**Phase 12 continued 变更：**
- `ShuffleEnabled` / `RepeatMode` / `RepeatActive` / `ToggleShuffle` / `CycleRepeat` **已移除**（提升到 `PlaylistsViewModel` 全局共享）。本 VM 通过 `Container` 属性代理读取全局状态：`ShuffleEnabled => Container?.ShuffleEnabled ?? false`，`RepeatMode => Container?.RepeatMode ?? RepeatMode.Off`
- `OpenAndPlay` 命令**已移除**（PlayerBar 上的 📂 按钮已删除）
- `Container`（`PlaylistsViewModel?`，internal）：由 `PlaylistsViewModel.HookPlaylistVm` 设置，用于读取全局 Shuffle/Repeat 状态
- `SortBy(string column)`：按指定列**物理重排 `Queue`**（TrackNumber / Title / Artist / Album / Duration）；再次点击同列切换升/降序；排序后更新 `CurrentIndex` 跟踪当前播放曲。Phase 20 起这里**没有** `SortedView`：原先的 `ICollectionView SortedView`（WPF 类型）已删除，`PlaylistView` 的 `ListBox` 直接绑 `Queue`，排序完全靠重排本身 —— 这也是 WinUI 壳能复用同一条排序路径的前提
- `GetSortKey(Track, string)`（私有静态）：排序键提取辅助方法
- `ClearShuffleHistory()`（internal）：由 `PlaylistsViewModel.ToggleShuffle` 调用，清空已播过历史
- `[RelayCommand] ImportFolderToCurrent()`：选文件夹 → 递归扫描音频文件 → 读取元数据入队当前歌单（不再创建新歌单）
- `AddToQueue` 现在读取元数据（`_metadataReader.ReadAsync`）而非仅创建占位 Track

构造时订阅 `IPlaybackService.TrackEnded` 用于自动推进；`Queue.CollectionChanged` 触发 Next/Prev/PlayCurrent 命令 `NotifyCanExecuteChanged`；构造尾段从 seed 恢复队列（`File.Exists` 过滤 + `MapCurrentIndexAfterFilter` 重映射）。

`[RelayCommand]`：`AddToQueue / RemoveTrack(int) / ClearQueue / PlayTrackAt(int) / NextTrack / PrevTrack / PlayCurrent` / `DropExternalFiles(IReadOnlyList<string>)`（Phase 5） / `MoveTracks(MoveTracksArgs)`（Phase 5） / `ImportFolderToCurrent`（Phase 12 continued）。

`ToRecord()`：把当前状态打包成 `Playlist` record（`ShuffleEnabled=false` / `RepeatMode=Off` 占位，实际全局状态由 `PlaylistsViewModel.BuildSnapshot` 负责）。

**Phase 5 新增成员：**
- `[RelayCommand] DropExternalFiles(IReadOnlyList<string> paths)`：与 `AddToQueue` 同语义入队管线（读元数据 → Queue.Add），不触发播放。路径白名单过滤由 View 层 `DragDropExtensions.FilterAudioPaths` 提前完成，VM 信任入参。
- `[RelayCommand] MoveTracks(MoveTracksArgs args)`：队列内重排算法（spec §4 八步算法）。**入口先 `_playToken++`** 顶替 in-flight `PlayTrackAtAsync`（同时偿还旧债 #5）。算法用对象身份（`ReferenceEquals` + `ReferenceEqualityComparer.Instance`）回找 `CurrentIndex` 与 `_shuffleHistory`，不做索引算术 —— Track record 的结构相等会让 `Queue.IndexOf(currentTrackObj)` 在出现重复占位时返回首个结构等价匹配而非原始那一个。不调 `_player.Unload()`，重排不中断播放。
- `RemoveTrack` 入口加 `_playToken++`（Phase 5 顺带还债 #5）：以前删除非当前曲不顶替 token，能让 in-flight `Queue[index] = meta` 写到错位；现已关闭。

**关键私有方法**（与旧 MainViewModel 等价）：
- `PlayTrackAtAsync(int, int skipCount=0)`：抢占 `_playToken` → 读元数据 → `Queue[i] = meta` → `LoadAsync` → `Play`；每个 `await` 后校验 token，被顶替则静默退出；失败连续 3 次自动停止
- `CalculateNextIndex(int? failedIndex)`：纯算法，按 (Shuffle × RepeatMode) 4 种组合返回下一索引；Shuffle 用 `_shuffleHistory` 排除已播
- `HandleTrackEnded()`：RepeatOne 重播当前，否则走 `CalculateNextIndex`
- `UnloadCurrentTrack()`：`_playToken++` 顶替 in-flight → `_player.Unload()`（NAudio 服务自动广播 TrackChanged(null) 让 PlayerVM 清屏）

`Cleanup()`：同步解绑 `TrackEnded`，由 `MainViewModel.CleanupAsync` 调用。

**Phase 18 新增成员**（ctor 新增 `IPlaylistFileService` 依赖；以下是**可 await 的公开方法而非 RelayCommand** —— View 在 async void 处理器里 await 返回值控制弹框流程，方法返回值可直接在单测断言）：
- `ImportPlaylistFileAsync(string? presetPath = null) → Task<PlaylistImportReport?>`：导入并**追加**到本歌单（与 `AddToQueue` 同层级语义）。`presetPath` 非空 = 拖拽入口（跳过文件对话框）；null = 走 `IFileDialogService.OpenFiles(PlaylistFileFormats.OpenFilter)`，取消返回 null（View 不弹任何框）。过滤已在服务层完成，`AcceptedPaths` 直接喂给**既有的 `DropExternalFiles`**（其"传入 paths 已过滤"契约因此多了一个调用方，COUPLING §5）；不给本 VM 加 `ILibraryScannerService` 依赖。返回结构化 `PlaylistImportReport`（`CreatedNewPlaylist = false`，`PlaylistName = Name`）。
- `ExportPlaylistFileAsync() → Task<string?>`：导出本歌单为 `.m3u8`。空队列直接返回 null（View 层已拦一次，这里兜底）；`IFileDialogService.SaveFile(SaveFilter, SanitizeFileName(Name), ".m3u8")` 取消返回 null；成功返回 null（不弹提示）；`ExportAsync` 抛异常时捕获并返回可直接展示的错误文案 `$"导出失败：{ex.Message}"`（设计稿 §7.5 的单一分支例外，见 COUPLING §5）。导出内容 = 当前队列顺序（不是 shuffle 后的播放顺序）。
- `SanitizeFileName(string)`（私有静态）：歌单名 → 合法文件名，`Path.GetInvalidFileNameChars()` 命中的字符替换为 `_`；空名退化为 `"playlist"`。

### 5.4c `ViewModels/PlaylistsViewModel`（多歌单容器，Phase 6 + Phase 10 + Phase 12 continued + Phase 18）

源生成器属性：`_viewedPlaylist`（UI 当前选中）、`_shuffleEnabled`、`_repeatMode`（Phase 12 continued：全局播放模式，所有歌单共享）。集合：`ObservableCollection<PlaylistViewModel> Playlists`。私有：`Func<Playlist, PlaylistViewModel>` 工厂委托、`IPlaybackService`、`IFileDialogService`、`ILibraryScannerService`、`ILibraryCache`、`ITrackMetadataReader`、`string _currentPlaylistId`。派生：`RepeatActive`（`RepeatMode != RepeatMode.Off`）。

**Phase 6 成员：**`[RelayCommand]`：`AddPlaylist / RemovePlaylist / RenamePlaylist`；`HandleDoubleClickPlay`（跨歌单双击路由）；`RecomputeIsActiveFlags`（切 CurrentPlaylistId 后批量刷新 sidebar ▶ 标记）；`StateChanged` 事件（debounce save 触发点）。

**Phase 12 continued 新增成员：**
- `IPlaybackService` 依赖（构造注入）：用于 `RemovePlaylist` 时停止播放
- `ITrackMetadataReader` 依赖（构造注入）：用于 `ApplyCachedMetadataSync` 回读旧缓存缺少 TrackNumber 的文件
- `ShuffleEnabled` / `RepeatMode`（`[ObservableProperty]`）：全局播放模式，从 Playlist 级别提升到容器级别
- `RepeatActive`（派生）：循环按钮激活状态
- `[RelayCommand] ToggleShuffle()`：切换 Shuffle → 同时清空当前歌单的 `_shuffleHistory`
- `[RelayCommand] CycleRepeat()`：循环模式三态循环 Off → List → One → Off
- `RemovePlaylist` 增强：若删除的是当前播放歌单，调 `_player.Stop()` + `_player.Unload()` 停止播放并清空 `CurrentPlaylistId`
- `HookPlaylistVm` 设置 `vm.Container = this`，让 PlaylistVM 代理读取全局 Shuffle/Repeat
- `Hydrate` 从 snapshot 加载全局 `ShuffleEnabled` / `RepeatMode`
- `BuildSnapshot` 保存全局 `ShuffleEnabled` / `RepeatMode`
- `ApplyCachedMetadataSync` 增强：当缓存条目 `TrackNumber` 为 null 时回读文件补全元数据

**Phase 10 新增成员：**
- 构造函数新增 `ILibraryScannerService scanner` + `ILibraryCache cache` 两个依赖
- `[RelayCommand] ImportFolderAsync()`：弹 `IFileDialogService.OpenFolder` → 递归扫描 → 创建文件夹绑定歌单（`Playlist` seed 带 `SourceFolder`）
- `RescanFolderBoundPlaylistsAsync()`：启动时由 `MainViewModel.InitializeAsync` 触发；遍历所有 `HasSourceFolder` 的 VM，逐个增量同步（`LibraryDiff`）
- `[RelayCommand] RefreshPlaylistAsync(PlaylistViewModel?)`：手动刷新单个文件夹绑定歌单
- `RescanSinglePlaylistAsync(vm, sourceFolder)`：核心扫描逻辑 —— 调 `ILibraryScannerService.ScanAsync` + `ILibraryCache.LoadAsync/SaveAsync` → 计算 diff → 增删 Queue → 设 `IsScanning`/`HasScanError`

**Phase 18 新增成员：**
- 构造函数新增 `IPlaylistFileService` 依赖；`NullFileDialogService` 补 `SaveFile`（返回 null）、新增 `NullPlaylistFileService` 空对象（internal 测试 ctor 用）
- `ImportPlaylistFileAsync(string? presetPath = null) → Task<PlaylistImportReport?>`：容器级导入 = **新建**一个普通歌单并设为当前查看项（与 `ImportFolderAsync` 同层级语义，骨架复用它）：取路径（对话框或 `presetPath`）→ `_playlistFiles.ImportAsync` → `AcceptedPaths.Count == 0` 时**不建歌单**只返回 `Imported = 0` 的报告 → `_scanner.ReadMetadataBatchAsync` 批量读元数据（一条都没读出来也不建空歌单）→ 造 `Playlist` seed（`Name = SuggestedName`、`CurrentIndex = -1`、**`SourceFolder = null`**）→ `_factory(seed)` → `vm.Queue.Clear()` 后填入带真实元数据的 Track（顶掉构造期按 `File.Exists` 预填的占位 Track，与 `ImportFolderAsync` 同一手法）→ `HookPlaylistVm` → `Playlists.Add`（CollectionChanged → `StateChanged` → MainViewModel debounce 存盘，无需额外接线）→ `ViewedPlaylist = vm` → 返回报告（`CreatedNewPlaylist = true`）
- `SourceFolder = null` 是**身份标记**：导入的歌单不是文件夹绑定歌单，不写 library cache、不显示"刷新文件夹"按钮，重启后由 `Hydrate` 走 `LoadMetadataForNormalPlaylistSync` 读文件元数据

### 5.5 `Views`

- **`MainWindow`**：三行 Grid —— Row 0 `TitleBar`（Phase 17 自绘标题栏）+ Row 1 `ContentGrid`（5 列：Sidebar | Splitter | Playlist | Splitter | TrackInfo）+ Row 2 `PlayerBar`（底部，自适应高度）；Phase 17 起 `WindowStyle=None` + `WindowChrome` 无边框。`SidebarCol` 和 `TrackInfoCol` 各限制为窗口宽度一半（`ContentGrid_SizeChanged` + `DragDelta` 中到达上限直接锁死）。构造时同步读取窗口尺寸（`GetAwaiter().GetResult()`，启动阻塞 < 几 ms 可接受）；若持久化的 `WindowHeight < 500`（Phase 1 旧值）则一次性迁移到 650，避免列表不可见。关闭时采用 **cancel-and-close 模式**（Phase 4）：首次进入 `e.Cancel=true` + `_isClosing=true`，跑完 settings 写盘、`CleanupAsync`、`SnapshotState` + queue 写盘后调 `Close()` 重新触发 Closing 直接放行；这是为了让 `async void` 多 await 链不被 `Application.Shutdown → Dispatcher.InvokeShutdown` 截断。
- **`PlayerBar`** *(UserControl)*：播放栏。两行 Grid：①`Position | Slider | Duration` 进度条；②⏮ ▶/⏸ ⏭ 🔀 ⇄/🔁/🔂 按钮组（居中）+ 🔊音量 + 🎚均衡器（Phase 14）+ ⚙设置（右对齐）。封面/信息已拆到 `TrackInfoView`。Phase 16：上述图标全部改为 `Themes/Icons.xaml` 矢量 `Path`（活跃态经 `BoolToAccentBrushConverter` 着 accent 色；音量/静音由 `BoolToVolumeIconConverter` 提供）。
  - Slider 的"单击跳转"由 `PreviewMouseLeftButtonDown` 手动从 `PART_Track` 计算比例并触发 `SeekCompletedCommand`；点击 Thumb 时不触发（通过 `FindAncestor<Thumb>` 检测，转交给原生 `DragStarted/DragCompleted`）。Thumb 默认 8px 圆点半透明，悬停/拖拽放大到 14px 不透明
  - `⏮` / `⏭` 通过 `{Binding DataContext.Playlists.ViewedPlaylist.<XxxCommand>, RelativeSource={RelativeSource AncestorType=Window}}` 跨级绑定到 `PlaylistViewModel`；`🔀` / `⇄/🔁/🔂` 绑到 `Playlists.ToggleShuffleCommand` / `Playlists.CycleRepeatCommand`
  - Stop 按钮和 📂 OpenAndPlay 按钮**已移除**（Phase 12 continued）
  - **🎚 均衡器按钮（Phase 14）**：`EqualizerBtn_Click` 经 `App.GetService<ISettingsPersistence>()` + `App.GetService<IPlaybackService>()` 取服务，`DataContext as PlayerViewModel` 作可选 VM，调 `EqualizerDialog.Show(Window.GetWindow(this), ...)`（与 ⚙ `SettingsBtn_Click` 同模式）。`TextBlock` 的 `Foreground` 绑 `{Binding EqualizerEnabled, Converter={StaticResource BoolToAccentBrush}}`，EQ 启用时高亮强调色（同 🔀/🔁 按钮）
  - **▶/⏸ 按钮的双绑定（Phase 4）**：默认 `Command={Binding PlayPauseCommand}`（PlayerVM 的 transport 切换）；当 `CurrentTrack==null` 时通过 `<DataTrigger Binding="{Binding CurrentTrack}" Value="{x:Null}">` 切到 `Playlists.ViewedPlaylist.PlayCurrentCommand` —— 启动后队列已恢复但 transport 空闲，第一次按 ▶ 触发首次加载 + 播放，`TrackChanged(track)` 让 trigger 失活，回到 PlayPauseCommand。**注意 inline `<Style TargetType="Button">` 必须 `BasedOn="{StaticResource {x:Type Button}}"`**，否则会替换掉 `Themes/Controls.xaml` 中的隐式主题样式，按钮回退到 OS 原生白底（COUPLING.md §5）
- **`PlaylistView`** *(UserControl, Phase 2 + Phase 5 拖拽 + Phase 12 continued + Phase 18)*：队列界面。两行 Grid：①工具栏 `[添加][导入文件夹][导入列表][导出列表][清空]` 左对齐（清空是危险操作保持最右）+ 右侧"刷新文件夹"按钮（仅文件夹绑定歌单可见）；②`ListBox` 直接绑 `Queue`（Phase 20 删掉 `SortedView` 后就是这一条路径，表头排序靠 `SortBy` 物理重排 `Queue`），每项含 ▶ 当前曲标记 + `#` 列（TrackNumber）+ 标题/艺术家/专辑/时长 + `×` 删除按钮
  - **Phase 18 导入/导出按钮**："导入列表"（`Icon.Import`）→ `PlaylistImportUi.RunDialogAsync(_vm.ImportPlaylistFileAsync, ...)` 追加到当前歌单；"导出列表"（`Icon.Export`）→ 空队列静默返回（与清空按钮同处理方式），`ExportPlaylistFileAsync` 返回非 null 错误文案时 `ConfirmDialog.ShowError`（主题化，不是系统 MessageBox），处理器自身再兜一层 try/catch
  - **Phase 18 拖拽分流**：`QueueList_DragOver` / `Root_DragOver` 的 Copy 判定 = `FilterAudioPaths` 非空 **或** `FilterPlaylistPaths` 非空；Drop 把两组分别送入既有音频入队管道（`DropExternalFilesCommand`）与导入管道（`PlaylistImportUi.RunForDroppedFilesAsync` → `ImportPlaylistFileAsync`，追加语义），混合拖入（音频 + m3u）两条都跑；内部重排格式（`QueueItemsFormat`）优先级不变，外部 FileDrop 分支在其后
  - **# 列**：显示元数据 `TrackNumber`（Phase 12 continued 新增），从 `ITrackMetadataReader` 读取
  - **表头排序**：点击列头触发 `SortBy(column)` 物理重排 Queue（Phase 12 continued）；表头用 TextBlock + MouseLeftButtonDown（非 Button，消除内边距对不齐问题）
  - 当前曲 ▶ 标记由 code-behind 维护：订阅 `PlaylistViewModel.PropertyChanged` (CurrentIndex) / `Queue.CollectionChanged` / `ItemContainerGenerator.StatusChanged`（应对虚拟化容器回收和 `Queue[i] = meta` 替换）；▶ 标记在 # 列之前（最左列）
  - 交互：双击播放、Delete 键/× 按钮（经 ConfirmDialog 确认）删除、工具栏按钮（清空需确认）触发命令
  - Shuffle / Repeat 按钮**已移到 PlayerBar**（Phase 12 continued）
  - **Phase 5 拖拽（XAML）：** 外层 `<Border AllowDrop="True">` 仅承载 OLE drop 区（覆盖工具栏 + 列表两行的 hit-test）；视觉高亮挂在 Row 1 的圆角 `<Border x:Name="QueueListBorder">`（用户期望仅看到列表区域被框住，不连带工具栏）。**BorderBrush 默认值放进 Style.Setter 而非 local 属性** —— WPF DP 优先级 `local > trigger setter > style setter`，写成 local 会让 `Style.Triggers` 失效（`docs/COUPLING.md §5` 隐式契约）。`ListBox` 加 `SelectionMode="Extended"` + `AllowDrop="True"` + 6 个事件挂接（`PreviewMouseLeftButton{Down,Up}` / `PreviewMouseMove` / `DragOver` / `DragLeave` / `Drop`）
  - **Phase 5 拖拽（code-behind）：** 拖拽启动用 `PreviewMouseLeftButtonDown` 记起点 + `PreviewMouseMove` 4px 阈值（`SystemParameters.MinimumHorizontal/VerticalDragDistance`）。**多选拖拽保护：** 用户 Ctrl+多选后再不带修饰键点击其中一项时，ListBox 默认会把选中塌成单项 —— `PreviewMouseLeftButtonDown` 在"已选 ≥ 2 项 + 无 Ctrl/Shift + 点中已选项"时 `e.Handled = true` 拦下默认塌选；若未过阈值就松手，`PreviewMouseLeftButtonUp` 手动塌成单选模拟原行为；过阈值真启动拖拽则保留多选。`DataObject` 自定义格式 `"DPlayer.QueueItems"` 区分内部重排，`DataFormats.FileDrop` 是外部文件。命中测试 `ComputeInsertIndex` 对每个 ListBoxItem 容器用 `TransformToAncestor(QueueList)` 算 bounds + 半高判定。`HideAdorner` 在 `Drop` / `DragLeave` 都清理插入线，避免残留
  - **Phase 5 高亮纪律：** `Root_DragEnter` 必须先 `FilterAudioPaths` 再决定是否高亮 —— 仅看 `FileDrop` 存在就亮会让文件夹/全非音频也亮（光标已显示禁止但边框还紫，视觉冲突）。`Root_Drop` 与 `QueueList_Drop` **都要清高亮** —— `QueueList_Drop` 设 `e.Handled=true` 后 Drop 事件不再冒泡到 `Root_Drop`，否则文件落到列表区高亮卡死
- **`PlaylistsSidebarView`** *(UserControl, Phase 6/9/10/12 continued/18)*：左侧歌单栏。`+` 按钮直接创建新歌单（Phase 12 continued 移除 ContextMenu 子菜单）；`-` 按钮删除选中歌单；**Phase 18 第三个 28×28 图标按钮**（`Icon.Import`，ToolTip「导入播放列表（M3U / M3U8 / PLS）→ 新建歌单」）→ `PlaylistImportUi.RunDialogAsync(_vm.ImportPlaylistFileAsync, ...)`（容器级 = 新建歌单）；ListBox 支持双击重命名、拖拽重排（Phase 9）。`▶` 标记由 `IsActivePlaylist` DataTrigger 驱动。文件夹绑定歌单显示 📂 图标 + 🔄 扫描指示。**Phase 18 拖拽**：`PlaylistList_DragOver/Drop` 接受外部 `.m3u/.m3u8/.pls`（`Effects = Copy`，**不显示重排插入线**——这不是重排；音频文件落在侧边栏无意义，不认），内部重排格式（`PlaylistItemsFormat`）优先；多个列表文件逐个导入（每个文件一个歌单）聚合成一份报告。导出**不放侧边栏**：侧边栏按钮作用于"选中项"，导出语义是"当前查看的歌单"，两个指针在键盘导航下可能不同步。
- **`TrackInfoView`** *(UserControl, Phase 12 continued/13)*：右侧曲目信息面板。`DataContext = PlayerViewModel`。`BackgroundSecondary` 背景 + 圆角 Border。两行 Grid：Row0（`*`）曲目信息 —— 封面用 `Viewbox MaxWidth/MaxHeight=250` 包裹自动缩放（内含 `Border` 180×180 + `Image Stretch="UniformToFill"`），文本元数据（标题/艺术家/专辑/采样率）居中，无曲目时 DataTrigger 显示"播放曲目以查看信息"占位；Row1（`Auto`）**Phase 13 `SpectrumView`**（高 120px，绑 `SpectrumData`/`SpectrumColorTheme`，`Visibility` 绑 `SpectrumEnabled`）。封面 Border 加 `ClipToBounds=True` 圆角裁切（ViewBox 缩放后内容溢出问题）。
- **`SettingsDialog`** *(Window, Phase 11/13)*：设置对话框。模态 ToolWindow（**420×520**，Phase 13 因可视化区增高），9 行 Grid：通用（音量滑块 0..1）+ 音频输出灰色占位 + **音频可视化（Phase 13：启用 CheckBox + 灵敏度滑块 0.5~2.0 + 颜色主题 ComboBox + 平滑度滑块 0~0.95，DockPanel LastChildFill 布局：标签左/数值右/滑块填充）**。静态 `Show(Window?, ISettingsPersistence, IPlaybackService, PlayerViewModel?)` 工厂。构造注入 `IPlaybackService`（音量滑块实时调 `_playbackService.Volume`）+ 可选 `PlayerViewModel`（保存后即时同步频谱属性，免重启）。OnLoaded async 读盘加载音量 + 频谱设置并绑定滑块 ValueChanged 实时更新数值标签；Save_Click 通过 `UpdateAsync` 原子写盘后同步 VM —— **故意不 `ConfigureAwait(false)`，留在 UI 线程**才能直接写 `PlayerViewModel` 属性；失败弹 MessageBox（Phase 13）。Phase 17：`WindowStyle=None` + `WindowChrome` + `TitleBar(ShowMaximize=False)`（仅关闭键）。
- **`EqualizerDialog`** *(Window, Phase 14)*：均衡器对话框。复用 SettingsDialog 模式（静态 `Show(Window?, ISettingsPersistence, IPlaybackService, PlayerViewModel?)` 工厂 + 模态 `ShowDialog()`，返回 true = 用户保存）。模态 ToolWindow（**480×400**），三行 Grid：Row0 启用 CheckBox + 预设 ComboBox；Row1（`*`）频段滑块区 `BandsPanel`（UniformGrid）；Row2 恢复 Flat + 取消/保存。
  - **11 根竖直滑块由 code-behind 动态构建**：`BuildBands()` 在 `BandsPanel` 里生成 10 段 + preamp 共 11 列（每列：dB 值标签 / 竖直滑块 / 频率标签，`MakeSliderColumn` 三行 Grid）。滑块绑 `EqBandSlider` 样式（Min −12 / Max 12）
  - **自定义 `EqBandSlider` 竖直模板**：`Controls.xaml` 的隐式 Slider 模板是**横向专用**（Height=20 + 填充条 VerticalAlignment=Center），竖直滑块直接用会渲染错位；故对话框资源里自定义 `EqBandSlider`（`Orientation=Vertical` + Width=24 + 填充条 HorizontalAlignment=Center + 14px 圆形 Thumb）
  - **实时预览**：`BandSlider_ValueChanged` → `RefreshLabels()` 更新数值标签 + `EqualizerPresets.Match(CurrentGains())` 回推预设名（手动偏离即 Custom）→ `PushToService(preset)` 把 `EqualizerConfig.Create(...)` 写 `_playbackService.EqualizerConfig`（拖动即时听感）；`PresetCombo_SelectionChanged` 选预设→`ApplyGains` 写滑块 + 实时下发；`RestoreFlat_Click` 恢复平直（preamp=0）
  - **`_suppress` 拦截程序化回推**：程序设滑块/下拉时用 `_suppress` 窗口包住，避免 `ValueChanged`/`SelectionChanged` 回推；**尤其 `OnLoaded` 填充预设下拉必须在 `_suppress` 内** —— WPF ComboBox 向空集合添加首项会自动选中 index 0 并触发一次 `SelectionChanged`（Phase 14 code review 拦下的 Critical）
  - **取消回滚**：`_initialConfig` 取“打开瞬间的实时 `_playbackService.EqualizerConfig`”（权威，不受 LoadAsync 失败影响），再试读盘覆盖；`OnClosed` 若未 `_saved` 则把 `_playbackService.EqualizerConfig = _initialConfig` 撤销实时预览（回滚基准非二次读盘 —— 读盘失败会把基准误置为禁用平直）
  - **Save_Click**：`UpdateAsync` 原子写 settings.json（EqualizerEnabled/Preamp/Bands/Preset）→ 再确认一次链上配置 → 写 `PlayerViewModel.EqualizerEnabled`（按钮高亮即时更新）—— **故意不 `ConfigureAwait(false)`，留在 UI 线程**；失败弹 MessageBox
  - **Phase 17**：`WindowStyle=None` + `WindowChrome` + `TitleBar(ShowMaximize=False)`（仅关闭键）；标题栏占 Row 0，频段区/按钮区行号顺延
- **`ConfirmDialog`** *(Window, 2026-10-05)*：主题化对话框（深色 WindowChrome + TitleBar + 自动换行文案），替代系统 MessageBox。`Show(Window?, string title, string message)` = 确认模式（取消/确定，返回 bool）；`ShowError(owner, title, message)` = 单按钮错误模式（仅确定，Esc 可关）；**`ShowInfo(owner, title, message)`（Phase 18）** = 单按钮信息模式（与 ShowError 共用 `ShowCore(showCancel: false)`，区别只在语义命名——导入报告不是错误）。接入点：删除歌单（`PlaylistsSidebarView`）、清空歌单与移除曲目（`PlaylistView` 的清空按钮 / × 按钮 / Delete 键，确认文案含歌单名/曲名）、保存失败提示（`SettingsDialog` / `EqualizerDialog`）、**导入报告与导出失败（Phase 18，经 `PlaylistImportUi` / `PlaylistView`）**。空队列点清空直接 no-op（不弹框）。

### 5.5a `Views/Controls/DragDropExtensions`（Phase 5/18）

静态类，承担拖拽相关的 attached DependencyProperty 与文件过滤辅助：

- `IsDragOver`（attached DP，bool，默认 false）：由 `PlaylistView.xaml.cs` 的 `Root_DragEnter` / `Root_DragLeave` / `Root_Drop` / `QueueList_Drop` 切换；XAML 用 `Style.Trigger Property="local:DragDropExtensions.IsDragOver"` 给 `QueueListBorder` 的 `BorderBrush` 设 `AccentPrimary` 实现高亮。所有切换调用都显式传 `QueueListBorder`（不是 `sender`），保证视觉范围只在列表圆角矩形上
- `AudioExtensions`（`IReadOnlyList<string>`）：代理到 `Models.AudioConstants.Extensions`（Phase 10 提取），白名单 `.mp3 / .wma / .flac / .aac / .wav`，与 `IFileDialogService` 在 `OpenFiles` 中使用的过滤器单一来源
- `FilterAudioPaths(IEnumerable<string>?)`：大小写不敏感后缀匹配；null/空字符串/空后缀（文件夹路径 `Path.GetExtension` 返回 ""）/ 后缀不在白名单都返回不入结果。VM 层 `DropExternalFilesCommand` 信任此函数已过滤完成
- `PlaylistFileExtensions` + `FilterPlaylistPaths(IEnumerable<string>?)`（Phase 18）：代理到 `PlaylistFileFormats.Extensions` / `IsPlaylistFile`（单一来源避免漂移），过滤出 `.m3u/.m3u8/.pls`（大小写不敏感，文件夹自动剔除）。**Drop 目标必须同时查 `FilterAudioPaths` 与 `FilterPlaylistPaths`** —— 两个白名单互不重叠，只查音频会让播放列表文件被静默丢弃（COUPLING §5）

### 5.5b `Views/Controls/DropInsertionAdorner`（Phase 5）

继承 `Adorner` 的 sealed 类，挂在 `QueueList` 的 `AdornerLayer` 上画拖拽插入线：

- 构造接收 `ListBox` 作为 `AdornedElement`，`IsHitTestVisible = false`（不抢鼠标事件）
- 内部 `_insertIndex`（int）；`Update(int)` 设值 + `InvalidateVisual()`
- `OnRender(DrawingContext)` 用 frozen `Pen`（颜色绑定 `AccentPrimary`，宽 2.0）画一条横线：`_insertIndex == Queue.Count` 时画在最后一项底部；否则画在 `Queue[insertIndex]` 项顶部。横线左右各缩 4px 留白
- 生命周期由 `PlaylistView.xaml.cs` 的 `_currentAdorner` 字段管理：`ShowAdorner` 懒构造一次后只调 `Update`；`HideAdorner` 在 Drop / DragLeave / 拖拽取消时 `AdornerLayer.Remove + null` 复位

### 5.5c `Views/Controls/SpectrumView`（Phase 13）

频谱可视化 UserControl，纯 code-behind 绘制（无 VM 逻辑）：

- **32 条 `Rectangle` 柱**挂在 `SpectrumCanvas`（`Height=120` / `ClipToBounds`）；`RecalculateBarLayout` 在 `SizeChanged` 时按 `ActualWidth` 重算柱宽（`(ActualWidth - 31*BarSpacing) / 32`，`BarSpacing=2`），`Canvas.SetBottom(0)` 底部对齐
- **两个 DependencyProperty**：`SpectrumData`（`float[32]`，默认全 0）、`ColorTheme`（`int`，默认 0）。`OnSpectrumDataChanged` 把每柱数据换算成目标高度 `_targetHeights[i] = MinBarHeight(2) + data[i]*(MaxBarHeight(118)-2)`
- **60fps 渲染循环**：`CompositionTarget.Rendering += OnRendering`，每帧 `newHeight = current + (target-current)*AnimationSmoothFactor(0.3)` 做缓动；`Unloaded` 时解绑（防内存泄漏）
- **4 种颜色主题**：紫/蓝/绿为 `LinearGradientBrush`（底→顶渐变，全部 `Freeze()`）；**彩虹（theme==3）为每柱一色** —— HSV 色相从左 0°(红) 线性到右 300°(紫)（`hue = 300/31*i`，`HsvToRgb`），水平渐变不循环。`OnColorThemeChanged` 切换 `Fill`
- 数据源：`TrackInfoView.xaml` 绑 `PlayerViewModel.SpectrumData` / `SpectrumColorTheme` / `SpectrumEnabled`

### 5.5d `Views/Controls/TitleBar`（Phase 17）

自绘无边框标题栏 UserControl（高 32px，`BackgroundPrimary`），配合各 Window 上的 `WindowStyle=None` + `WindowChrome(CaptionHeight=32, UseAeroCaptionButtons=False, GlassFrameThickness=0)`：

- **两个依赖属性**：`Title`(string) / `ShowMaximize`(bool，默认 true)；`ApplyShowMaximize` 控制最小化/最大化按钮显隐 —— MainWindow 显示三键，3 个对话框 `ShowMaximize=False` 仅显示关闭键
- **布局**：左标题文本（ForegroundPrimary）+ 右侧按钮组（最小化 `Icon.Minus` / 最大化-还原 `Icon.Maximize`/`Icon.Restore` / 关闭 `Icon.Close`，均为 `IconPath` 风格矢量 `Path`，Stroke=ForegroundSecondary）；按钮标 `shell:WindowChrome.IsHitTestVisibleInChrome=True` 以接收点击（否则被 caption 拖动吞掉）
- **动作全走 `SystemCommands`**：`MinimizeWindow / MaximizeWindow / RestoreWindow / CloseWindow`（作用于 `Window.GetWindow(this)`）；拖动/双击最大化/Aero Snap 由 OS caption 负责，不自写 DragMove
- **`Loaded` 订阅父窗 `StateChanged` → `ApplyWindowState`**：Maximized 时切换 Max/Restore 图标显隐，并给 `Window.Content` 根元素加**常量式工作区边距**；还原时清零。边距按「`WorkArea` 偏移 + `WindowResizeBorderThickness`」计算（`wa.Left+rb.Left` / `wa.Top+rb.Top` / `PrimaryScreenWidth-wa.Right+rb.Right` / `PrimaryScreenHeight-wa.Bottom+rb.Bottom`）—— 不读窗口实际边界，避免布局时序导致 right/bottom 边距偏大（大片留空）

### 5.5e `PlaylistImportReportFormatter`（Core/ViewModels）+ `Views/Controls/PlaylistImportUi`（Phase 18/20）

导入的展示侧基础设施，四个入口（侧边栏按钮 / 侧边栏拖拽 / 工具栏按钮 / 列表区拖拽）共用。Phase 20 把格式化器从 `Views/Controls` 搬进 `D-player.Core/ViewModels`（纯字符串函数、无 WPF 依赖，第二壳要复用），执行器 `PlaylistImportUi` 留在 WPF 壳（它弹的是 WPF 的 `ConfirmDialog`）：

- **`PlaylistImportReportFormatter`**（纯静态字符串函数，无 WPF 依赖，可单测）：把 VM 返回的结构化 `PlaylistImportReport` 拼成中文提示文案。`DialogTitle = "导入播放列表"`（所有入口统一）；`Format(IReadOnlyList<PlaylistImportReport>)` 单份报告走 `FormatSingle`（新建/追加/全跳过三分支 + "跳过 N 条（文件缺失 X / 格式不支持 Y）"），多份（拖拽聚合）走 `FormatMany`（"已导入 N 个播放列表" + 每文件一行）。**分层纪律**：VM 只给数据、文案规则集中在这一处 —— 唯一例外是导出错误文案由 VM 直传（单一分支、单一调用方，设计稿 §7.5）。
- **`PlaylistImportUi`**（静态执行器）：封装"跑导入 → 聚合报告 → `ConfirmDialog.ShowInfo` → 兜异常"完整流程。`RunDialogAsync(importOne, owner)` = 对话框入口（传 null 让 VM 自己弹文件对话框；VM 返回 null（用户取消）时**一个框都不弹**）；`RunForDroppedFilesAsync(importOne, owner, paths)` = 拖拽入口（逐个文件导入，每个文件一个歌单/一次追加，聚合成一份报告）。**异常必须在这里兜住**（转 `ShowError("导入失败：…")`）：调用方是 `async void` 事件处理器，未观察异常会直接崩进程。

### 5.6 `Themes`

深色 + 紫色强调（Catppuccin Mocha 风格）。所有控件模板写入 `Themes/Controls.xaml`，包括自定义的 Slider 模板（紫色已填充段 + 圆形 Thumb）。资源在 `App.xaml` 合并为应用级资源。Phase 12 continued 色板微调：`AccentPrimary` #7C4DFF → #9E7CFF（提亮）、`AccentHover` → #B9A0FF、`SliderThumb` → #9E7CFF；随机/循环激活色改用 `AccentHover`（更亮，深色背景下易辨认）。Phase 13：全局 Slider 隐式样式加 `IsMoveToPointEnabled=True` setter —— 所有滑块（音量/灵敏度/平滑度）单击轨道即跳到点击位置，无需拖动 Thumb。Phase 16 新增 `Icons.xaml`：20 个 `Icon.*` 描边 `Geometry`（24×24 viewbox，Feather/Lucide 署名）+ 共享 `IconPath` 样式（16px、StrokeThickness 1.75、圆头圆角）；`Icon.PlayMarker` 为实心 `Fill=AccentPrimary` 例外；活跃态由使用处绑 `BoolToAccentBrushConverter` 着 `Stroke`。Phase 17：新增 `Icon.Maximize`/`Icon.Restore`；Phase 18：新增 `Icon.Import`/`Icon.Export`（现共 24 个）；`Controls.xaml` 扩充 ComboBox（自绘 ToggleButton 可点击表面 + 深色 Popup + ComboBoxItem 悬停/选中态）、CheckBox（深色方框 + accent 勾）、ScrollBar（横竖双模板、隐藏箭头）、ToolTip/ContextMenu/MenuItem/Separator 深色模板、TextBox（深色底 + 圆角描边，悬停/聚焦 accent 边框，2026-10-05 补）—— 消除残留 OS 浅色元素。

### 5.7 `D-player.WinUI`（Phase 20 第二 UI 壳，第一条纵向切片）

WinUI 3 / Windows App SDK **2.5.1**（钉具体版本以保证可复现构建），unpackaged（`WindowsPackageType=None`）+ `WindowsAppSDKSelfContained=true`，TFM `net10.0-windows10.0.19041.0`，`<Platforms>x64</Platforms>` + `<Platform>x64</Platform>`（后者是必需的：`Platforms` 只声明支持面、不设默认值，裸 `dotnet build` / `dotnet run` 会因求值出的 `Platform=AnyCPU` 被 WASDK 的 `WindowsAppSDKSelfContained.targets` 直接拒掉）。**只消费 Core 的公开成员，不反向引用 WPF 壳，也不进 `D-player.slnf`。**

- `App.xaml.cs`：**构造函数里 `RequestedTheme = ApplicationTheme.Dark`**（spec §4.4 要求的那句，最终修复波 A1 才补上；不设 = 跟随系统主题，浅色 Windows 上两壳会像两个产品。类型注意：WinUI 3 的 `Application.RequestedTheme` 是 `ApplicationTheme`（只有 Light/Dark），不是 FrameworkElement 上的 `ElementTheme` —— 写错是 CS0266）。之后在 UI 线程 `AddDPlayerCore(configuration, new DPlayerDataPaths { FolderName = "D-player-winui" })` + `AddSingleton<IFileDialogService, WinUiFileDialogService>()` → 解析 `MainViewModel` → `new MainWindow(vm)` → `Activate()` → `_ = StartAsync(vm)`（水化 + 断点续播就位）。刻意**不**同步 `Dispose` 容器（见 §4.3 第 31 条）。恢复被跳过时只 `Debug.WriteLine`（WinExe 无控制台，等于用户看不见 —— 与 WPF 的 `ConfirmDialog` 是已知差异）。
- `MainWindow.xaml(.cs)`：三行布局 —— 自绘标题栏区（`ExtendsContentIntoTitleBar` + `SetTitleBar`，`Title = "D-player"`）/ `NavigationView` 左栏 + 中区 `ListView` / 底部播放器栏。`SystemBackdrop = new MicaBackdrop()`（该类型在 WASDK 2.5.1 里住 `Microsoft.UI.Xaml.Media`，不是 `...Composition.SystemBackdrops` —— 文件顶部有逐条删除实测的注释），根 `Grid` 必须 `Background="Transparent"`（§4.3 第 32 条）。左栏是 `PlaylistsViewModel.ViewedPlaylist` 的**投影**：`SyncPlaylistMenu` 的选中优先级固定为 `ViewedPlaylist` → 重建前已选项 → 第一项，并有 `_syncingMenu` 重入守卫（设 `SelectedItem` 会绕回 `SelectionChanged`）；`Playlists.PropertyChanged` 回灌左栏（Core 的 `Hydrate` 在 `Playlists.Add` 之后才恢复 `ViewedPlaylist`）。重建时机 = `RootGrid.Loaded` + `Playlists.CollectionChanged`（歌单是异步水化的，比 `Loaded` 晚）。
- 双击：`TrackList_DoubleTapped` **先在可视化树里解析被点中的 `ListViewItem`**（`e.OriginalSource` → `FindAncestor<ListViewItem>` → `container.Content as Track`；`ListView.ContainerFromPoint`/`ContainerFromElement` 在 WinUI 3 的托管投影里不存在，CS1061），解析不出来（列表下方的空白区）就**什么都不做** —— 与 WPF `PlaylistView.xaml.cs:162-166` 同纪律（A3；切片版读 `SelectedItem`，空白区双击会把上次选中的那首再播一遍）。命中后按**引用身份**回找索引（`Track` 是 record，`Queue.IndexOf` 会塌陷重复占位，同 COUPLING §5 那条纪律）→ `await Playlists.HandleDoubleClickPlay(pl, index)`（与 WPF 同一个入口，§4.3 第 30 条）。
- 播放器栏：**三个** `x:Bind OneWay` 投影属性（`PlayPauseGlyph` / `NowPlayingText` / `TimeText`）+ 本类自实现 `INotifyPropertyChanged`（不加就 WMC1506 且值不刷新）；进度条**不在投影之列**——它直接绑 Core 的 `PlayerViewModel.PositionNormalized`（A4：壳侧那份同公式的 `PositionFraction` 已删，WPF 的 `PlayerBar.xaml` 绑的也是同一个成员）。`▶` 在无当前曲时走 `ViewedPlaylist.PlayCurrentCommand`、有曲时走 `Player.PlayPauseCommand`（与 WPF 的 DataTrigger 同语义，只是改成代码判断）。**拖动语义与 WPF 一致（A2）**：WinUI 3 的 `Slider` 不暴露 `DragStarted`/`DragCompleted`（XAML 属性 WMC0011 / 附加属性 WMC0010 / C# 订阅 CS1061，三条路实测全堵；WASDK 2.5.1 的托管投影里**不存在** WPF 那个 `SliderBase` 类型，这对事件只在模板内部的 `Thumb` 上——本段旧版写的"`SliderBase` 不暴露"是拿 WPF 的类层次命名一个不存在的类型，已按台账 P-23 更正），故从 `Slider` 模板的 `Thumb`（`HorizontalThumb`）上订阅同一对事件 —— 拖动期间 `IsSeeking` 为真、30 Hz 回写被 Core 抑制，松手才提交一次 Seek；`ValueChanged` 只承担"值变了才提交 Seek"这一半并跳过"程序写回值"那条路径（`_suppressSeek` 已删）。**但这半在真机上不等于"单击定位"**：2026-10-07 用户实测——单击轨道靠近开头的半边跳到开头、靠近结尾的半边跳到结尾，位置**不落到点击处**。机制**未量**，最可能的假设是 `RangeBase.LargeChange` 的默认值在 0..1 的进度条上等于整个量程，于是"一页"步进直接顶到端点——这是**标注为假设的解释，不是结论**，改之前先量。此前这里写过"`IsMoveToPointEnabled` 默认生效"，那是 **WPF** 的属性、WinUI 3 的 `Slider` 上没有（引用即 CS1061），假依据早已撤走。**残留风险**：这条挂接依赖模板部件名，找不到时静默退回连续 Seek，而 WinUI 不进门禁 → 没有自动回归能发现（对比材料 §5 关切 5）；**单击定位的缺陷与拖动语义是两件事**，A2 那套拖动修法不受它影响（"松手是否停在手指处"至今仍没实测）。逐项验收结果见对比材料 §2.2。
- 关闭：`AppWindow.Closing` → cancel-and-close → `await MainViewModel.CleanupAsync()` → `Close()`。
- `Services/WinUiFileDialogService.cs`：切片期空实现（三个方法各返回"用户取消"语义值）。它存在的理由是**让 DI 图可解析**（Core 的 `PlaylistViewModel` 工厂在构造歌单时才取该服务），不是功能实现；类注释里记着第二阶段接真 picker 的死锁障碍（同步接口 vs 异步 API）。
- 已知未验证 / 未做：见 [`PHASE20-COMPARISON.md`](./PHASE20-COMPARISON.md) 第 2、3 节；**2026-10-07 用户在真机走完第 2.1 那份走查清单后的逐项结论在第 2.2 节**（除进度条单击定位外全部通过；决策门仍未拍板）。构建/运行命令见 §8.2。

---

## 6. 配置

### 6.1 默认值：`appsettings.json`

```json
{
  "Player": {
    "DefaultVolume": 0.8,
    "OutputMode": "WasapiShared",
    "PreferredDeviceId": null,
    "LastPlayedPath": null,
    "LastPlayedPositionSeconds": 0,
    "WindowLeft": 100,
    "WindowTop": 100,
    "WindowWidth": 800,
    "WindowHeight": 650
  }
}
```

复制到输出目录（`<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>`），通过 `services.Configure<AppSettings>(configuration.GetSection("Player"))` 绑定为 `IOptions<AppSettings>`。

### 6.2 运行时持久化：`%LocalAppData%\D-player\settings.json`（WPF 壳）

> **Phase 20：数据目录由 UI 壳注入**，下面 §6.2/§6.3/§6.4 三个文件在两个壳里各有一份，落点由 `DPlayerDataPaths.FolderName` 决定：WPF 壳 = `%LocalAppData%\D-player\`，WinUI 壳 = `%LocalAppData%\D-player-winui\`。**两壳不得共用目录**（`JsonSettingsPersistence`/`JsonPlaylistService`/`JsonLibraryCache` 各自的 `SemaphoreSlim` 只在一个进程内生效，跨进程没有锁，共用会互相覆盖）。

由 `JsonSettingsPersistence` 读写，包含与 `AppSettings` 相同的字段；首次启动文件不存在时使用 record 默认值。

**更名数据迁移（UmaPlayer → D-player，只属 WPF 壳）**：数据目录从 `%LocalAppData%\UmaPlayer\` 改为 `%LocalAppData%\D-player\`。`App.OnStartup` 最早期调用 `LegacyDataMigration.MigrateIfNeeded(dataPaths)`（签名 Phase 20 起接受 `DPlayerDataPaths`；在任何持久化服务被 DI 构造前），把旧目录内文件逐个搬到新目录（新目录已有同名文件则跳过 → 幂等；失败静默吞掉不阻断启动，旧数据保留）。**WinUI 壳不做这条迁移**（它面对的是新用户与新目录，迁移逻辑一旦跑在两壳上会把同一份旧数据搬进两个目录并互相看不见了）。

**当前被持久化的字段**：`DefaultVolume`、`WindowLeft/Top/Width/Height`、**`SpectrumEnabled/SpectrumSensitivity/SpectrumColorTheme/SpectrumSmoothing`（Phase 13）**、**`EqualizerEnabled/EqualizerPreamp/EqualizerBands/EqualizerPreset`（Phase 14）**、**`LastPlayedPath` / `LastPlayedPositionSeconds`（断点续播，2026-10-06）**。
**已建模但未启用**：`OutputMode`、`PreferredDeviceId`。

**断点续播（2026-10-06）**：`LastPlayedPath` + `LastPlayedPositionSeconds` 记录"上次播放的曲目与位置"——`PlayerViewModel` 在暂停/停止/切歌时**立即**写、播放中由 `PositionChanged` 驱动但**最多每 30 秒写一次**、关闭时在 `CleanupAsync` 兜底写（在 `IPlaybackService.Dispose` 之前读出）。启动时 `MainViewModel.InitializeAsync` 读回，在已水化的歌单里按路径找到该曲（`PlaylistsViewModel.FindTrackByPath`，"正在播放"歌单优先），`LoadAsync` + `Seek` **只就位不出声**——播放器栏显示曲目/时长/封面，按 `▶` 才从断点继续（`CurrentTrack != null` 后 ▶ 由 `PlayCurrent` 自动切回 `PlayPauseCommand`）。文件已缺失或该曲已不在任何歌单中时跳过恢复，由 `MainWindow` 弹一次 `ConfirmDialog.ShowInfo` 告知原因。恢复成功后立即回写一次，避免"启动后马上退出"把位置退回 0。无当前曲目时不写盘（清空队列/卸载后旧断点仍有效）。**Phase 20 的两壳差异**：逻辑全在 Core，但"跳过恢复"的**提示出口**各壳自给 —— WPF 弹 `ConfirmDialog`，WinUI 切片期只 `Debug.WriteLine`（WinExe 无控制台，等于不可见；列在对比材料的已知缺口里）。

### 6.3 队列快照：`%LocalAppData%\D-player\queue.json`（Phase 4/6/10；WinUI 壳写自己那份 `D-player-winui\queue.json`）

由 `JsonPlaylistService` 读写。Schema v3：

```json
{
  "SchemaVersion": 3,
  "Playlists": [
    {
      "Id": "guid...",
      "Name": "默认歌单",
      "Items": ["C:\\Music\\foo.mp3", "C:\\Music\\bar.flac"],
      "CurrentIndex": 0,
      "ShuffleEnabled": false,
      "RepeatMode": "Off",
      "SourceFolder": null
    }
  ],
  "CurrentPlaylistId": "guid...",
  "ShuffleEnabled": false,
  "RepeatMode": "Off"
}
```

Phase 12 continued: `ShuffleEnabled` / `RepeatMode` 提升到 QueueState 根级别（全局共享），Playlist 级别的同名字段保留但写入时固定为 `false` / `Off`（向后兼容）。

写时机：`MainWindow.Window_Closing`（每次关闭整队列覆盖一次）。读时机：`PlaylistViewModel` 构造期同步读盘。文件不存在/JSON 损坏/版本不匹配 → 静默 fallback 到空队列（保留旧文件供用户排查）。`Items` 中已被外部移动/删除的路径在加载时自动过滤；`CurrentIndex` 通过"向后滑、再向前回退"的算法映射到过滤后的位置（spec §5.1）。

### 6.4 元数据缓存：`%LocalAppData%\D-player\library-cache.json`（Phase 10）

由 `JsonLibraryCache` 读写。缓存文件夹绑定歌单扫描到的音频文件元数据，避免重复解析。Schema 为 `Dictionary<string, LibraryCacheEntry>`（key 为文件绝对路径）。`LibraryCacheEntry` 包含修改时间戳 (`LastWriteTimeUtc`) 与完整 `Track` 元数据。

读时机：`RescanSinglePlaylistAsync` 扫描前调 `ILibraryCache.LoadAsync` 加载缓存。写时机：扫描完成后调 `ILibraryCache.SaveAsync` 更新。`LoadAsync` 隐式契约：**绝不抛**（catch-all 静默 fallback 到空字典）。

---

## 7. 数据流：典型播放流程

### 7.1 启动 + v1→v2 迁移

```
App.OnStartup 构建 DI → MainWindow Show → MainWindow.Loaded → MainViewModel.InitializeAsync()
→ JsonPlaylistService.LoadAsync 读 queue.json:
    SchemaVersion==1 → 自动迁移成单条 "默认歌单" + 立即覆盖写
    SchemaVersion==2 → 正常反序列化
→ Hydrate 进 PlaylistsViewModel 后, ViewedPlaylist 默认对齐 CurrentPlaylistId
→ MainViewModel.InitializeAsync() 触发 Playlists.RescanFolderBoundPlaylistsAsync() 后台扫描
```

### 7.2 创建/重命名/删除歌单

```
PlaylistsSidebarView + 按钮 → AddPlaylistCommand（直接创建新歌单，无 PromptDialog）
→ 容器结构变化触发 StateChanged → MainViewModel debounce 500ms 后 SaveAsync
→ 删最后一个 → RemovePlaylistCommand 自动重建 "默认歌单"
```

### 7.3 切换查看(viewed) 不打断播放

```
sidebar 选中 → ViewedPlaylist 改 → MainWindow.xaml DataContext 切换 → PlaylistView 重绑数据
→ PlaylistView.RefreshCurrentIndicator 检查 _vm.IsActivePlaylist —— 非当前播放歌单上不显示 ▶
→ 当前正在播放的 PlayerBar 仍指向 CurrentPlaylistId 对应的歌单, 不变
```

### 7.4 双击跨歌单播放

```
PlaylistView.QueueList_MouseDoubleClick → App.GetService<PlaylistsViewModel>() →
HandleDoubleClickPlay(target, index) → 若 target.Id != CurrentPlaylistId 先切 CurrentPlaylistId
(触发 RecomputeIsActiveFlags + StateChanged) → await target.PlayTrackAtCommand.ExecuteAsync(index)
→ sidebar ▶ 标记跟随 CurrentPlaylistId 移动; PlaylistView ▶ 标记按 IsActivePlaylist 显隐
```

```
用户点击 ▶ (PlayerBar 按钮 → DataTrigger 跨级绑定 Playlists.ViewedPlaylist.PlayCurrentCommand)
    │
    ▼
PlaylistViewModel.PlayCurrent
  → PlayTrackAtAsync(CurrentIndex)
    │
    ▼
PlayTrackAtAsync (PlaylistViewModel)
  → ++_playToken（重入哨兵）
  → meta = await ITrackMetadataReader.ReadAsync(path)  ── Task.Run ──▶ ATL.Track 读元数据 (+封面)
  → 若 token 被顶替则静默退出
  → Queue[index] = meta（占位 Track → 完整 Track，ObservableCollection 触发 Replace）
  → await IPlaybackService.LoadAsync(meta)  ── Task.Run ──▶ MediaFoundationReader
                                                              VolumeSampleProvider
                                                              WasapiPlayer(Shared + 事件同步, 100ms；Phase 19 前为 WasapiOut)
                                                              track with { Duration, SampleRate }
    │
    │ 事件:  PositionChanged(Zero)  ─▶ PlayerViewModel.Position
    │       DurationChanged       ─▶ PlayerViewModel.Duration
    │       TrackChanged          ─▶ PlayerViewModel.CurrentTrack + AlbumArtImage
    ▼
IPlaybackService.Play() ─▶ WasapiPlayer.Play() + PollPositionAsync 循环
    │
    │ (每 33ms)
    ▼
PositionChanged ─▶ PlayerViewModel.HandlePositionChanged
                   → if (!IsSeeking) Position = ClampToDuration(pos)
    │
    ▼
PlayerBar.Slider 绑定 PositionNormalized (Mode=OneWay) → UI 实时更新

曲目自然播完：
IPlaybackService.OnPlaybackStopped 检测距 TotalTime ≤ 200ms
  → TrackEnded ─▶ PlaylistViewModel.HandleTrackEnded
                  → RepeatOne：PlayTrackAtAsync(CurrentIndex)
                  → 否则：CalculateNextIndex → PlayTrackAtAsync(next)
```

### 7.5 频谱数据流（Phase 13）

```
WasapiPlayer 拉流 → VolumeSampleProvider → SampleAggregator.Read(buffer)
  → 混单声道 + 填 FFT 缓冲区（满 8192 点）
  → 汉宁窗 → FFT → 对数分组 32 桶（RMS + gain8x + dB + gamma）→ 50% 重叠
  → SpectrumDataReady(float[32].ToArray())   ── 音频线程 ──▶
NAudioPlaybackService.OnSpectrumDataReady
  → _syncContext.Post 封送 ── UI 线程 ──▶ SpectrumDataAvailable(data)
PlayerViewModel.HandleSpectrumData(data)
  → if (!SpectrumEnabled) return
  → 复制 → 乘灵敏度增益 → 指数平滑(_smoothedSpectrum) → SpectrumData = smoothed.ToArray()
TrackInfoView → SpectrumView.SpectrumData (DP) → OnSpectrumDataChanged 算 _targetHeights
  → CompositionTarget.Rendering 每帧缓动 Rectangle.Height（60fps）

设置变更（SettingsDialog.Save_Click / PlayerViewModel.OnSpectrum*Changed）
  → UpdateAsync 写 settings.json + 同步 PlayerViewModel 属性
  → OnSpectrumEnabledChanged 额外回写 _player.SpectrumConfig.Enabled（禁用即停 FFT）
```

### 7.6 均衡器数据流（Phase 14）

```
启动：PlayerViewModel.Initialize → LoadEqualizerSettings(settings)
  → 先设 EqualizerEnabled observable（构造期 _isInitializing=true，OnEqualizerEnabledChanged 不写盘）
  → _player.EqualizerConfig = EqualizerConfig.Create(Enabled/Preamp/Bands/Preset)（存字段，待首次 LoadAsync 拾取）

播放：PlaylistViewModel.PlayTrackAtAsync → IPlaybackService.LoadAsync(track)
  → DisposePlayback 重建链：ToSampleProvider → new EqualizerSampleProvider(sampleProvider, _equalizerConfig)
    → new SampleAggregator(_equalizer, _spectrumConfig) → VolumeSampleProvider → WasapiPlayer
  （EQ 在 SampleAggregator 之前 → 频谱反映 EQ 后信号）

拖动/预设（实时）：EqualizerDialog.BandSlider_ValueChanged / PresetCombo_SelectionChanged / Enable_Changed
  → PushToService(preset)：_playbackService.EqualizerConfig = EqualizerConfig.Create(...)
  → setter：存字段 + _equalizer?.Update(value)
  → Update 在 buffer 粒度 lock 内对每声道每段 existing.SetPeakingEq(...) 就地重算系数（保留 x1/x2/y1/y2）
  → 下一个 Read 缓冲区即听感生效（且频谱同步反映；链未建时仅存字段待 LoadAsync 拾取）

保存：EqualizerDialog.Save_Click
  → _persistence.UpdateAsync 写 settings.json（EqualizerEnabled/Preamp/Bands/Preset）
  → 再确认一次链上 _playbackService.EqualizerConfig
  → 留在 UI 线程写 PlayerViewModel.EqualizerEnabled（🎚 按钮高亮即时更新）→ DialogResult=true

取消/关闭：EqualizerDialog.OnClosed（未 _saved）
  → _playbackService.EqualizerConfig = _initialConfig（打开瞬间的实时链快照）→ setter → Update 撤销实时预览
```

### 7.7 播放列表文件导入/导出（Phase 18）

```
导入（四个入口共用同一管道；presetPath 是拖拽与对话框的接点）：

侧边栏按钮 ImportBtn_Click ──┐                    ┌─ PlaylistImportUi.RunDialogAsync（presetPath=null）
侧边栏 Drop（FilterPlaylistPaths）─┤              │
工具栏"导入列表" ImportListButton_Click ─┤        ├▶ VM.ImportPlaylistFileAsync(presetPath?)
列表区 Drop（FilterPlaylistPaths）──┘             └─ PlaylistImportUi.RunForDroppedFilesAsync（逐文件，聚合报告）
    │
    ▼
presetPath ?? IFileDialogService.OpenFiles(OpenFilter)   ── 取消 → null（View 不弹任何框）
    │
    ▼
IPlaylistFileService.ImportAsync(path)（绝不抛）
  → File.ReadAllBytesAsync → PlaylistFileEncoding.Decode（BOM → 严格 UTF-8 → GBK 回退）
  → .pls ? PlsParser : M3uParser → Classify：
      URL 前缀 → SkippedUnsupported ／ 相对路径按列表所在目录 GetFullPath 归一化
      → 后缀不在 AudioConstants 白名单 → SkippedUnsupported（判定先于存在性）
      → File.Exists 为假 → SkippedMissing ／ 通过 → AcceptedPaths（不去重）
    │
    ├─ 容器级（PlaylistsViewModel）：AcceptedPaths 空 → 不建歌单只回报告；
    │    否则 ReadMetadataBatchAsync → Playlist seed（SourceFolder=null）→ _factory
    │    → Queue 顶掉占位 Track → HookPlaylistVm → Playlists.Add → ViewedPlaylist=vm
    │    （CollectionChanged → StateChanged → MainViewModel debounce 存盘）
    │
    └─ 歌单级（PlaylistViewModel）：AcceptedPaths 直接喂既有 DropExternalFiles
         （逐个 ReadAsync → Queue.Add，不自动播放；不给 VM 加 ILibraryScannerService）
    │
    ▼
返回 PlaylistImportReport（纯数据）→ PlaylistImportReportFormatter.Format（View 拼中文文案）
  → ConfirmDialog.ShowInfo("导入播放列表", 文案)；意外异常由 PlaylistImportUi 兜住转 ShowError

导出（仅工具栏"导出列表"，作用于当前查看的歌单）：

ExportListButton_Click：Queue.Count==0 → 静默返回
  → PlaylistViewModel.ExportPlaylistFileAsync()
      → IFileDialogService.SaveFile(SaveFilter, SanitizeFileName(Name), ".m3u8")
        （Win32 SaveFileDialog：AddExtension + OverwritePrompt；取消 → null）
      → IPlaylistFileService.ExportAsync(dest, Queue.ToArray())
        → M3u8Writer.Write：#EXTM3U + 每条 #EXTINF:{秒},{Artist - Title} + 绝对路径
          （UTF-8 无 BOM、CRLF；未知时长 -1；Artist 空只写 Title；Title 空写文件名）
      → 成功/取消返回 null（不弹框）；IOException 等 → VM 捕获返回 "导出失败：{ex.Message}"
  → View 收到非 null → ConfirmDialog.ShowError("导出播放列表", 文案)
```

### 7.8 WinUI 壳的启动 / 双击 / 关闭（Phase 20）

```
启动（全部在 UI 线程）：
App.OnLaunched
  → new DPlayerDataPaths { FolderName = "D-player-winui" }
  → services.AddDPlayerCore(configuration, dataPaths)
  → services.AddSingleton<IFileDialogService, WinUiFileDialogService>()   ← 各壳自己注册（Core 注册表里没有）
  → BuildServiceProvider → GetRequiredService<MainViewModel>             ← NAudioPlaybackService 在此捕获
                                                                            DispatcherQueueSynchronizationContext
  → new MainWindow(vm) → Activate()
  → _ = vm.InitializeAsync()   → 水化 queue.json + Hydrate + 断点就位（只就位不出声）
                                   失败/跳过 → Debug.WriteLine（WPF 是弹 ConfirmDialog）
  → MainWindow ctor 里：SystemBackdrop = MicaBackdrop（要求根 Grid Background=Transparent）
                        AppWindow.Closing += AppWindow_Closing
                        Playlists.Playlists.CollectionChanged += SyncPlaylistMenu（歌单比 Loaded 晚到）

双击（与 WPF 同一个 Core 入口）：
TrackList_DoubleTapped
  → IndexOfByReference(ViewedPlaylist.Queue, track)     ← 按引用身份，不用 Queue.IndexOf
  → await Playlists.HandleDoubleClickPlay(pl, index)
      → pl.Id != CurrentPlaylistId 时先切指针（RecomputeIsActiveFlags + StateChanged）
      → index 合法才 await pl.PlayTrackAtCommand → PlayTrackAt 里 _shuffleHistory.Clear() → PlayTrackAtAsync
                                                        （"视为新会话"，两壳语义一致）

关闭（cancel-and-close，复刻 WPF）：
AppWindow.Closing → args.Cancel = true; _isClosing = true
  → await MainViewModel.CleanupAsync()   ← 写最终断点位置 + queue.json + 释放 WASAPI 设备
  → Close()                              ← 第二次 Closing 直接放行
刻意不：同步 Dispose ServiceProvider（MainViewModel 只实现 IAsyncDisposable，同步 Dispose 会抛）
```

---

## 8. 构建与运行

### 8.1 先决条件

- Windows 10/11（开发于 Windows 11 IoT Enterprise LTSC 2024）；WinUI 壳的 TFM 要求 build 19041（Windows 10 2004）及以上，self-contained 形态不需另装 Windows App SDK 运行时
- .NET 10 SDK（`net10.0-windows`）
- 构建 WinUI 壳需 nuget.org 可达（首次 restore 拉 `Microsoft.WindowsAppSDK` 2.5.1）；WinUI 工程只支持 x64
- 任意 IDE：Visual Studio 2026+ / JetBrains Rider / VSCode + C# Dev Kit

### 8.2 命令行构建与门禁

**门禁 = 两条命令，走解决方案筛选器 `D-player.slnf`（= `D-player.Core` + `D-player.csproj` + `Tests`），每次改动都跑，不是 `D-player.sln`**：

```bash
dotnet restore D-player.slnf
dotnet build   D-player.slnf -c Debug --nologo -v q      # 门禁一：0 警告 0 错误
dotnet test    D-player.slnf -c Debug -v q               # 门禁二：180 通过 / 0 失败（切勿加 --nologo，见下）
dotnet run     --project D-player.csproj                 # 跑 WPF 壳
dotnet run     --project Tests/D-player.Tests.csproj -c Debug   # 跑测试（MTP，直接跑测试可执行程序）
```

**一次跑完（推荐，逐步 fail-fast，任一步非零退出即中止）**：

```bash
powershell -File tools/verify-gates.ps1 -Fast   # 门禁一 + 门禁二
powershell -File tools/verify-gates.ps1 -Full   # 门禁一 + 门禁二 + WinUI 壳侧检查
```

输出目录：`bin/Debug/net10.0-windows/`，可执行：`D-player.exe`。

**词汇只有三层（口径与 §4.3 第 28 条一致）**：**门禁**（上面两条，自动化、每次改动必跑）／**壳侧检查**（WinUI 单工程构建，**不是门禁**——没有自动化会跑它，但有确定的触发条件，见下）／**可选**（整解 `D-player.sln` 构建）。判据很简单：**把需要手跑的命令叫成"门禁"，就是在宣称它会自动跑**；本文档此前用过"门禁三件套"和"门禁三"来指含 WinUI 的那三条，这两个说法已作废。

**为什么要筛选器**：WinUI 工程引用 `Microsoft.WindowsAppSDK`，把它留在每次构建/测试里会让"改一行共享层跑全量"从秒级变成十秒级、首次 restore 从秒级变成约 8 分钟。`D-player.slnf` 让 WinUI **刻意不进门禁**，代价是它没有自动回归（见 §4.3 第 28 条与 §9）——所以它必须由一条**说清了何时跑**的壳侧检查补位，而不是被叫做门禁。

WinUI 3 壳的**壳侧检查**（不是门禁）与运行：

```bash
dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q   # 壳侧检查：0 警告 0 错误
dotnet run   --project D-player.WinUI/D-player.WinUI.csproj -c Debug
```

**这条构建什么时候跑（必须连着读，孤立的一行命令没有意义）**：`D-player.Core` 的公开面一变就跑（构造参数、`IFileDialogService` 的新成员、`HandleDoubleClickPlay` 的元数、`ViewedPlaylist` 的 setter……），以及任何改到 WinUI 本身的时候。理由就是 B1 的要害：**这类改动让 `.slnf` 两条门禁照样全绿，而 WinUI 壳已经编译不过**。成本不是借口——首次 restore 之后它是增量构建，2026-10-07 实测 **9.31 秒**。

**`dotnet restore D-player.sln` 与 `dotnet build D-player.sln` 仍然存在，但不是门禁**：`.sln` 里四个工程全在，restore 会**首次**从 nuget.org 拉 Windows App SDK（2.5.1，连带 9 个子包），本机实测约 **8.1 分钟**，且 self-contained 输出目录很大。只有这两种情况才需要它：① 要构建/跑 WinUI；② 要确认 IDE 的「Build Solution」不被 WinUI 打挂（Phase 20 实测整解构建 0 警告 0 错误）。

**测试栈（Phase 19）**：`xunit.v3` 4.0.1（测试工程是**可执行程序**，`OutputType=Exe`）。runner 只有一个 —— Microsoft.Testing.Platform（MTP）；上面两条测试命令是同一个 runner 的两个入口，不是"MTP + VSTest 双 runner"。**测试总数 180**（172 个 `[Fact]` + 1 个 `[Theory]` 展开的 8 条 `[InlineData]`；逐文件分解见 §3 的 Tests 树）。

> `dotnet test` 之所以还能跑，靠仓库根 `global.json` 的 `{"test":{"runner":"Microsoft.Testing.Platform"}}`（.NET 10 SDK 的原生 opt-in）。csproj 里的 `TestingPlatformDotnetTestSupport` 是 .NET 9 及更早版本的路由开关，在本 SDK 上不参与执行路径；`xunit.runner.visualstudio` 4.0.0 与 `Microsoft.NET.Test.Sdk` 18.10.1 为 IDE 测试发现（测试浏览器）的兼容性而保留 —— **该能力在本阶段未被验证过**（验收只跑上面两条命令行路径，未驱动过 Rider/Visual Studio 的测试浏览器），也不在跑测试的路径上。**删除 `global.json` 会让 `dotnet test D-player.slnf` 失败**（Microsoft.Testing.Platform 2.x 在 .NET 10 SDK 上不再支持 VSTest 目标）。
>
> **两条测试命令都不要加 `--nologo`**：MTP 不识别该参数，加上后一条测试都不会跑，摘要却打印 `成功: 0`（易被当成全绿；实际输出是"运行了零个测试"+ 退出码 5）。`--filter "FullyQualifiedName~X"` 照常可用。`--nologo` 只能用在 `dotnet build` 上。

音频集成测试（`NAudioPlaybackService*Tests`）真实占用 WASAPI 设备，并行度结论见 §9。

### 8.3 发布（独立可执行）

```bash
dotnet publish D-player.csproj -c Release -r win-x64 \
    --self-contained false /p:PublishSingleFile=true
```

---

## 9. 已知约束与陷阱

- **格式限制**：仅支持 Windows Media Foundation 原生解码的格式；OGG/Vorbis 需用户系统安装第三方编解码器或后续切换 reader。
- **静默错误**：`HandlePlaybackError` 仅 TODO，未弹窗或写日志；调试期可通过断点检视。
- **启动时同步 IO**：`PlayerViewModel.Initialize()` 与 `MainWindow` 构造函数中均使用 `LoadAsync().GetAwaiter().GetResult()`；`PlaylistViewModel.LoadFromDisk()` 同理（Phase 4）。三个文件都极小（settings 几百字节、queue 视队列长度，典型 < 10KB），可接受，未来若数据膨胀需重构。
- **DI 解析跨线程**：`IPlaybackService` 必须由 UI 线程首次构造（依赖 `SynchronizationContext.Current` 捕获），目前由 `App.OnStartup` 保证。
- **UpdateAsync 跨线程读 DP**：`JsonSettingsPersistence.UpdateAsync` 内部 `.ConfigureAwait(false)` 把 mutator 调用 / 继续上下文带到 threadpool；若 mutator 闭包内读 WPF DependencyProperty（如 `Left/Top/Width/ActualHeight`）会抛 `InvalidOperationException`。**调用方必须先在 UI 线程把 DP 值捕获到局部变量再 await**。`MainWindow.xaml.cs:Window_Closing` 即采用此模式。
- **`async void Window_Closing` 多 await dispatcher race**（Phase 4）：超过 1-2 个 await 时第一个 await yield 后 WPF 立即继续关闭流程，`ShutdownMode.OnLastWindowClose` 触发 `Application.Shutdown → Dispatcher.InvokeShutdown`，后续 await 续延 post 到死 dispatcher 上**永不运行**。修复纪律：用 cancel-and-close 模式（首次 `e.Cancel=true` 加 `_isClosing` 标志，做完异步工作再 `Close()`）。Phase 4 加入 queue.json 写盘后从 2 个 await 涨到 4 个，settings 还能写但 queue 永远不更新；commit `9bae7ce` 用此模式修复（COUPLING.md §5）。
- **WPF inline Style 必须 `BasedOn` 隐式主题样式**：`<X.Style><Style TargetType="X">` 没有 `BasedOn="{StaticResource {x:Type X}}"` 会完全替换 `Themes/Controls.xaml` 中的隐式 Style，回退到 OS 原生外观（Button 白底灰框、Slider 灰色等）。Phase 4 ▶ 按钮加 DataTrigger 时漏 BasedOn → 按钮变白底；commit `ca66fa9` 修复。
- **WPF DP 优先级 `local > trigger setter > style setter`**（Phase 5）：要被 `Style.Triggers` 改的属性，**默认值必须放进 `Style.Setter`**，不能作为元素的 local attribute 写。Phase 5 拖拽边框高亮初次落地时 `<Border BorderBrush="Transparent">` 写成 local，让 `IsDragOver=True` 的 trigger setter 永远赢不了 → 高亮失效；commit `214d595` 把默认值挪进 Style.Setter 修复。Code review checklist：看到 inline Style.Trigger 改某 DP 时，对照查这个 DP 在元素自身上没有 local 写法。
- **WPF DragDrop RoutedEvent 冒泡 + Handled 拦截**（Phase 5）：`Drop` / `DragOver` 等都是冒泡事件；子元素设 `e.Handled = true` 后父元素的同名 handler 不再触发。Phase 5 验收时撞过：`QueueList_Drop` 处理完入队/重排设 `Handled=true`，原本想靠 `Root_Drop` 清高亮的逻辑被吃掉 → 高亮卡死。修复：清高亮（清 Adorner、清 IsDragOver）必须在两条路径都做（`QueueList_Drop` finally + `Root_Drop`），不能假定事件会冒泡上来。
- **WPF ListBox `PreviewMouseLeftButtonDown` 不消费事件 → 多选拖拽塌选**（Phase 5）：Preview 阶段不 `Handled=true` 时，ListBox 自身的选中处理仍会执行；用户 Ctrl+多选后再不带修饰键按下其中一项，ListBox 默认行为会立刻塌成单选，让随后启动的 DoDragDrop 拿到 `SelectedItems.Count==1`。修复：在按下点是"已选 + 多选 ≥2 + 无 Ctrl/Shift"时拦掉 `Handled=true`，没真正拖起来时再在 `MouseUp` 手动塌成单选模拟原行为；commit `af51dde`。
- **WPF record 结构相等会让 `Queue.IndexOf` / `HashSet<Track>` 塌陷重复占位**（Phase 5）：Track 是 `sealed record`；两个 `CreateFallback("X.mp3")` 在结构上相等。基于相等性的查找/去重会把它们认作同一个，重排时 `Queue.IndexOf(currentTrackObj)` 返回首个结构等价匹配而非原始那一个 → ▶ 跟到错的曲、Shuffle 历史塌陷。修复纪律：所有需要"找回原来那一个 Track 实例"的代码用 `ReferenceEquals` + `ReferenceEqualityComparer.Instance`（见 `PlaylistViewModel.MoveTracks` 与 `PlaylistView.QueueList_PreviewMouseMove`）。
- **ViewBox + CornerRadius + ClipToBounds（Phase 12 continued）**：`ViewBox` 缩放子元素时会突破父 `Border` 的 `CornerRadius` 圆角裁切区域，导致封面方形直角溢出圆角边框。修复：给封面 `Border` 加 `ClipToBounds=True`，让 WPF 裁切到 Border 边界内。同时封面 `Border` 不能有 `CornerRadius`（与播放时直角不一致），圆角仅在外层容器 Border 上设置。
- **频谱事件跨线程（Phase 13）**：`SampleAggregator.SpectrumDataReady` 在 NAudio 音频渲染线程触发，`NAudioPlaybackService.OnSpectrumDataReady` 必须经 `_syncContext.Post` 封送到 UI 线程再广播 `SpectrumDataAvailable`；`PlayerViewModel.HandleSpectrumData` 直接在 UI 线程更新 `SpectrumData` 绑定属性。若跳过封送会跨线程触碰 DP 抛异常。`SettingsDialog.Save_Click` 同理故意不用 `ConfigureAwait(false)`，留在 UI 线程才能直接写 `PlayerViewModel` 属性。
- **EQ 系数实时更新的线程安全（Phase 14）**：`EqualizerSampleProvider.Read`（NAudio 音频线程）与 `Update`（UI 线程）共用 buffer 粒度 `lock` 互斥，防止撕裂系数；`Update` **必须用 `SetPeakingEq` 就地重算**（保留 x1/x2/y1/y2 延迟线状态），若重建 `BiQuadFilter` 会清空延迟线导致拖动时爆音。立体声必须每声道独立 `BiQuadFilter?[channel][band]`（左右共享实例会串扰滤波状态）；中心频率 ≥ 奈奎斯特（`sampleRate/2`）的频段置 null slot 旁路（PeakingEQ 在 ≥Nyquist 时不稳定）。`EqualizerDialog.Save_Click` 同 `SettingsDialog` 故意不用 `ConfigureAwait(false)`，留在 UI 线程才能写 `PlayerViewModel.EqualizerEnabled`。
- **WPF ComboBox 向空集合添加首项会自动选中 index 0（Phase 14）**：`ComboBox` 在从空集合添加第一个项时会自动选中该项并触发一次 `SelectionChanged`。`EqualizerDialog.OnLoaded` 填充预设下拉必须在 `_suppress` 窗口内进行，否则打开对话框即误 push 一次 Flat/禁用配置，扰动正在播放的 EQ（Phase 14 code review 拦下的 Critical）。
- **播放链生命周期必须串行化；"停止"意图必须显式标记**（Phase 18 后修复，Phase 19 迁移到 WasapiPlayer）：`NAudioPlaybackService.LoadAsync` 在线程池上重建整条播放链，而 `Unload` / `Dispose` / 传输命令来自 UI 线程。缺串行化时，后一次重建会释放前一次正在使用或构建的链（`_reader` / `_volumeProvider` / `_wavePlayer` 三个共享字段被交错读写）→ 异常从链内部抛出并冒进 `async void` 事件处理器（进程崩溃）。修复是单闸门 `_chainGate`，唯一例外是 `OnPlaybackStopped`（播放线程回调）——持锁方可能正阻塞在输出类的 `Stop()` / `Dispose()` 上等待播放线程退出（`NAudio.Wasapi 3.1.0` 随包 XML 文档：`Dispose` 一条明确写着 "Stops playback (blocking) and releases all resources"，而 `Stop` 只有 "Stop playback and flush buffers"），回调里取锁即死锁，故其读取一律防御式。**NAudio 3 的 guarded dispose 不使这条失效**：它只保护 NAudio 自己的对象，管不到我们的共享字段。同源的第二条陷阱：用户停止与自然播完都只表现为一次 `PlaybackStopped`，输出类不告知发起方，仅凭播放头位置无法区分（曲尾 200ms 内按停止会被误判为播完并自动推进下一首）；修复是 `_stopRequested` 意图标记（`Stop`/`DisposePlayback` 置位、`Play` 清零、`OnPlaybackStopped` 命中即提前返回）——该不确定性不随实现变化，故换类后依然必要。回归测试（两条的证据强度不同，别混着引用）：闸门侧 `NAudioPlaybackServiceConcurrencyTests` 的 `LoadAsync_TwoOverlappingCalls_DoNotThrow` / `Unload_DuringLoadAsync_DoesNotThrow` 以 1–5 ms 错位窗口断言"不抛异常"，是真实但**随机**的证据（窗口未命中时只是没有触发竞态）；意图标记侧只有 `NAudioPlaybackServiceStopSemanticsTests.Stop_WhenPlayheadIsAtTrackEnd_DoesNotRaiseTrackEnded` 一条确定性钉住"停止不得冒充播完"（`Seek` 到距曲尾 100ms 再 `Stop`），同类中的 `Unload_WhenPlayheadIsAtTrackEnd_DoesNotRaiseTrackEnded` **读不到** `_stopRequested`（`DisposePlayback` 会写入它，但先解订阅 `PlaybackStopped` 再停止，回调不触发，判定分支从未执行），故它钉住的是"解订阅早于停止"这条顺序；反方向由上句那 5 条中的 `NAudioPlaybackServiceConcurrencyTests.PlayToNaturalEnd_ThenAdvance_DoesNotThrow` 钉住（真自然播完仍须发 `TrackEnded`——注意它在闸门侧那个类里，不在 `…StopSemanticsTests`）。
- **音频集成测试的并行度**（Phase 19）：仓库有 5 条测试真实占用 WASAPI 设备（`NAudioPlaybackServiceConcurrencyTests` 3 条 + `NAudioPlaybackServiceStopSemanticsTests` 2 条）。迁到 xunit v3 时在默认并行下实测：结论是 **level 0 —— 沿用 xunit v3 的默认并行，既没有加 `[Collection("AudioDevice")]`，也没有 `Tests/AssemblyInfo.cs` / `DisableTestParallelization`**。证据：10 次 MTP 运行 + 3 次 `dotnet test` 运行，每次都是 161 通过 / 0 失败 / 0 跳过（含那 5 条真设备用例），单次墙钟 5.875–6.249 s、runner 内 1.69–1.95 s。改这两个测试类或新增音频测试时沿用同一约束；若真出现设备争用/时序漂移，再按设计稿 §5.3 的顺序收紧（先给音频测试类加同一个 `[Collection("AudioDevice")]`，仍不稳才全局禁用并行）。
- **给 `IPlaybackService` 加成员必须同步改 `NullPlaybackService`（Phase 14）**：`PlaylistsViewModel` 内有一个手写的 `NullPlaybackService` 空对象（该接口的第二个生产实现者，供 internal 测试构造器用），NSubstitute 只覆盖测试替身。给 `IPlaybackService` 加 `EqualizerConfig` 时必须同步给 `NullPlaybackService` 补上该成员，否则 CS0535 编译失败。
- **矢量图标经 `Application.Current.FindResource` 解析（Phase 16）**：`PlayStateToIconConverter` / `RepeatModeToIconConverter` / `BoolToVolumeIconConverter` 返回的 `Geometry` 依赖 `Icons.xaml` 已在 `App.xaml` 合并；若漏合并会运行时抛异常。新增图标须同步 `Icons.xaml` 键与使用处（COUPLING.md §5 Phase 16 契约）。▶ 标记元素类型为 `Path`：`FindChildByName<Path>(container, "PART_Marker"/"PART_SidebarMarker")`，改回 TextBlock 或改泛型会静默失效。
- **WindowChrome 自绘按钮必须 `IsHitTestVisibleInChrome=True`（Phase 17）**：caption 高度（32px）区域内的自绘按钮不加此 attached 属性会被 caption 拖动吞掉点击；改动标题栏按钮/新增标题栏控件时必查。
- **无边框窗最大化边距必须用常量式工作区计算（Phase 17）**：`TitleBar.ApplyWindowState` 给根内容加「`WorkArea` 偏移 + `WindowResizeBorderThickness`」边距；不要改回"读窗口实际边界"的实现 —— 布局时序会让 right/bottom 边距偏大导致大片留空（commit `d86694b` 修）。
- **WPF ComboBox 深色模板的 ToggleButton 用 `ClickMode=Press`（Phase 17）**：ComboBox 本体由自绘 `ToggleButton` 承载点击（展开/收起），`ContentPresenter` 设 `IsHitTestVisible=False` 叠加显示选中值；点击必须落在 toggle 表面才能开合下拉（commit `dd435bd` 修）。
- **单击进度条跳转必须 `handledEventsToo: true` 挂接（2026-10-05 修复）**：隐式 Slider 样式开启 `IsMoveToPointEnabled`（Phase 13 `781d35d`）后，Slider 类处理器按轨道时会先置 `e.Handled=true`，XAML 属性挂接的实例处理器（不接收已处理事件）被静默跳过 → 单击跳转整体失效。`PlayerBar` 构造函数改用 `SeekBar.AddHandler(PreviewMouseLeftButtonDownEvent, ..., handledEventsToo: true)`。音量滑块 TwoWay 不受影响；进度条 Value 是 OneWay，类处理器的本地改值不回传 VM 且被 30Hz 轮询覆盖。
- **`UnloadCurrentTrack` 必须以 `IsActivePlaylist` 守卫（2026-10-05 修复）**：`IPlaybackService` 是全局单例——清空/删除非播放中歌单的曲目不得停掉正在播放的歌（`ClearQueue`/`RemoveTrack` 经此方法）。只有当前播放歌单才有权 `_player.Unload()`；`_playToken++` 与 `CurrentIndex=-1` 保持无条件。
- **net10.0 显式引用 `System.Text.Encoding.CodePages` 会触发 NU1510（Phase 18）**：该包在 net10.0 上框架隐含（framework-implicit），`Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` + `Encoding.GetEncoding(936)` 开箱可用；显式 `PackageReference` 会被 SDK 判定冗余并给 NU1510 警告，破坏本项目 0 警告门禁。`D-player.csproj` 因此**没有**该包引用，不要"补依赖"加回去。
- **GBK 解码不得搬出 `PlaylistFileEncoding`（Phase 18）**：CodePages provider 在该类型的**静态构造函数**里注册，`Encoding.GetEncoding(936)` 只出现在该类内部，"注册永远早于解码"是类型不变量而非启动顺序约定（`App.xaml.cs` 无需改动）。把 GBK 解码搬到别的类型就等于把注册时机重新变成一条口头约定（COUPLING §5）。
- **GBK 编码探测存在可接受的误判（Phase 18）**：严格 UTF-8 试解码成功即认定 UTF-8；极少数 GBK 字节序列恰好是合法 UTF-8 时会被误判并产生乱码路径——后果是这些条目落入"文件缺失"计数，用户能从导入报告里看出来，不会静默错乱。
- **Core 必须 WPF-free（Phase 20）**：`D-player.Core/` 下不得出现 `System.Windows.*` / `PresentationFramework` / `ICollectionView` / `CollectionViewSource`，否则第二壳被拖回 WPF。可 grep 自检（四个类型名一个都不能少，命令与上面这条规则同口径）：`grep -rn "System.Windows\|PresentationFramework\|ICollectionView\|CollectionViewSource" D-player.Core --include=*.cs | grep -v "/obj/\|/bin/"` → **应无命中，2026-10-07 实跑确实零命中**。注意 `BitmapImage` **不在这个自检的模式里**：Core 有三行注释提到它（`Models/Track.cs:13`、`ViewModels/PlayerViewModel.cs:18`/`:47`，说的是 WPF 壳侧 converter 的行为），那是散文不是类型引用，加进模式只会让自检长期"命中注释"而失去意义。
- **仓库根的兄弟目录必须进 `D-player.csproj` 的 glob 排除集（Phase 20）**：WPF SDK 会生成 `*_wpftmp.csproj` 并从仓库根重新 glob `**/*.cs` / `**/*.xaml`；`Tests/**`、`D-player.Core/**`、`D-player.WinUI/**` 三组各有一份 `Compile/Page/ApplicationDefinition/Resource/None/EmbeddedResource Remove`。漏掉的后果分别是重复编译进主程序集（AssemblyInfo 重复、xunit 引用缺失）与 `MC3074`/`CS0234`（WPF 侧不存在 `Microsoft.UI.Xaml`）。新增仓库根级工程时必须同步补一组，并**重跑门禁**确认隔离仍然成立。
- **WinUI 不进门禁，靠一条有明确触发条件的壳侧检查补位（Phase 20 的设计决策，不是遗漏）**：`D-player.slnf` 只含 Core + WPF + Tests，所以 WinUI 的 XAML 编译、左栏（pane）逻辑、关闭落盘**没有自动回归**，只能靠**手跑的**单壳构建 + 真机手测。既然没有自动化会跑它，就必须把触发条件写死：**`D-player.Core` 的公开面一变就跑壳侧构建**（构造参数、`IFileDialogService` 新成员、`HandleDoubleClickPlay` 的元数、`ViewedPlaylist` 的 setter……这些改动下 `.slnf` 两条门禁全绿，而壳已经编译不过），改到 WinUI 自身时同样跑；一条命令 `powershell -File tools/verify-gates.ps1 -Full` 就把它和门禁一起跑完（增量，实测 9.31 秒）。`Tests/Extensions/AddDPlayerCoreTests.cs` 钉住 Core 的 DI 图，但**钉不住 WinUI 壳忘记注册自己的 `IFileDialogService`** —— 那种缺失要到第一次构造歌单才炸。要收紧这条，就得先接受 WinUI 进门禁带来的 restore/构建成本（见 §8.2）。
- **WinUI 的 Mica 可见性依赖"根 Grid 背景必须是 `Transparent`"这条约定（Phase 20）**：材质挂在窗口上，但只在没有不透明背景刷的表面后面可见。以后任何人往根 Grid 或某个铺满的容器上加不透明背景刷，材质就"看起来消失"。护栏写在 `D-player.WinUI/MainWindow.xaml` 顶部注释（含三行像素对照）。反向陷阱：`DWMWA_SYSTEMBACKDROP_TYPE` 对组合器挂载的 backdrop **不是判据**（本项目实测恒为 0），基于它的自动化断言必然得出错结论 —— 实施过程中就真的被它误导过一次，把已生效的 Mica 判成了"未挂载"。
- **两壳不得共用数据目录（Phase 20）**：三个持久化服务的 `SemaphoreSlim` 只在单进程内生效，跨进程无协调；`D-player` 与 `D-player-winui` 必须分开。`LegacyDataMigration` 的 UmaPlayer 迁移**只属 WPF 壳**（跑两遍会把同一份旧数据搬进两个目录，此后两边互相看不见）。
- **`IFileDialogService` 是同步接口，WinUI picker 只有异步 API（Phase 20 已知障碍，未解）**：用同步接口驱动异步 picker 必须在 UI 线程阻塞等待，而模态 picker 的消息正需要这条线程 → 死锁。第二阶段要么把 Core 接口改异步（跨壳改动，需单独决策），要么壳侧走异步入口 + 完成后回填。切片期的 `WinUiFileDialogService` 因此是**空实现**，只保证 DI 图可解析。
- **WinUI 壳的运行期异常没有可见出口（Phase 20）**：WinExe 无控制台，`Debug.WriteLine` 不进任何流，UIA 也看不到；"未观察到异常"的证据强度只到"进程活着 + 退出码 0"。本机读取 Windows 应用程序日志 / WER 被安全策略拒绝，所以连这一条都没有第三方佐证。第二阶段应先给壳加一条最小崩溃/日志出口，再往上堆功能。

---

## 10. 历史与参考

- 耦合分析：[`docs/COUPLING.md`](./COUPLING.md) — 风险登记册 + 启动检查清单（Phase 15 完成）
- Phase 20 两壳对比材料与决策门记录位：[`docs/PHASE20-COMPARISON.md`](./PHASE20-COMPARISON.md) — 功能等价核对表 + 六维评分表（**决策门仍未拍板**，结论栏留空待用户填写；§2.1 那份走查清单已由用户 2026-10-07 在真机走完，逐项结果与那处"进度条单击定位"缺陷记在 §2.2）
- 设计稿：
  - [`docs/superpowers/specs/2026-04-23-uma-player-design.md`](./superpowers/specs/2026-04-23-uma-player-design.md) — Phase 1 整体设计
  - [`docs/superpowers/specs/2026-06-06-uma-player-playlist-design.md`](./superpowers/specs/2026-06-06-uma-player-playlist-design.md) — Phase 2 播放列表设计
  - [`docs/superpowers/specs/2026-06-07-uma-player-phase3-design.md`](./superpowers/specs/2026-06-07-uma-player-phase3-design.md) — Phase 3 VM 拆分 + 技术债清算设计
  - [`docs/superpowers/specs/2026-06-12-uma-player-phase4-queue-persistence-design.md`](./superpowers/specs/2026-06-12-uma-player-phase4-queue-persistence-design.md) — Phase 4 队列持久化设计
  - [`docs/superpowers/specs/2026-06-12-uma-player-phase5-drag-drop-design.md`](./superpowers/specs/2026-06-12-uma-player-phase5-drag-drop-design.md) — Phase 5 拖拽支持设计
  - [`docs/superpowers/specs/2026-06-13-uma-player-phase6-named-playlists-design.md`](./superpowers/specs/2026-06-13-uma-player-phase6-named-playlists-design.md) — Phase 6 多命名歌单设计
  - [`docs/superpowers/specs/2026-06-14-uma-player-phase10-library-scan-design.md`](./superpowers/specs/2026-06-14-uma-player-phase10-library-scan-design.md) — Phase 10 文件夹绑定歌单设计
  - [`docs/superpowers/specs/2026-06-15-uma-player-phase11-settings-panel-design.md`](./superpowers/specs/2026-06-15-uma-player-phase11-settings-panel-design.md) — Phase 11 设置面板设计
  - [`docs/superpowers/specs/2026-06-22-uma-player-phase12-ui-refactor-design.md`](./superpowers/specs/2026-06-22-uma-player-phase12-ui-refactor-design.md) — Phase 12 UI 重构设计
  - [`docs/superpowers/specs/2026-06-24-uma-player-phase13-audio-visualization-design.md`](./superpowers/specs/2026-06-24-uma-player-phase13-audio-visualization-design.md) — Phase 13 音频可视化设计
  - [`docs/superpowers/specs/2026-09-08-d-player-phase14-equalizer-design.md`](./superpowers/specs/2026-09-08-d-player-phase14-equalizer-design.md) — Phase 14 均衡器设计
  - [`docs/superpowers/specs/2026-09-12-d-player-phase15-coupling-audit-design.md`](./superpowers/specs/2026-09-12-d-player-phase15-coupling-audit-design.md) — Phase 15 耦合审计设计
  - [`docs/superpowers/specs/2026-09-12-d-player-phase15-coupling-audit-report.md`](./superpowers/specs/2026-09-12-d-player-phase15-coupling-audit-report.md) — Phase 15 审计报告
  - [`docs/superpowers/specs/2026-09-13-d-player-phase16-icon-refactor-design.md`](./superpowers/specs/2026-09-13-d-player-phase16-icon-refactor-design.md) — Phase 16 图标矢量化设计
  - [`docs/superpowers/specs/2026-09-13-d-player-phase17-ui-dark-theming-design.md`](./superpowers/specs/2026-09-13-d-player-phase17-ui-dark-theming-design.md) — Phase 17 UI 深色定制设计
  - [`docs/superpowers/specs/2026-10-05-d-player-phase18-playlist-file-io-design.md`](./superpowers/specs/2026-10-05-d-player-phase18-playlist-file-io-design.md) — Phase 18 播放列表文件导入导出设计
  - [`docs/superpowers/specs/2026-10-06-d-player-phase19-dependency-migration-design.md`](./superpowers/specs/2026-10-06-d-player-phase19-dependency-migration-design.md) — Phase 19 依赖迁移设计
  - [`docs/superpowers/specs/2026-10-06-d-player-phase20-winui-shell-design.md`](./superpowers/specs/2026-10-06-d-player-phase20-winui-shell-design.md) — Phase 20 WinUI 3 第二壳 + 决策门设计
- 实现计划:
  - [`docs/superpowers/plans/2026-04-24-uma-player-implementation.md`](./superpowers/plans/2026-04-24-uma-player-implementation.md) — Phase 1
  - [`docs/superpowers/plans/2026-06-06-uma-player-playlist-implementation.md`](./superpowers/plans/2026-06-06-uma-player-playlist-implementation.md) — Phase 2
  - [`docs/superpowers/plans/2026-06-07-uma-player-phase3-implementation.md`](./superpowers/plans/2026-06-07-uma-player-phase3-implementation.md) — Phase 3
  - [`docs/superpowers/plans/2026-06-12-uma-player-phase4-queue-persistence-implementation.md`](./superpowers/plans/2026-06-12-uma-player-phase4-queue-persistence-implementation.md) — Phase 4
  - [`docs/superpowers/plans/2026-06-12-uma-player-phase5-drag-drop-implementation.md`](./superpowers/plans/2026-06-12-uma-player-phase5-drag-drop-implementation.md) — Phase 5
  - [`docs/superpowers/plans/2026-06-13-uma-player-phase6-named-playlists.md`](./superpowers/plans/2026-06-13-uma-player-phase6-named-playlists.md) — Phase 6
  - [`docs/superpowers/plans/2026-06-14-uma-player-phase10-library-scan.md`](./superpowers/plans/2026-06-14-uma-player-phase10-library-scan.md) — Phase 10
  - [`docs/superpowers/plans/2026-06-15-uma-player-phase11-settings-panel.md`](./superpowers/plans/2026-06-15-uma-player-phase11-settings-panel.md) — Phase 11
  - [`docs/superpowers/plans/2026-06-22-uma-player-phase12-ui-refactor-implementation.md`](./superpowers/plans/2026-06-22-uma-player-phase12-ui-refactor-implementation.md) — Phase 12
  - [`docs/superpowers/plans/2026-06-24-uma-player-phase13-audio-visualization-implementation.md`](./superpowers/plans/2026-06-24-uma-player-phase13-audio-visualization-implementation.md) — Phase 13
  - [`docs/superpowers/plans/2026-09-08-d-player-phase14-equalizer-implementation.md`](./superpowers/plans/2026-09-08-d-player-phase14-equalizer-implementation.md) — Phase 14
  - [`docs/superpowers/plans/2026-09-12-d-player-phase15-coupling-audit-implementation.md`](./superpowers/plans/2026-09-12-d-player-phase15-coupling-audit-implementation.md) — Phase 15
  - [`docs/superpowers/plans/2026-09-13-d-player-phase16-icon-refactor-implementation.md`](./superpowers/plans/2026-09-13-d-player-phase16-icon-refactor-implementation.md) — Phase 16
  - [`docs/superpowers/plans/2026-09-13-d-player-phase17-ui-dark-theming-implementation.md`](./superpowers/plans/2026-09-13-d-player-phase17-ui-dark-theming-implementation.md) — Phase 17
  - [`docs/superpowers/plans/2026-10-05-d-player-phase18-playlist-file-io-implementation.md`](./superpowers/plans/2026-10-05-d-player-phase18-playlist-file-io-implementation.md) — Phase 18
  - [`docs/superpowers/plans/2026-10-06-d-player-phase19-dependency-migration-implementation.md`](./superpowers/plans/2026-10-06-d-player-phase19-dependency-migration-implementation.md) — Phase 19
  - [`docs/superpowers/plans/2026-10-06-d-player-phase20-winui-shell-implementation.md`](./superpowers/plans/2026-10-06-d-player-phase20-winui-shell-implementation.md) — Phase 20（实施中被代码推翻的草稿在文内原地标注，未删除）
- 主要里程碑提交：
  - **Phase 1**
    - `02c7012` feat: implement NAudioPlaybackService with throttled position updates
    - `07f7957` feat: implement MainViewModel with playback commands and seek handling
    - `e2d9d7b` feat: add PlayerBar, MainWindow, and wire App.xaml with DI and dark theme
  - **Phase 2**（feature/playlist-queue → master `8efc109`）
    - `1efa70c` feat(playback): add TrackEnded event for natural-end detection
    - `c0f39d6` feat(vm): add Phase 2 commands and TrackEnded auto-advance
    - `3a6d8c9` feat(view): add PlaylistView UserControl (queue UI)
    - `7d23b2b` feat(view): integrate PlaylistView and migrate window height
    - `7a10674` feat(player-bar): add Prev/Next buttons
    - `3ee16a2` fix(playback): add IPlaybackService.Unload() and use it for queue clear
    - `8efc109` merge: Phase 2 playlist queue feature
  - **Phase 3**（feature/phase3-tech-debt → master `70bd36a`）
    - `476f08e` feat(services): add ITrackMetadataReader interface
    - `c9cd1bd` feat(services): add AtlMetadataReader (ATL-based metadata reader)
    - `a2a0ee7` refactor(vm): switch MainViewModel to ITrackMetadataReader
    - `fadae44` refactor(settings): replace SaveAsync with UpdateAsync(Func<>)
    - `9883dff` feat(vm): add PlayerViewModel (transport half of split)
    - `efd5b5d` feat(vm): add PlaylistViewModel (queue half of split)
    - `54edf9a` refactor(vm): split MainViewModel into Player/Playlist facade
    - `196eb64` fix(playback): broadcast PositionChanged on Seek so paused-state click jumps immediately
    - `da500f6` fix(playback): clear PlayerBar metadata on Unload
    - `ee76afe` fix(playback): clamp Position to [0, Duration] and reset on LoadAsync
    - `70bd36a` merge: Phase 3 tech-debt cleanup
  - **Phase 4**（feature/phase4-queue-persistence → master）
    - `4ad58c5` feat(models): add QueueState record (Phase 4 schema)
    - `f30ede8` feat(services): add IQueuePersistence interface
    - `b52de5d` feat(services): add JsonQueuePersistence (queue.json read/write with semaphore)
    - `7cb655d` feat(di): register IQueuePersistence singleton
    - `53f9cd6` feat(vm): inject IQueuePersistence into PlaylistViewModel + LoadFromDisk on ctor
    - `75e863b` feat(vm): add PlaylistViewModel.SnapshotState() + PlayCurrentCommand
    - `e565cb4` feat(view): wire MainWindow.Window_Closing to write queue.json
    - `519ead8` feat(view): PlayerBar ▶ button DataTrigger for null CurrentTrack → PlayCurrent
    - `ca66fa9` fix(view): PlayerBar ▶ Style must chain BasedOn implicit Button style
    - `9bae7ce` fix(view): cancel-and-close pattern in Window_Closing for queue.json write
  - **Phase 5**（feature/phase5-drag-drop → master）
    - `e10ae0d` docs: add Phase 5 drag-drop design spec
    - `73bdafe` docs: add Phase 5 drag-drop implementation plan
    - `b8d5d37` feat(models): add MoveTracksArgs record (Phase 5 reorder command param)
    - `a76a66e` feat(vm): add PlaylistViewModel.DropExternalFiles command
    - `170bca8` feat(vm): add PlaylistViewModel.MoveTracks command + _playToken bump in RemoveTrack
    - `451ff64` feat(view): add DragDropExtensions (IsDragOver attached prop + audio suffix filter)
    - `78f9310` feat(view): add DropInsertionAdorner (1px accent-color insertion line)
    - `c76d7a7` feat(view): PlaylistView XAML adds AllowDrop, IsDragOver border trigger, multi-select
    - `6351d9d` feat(view): wire up PlaylistView drag-drop handlers (Phase 5)
    - `214d595` fix(view): restore drag-over border highlight via Style.Setter default
    - `c21c0d4` fix(view): scope drag-over highlight to queue list rounded box only
    - `a80afdd` fix(view): clear drag-over highlight in QueueList_Drop
    - `7af6bec` fix(view): only highlight when drag payload contains audio (folder fix)
    - `af51dde` fix(view): preserve multi-select when starting drag from a selected item
    - `5aa0c25` test: Phase 5 manual acceptance pass
  - **Phase 6**（feature/phase6-named-playlists）
    - `041d3bc` test: add xUnit skeleton with smoke test (Phase 6 sub-project A)
    - `c2bb0b3` feat(models): add Playlist record (Phase 6 B1)
    - `ad6148d` feat(playlists): Phase 6 multi-playlist core — schema v2 + container VM (B1-B11)
    - `991bb9e` feat(views): Phase 6 sidebar + prompt dialog + dual-state PlaylistView (B12-B14)
    - `9f07eb4` feat(converters): add BytesToBitmapImageConverter (Phase 6 sub-project C, debt #1 partial)
    - `18ff69a` docs: update PROJECT.md and COUPLING.md for Phase 6
    - `dd24ab3` fix: address Phase 6 code review findings (W3/W4/I9 + W1 doc)
  - **Phase 7**（debt #1 完整偿还，master 直接提交）
    - `012d872` refactor(vm): complete debt #1 — PlayerViewModel.BitmapImage → byte[]
  - **Phase 8**（ViewModel 单元测试，master 直接提交）
    - `f2d6de8` test: add PlayerViewModel unit tests (15 tests)
    - `e71a5a5` test: add PlaylistViewModel unit tests (16 tests)
    - `aa4040e` test: add PlaylistsViewModel unit tests (17 tests)
  - **Phase 9**（sidebar 歌单拖拽重排，master 直接提交）
    - `01b7db3` feat(views): add sidebar playlist drag-reorder
  - **Phase 10**（文件夹绑定歌单，master 直接提交）
    - `9d6a032` docs: add Phase 10 library scan design spec (folder-bound playlists)
    - `2cf99e2` docs: add Phase 10 library scan implementation plan
    - `8ffad2c` feat(models): add Playlist.SourceFolder + QueueState v3 (Phase 10)
    - `0d1f522` feat(models): add LibraryCacheEntry + LibraryDiff records (Phase 10)
    - `daf356e` docs: add SourceFolder XML doc to Playlist record
    - `816e57d` feat(services): add ILibraryScannerService + LibraryScannerService (Phase 10)
    - `56bbcab` revert: remove premature DI registration for Phase 10 services (belongs to Task 6)
    - `1c680e5` refactor: extract AudioExtensions to Models.AudioConstants (fix layer violation)
    - `b46ab6a` feat(services): add ILibraryCache + JsonLibraryCache (Phase 10)
    - `82474b0` feat(services): add IFileDialogService.OpenFolder (Phase 10)
    - `0131423` feat(di): register ILibraryScannerService + ILibraryCache (Phase 10)
    - `02f9d0e` feat(vm): add PlaylistViewModel.IsScanning/HasScanError/SourceFolder (Phase 10)
    - `8fc5322` feat(vm): add PlaylistsViewModel ImportFolder/Rescan/Refresh (Phase 10)
    - `589d4d8` feat(vm): MainViewModel.InitializeAsync triggers background rescan (Phase 10)
    - `5309984` feat(view): PlaylistView toolbar adds ImportFolder + Refresh buttons (Phase 10)
    - `2bf93c0` feat(view): sidebar shows folder icon for folder-bound playlists + scanning indicator (Phase 10)
  - **Phase 11**（设置面板）
    - `6b5658d` docs: add Phase 11 settings panel design spec
    - `508c6b1` docs: add Phase 11 settings panel implementation plan
    - `eb6cfd4` feat(view): add SettingsDialog with volume slider (Phase 11)
    - `8949824` fix(view): SettingsDialog async OnLoaded + error handling
    - `c83cbfe` feat(view): PlayerBar adds gear button for settings (Phase 11)
    - `4940aed` feat(view): MainWindow adds Ctrl+, shortcut for settings (Phase 11)
    - `86c5b91` fix(view): MainWindow Ctrl+, uses existing _persistence field
  - **Phase 12**（UI 界面重构）
    - `9b0bf1d` feat(theme): adjust color palette — improve selected/disabled contrast, add DangerHover
    - `2b82984` feat(view): move PlayerBar to bottom (Spotify-style layout)
    - `36d083f` feat(view): PlayerBar — round play button, seek bar thumb hover, unified spacing
    - `1b0ea7c` feat(view): PlaylistView — header row, duration column, row dividers, red delete hover
    - `a14fc85` feat(view): Sidebar — music/folder icons, selected state background color, rounded corners
  - **Phase 12 continued**（UI 界面重构续）
    - `8dab75b` docs: update PROJECT.md and COUPLING.md for Phase 12 UI refactor
    - `d19599d` feat(theme): Button style — explicit default background, improved comments
    - `9d574e3` fix(view): seek bar Thumb visible by default — small (8px, 50% opacity) then enlarges on hover (14px, 100%)
    - `9b7a1ff` fix(view): SeekBar Thumb 圆形裁切 — 覆盖 Thumb 模板直接控制 Ellipse 尺寸
    - `3c0eaf3` fix(view): 进度条点击区域扩大 — 外层透明 Border 填满 Slider 高度
    - `9c17b6d` fix(view): 去掉按钮和 Thumb 的黑色虚线焦点框
    - `20bcc0b` refactor(view): 移除 PlayerBar 停止和导入歌曲按钮及相关逻辑
    - `def7770` fix(view): 修复上一首/下一首按钮绑定路径
    - `a8202cb` fix: 歌曲时长显示 0:00 — 用 ATL 读到的 DurationMs 替代硬编码 Zero
    - `a18df44` fix(view): 表头与数据列对齐 — 覆盖 ListBox 模板共享容器宽度
    - `013abb5` fix(view): 曲目列表标题/艺术家/专辑列居中对齐
    - `2800d41` refactor(view): 随机/循环按钮从 PlaylistView 移到 PlayerBar
    - `0ae9672` fix: 随机/循环按钮未激活时颜色与其它按钮统一 — ForegroundSecondary → ForegroundPrimary
    - `97de388` fix(view): 循环按钮 FontFamily 统一为 Segoe UI Emoji — 与随机按钮一致
    - `3d9a182` fix(view): 曲目列表顶部边距 0→8
    - `cb0b50c` refactor(view): 曲目信息从 PlayerBar 拆出到独立 TrackInfoView，置于曲目列表右侧
    - `6fcaf6d` fix(view): 全局关闭焦点虚线框 — ListBox/ListBoxItem/TextBox/Slider 加 FocusVisualStyle={x:Null}
    - `87d8f0d` fix(view): GridSplitter 拖拽时的黑色虚线框 — 加 FocusVisualStyle={x:Null}
    - `d44dd07` fix(view): 歌单/曲目列表项点击虚线框 — 显式 ListBoxItem 样式加 FocusVisualStyle={x:Null}
    - `06f8786` fix: 添加/拖入歌曲时立即读取元数据 — CreateFallback → ReadAsync
    - `7428c38` fix: 删除当前播放歌单后停止播放并清空指针
    - `0165c2c` refactor: Shuffle/Repeat 改为全局设置，所有歌单共享
    - `3ffd00e` fix(view): TrackInfoView 加 BackgroundSecondary 背景 + 内容顶部居中
    - `0eecaef` fix(view): TrackInfoView 封面随宽度缩放 — ViewBox 包裹 + 去掉文本固定 MaxWidth
    - `5c1c577` fix(view): TrackInfoView 封面去掉 MaxWidth/MaxHeight 限制，完全跟随容器缩放
    - `04ca124` fix(view): 三个区域底部对齐 — PlaylistView/TrackInfoView 底部 margin 改为 0
    - `0d4c31e` feat(view): 曲目列表添加 # 行号列
    - `7b8cdfe` feat: # 列改为读取元数据 TrackNumber + 修复 marker 索引
    - `e1b26e0` fix(view): ▶ 标记位置 — 用 x:Name 定位替代 FindChildByOrder 索引
    - `63ed02a` fix(view): ▶ 标记移到最左列（# 列之前）
    - `6c6392f` fix: 文件夹歌单旧缓存缺少 TrackNumber 时回读文件补全
    - `a8dc5f6` feat(view): 曲目列表表头点击排序
    - `6c5fc13` feat(view): # 列表头可排序 — Tag=TrackNumber
    - `fc91ab9` fix(view): 表头改回 TextBlock + MouseLeftButtonDown — 消除 Button 内边距导致的对不齐
    - `e9d948d` fix: 随机/循环激活色 AccentPrimary→AccentHover（更亮，深色背景下易辨认）
    - `37fdc00` fix: 强调色提亮 — AccentPrimary #7C4DFF→#9E7CFF, AccentHover→#B9A0FF
    - `d9db167` fix: Thumb 默认透明度 0.5→0.8，减少与轨道的明暗差异
    - `7425273` fix(view): 侧边栏/曲目信息列宽限制为窗口宽度一半
    - `b45ec3c` fix(view): 拖拽实时限制列宽 — DragDelta 中到达上限直接锁死
    - `d6073fb` fix: 曲目信息拖拽限制方向修正 — 向左拖(HorizontalChange<0)才拦截
    - `96d4189` fix: 排序改为物理重排 Queue — 上一曲/下一曲跟随新顺序
    - `3e1e959` refactor(view): 导入文件夹从曲目列表移到歌单列表 + 按钮子菜单
    - `cf71241` refactor: 导入文件夹改为添加到当前歌单
    - `1efda5d` refactor(view): + 按钮恢复直接新建歌单，去掉 ContextMenu
    - `862ae9a` fix(view): UI 布局改进五项
    - `2b2d5ad` fix(view): 封面 Border 加 ClipToBounds — ViewBox 缩放后保持圆角裁切
    - `a07bc6c` fix(view): 封面 Border 去掉 CornerRadius，避免与播放时直角不一致
    - `0b7137e` fix(view): 去掉 PlayerBar 上方分隔线
  - **Phase 13**（音频可视化）
    - `b78747d` feat(models): add SpectrumConfig record for audio visualization
    - `12d5def` feat(services): add SampleAggregator for FFT spectrum analysis
    - `0aae53f` feat(services): integrate SampleAggregator into NAudioPlaybackService
    - `5a56ae8` feat(config): add spectrum visualization settings to AppSettings
    - `aae42ca` feat(vm): add spectrum visualization properties and data processing
    - `75c9b59` fix(vm): copy spectrum data before applying sensitivity gain
    - `c0fcefc` feat(view): add SpectrumView custom control for audio visualization
    - `c6977a9` feat(view): integrate SpectrumView into TrackInfoView
    - `e1d4aa8` feat(view): add spectrum visualization settings to SettingsDialog
    - `dd28308` fix(view): update spectrum slider labels on settings load
    - `3814eb6` test: add unit tests for spectrum visualization in PlayerViewModel
    - `d030abe` fix(spectrum): copy array before passing to SpectrumDataReady event
    - `51a04c6` fix(spectrum): remove double-log binning, propagate Enabled, fix double-save
    - `4fc18d5` fix(settings): sync spectrum properties to PlayerViewModel on save
    - `305480e` test(spectrum): update tests for 32-bar input and SpectrumConfig mock
    - `0337aeb` docs: update PROJECT.md for Phase 13 audio visualization
  - **Phase 13 频谱调优**（doc 更新后 24 commits：FFT 参数/布局/主题打磨）
    - `825ceb3` fix(view): remove incorrect TextBlock style from CheckBox
    - `8e76842` fix(services): improve spectrum frequency mapping with proper logarithmic grouping
    - `eab1a7d` fix(services): improve spectrum response with RMS, gain, and gamma curve
    - `b6d08fa` fix(services): adjust spectrum range 60Hz-16kHz, reduce gain to 8x
    - `e8897b7` fix(services): raise minimum spectrum frequency to 80Hz
    - `0d076bd` fix(models): increase FFT size from 1024 to 2048 for better low-frequency resolution
    - `7979d24` fix(services): restore minimum spectrum frequency to 20Hz
    - `f0c3248` fix(services): mono-mix stereo before FFT + clamp smoothing/sensitivity range
    - `15679b2` fix(models): increase FFT size to 8192 for better frequency resolution
    - `ea31c32` fix(services): add 50% FFT overlap for smoother spectrum animation
    - `d447664` fix(view): move spectrum view to bottom of TrackInfoView
    - `04049cd` fix(view): use DockPanel layout to keep spectrum within background
    - `e932d34` fix(view): reduce spectrum bottom margin to fit within background
    - `b074175` fix(view): revert to StackPanel layout, set VerticalAlignment=Top
    - `2876b11` refactor(view): move spectrum to independent row between content and player bar
    - `80791ea` fix(view): add background to SpectrumView for visibility
    - `f1ceb78` refactor(view): move spectrum back into TrackInfoView as bottom section
    - `0135430` fix(view): increase spectrum height to 120px
    - `dbed4fa` fix(view): add detailed error message for settings save failure
    - `a76d92a` fix(view): remove ConfigureAwait(true) to stay on UI thread
    - `59932fc` fix(view): change rainbow gradient to horizontal (left to right)
    - `8c4774d` fix(view): rainbow gradient from red (left) to purple (right) without cycling
    - `781d35d` fix(theme): add IsMoveToPointEnabled to Slider style
    - `808fb98` fix(view): fix slider layout in SettingsDialog using DockPanel LastChildFill
  - **Phase 14**（均衡器）
    - `ca5242a` feat(models): add EqualizerConfig + EqualizerPresets (Phase 14)
    - `49fb593` test(models): cover EqualizerConfig.Create truncate path + note equality semantics
    - `e4c3f40` feat(services): add EqualizerSampleProvider for 10-band graphic EQ (Phase 14)
    - `1f5be2a` test(services): cover stereo independence + Nyquist bypass; harden _enabled visibility
    - `160a42d` feat(playback): wire EqualizerSampleProvider into playback chain (Phase 14)
    - `8eb0de1` feat(config): add equalizer settings to AppSettings (Phase 14)
    - `a98bb91` feat(vm): add EqualizerEnabled to PlayerViewModel with startup apply (Phase 14)
    - `7a96162` test(vm): assert PlayerViewModel suppresses persistence during construction
    - `535a511` feat(view): add EqualizerDialog with 10-band vertical sliders + presets (Phase 14)
    - `857444b` fix(view): suppress preset-combo init push + robust EQ dialog rollback/DRY (Phase 14)
    - `e1bc557` feat(view): add EQ button to PlayerBar with active-state highlight (Phase 14)
  - **Phase 15**（耦合健康度审计，master 直接提交；merge `f3cc1b6`）
    - `9127057` feat(tools): add coupling audit script (M1 dependency graph, M2 cycles, M3 layer violations)
    - `8390d86` feat(tools): coupling audit script adds M4 interface width, M5 LOC, M6 DI unconsumed
    - `5dd64d7` fix(tools): coupling audit M5 uses physical line count + deterministic secondary sort
    - `2c46ffa` docs: add Phase 15 coupling audit report (M1-M7 metrics + D1-D5 verdicts)
    - `e028401` docs: update COUPLING.md with Phase 15 coupling audit verdicts
  - **Phase 16**（图标矢量化；merge `1aef1d9`）
    - `507b23d` feat(theme): add vector icon set Icons.xaml (Phase 16)
    - `7a67436` refactor(view): vector icons for PlayerBar + converters return Geometry (Phase 16)
    - `117a3f0` refactor(view): vector icons for PlaylistView incl. play marker (Phase 16)
    - `d6635cb` refactor(view): vector icons for sidebar incl. active marker (Phase 16)
    - `d4ce83c` fix(view): vectorize music-note playlist icon (Phase 16)
    - `1b168e0` fix(view): vectorize import-folder icon + restore delete hover color (Phase 16)
    - `b328696` fix(view): bind clear-button icon stroke to button foreground for hover (Phase 16)
    - `18d1ea7` docs: update COUPLING.md marker/icon contracts + design icon list (Phase 16)
  - **Phase 17**（UI 深度深色定制；merge `d103a78`）
    - `9192f58` feat(theme): add maximize/restore icons (Phase 17)
    - `34d7dff` feat(view): add reusable custom TitleBar control (Phase 17)
    - `2e9836a` fix(view): TitleBar title uses ForegroundPrimary per design (Phase 17)
    - `8362efa` feat(view): MainWindow custom borderless chrome with TitleBar (Phase 17)
    - `41c95ef` feat(view): dialogs custom borderless chrome with TitleBar (Phase 17)
    - `24d4544` feat(theme): dark implicit styles for ComboBox/CheckBox/ScrollBar/ToolTip/ContextMenu (Phase 17)
    - `dd435bd` fix(view): workarea-based maximize margin + clickable ComboBox toggle (Phase 17)
    - `d86694b` fix(view): constant workarea-based maximize margin to remove right/bottom blank (Phase 17)
  - **Phase 18**（播放列表文件导入导出，master 直接提交）
    - `27ed09c` feat(services): playlist file encoding probe with GBK fallback (Phase 18)
    - `fc528db` fix(tests): annotate nullable id parameter to clear pre-existing CS8625
    - `cebb405` feat(services): playlist formats + M3U/PLS parsers (Phase 18)
    - `34e2048` feat(services): IPlaylistFileService import pipeline (Phase 18)
    - `52c120f` test(services): make the extension-vs-existence ordering assertion discriminating
    - `1a795d2` feat(services): M3U8 export + import/export round trip (Phase 18)
    - `834e9d0` feat(services): SaveFile on IFileDialogService for playlist export (Phase 18)
    - `817cab4` feat(vm): playlist-level import (append) and M3U8 export (Phase 18)
    - `7a6f60b` feat(vm): container-level playlist import creates a normal playlist (Phase 18)
    - `29bd5c6` feat(view): import report plumbing for playlist files (Phase 18)
    - `03bf606` feat(view): sidebar playlist-file import creates a new playlist (Phase 18)
    - `1bff9dd` feat(view): playlist toolbar import/export + drop routing (Phase 18)
  - **Phase 19**（依赖迁移，master 直接提交）
    - `40338f5` chore(deps): upgrade to NAudio 3.1.0 and the current test stack
    - `cb5ca1a` fix(playback): serialize playback-chain lifecycle to stop dispose-during-init crash
    - `2f7bd88` fix(playback): stop near the track end must not fake a natural end
    - `7d42c08` docs: add Phase 19 design spec for the new-dependency migration
    - `a73b579` docs: add Phase 19 implementation plan for the dependency migration
    - `b7f0251` chore(deps): narrow NAudio to the two sub-packages we actually use
    - `ce67d9e` refactor(playback): move the output to WasapiPlayer and re-argue both invariants
    - `50d9ba3` fix(playback): keep the output behind IWavePlayer and correct the interface premise
    - `bf6068c` test: migrate the suite to xunit.v3 with both runners enabled
    - `a80065e` docs: sync Phase 19 into PROJECT/COUPLING/README
    - `b32fb6d` docs: annotate the Phase 19 plan where the delivered code refuted its draft
    - `24bc488` chore(audio): retire the last obsolete-API suppression by migrating the stub factory
    - `1f11f80` chore: close the four parked doc nits and the last xUnit1051 site
  - **Phase 20 前的独立功能增量**（断点续播，2026-10-06）
    - `3fb9497` feat(player): resume the last played track and position on startup
  - **Phase 20**（WinUI 3 第二壳 + 决策门；设计稿/计划 `e631731` `6503265` `92b8412` `3f6a340`）
    - `44b8497` refactor: extract a WPF-free D-player.Core shared by the UI shells（其提交信息里"smoke-tested"那半句由 `dc3b0cc` 开头显式更正 —— 视觉项当时并未真机确认）
    - `dc3b0cc` test: cover header-click sorting, the one behaviour Task 1 actually changed
    - `71bdccd` refactor: make the user data folder injectable per UI shell
    - `69fedad` feat(winui): scaffold an unpackaged WinUI 3 shell and prove the toolchain（WindowsAppSDK 2.5.1 钉版本 + `net10.0-windows10.0.19041.0` + x64）
    - `4d715bc` feat(winui): build the first vertical slice of the new UI
    - `71613f2` fix(winui): share the double-click entry, follow ViewedPlaylist, show Mica
