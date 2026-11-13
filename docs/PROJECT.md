# D-player 项目文档

> 一个轻量级、本地优先的 Windows 音乐播放器（.NET 11 + NAudio，WPF / WinUI 两套壳；共享层 `D-player.Core` 是 WPF-free 的）。
>
> 文档日期：2026-11-13 · 分支：`phase30-audio-realtime` · 当前阶段：**Phase 30 音频实时路径性能重写（实现完成 / core 验收完成 / GUI 走查完成——Task 13b 2026-11-13，数据已按字节还原）**。音频线程 full-chain 7.885 → 2.031/2.028 ms/音频秒（−74%）、p99 0.337 → 0.031/0.033 ms、音频线程稳态零产品分配；WinUI 壳与 WPF 视图零改动。**2026-11-02 路线决策：WinUI 壳开发搁置（Phase 23 是最后一个 WinUI 功能阶段），后续回归 Core + WPF；Avalonia 重构仅保留为未来可能（未排期）。** 测试 **368 = 363 默认 + 5 真实音频**（Phase 30 新增 26 条默认测试）。WinUI 只由 `verify-gates.ps1 -Full` 门禁 3 编译，行为仍需真机验收。最近验收见 [Phase 30](./PHASE30-ACCEPTANCE.md)；上一阶段见 [Phase 29](./PHASE29-ACCEPTANCE.md)；历史验收见 [Phase 28](./PHASE28-ACCEPTANCE.md) / [Phase 27](./PHASE27-ACCEPTANCE.md) / [Phase 26](./PHASE26-ACCEPTANCE.md) / [Phase 25](./PHASE25-ACCEPTANCE.md) / [24](./PHASE24-ACCEPTANCE.md) / [23](./PHASE23-ACCEPTANCE.md) / [22](./PHASE22-ACCEPTANCE.md) / [21](./PHASE21-ACCEPTANCE.md) / [20 对比](./PHASE20-COMPARISON.md)；历史结论不回改。**项目名：D-player（原 UmaPlayer；C# 命名空间 DPlayer）**

---

## 1. 项目简介

**当前范围**：Phase 30 已交付音频实时路径性能重写（频谱 FFT 离音频线程 + 重叠帧用真实时域样本、EQ 无锁配置下发 + 单位增益频段跳过、`PlaybackChain` 装载/回调重构、`PeriodicTimer` 位置轮询；`tools/AudioBench` 可复现基线；core + GUI 验收完成 2026-11-13，含 Task 13b GUI 走查、数据已按字节还原）；Phase 29 已交付非音频路径现代化（RootGates 回收、壳侧树查找 helper 合并、JsonFileStore/JSON 源生成、union/closed 模型、OneShotCleanup 与生命周期释放；验收完成 2026-11-12）；Phase 28 已交付升级到 .NET 11（四工程 TFM 抬 net11.0、`CommunityToolkit.Mvvm` 钉 8.4.2；零行为变更意图）；Phase 27 已交付多碟专辑归并（Disc N / CD N 等碟子文件夹上提一层作专辑边界，曲目按碟序 + 曲号排序）；Phase 26 已交付 Core + WPF 主壳的库 → 歌单互通（库内右键把单曲 / 专辑加入普通歌单，或新建歌单并加入；文件夹歌单不可选）；上一阶段 Phase 25 交付音乐库搜索与实时监听（三组搜索、按根 watcher、共享根扫描事务、可等待清理）；Phase 24 交付音乐库视图与隐藏库播放队列。WinUI 壳自 2026-11-02 冻结，不启动 watcher，只由门禁 3 编译。验收见 [PHASE30-ACCEPTANCE.md](./PHASE30-ACCEPTANCE.md)（当前）/ [PHASE29-ACCEPTANCE.md](./PHASE29-ACCEPTANCE.md) / [PHASE28-ACCEPTANCE.md](./PHASE28-ACCEPTANCE.md) / [PHASE27-ACCEPTANCE.md](./PHASE27-ACCEPTANCE.md) / [PHASE26-ACCEPTANCE.md](./PHASE26-ACCEPTANCE.md) / [PHASE25-ACCEPTANCE.md](./PHASE25-ACCEPTANCE.md) / [PHASE24-ACCEPTANCE.md](./PHASE24-ACCEPTANCE.md)。

**D-player** 是一款面向 Windows 桌面的本地音乐播放器，灵感来源于 foobar2000 / Winamp。Phase 1–19 在 WPF 单壳内完成播放、队列、歌单、扫描、设置、UI/EQ 与依赖迁移；Phase 20–23 抽出 WPF-free 的 `D-player.Core` 并完成 WinUI 第二壳（后于 2026-11-02 冻结）；Phase 24–26 回归 Core + WPF，交付音乐库视图、搜索与实时监听以及库 → 歌单互通；Phase 27 交付多碟专辑归并，Phase 28 把四个工程抬到 net11.0，Phase 29 完成非音频路径现代化，Phase 30 完成音频实时路径性能重写。逐阶段一览见 [README 的开发阶段](../README.md#开发阶段)；当前状态与验收链接见页首。

### 1.1 关键特性（已实现）

> 下表是 WPF 主壳的当前能力清单。WinUI 壳自 2026-11-02 冻结，能力与缺口见 [README 的已知限制](../README.md#已知限制) 与 [Phase 20 对比](./PHASE20-COMPARISON.md) §3（历史快照）；Phase 21–23 的验收记录保留在当时基线，不随本文更新。

| 类别   | 能力                                                             |
|------|----------------------------------------------------------------|
| 文件导入 | Win32 OpenFileDialog 多文件选择                                     |
| 支持格式 | MP3 / WMA / FLAC / AAC / WAV（基于 Windows Media Foundation 原生解码） |
| 播放控制 | 播放 / 暂停 / 上一首 / 下一首 / 随机 / 循环                                  |
| 进度控制 | 拖拽 + 单击跳转的进度条；位置实时更新（`PeriodicTimer` 33 ms 轮询 ≈30 Hz，无双层节流）                                |
| 音量控制 | 0~1 线性滑块、一键静音/取消静音；通过 `VolumeSampleProvider` 实现                |
| 元数据  | 标题 / 艺术家 / 专辑 / 流派 / 年份 / 采样率 / 曲目号 / 内嵌封面（z440.atl.core）     |
| 主题   | 内置深色主题（深紫强调色）；Phase 17：无边框自定义标题栏 + ComboBox/CheckBox/ScrollBar/ToolTip/ContextMenu 深色隐式样式 |
| 持久化  | 窗口几何、音量、频谱/EQ、库根与库导航、断点续播保存到 `%LocalAppData%\D-player\settings.json` |
| 播放列表 | 内存队列：多选入队、单项删除、清空、上/下一首、自然播完自动推进、#列元数据 TrackNumber、表头点击排序 |
| 队列持久化 | `StateChanged` 500ms debounce + 关闭 flush 写 `%LocalAppData%\D-player\queue.json`；启动恢复列表 + CurrentIndex + Shuffle/Repeat + 隐藏 `LibraryPlayback`（Phase 4/24） |
| 拖拽 | 外部音频文件拖入末尾入队（白名单 .mp3/.wma/.flac/.aac/.wav）；队列内单/多选拖拽重排（含 ▶ 当前曲跟随、Shuffle 历史按对象身份重映射）；插入线 Adorner + 圆角列表框边框高亮（Phase 5） |
| 多命名歌单 | 创建/删除/重命名多个独立歌单；Viewed vs Current 双指针（切查看不打断播放，双击才跨歌单切换音频）；全局 Shuffle/Repeat（所有歌单共享）；各歌单独立 CurrentIndex；v1→v2→v3 schema 自动迁移（Phase 6/10） |
| 文件夹绑定歌单 | 指定文件夹扫描 → 创建歌单; 启动后台自动同步增删; 手动刷新; 元数据缓存; 导入文件夹添加到当前歌单 (Phase 10) |
| 设置 | 模态对话框：默认音量、音频输出只读占位、频谱设置、音乐库根目录；Ctrl+, 快捷键 (Phase 11/13/24) |
| 曲目信息面板 | 右侧独立 TrackInfoView：封面（ViewBox 自动缩放）+ 标题/艺术家/专辑/采样率；BackgroundSecondary 背景 (Phase 12 continued) |
| 音频可视化 | 32 条垂直频谱柱（8192 点 FFT + 汉宁窗 + 50% 重叠 + 对数分组 20Hz–16kHz + RMS/gamma）；4 种颜色主题（紫/蓝/绿/彩虹，彩虹为红→紫水平渐变）；灵敏度/平滑度配置；启用/禁用开关；置于 TrackInfoView 底部（高 120px）；设置持久化 (Phase 13) |
| 均衡器 | 10 段图形 EQ（ISO 倍频程 31Hz–16kHz ±12dB 峰值滤波 Q≈1.1 + preamp −12~+12dB）；9 个内置预设（Flat/Rock/Pop/Jazz/Classical/Dance/Bass Boost/Treble Boost/Vocal）+ 手动 Custom；实时生效（拖动即时听感）；启用开关（默认关，透明旁路）；独立 EqualizerDialog（PlayerBar 🎚 按钮打开，启用态高亮）；设置持久化 (Phase 14) |
| 图标 | 全量描边矢量图标集（Themes/Icons.xaml，24 个 `Icon.*`，Feather/Lucide 几何）；随机/循环/EQ 活跃态 accent 着色；▶ 标记实心三角 (Phase 16) |
| 窗口外观 | 无边框 WindowChrome + 自绘 TitleBar（最小化/最大化/关闭；对话框仅关闭按钮；最大化常量式工作区边距；OS 保留拖动/快照行为）(Phase 17) |
| 播放列表文件 | 导入 .m3u / .m3u8 / .pls（相对路径按列表所在目录解析；UTF-8/UTF-16(BOM)/GBK 编码探测；URL/后缀/存在性严格过滤 + 跳过计数报告）；导出 .m3u8（绝对路径 + `#EXTINF`）；侧边栏入口新建歌单、歌单工具栏入口追加当前歌单、拖拽按落点分流 (Phase 18) |
| 音乐库 | 多库根目录（设置里添加/移除，规范化 + 大小写不敏感去重，多根嵌套不重复计数）；侧栏固定「音乐库 / 艺术家 / 专辑」节点 + 中间面板四级钻取（艺术家列表 / 专辑封面网格 / 艺术家详情 / 专辑详情）；专辑封面按首曲惰性读取；`播放全部`/双击从当前上下文播放；库播放走隐藏的专用队列（`queue.json` 的 `LibraryPlayback`，支持上一首/下一首、Shuffle/Repeat、断点续播与重启恢复，不污染用户歌单）；不可用根目录跳过且不清缓存 (Phase 24)；全局搜索（艺术家/专辑/曲目三组、200ms 去抖、CJK 字面匹配、Ctrl+F、返回快照）与按根实时监听（800ms / 5s 窗口、共享根扫描事务、删除/改名不打断播放）(Phase 25)；库内右键将单曲 / 专辑加入普通歌单，或新建歌单并加入 (Phase 26)；多碟子文件夹（Disc N / CD N / Disk N / 碟N / ディスクN）归并为同一张专辑、按碟序 + 曲号排序 (Phase 27) |

### 1.2 已知边界与后续候选

- 音乐库搜索为内存字面匹配（不做拼音/罗马字/模糊/正则，不分词），查询与结果不持久化 (Phase 25)；实时监听为根级 best-effort，不监听同路径标签内容写入，不承诺网络根、复制中文件或缓冲区溢出的固定延迟；库播放队列仍无独立管理界面 (Phase 25)。
- 库内曲目 / 专辑只能加入普通歌单（文件夹歌单由目录管理）；不支持拖拽入歌单；右键可能让该行获得选中态，但不起播、不切换视图 (Phase 26)。
- OGG/Vorbis 支持（MF 不原生支持，需额外解码器）
- 多设备 / 输出模式切换（WASAPI Shared / Exclusive / ASIO）—— 接口已预留
- **WinUI 壳的功能补齐（Phase 20 决策门已于 2026-10-07 拍板续投，Phase 21 视觉打磨已交付，Phase 22 功能层已交付；2026-11-02 起 WinUI 壳开发搁置，本项不再跟进）**：Phase 22 补齐了导入/导出、歌单增删改、完整播放控制、基础设置、窗口几何与本地日志（验收已完成，验收 23 项全部 PASS（含用户 2026-10-24 确认的 C4/D3 与 D1 轮转），步骤见 `docs/PHASE22-ACCEPTANCE.md`）。**Phase 23 频谱/EQ 已交付并验收通过（2026-11-02）**（[验收记录](./PHASE23-ACCEPTANCE.md)）；**仍缺**：拖拽 / 表头排序 / 文件夹绑定歌单的管理与刷新；TrackList 的 ▶ 当前曲标记与 InfoPanel 采样率已在 2026-10-24 补齐。剩余范围以对比材料 [`PHASE20-COMPARISON.md`](./PHASE20-COMPARISON.md) §3 的差距表为准，此处只给指针、不另列一份。剩余 WinUI 缺口随壳冻结保留；后续功能开发回归 Core + WPF。

---

## 2. 技术栈

| 层 | 选型 |
|----|------|
| 运行时 | .NET 11（共享层与 WPF 壳 `net11.0-windows`；WinUI 壳 `net11.0-windows10.0.19041.0`） |
| 共享层 | `D-player.Core` 类库（Phase 20，`net11.0-windows` 且**不开** `UseWPF`）：Models / Services / ViewModels / Configuration / Extensions；被两个 UI 壳引用，不得出现任何 WPF 类型 |
| UI 框架 | WPF（`UseWPF=true`，主壳，完整功能）· WinUI 3 / Windows App SDK **2.6.5**（Phase 20 建壳，Phase 22 功能补齐；unpackaged + self-contained，`<Platforms>x64</Platforms>` + `<Platform>x64</Platform>`） |
| MVVM | [CommunityToolkit.Mvvm 8.4.2（钉死）](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) |
| DI 容器 | `Microsoft.Extensions.DependencyInjection` 11.x |
| 配置 | `Microsoft.Extensions.Configuration.Json` + `IOptions<AppSettings>` |
| 音频引擎 | [NAudio 3.3.0](https://github.com/naudio/NAudio)（Phase 19 把 meta 包 `NAudio` 收窄为实际用到的两个子包：`NAudio.Core` + `NAudio.Wasapi`；2026-10-24 升到 3.3.0 后 `NAudio.Wasapi` 仍只依赖 `NAudio.Core`，而 `NAudio.Core 3.3.0` 新增传递依赖 `System.Numerics.Tensors` 9.0.0，两个壳的输出目录因此是三个 DLL。3.3.0 是 nullable 注解 + COM 包装异常类型调整 + `MediaFoundationReader` 帧对齐 seek 修复，源码级兼容） |
| 音频解码 | `MediaFoundationReader` |
| 输出后端 | WASAPI Shared（`WasapiPlayer`：经 `WasapiPlayerBuilder` 建链 —— `WithSharedMode()` + `WithEventSync()` + `WithLatency(100)`，与迁移前的 `WasapiOut(Shared, 100)` 语义等价；Phase 19 起替代 legacy 的 `WasapiOut`，`WasapiPlayer` 实现 `IWavePlayer` 故服务侧字段类型未变） |
| 元数据/标签 | [z440.atl.core 7.19.0](https://github.com/Zeugma440/atldotnet)（当前 pin 7.19.0；7.14–7.18 的解析层修复、零 API 变更记录保留） |

---

## 3. 目录结构

```
D-player/                        # 仓库根 = WPF 壳工程目录（D-player.csproj 就在根上）
├── App.xaml(.cs)                # WPF 壳入口：UI 线程建 DI 容器（AddDPlayerCore + 本壳的 Win32FileDialogService）、跑 LegacyDataMigration、加载主窗口
├── AssemblyInfo.cs              # ThemeInfo（资源字典位置）
├── D-player.csproj              # WPF 壳工程；ProjectReference 到 D-player.Core；四组兄弟目录（Tests / D-player.Core / D-player.WinUI / tools）的 glob 排除集（见 §9）
├── D-player.sln                 # 四个工程全在里面（IDE 用）—— **不是门禁**
├── D-player.slnf                # 门禁筛选器：D-player.Core + D-player.csproj + Tests（WinUI 刻意不进）
├── appsettings.json             # 启动默认配置（构建时复制到输出目录；WinUI 壳链接同一份，不复制第二份真相）
├── global.json                  # 测试 runner 路由：{"test":{"runner":"Microsoft.Testing.Platform"}}（.NET 11 SDK 原生 opt-in）。不要删除 —— 删掉后 `dotnet test D-player.slnf` 直接失败
│
├── D-player.Core/               # Phase 20 抽出的共享类库（net11.0-windows、不开 UseWPF，被两个 UI 壳引用；层目录下不得出现 WPF 类型）
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
│   │   ├── LibraryScanResult.cs      # 扫描结果 closed 层级：ScanAvailable(Files) / ScanUnavailable（区分"根不可用"与"根为空"，Phase 24；Phase 29 closed 化）
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
│   │   ├── IFileDialogService.cs    # 文件/文件夹对话框抽象（Phase 22 起三个方法返回 Task）；**实现由各 UI 壳自己注册**（WPF: `D-player/Services/Win32FileDialogService.cs`；WinUI: `D-player.WinUI/Services/WinUiFileDialogService.cs`，真实 picker）(Phase 20/22)
│   │   ├── ISettingsPersistence.cs
│   │   ├── JsonSettingsPersistence.cs # 落点由 DPlayerDataPaths 决定：%LocalAppData%\D-player\settings.json（WPF 壳）(Phase 20 参数化)
│   │   ├── LegacyDataMigration.cs   # 启动一次性迁移旧数据目录 UmaPlayer → D-player；`MigrateIfNeeded(DPlayerDataPaths)`，只由 WPF 壳调用 (Phase 20 加参数)
│   │   ├── IPlaylistService.cs      # 多歌单持久化抽象 (Phase 6, 替换 IQueuePersistence)
│   │   ├── JsonPlaylistService.cs   # 落点由 DPlayerDataPaths 决定：%LocalAppData%\D-player\queue.json; 内置 v1→v2 迁移 (Phase 6/20)
│   │   ├── ILibraryScannerService.cs      # 库扫描抽象 (Phase 10)；Phase 24 增 ScanFolderWithStatus（绝不抛，返回 ScanAvailable / ScanUnavailable）
│   │   ├── LibraryScannerService.cs       # 递归扫描 + Diff 实现 (Phase 10/24，ScanFolder 委托给 ScanFolderWithStatus)
│   │   ├── LibraryPathNormalizer.cs       # 库根目录规范化：GetFullPath、去尾分隔符、NormalizeMany 大小写不敏感去重 (Phase 24)
│   │   ├── LibraryScanLease.cs            # 根扫描取消/保存准入的线性化边界 (Phase 25)
│   │   ├── ILibraryRootScanner.cs / LibraryRootScanner.cs # 同根串行枚举、差分、标签 fallback、InvalidPaths 分类与原子保存 (Phase 25)
│   │   ├── LibraryRefreshScheduler.cs     # 单 worker：每根 800ms 静默 / 5s 上限、全刷优先、扫描中 pending、1s/3s 有界补扫 (Phase 25)
│   │   ├── ILibraryWatcher.cs / FileSystemLibraryWatcher.cs # 惰性递归 watcher、AffectedRoots 路径边界、Overflow/Unavailable 分类 (Phase 25)
│   │   ├── ILibraryCache.cs               # 元数据缓存抽象 (Phase 10)
│   │   ├── JsonLibraryCache.cs            # JSON 缓存实现 (Phase 10)
│   │   ├── ITrackMetadataReader.cs # 元数据读取抽象 (Phase 3)
│   │   ├── AtlMetadataReader.cs    # 基于 z440.atl.core 的实现 (Phase 3)
│   │   ├── IAudioDeviceManager.cs   # 预留：设备枚举/切换
│   │   ├── StubAudioDeviceManager.cs# 占位实现，返回空集
│   │   ├── IAudioOutputFactory.cs   # 输出后端工厂（Phase 30 起由 NAudioPlaybackService 每链消费）
│   │   ├── StubAudioOutputFactory.cs# 生产默认输出工厂（占位命名；固定返回 WASAPI Shared，Phase 19 经 WasapiPlayerBuilder 构建）
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
│   │   ├── MainViewModel.cs        # Strict Facade：Player/Playlists/Library + IsLibraryViewActive + ShowLibrary/ShowPlaylist + InitializeAsync/CleanupAsync + debounce save (Phase 3/6/24)
│   │   ├── PlayerViewModel.cs      # Transport 子 VM：播放/暂停/进度/音量 (Phase 3)
│   │   ├── PlaylistViewModel.cs    # 队列子 VM：Queue/推进算法 + Id/Name/IsActivePlaylist + SortBy（物理重排 Queue，Phase 20 删掉 SortedView）+ ImportFolderToCurrent + ImportPlaylistFileAsync/ExportPlaylistFileAsync (Phase 3/6/12 continued/18/20)
│   │   ├── PlaylistImportReport.cs # 一次导入的结构化报告 record（只带数据不带文案）(Phase 18)
│   │   ├── PlaylistImportReportFormatter.cs # 导入报告 record → 中文文案（单个/多个/全跳过分支；四个入口共用的纯字符串函数）(Phase 18，Phase 20 从 Views/Controls 搬进 Core)
│   │   ├── PlaylistsViewModel.cs   # 多歌单容器：ObservableCollection<PlaylistVM> + 全局 Shuffle/Repeat + Add/Remove/Rename + HandleDoubleClickPlay + ImportFolder/Rescan/Refresh + ImportPlaylistFileAsync；Phase 24 增隐藏库播放队列（LibraryPlayback/IsLibraryPlaybackActive/PlayLibraryTracksAsync）(Phase 6/10/12 continued/18/20/24)
│   │   ├── LibraryModels.cs        # LibraryPage enum（Phase 25 尾部追加 Search）+ LibraryNavigationSnapshot + LibraryArtist（Name/IsUnknown/Albums/Tracks/Summary）+ LibraryAlbum（Title/Artist/Year/Tracks/CoverArt 惰性）(Phase 24/25)
│   │   ├── LibraryIdentity.cs      # 公共专辑键 (Album, Directory) + AlbumComparer + AlbumOf(Track/LibraryAlbum) + DiscOrderOf，分组/选择/搜索共用；碟子文件夹上提一层 (Phase 25/27)
│   │   ├── LibrarySearch.cs        # 纯内存三组匹配、稳定排序、只读结果；无 IO (Phase 25)
│   │   ├── LibraryGrouping.cs      # internal 分组算法：专辑 key 复用 LibraryIdentity；艺术家 key = 逐曲 Artist（合辑贡献者保留）；混合艺术家专辑显示「多位艺术家」；未知组排最后；曲目按碟序/号/标题/路径排序 (Phase 24，2026-11-02 合辑修订，Phase 25 复用键，Phase 27 碟序)
│   │   ├── LibraryViewModel.cs     # 多根库视图模型：初始化/导航/播放/封面/发布/状态行/清理；Phase 25 拆出 Scanning/Search 两个 partial (Phase 24/25)
│   │   ├── LibraryViewModel.Scanning.cs # 每根快照、根变化、调度器 callback、watcher 接入、可等待 Cleanup (Phase 25)
│   │   └── LibraryViewModel.Search.cs   # 查询版本/去抖、返回快照、结果导航、完整专辑播放、库更新重算 (Phase 25)
│   │
│   └── Extensions/
│       └── ServiceCollectionExtensions.cs # AddDPlayerCore(IConfiguration, DPlayerDataPaths)：共享服务与 VM 注册；Phase 25 增 ILibraryRootScanner / ILibraryWatcher / TimeProvider；**不含任何 UI 相关服务**（Phase 20 由 AddDPlayerServices 改名并拆出对话框注册）
│
├── Views/                       # ↓↓↓ 以下三个目录 + 根上的 App.xaml/AssemblyInfo.cs + D-player/Services/ 属于 WPF 壳 ↓↓↓
│   ├── MainWindow.xaml(.cs)     # 主窗口；3 行(TitleBar | 内容区 | PlayerBar)；内容区 5 列(Sidebar | Splitter | Playlist/Library | Splitter | TrackInfo)；Phase 17 无边框 WindowChrome；Phase 24 中间列按 IsLibraryViewActive 在队列与库视图间切换；Phase 25 Ctrl+F 聚焦搜索、Loaded 显式启动监听、双阶段关闭守卫等待清理
│   ├── Dialogs/
│   │   ├── PromptDialog.xaml(.cs)    # 共享单输入对话框（新建/重命名歌单）(Phase 6)；Phase 17 无边框 + TitleBar；输入框深色样式
│   │   ├── ConfirmDialog.xaml(.cs)   # 主题化确认对话框（删歌单/清空/移除曲目；替代系统 MessageBox）；Phase 18 加 ShowInfo 单按钮信息模式
│   │   ├── SettingsDialog.xaml(.cs)  # 设置对话框（音量 + 音频输出占位 + 频谱 + EQ + Phase 24 音乐库根目录区）；Phase 17 无边框 + TitleBar；Phase 24 内容区可滚动、取消/保存固定
│   │   └── EqualizerDialog.xaml(.cs) # 均衡器对话框（11 根竖直滑块 + 预设下拉 + 启用开关 + 实时预览）(Phase 14)；Phase 17 无边框 + TitleBar
│   └── Controls/
│       ├── TitleBar.xaml(.cs)   # 自绘无边框标题栏（Title/ShowMaximize DP + SystemCommands + 最大化常量边距 + Max/Restore 图标切换）(Phase 17)
│       ├── PlayerBar.xaml(.cs)  # 播放栏（进度/控制/音量 + 随机/循环 + 均衡器/设置按钮；Phase 16 图标全部矢量 Path）
│       ├── PlaylistView.xaml(.cs)    # 播放队列（Phase 2 + Phase 5 拖拽 + Phase 6 IsActivePlaylist guard + #列/表头排序/导入文件夹；Phase 16 ▶ 标记改 Path；Phase 18 导入列表/导出列表按钮 + 拖拽分流播放列表文件）
│       ├── PlaylistsSidebarView.xaml(.cs) # 左侧栏（Phase 24 起三行：固定「音乐库/艺术家/专辑」节点 + 歌单工具栏 + 歌单 ListBox，库播放时 音乐库 旁显示 ▶；双击重命名、▶ 标记、文件夹/扫描图标；Phase 18 拖入播放列表文件新建歌单）(Phase 6/10/12 continued/18/24)
│       ├── LibraryView.xaml(.cs)     # 音乐库视图（艺术家列表 / 专辑封面网格 / 艺术家详情 / 专辑详情 / 搜索页）+ 状态行 + 空态；双击/Enter 导航与按索引播放、封面 Loaded 惰性读取、返回/刷新；Phase 25 搜索框/清空、分组虚拟化结果、IME/手势与 Ctrl+F (Phase 24/25)
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
│   ├── BytesToBitmapImageConverter.cs    # byte[] → Frozen BitmapImage (Phase 6, 债务 #1 部分偿还)
│   └── InverseBooleanToVisibilityConverter.cs # true→Collapsed / false→Visible；队列与库面板互斥显示 (Phase 24)
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
├── D-player.WinUI/              # Phase 22 功能补齐后的 WinUI 壳（WinUI 3 / Windows App SDK 2.6.5，unpackaged + self-contained，x64）；**仍不进 `D-player.slnf`**，构建由 `verify-gates.ps1 -Full` 的门禁 3 覆盖（见 §4.3 第 28 条）
│   ├── D-player.WinUI.csproj    # WindowsPackageType=None + WindowsAppSDKSelfContained=true + Platforms/Platform=x64；ProjectReference 到 Core；链接根上的 appsettings.json
│   ├── App.xaml(.cs)            # UI 线程建容器（AddDPlayerCore + 本壳的 WinUiFileDialogService + ISettingsPersistence，数据目录 D-player-winui）+ 合并 Theme/ 资源字典 + AppLog 初始化/异常钩子；刻意**不**同步 Dispose ServiceProvider
│   ├── MainWindow.xaml(.cs)     # 三栏 IA 主窗口：标题栏 + NavRail + TrackList + InfoPanel + PlayerBar；窗口几何恢复/保存（物理像素）、`_cleanupCompleted` 关闭守卫、对话框/文本输入的空格守卫；AppWindow.Closing → await CleanupAsync；Mica 依赖根 Grid Background=Transparent
│   ├── Theme/                   # Phase 21 设计令牌与样式
│   │   ├── Tokens.xaml          # 间距（4/8/12/16/24）、圆角（4/8/12）、字号（12/13.5/15/20）、结构尺寸（含 Phase 22 工具栏 40/28）
│   │   ├── Motion.cs            # MotionTokens: Fast=100ms / Normal=150ms / Slow=200ms + StandardEasing
│   │   └── Styles.xaml          # 六态容器模板（default/hover/pressed/selected/focus/disabled）
│   ├── Views/                   # Phase 21 四个 UserControl + Phase 22 设置对话框
│   │   ├── NavRail.xaml(.cs)    # 左栏导航（200px，可折叠至 48px）+ 歌单新建/导入/删除工具栏与 ▶ 活跃标记
│   │   ├── TrackList.xaml(.cs)  # 中栏曲目列表（弹性宽度，36px 行高）+ 导入/导出/清空工具栏与忙碌环
│   │   ├── InfoPanel.xaml(.cs)  # 右栏信息面板（260px，可折叠至 48px、栏头折叠钮，<960px 自动收起；封面/标题/艺术家/专辑/时长）
│   │   ├── PlayerBar.xaml(.cs)  # 底栏播放器（64px，上一首/下一首/随机/循环/音量/静音/设置）
│   │   └── Dialogs/SettingsDialog.xaml(.cs) # 基础设置：音量 + 只读音频输出 + 日志目录入口
│   └── Services/
│       ├── WinUiFileDialogService.cs # FileOpenPicker / FolderPicker / FileSavePicker（InitializeWithWindow）；取消/失败返回空集合或 null
│       ├── WinUiDialogService.cs     # ContentDialog 宿主（prompt/confirm/info/error），IsOpen 供空格守卫
│       ├── WinUiAppPaths.cs          # D-player-winui 数据目录与 logs 子目录常量
│       └── AppLog.cs                 # app.log 追加 + 1 MiB 轮转到 app.log.1；IO 失败静默
│
├── Tests/                       # xUnit v3 测试项目 (Phase 6+，共 **368** 个测试 = 363 默认 + 5 真实 WASAPI；下列逐文件括号计数按 `[Fact]`/`[InlineData]` 实测口径，以 2026-11-13 Phase 30 验收后为准，Phase 29 新增 27 条、Phase 30 新增 26 条默认测试；Phase 20 起只引用 D-player.Core，不引用任何 UI 壳)
│   ├── D-player.Tests.csproj   # 测试项目文件 (xUnit v3 + NSubstitute；CLI/MTP 路径，无 Coverlet/Test.Sdk/runner.visualstudio；OutputType=Exe)
│   ├── Configuration/
│   │   └── DPlayerDataPathsTests.cs      # 数据目录组合 + 两个壳各自注入的目录名钉桩 (2) (Phase 20)
│   ├── Extensions/
│   │   └── AddDPlayerCoreTests.cs        # DI 图解析：从容器取 MainViewModel 并真的构造一个歌单 VM；Phase 24 加 LibraryViewModel 解析断言，Phase 25 加 ILibraryRootScanner / ILibraryWatcher / TimeProvider 解析且 watcher 未启动；Phase 29 终审加容器同步 Dispose 不抛且 Json 单例确已释放 (2) (Phase 20/24/25/29)
│   ├── Models/
│   │   ├── EqualizerConfigTests.cs       # 均衡器配置 record (5) (Phase 14)
│   │   ├── EqualizerPresetsTests.cs      # 均衡器预设 (5) (Phase 14)
│   │   └── AudioConstantsTests.cs        # 音频后缀大小写/空后缀拒绝 + 对话框过滤器与 AudioExtensions 五项全量对齐 (9) (Phase 29)
│   ├── Services/
│   │   ├── LibraryScannerServiceTests.cs # 库扫描（ScanFolderWithStatus 不可用根/空根判定 + Phase 25 取消令牌）(23) (Phase 10/24/25)
│   │   ├── LibraryRootScannerTests.cs    # 同根串行、保存准入、不可用根不写、有效缓存不读标签、fallback/InvalidPaths、写失败保留；Phase 29 加根闸门归零/异根并行/持有中撤销/预取消等待者 (13) (Phase 25/29)
│   │   ├── LibraryRefreshSchedulerTests.cs # 每根去抖/5s 上限、全刷优先、pending、旧 epoch、1s/3s 重试、Stop 排空 (9) (Phase 25)
│   │   ├── FileSystemLibraryWatcherTests.cs # AffectedRoots 边界、错误分类、真实 Created、Dispose 停止 (3) (Phase 25)
│   │   ├── JsonLibraryCacheTests.cs      # 元数据缓存 (5) (Phase 10)
│   │   ├── JsonPlaylistServiceTests.cs   # queue 快照往返（含 Phase 24 的 LibraryPlayback 往返、旧 JSON 缺字段为 null、只有库队列时不回 Seed）+ Phase 29 v1 fixture → v3 迁移 (4) (Phase 24/29)
│   │   ├── JsonSettingsPersistenceTests.cs # settings 往返（Phase 24 的 LibraryFolders 往返 + 缺文件空数组）+ Phase 29 写失败保留旧设置 (3) (Phase 24/29)
│   │   ├── JsonFileStoreTests.cs         # 原子写替换 + tmp 清理；锁定目标失败保留旧文件 (2) (Phase 29)
│   │   ├── JsonContractParityTests.cs    # QueueState / AppSettings / 缓存字典反射 vs 源生成全量输出对照 (3) (Phase 29)
│   │   ├── DisposalTests.cs              # Json 服务与 watcher 双 Dispose 不抛 (2) (Phase 29)
│   │   ├── LibraryPathNormalizerTests.cs # 库根目录规范化/去尾分隔符/大小写不敏感去重 (4) (Phase 24)
│   │   ├── EqualizerSampleProviderTests.cs # 均衡器中间件（立体声独立 + Nyquist 旁路 + Phase 30 的 0 dB 旁路/缓冲边界生效/禁用期 pending/并发读）(11) (Phase 14/30)
│   │   ├── NAudioPlaybackServiceConcurrencyTests.cs # 播放链并发/播完回归（真实占用 WASAPI 设备）(3) (Phase 18 后修复 / Phase 19)
│   │   ├── NAudioPlaybackServiceDeterministicTests.cs # 确定性播放链生命周期（假音源/假输出，无音频设备；Phase 30 新增）(14) (Phase 30)
│   │   ├── NAudioPlaybackServiceStopSemanticsTests.cs # 曲尾停止语义回归（真实占用 WASAPI 设备）(2) (Phase 18 后修复 / Phase 19)
│   │   ├── PlaylistFileEncodingTests.cs  # 编码探测（BOM / 严格 UTF-8 / GBK 回退 / 空输入）(6) (Phase 18)
│   │   ├── PlaylistFileParserTests.cs    # 后缀判定 + M3U/PLS 解析 (16) (Phase 18)
│   │   ├── PlaylistFileServiceTests.cs   # 导入管道（归一化/过滤计数/不抛）+ 导出与往返 (20) (Phase 18)
│   │   ├── SampleAggregatorTests.cs      # 频谱聚合器（时域重叠/已知正弦分桶/预计算表/静音/禁用旁路/订阅隔离/Dispose/启用切换；Phase 30 新增）(8) (Phase 30)
│   │   └── TestAudio.cs                  # 音频测试的公共夹具（非测试类，不计数）
│   └── ViewModels/
│       ├── PlaybackResumeTests.cs         # 断点续播（落盘时机 + 启动恢复 + 缺文件提示 + 归属同步；Phase 24 库队列恢复；Phase 25 搜索态关闭持久化非搜索导航 + 默认不启动 watcher + 清理幂等；2026-11-08 库视图恢复清 ViewedPlaylist；Phase 29 +1）(24) (2026-10-06/24/25；2026-11-08 +1；Phase 29 +1)
│       ├── OneShotCleanupTests.cs         # 清理模板：同 Task/只跑一次、异常传播、晚到完成丢弃 (3) (Phase 29)
│       ├── PlayerViewModelTests.cs        # Transport + 设置批量同步不重复写盘 (16) (Phase 8；2026-11-03 +1)
│       ├── PlayerViewModelSpectrumTests.cs# 频谱 (5) (Phase 13)
│       ├── PlayerViewModelEqualizerTests.cs # 均衡器（启用态传播 + 构造期抑制写盘）(2) (Phase 14)
│       ├── PlaylistViewModelTests.cs      # 队列 + 追加导入/导出 + 表头排序物理重排 + TrackEnded 归属守卫 (22) (Phase 8/18/20；2026-10-24 +1)
│       ├── PlaylistImportReportFormatterTests.cs # 导入报告文案分支 (5) (Phase 18；Phase 20 随格式化器从 Tests/Views 搬来)
│       ├── PlaylistsViewModelTests.cs     # 多歌单 + 双击入口/归属 + 隐藏库队列 + Phase 25 文件夹队列成员差分/共享根缓存/导入清理 + Phase 26 普通歌单追加/新建并加入规则 (54) (Phase 8/12/18/20/24/25/26)
│       ├── LibraryGroupingTests.cs        # 分组：未知标签、合辑合并/同名不同目录拆分、贡献艺术家保留、排序、多根去重、Phase 27 多碟归并/碟序排序/命名变体/误并守卫 (22) (Phase 24/27)
│       ├── LibrarySearchTests.cs          # 三组字面匹配、CJK/大小写/内部空格、未知占位、专辑键边界、稳定排序、取消 (9) (Phase 25)
│       └── LibraryViewModelTests.cs       # 库 VM：缓存命中不读标签/初始化不扫盘、根快照合并/不可用根/空根/多根去重、无差异不重置、同名专辑选择、监听只刷受影响根、启动先订阅后校准、清理晚到结果、搜索/播放/导航、Phase 26 库 → 歌单转发 (32) (Phase 24/25/26)
│
├── tools/
│   ├── verify-gates.ps1         # 一键跑校验：`-Fast` = 门禁 1/2（`.slnf` 构建 + 测试，默认排除音频）；`-Full` = 再加门禁 3（WinUI 单工程构建，0 警告）；`-Audio` = 追加 5 条真实 WASAPI 集成测试。逐步 fail-fast：任一步非零退出、**或两个构建步骤在 MSBuild 安静日志里报出任一行警告**即中止（门禁口径是 0 警告 0 错误，而 `dotnet build` 只报警告时退出码仍是 0）；`dotnet test` 那一步不带 `--nologo`
│   └── coupling-audit/          # Phase 15 耦合审计脚本（Invoke-CouplingAudit.ps1，M1–M6 度量；层目录按**四个 scan root** 收集：`D-player.Core/<层>`、`D-player.WinUI/<层>`、`D-player/<层>`、仓库根 `<层>`，外加仓库根自己的根级 `*.cs`；DI 注册表也从 Core 下解析。盲区与读数口径见 COUPLING §5 末尾的"Phase 20 审计复跑"块）
│
└── docs/
    ├── PROJECT.md               # 本文档：架构、模块、数据流
    ├── COUPLING.md              # 耦合分析与隐式契约
    ├── README.md                # 文档地图
    ├── PHASE29-ACCEPTANCE.md    # Phase 29 验收（当前）
    ├── PHASE28-ACCEPTANCE.md    # Phase 28 验收（上一阶段）
    ├── PHASE27-ACCEPTANCE.md    # Phase 27 验收（历史）
    ├── PHASE26-ACCEPTANCE.md    # Phase 26 验收（历史）
    ├── PHASE25-ACCEPTANCE.md    # Phase 25 验收（历史）
    ├── PHASE24-ACCEPTANCE.md    # Phase 24 验收（历史）
    ├── PHASE23-ACCEPTANCE.md    # Phase 23 验收（历史）
    ├── PHASE22-ACCEPTANCE.md    # Phase 22 验收（历史）
    ├── PHASE21-ACCEPTANCE.md    # Phase 21 验收（历史）
    ├── PHASE20-COMPARISON.md    # Phase 20 两壳对比与决策门（历史）
    ├── superpowers/specs/       # Phase 14–28 设计稿（Phase 1–13 已移入 archive）
    ├── superpowers/plans/       # 四份刻意未跟踪的 Phase 24–28 本地计划（不属于发布面）
    └── archive/
        ├── README.md            # 归档说明
        ├── specs/               # UmaPlayer 时代设计稿（Phase 1–13）
        └── plans/               # Phase 1–23 实现计划
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
   │  (View, code-behind) │◀──▶│  持有子 VM + 生命周期     │
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
注（Phase 24/25）：`MainViewModel` 还持有 `LibraryViewModel`；图中为简洁未画。库播放只经 `PlaylistsViewModel.PlayLibraryTracksAsync`，库 VM 不直接依赖 `IPlaybackService`。

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
| `IFileDialogService` | Singleton（**注册在各壳里，不在 Core**） | 无状态；Core 只声明接口（Phase 22 起三方法返回 `Task`）。WPF 壳注册 `Win32FileDialogService`（同步对话框 + `Task.FromResult`），WinUI 壳注册真实 picker 实现 `WinUiFileDialogService` (Phase 20/22) |
| `ISettingsPersistence` | Singleton | 内部 `SemaphoreSlim` 并发互斥 |
| `IPlaylistService` | Singleton (Phase 6，替换 Phase 4 `IQueuePersistence`) | 独立 `SemaphoreSlim`，与 settings 文件锁互不影响；`LoadAsync` 绝不抛 |
| `ITrackMetadataReader` | Singleton (Phase 3) | 无状态，封装 z440.atl.core；`ReadAsync` 不抛 |
| `ILibraryScannerService` | **Singleton** (Phase 10) | 递归文件夹扫描 + Diff 计算；无状态 |
| `ILibraryCache` | **Singleton** (Phase 10) | JSON 元数据缓存 (`library-cache.json`)；内部 `SemaphoreSlim` |
| `IPlaylistFileService` | **Singleton** (Phase 18) | 播放列表文件读写门面；无状态；`ImportAsync` 绝不抛、`ExportAsync` 可抛（错误策略刻意不对称） |
| `ILibraryRootScanner` | **Singleton** (Phase 25) | 同根串行枚举、差分、标签 fallback 与原子缓存保存；库 VM 与文件夹歌单共用 |
| `ILibraryWatcher` | **Singleton** (Phase 25) | 惰性 `FileSystemWatcher`；构造/DI 解析不访问文件系统，只在显式 `StartMonitoringAsync` 后订阅 |
| `TimeProvider` | **Singleton** (Phase 25) | 调度器的可测试时钟；生产注册 `TimeProvider.System` |
| `IAudioDeviceManager` | Singleton（Stub） | 预留 |
| `IAudioOutputFactory` | Transient（Stub） | 生产默认实现 `StubAudioOutputFactory`（固定返回 WASAPI Shared）。**工厂已由 `NAudioPlaybackService` 的公开构造函数取默认值 `new StubAudioOutputFactory()` 消费**（每建一条链调用一次 `CreateOutput()`），但**这条 DI 注册本身仍无人解析**：容器构造 `NAudioPlaybackService` 走公开 ctor，不会注入容器里的 Transient 注册（见 §5.2 与 COUPLING.md §4） |
| `PlayerViewModel` | **Transient** (Phase 3) | Transport 子 VM；DI 中**必须先于** `PlaylistViewModel` 注册；Phase 13 订阅 `SpectrumDataAvailable`；Phase 14 仅持有 `EqualizerEnabled` observable（供 PlayerBar 🎚 按钮高亮） |
| `PlaylistViewModel` | **Transient**（经 `Func<Playlist, PlaylistViewModel>` 工厂） (Phase 3/6) | 队列子 VM；由 `PlaylistsViewModel` 用工厂按需创建，seed 为动态参数 |
| `PlaylistsViewModel` | **Singleton** (Phase 6) | 多歌单容器；WPF 壳的 View code-behind 经 `App.GetService<PlaylistsViewModel>()` 取用（双击跨歌单播放），WinUI 壳则经 `MainViewModel.Playlists` 拿到同一个实例 —— 两种取法都要求它必须单例。Phase 24 起还私有持有隐藏的库播放队列（不加入 `Playlists` 集合） |
| `LibraryViewModel` | **Singleton** (Phase 24/25) | 多根音乐库视图模型；依赖 `ISettingsPersistence` / `ILibraryRootScanner` / `ILibraryCache` / `ITrackMetadataReader` / `PlaylistsViewModel` / `ILibraryWatcher` / `TimeProvider`；构造时捕获 `SynchronizationContext`，扫描发布与封面读取回 UI 上下文 |
| `MainViewModel` | **Transient** | Strict Facade，构造时聚合 Player / Playlists / Library 三个子 VM，并持有壳级导航状态 `IsLibraryViewActive` |

> **Phase 14 注：** `EqualizerSampleProvider` **不是 DI 服务** —— 与 `SampleAggregator` 同样在 `NAudioPlaybackService.LoadAsync` 内按曲创建（每首新曲重建一条播放链），不注册进容器。`IPlaybackService` 仅新增 `EqualizerConfig` 属性（镜像 Phase 13 的 `SpectrumConfig`），0 新 DI 依赖、 0 新 ViewModel（方案 A：`EqualizerDialog` 直写 `IPlaybackService` + `ISettingsPersistence`）。

> **Phase 20 注（两壳并存带来的三条纪律）：** ① 容器必须在 **UI 线程**构造（`NAudioPlaybackService` 构造时捕获 `SynchronizationContext.Current`；WinUI 3 的 UI 线程上是 `DispatcherQueueSynchronizationContext`，实测可用，不需要壳自造上下文）。② 每壳必须**自己**注册 `IFileDialogService` —— Core 的 `PlaylistViewModel` 工厂是在**被调用时**才 `GetRequiredService<IFileDialogService>()`，漏注册不会在 `GetRequiredService<MainViewModel>()` 时炸，而是第一次构造歌单才炸；`Tests/Extensions/AddDPlayerCoreTests.cs` 钉住的是 **Core 图**，钉不住壳忘记自己那一条。③ 关闭时必须 `await MainViewModel.CleanupAsync()`（写最终断点位置 + 释放 WASAPI 设备）**之后再**放行关闭，且**不得**在 UI 收尾路径上同步 `Dispose` 容器 —— WPF 壳 `App.OnExit` 里那条同步 Dispose 是已知既有缺陷，WinUI 壳刻意没有把它复制过来。

### 4.3 关键设计决策

1. **配置双源**：启动只读默认值用 `IOptions<AppSettings>`；运行时可变状态（窗口、音量、最后播放路径）走 `ISettingsPersistence` 写入用户数据目录。两者通过 `record with` 不可变更新协同。

2. **位置事件节流**：`NAudioPlaybackService.PollPositionAsync` 用 `PeriodicTimer(33 ms)`（≈30 Hz）轮询，**只**用 `_playSession` 代际终止旧循环；Phase 30 删除了 `_lastPositionEvent` + `DateTime` 的双层节流（每次 `Play()` 都会起一个新循环，旧实现会叠加多个循环）。

3. **UI 线程封送**：服务层捕获启动时的 `SynchronizationContext`（必然是 UI 线程，因为 `IPlaybackService` 由 `App.OnStartup` 间接解析），所有事件通过 `_syncContext.Post` 派发，VM 直接绑定即可。

4. **拖拽 Seek 防回跳**：`PlayerViewModel.IsSeeking` 标志在拖动期间抑制 `PositionChanged` → `Position` 写入，避免拖拽时滑块被服务回写"拽回去"。

5. **静音状态保留音量**：`ToggleMute` 把当前 `Volume` 存到 `_volumeBeforeMute`，置 `Volume=0`；取消静音恢复。拖动滑块若有非零值会自动取消静音。

6. **窗口可见性自愈**：`MainWindow.EnsureVisible()` 检查恢复的位置是否在虚拟屏内（防止外接屏拔掉后窗口飘到屏外），不在则回退到主屏居中。

7. **面向接口 + 占位实现**：`IAudioDeviceManager` 已注册 Stub、`IAudioOutputFactory` 注册了 Stub 且同时是生产默认输出实现（由 `NAudioPlaybackService` 公开 ctor 取默认值消费），便于后续替换为真实多设备/Exclusive/ASIO 实现而不动 VM。

8. **Strict VM Facade（Phase 3/6/24）**：`MainViewModel` 作为子 VM 容器，暴露 `Player` + `Playlists` + `Library` + `IsLibraryViewActive` + `InitializeAsync` + `CleanupAsync`，集中 debounce save 与关闭排空。`PlayerViewModel`（transport）与 `PlaylistViewModel`（队列）**互不持引用**，仅通过 `IPlaybackService` Singleton 间接协作（PlayerVM 订阅 transport 事件；PlaylistVM 单独订阅 `TrackEnded` 推进队列）。PlaylistVM 通过 `Container` 属性代理读取 PlaylistsVM 的全局 Shuffle/Repeat 状态。View 跨域命令（如 PlayerBar 上的 ⏮/⏭ 调用 PlaylistVM，🔀/🔁 调用 PlaylistsVM）通过 `{Binding DataContext.Playlists.<sub>.<cmd>, RelativeSource={RelativeSource AncestorType=Window}}` 跨级绑定到 MainWindow 的 DataContext 解决。

9. **持久化 read-modify-write 原子化（Phase 3）**：`ISettingsPersistence` 接口由 `SaveAsync(AppSettings)` 改为 `UpdateAsync(Func<AppSettings, AppSettings> mutator)`，把"读盘 → 应用 mutator → 写盘"整个序列封进 `SemaphoreSlim` 锁内，根治了 VM 写音量与 `Window_Closing` 写窗口尺寸的合并竞态（COUPLING.md 旧债 #3）。注意：`UpdateAsync` 内部 `.ConfigureAwait(false)`，故调用方若需要在 mutator 内读取 WPF DependencyProperty，必须在 await 之前先把值捕获到 UI 线程局部变量（见 `MainWindow.xaml.cs:Window_Closing`）。

10. **队列持久化与 settings 隔离（Phase 4）**：队列状态独立写到 `%LocalAppData%\D-player\queue.json`。**为什么不复用 settings.json**：(a) 队列条目数量级远大于 settings 字段，混在一起每次拖音量都会让队列 JSON 重新序列化；(b) settings 是高频更新（音量、窗口尺寸），queue 由 `StateChanged` 驱动、500ms debounce 写盘并在关闭时 flush，写入节奏不同；(c) 模式失败隔离 —— queue.json 损坏不影响窗口/音量恢复。`IPlaylistService` 只有 `LoadAsync()` 与 `SaveAsync(QueueState)`，没有 `UpdateAsync` —— `PlaylistsViewModel` 是唯一权威源（"读队列" = `BuildSnapshot()`），无需读-改-写原子化。

11. **Cancel-and-close 关闭模式（Phase 4）**：`MainWindow.Window_Closing` 是 `async void`；从 Phase 3 的 2 个 await（settings + CleanupAsync）涨到 Phase 4 的 4 个（+ snapshot + queue 写盘）后撞上致命 race —— 第一个 await yield 后 WPF 立即继续关闭流程，`ShutdownMode.OnLastWindowClose` 触发 `Application.Shutdown` → `Dispatcher.InvokeShutdown`，把后续 await 续延 post 到死 dispatcher 上永不运行（settings 通常能抢到，queue 永远丢）。修复：首次进入 `e.Cancel = true` 拦下，跑完所有异步工作后调 `Close()` 重新触发 Closing，第二次进入凭 `_isClosing` 标志直接 fall-through。这是 WPF `async void` Closing 的标准纪律 —— 任何新增 await 都该用此模式。

12. **拖拽 View/VM 边界（Phase 5）**：所有 OLE DragDrop 事件、命中测试（`ComputeInsertIndex`）、文件后缀过滤（`DragDropExtensions.FilterAudioPaths`）、Adorner 绘制（`DropInsertionAdorner`）都在 View 层；VM 仅暴露纯数据命令 —— `DropExternalFiles(IReadOnlyList<string>)` 和 `MoveTracks(MoveTracksArgs)`。VM 不感知 `DataObject` / `DragEventArgs` / `AdornerLayer`，仍可单测。

13. **重排算法用对象身份而非索引算术（Phase 5）**：`MoveTracks` 缓存被移动的 Track 引用 + 当前曲引用 + Shuffle 历史引用集合，删-插完成后用 `ReferenceEquals` 扫一遍 Queue 重建 `CurrentIndex` 和 `_shuffleHistory`。**不能用 `Queue.IndexOf`**：Track 是 `sealed record`（结构相等），多个 `CreateFallback("X.mp3")` 占位是结构相等但引用不同的对象，IndexOf 会返回首个结构等价匹配而非原始那一个，导致重排后 ▶ 跟到错的曲、Shuffle 历史塌陷。HashSet 同理需 `ReferenceEqualityComparer.Instance`。

14. **拖拽期间 in-flight 重入用 `_playToken++` 顶替（Phase 5）**：`PlaylistViewModel.RemoveTrack` 与新增的 `MoveTracks` 入口都自增 `_playToken`，关上 in-flight `PlayTrackAtAsync` 在 `await` 元数据期间 `Queue[index] = meta` 写到错位的窗口（COUPLING.md 旧债 #5）。`MoveTracks` 不调 `_player.Unload()` —— 重排不中断播放，NAudio 在另一线程继续推流，仅 ▶ 标记跟到新位置。

15. **GUID 主键, Name 仅展示（Phase 6）**：`Playlist.Id` 是 GUID 字符串，创建时一次性确定，不可变。`Name` 是显示名，可重命名、可重复，不破坏持久化绑定。

16. **Viewed vs Current 双指针（Phase 6）**：`ViewedPlaylist`（UI 选中，不持久化）与 `CurrentPlaylistId`（正在播放，持久化）解耦。用户切查看不打断播放，只有双击才跨歌单切换音频（Spotify 模型）。

17. **Schema v2 一次性自动迁移（Phase 6）**：`JsonPlaylistService.LoadAsync` 检测 v1 包成单条 "默认歌单"，立即写盘；迁移失败则吞掉，内存仍是 v2，下次重迁（幂等）。

18. **删除最后一个歌单自动重建（Phase 6）**：永远不存在 0 歌单状态；UI 不需要"空状态"分支。

19. **Debounce save 集中在 MainViewModel（Phase 6）**：子 VM 不感知存盘；`PlaylistsViewModel.StateChanged` → MainVM 500ms debounce → `SaveAsync`。`CleanupAsync` 同步 flush 一次。

20. **Phase 11 SettingsDialog 不加 SettingsViewModel（YAGNI）**：仅 DefaultVolume 可编辑，OutputMode/PreferredDeviceId 无消费者。对话框直接读写 `ISettingsPersistence`。Phase 12 音频设置有复杂交互（如切换输出模式需重载设备列表）时再引入 SettingsViewModel。

21. **debt #1 偿还（Phase 6/7）**：Phase 6 交付 `BytesToBitmapImageConverter`；Phase 7 完成数据流切换 —— `PlayerViewModel.AlbumArtBytes` 改为 `byte[]`，XAML 通过 Converter 转为 Frozen BitmapImage。VM 层不再依赖任何 WPF 类型。

22. **全局 Shuffle/Repeat（Phase 12 continued）**：Shuffle/Repeat 从 Playlist 级别提升到 PlaylistsViewModel 全局共享。原因：用户切换歌单时 Shuffle/Repeat 状态被重置（每歌单独立）的体验反直觉，且与主流播放器（Spotify / foobar2000）行为不一致。实现：PlaylistsViewModel 持有 `[ObservableProperty] ShuffleEnabled/RepeatMode`，PlaylistViewModel 通过 `Container` 属性代理读取；Playlist record 的同名字段保留但写入时固定 `false/Off`（向后兼容旧 queue.json）；QueueState 根级别新增 `ShuffleEnabled/RepeatMode` 持久化字段。

23. **图标矢量化（Phase 16）**：全部 emoji/字形图标替换为 `Themes/Icons.xaml` 描边矢量 `Geometry`（Phase 16 时 22 个，Phase 18 增至 24 个 `Icon.*`；24×24 viewbox，Feather/Lucide 几何署名）。转换器（`PlayStateToIconConverter` / `RepeatModeToIconConverter` / 新增 `BoolToVolumeIconConverter`）返回 `Geometry` 而非字符串（经 `Application.Current.FindResource` 查资源）；▶ 标记由 TextBlock 改 `Path`（`Icon.PlayMarker` 实心三角 `Fill=AccentPrimary`，小尺寸下描边不清晰的例外）；活跃态经 `BoolToAccentBrushConverter` 着 `Path.Stroke`。动机：emoji 由系统字体渲染、彩色不可主题化、跨 Windows 版本不一致。VM 层移除 `VolumeIcon`（守住"VM 无 WPF 类型/无 emoji"纪律）。

24. **无边框 chrome + 自绘标题栏（Phase 17）**：`WindowStyle=None` + `WindowChrome(CaptionHeight=32, UseAeroCaptionButtons=False, GlassFrameThickness=0)` + 复用 `Views/Controls/TitleBar` UserControl；OS 仍负责拖动/双击最大化/Aero Snap（不做手写 DragMove）。自绘按钮必须 `shell:WindowChrome.IsHitTestVisibleInChrome=True`（否则点击被 caption 拖动吞掉）。最大化时给 `Window.Content` 根元素加**常量式工作区边距**（`WorkArea` 偏移 + `WindowResizeBorderThickness`，不读窗口实际边界 —— 布局时序会让 right/bottom 边距偏大留空）。应用：MainWindow `ShowMaximize=True`（三键）；3 个对话框 `ShowMaximize=False`（仅关闭键）。

25. **深色控件隐式样式（Phase 17）**：`Controls.xaml` 扩充 ComboBox（自绘 ToggleButton 可点击表面 `ClickMode=Press` + 深色 Popup + ComboBoxItem 悬停/选中态）、CheckBox（深色方框 + accent 勾）、ScrollBar（横竖双模板、仅 track+thumb 隐藏箭头）、ToolTip/ContextMenu/MenuItem/Separator 深色。纯资源字典改动，全局生效，0 代码路径变化。

26. **播放列表文件导入导出（Phase 18）**：新增门面服务 `IPlaylistFileService`（Singleton，无状态）集中「编码探测 → 格式解析 → 路径归一化 → URL/后缀/存在性过滤计数」，两个 VM 共用同一条管道。**错误策略刻意不对称**：`ImportAsync` 绝不抛（读侧对齐 `JsonPlaylistService.LoadAsync`，任何失败退化为空结果，由 View 的"没有可导入的条目"报告兜住）；`ExportAsync` 让 `IOException`/`UnauthorizedAccessException` 冒到 VM 转错误文案（写侧对齐设置/EQ 对话框的 try/catch + 错误框）。**双入口分层**沿用既有约定：容器级（侧边栏按钮/拖到侧边栏）= 新建歌单（`PlaylistsViewModel.ImportPlaylistFileAsync`，seed 的 `SourceFolder = null` → 普通歌单，不写 library cache）；歌单级（工具栏"导入列表"/拖到列表区）= 追加当前歌单（`PlaylistViewModel.ImportPlaylistFileAsync`，复用既有 `DropExternalFiles`，不新增元数据依赖）。VM 方法的 `presetPath` 参数是拖拽入口与对话框入口共用同一管道的接点（非空跳过对话框）。**GBK 解码不新增 NuGet 包**：`System.Text.Encoding.CodePages` 在 net10.0 框架隐含，显式 `PackageReference` 会触发 NU1510 警告破坏 0 警告门禁，`D-player.csproj` 未改动；CodePages provider 在 `PlaylistFileEncoding` 静态构造函数注册（`Encoding.GetEncoding(936)` 只出现在该类内部，"注册早于解码"是类型不变量而非启动顺序约定，`App.xaml.cs` 未改动）。**分层纪律**：VM 只返回结构化 `PlaylistImportReport`，中文文案由 `PlaylistImportReportFormatter` 组装（四个入口共用；Phase 18 时在 `Views/Controls`，Phase 20 搬进 `D-player.Core/ViewModels` 以便第二壳复用）；唯一例外是导出错误文案 `$"导出失败：{ex.Message}"` —— 单一分支直传、只有一个调用方（设计稿 §7.5）。

27. **共享层抽成 WPF-free 的 `D-player.Core`（Phase 20）**：Models / Services / ViewModels / Configuration / DI 注册整体搬进类库（`net10.0-windows`，**不开** `UseWPF`），两个 UI 壳各引用它。搬家只清掉两处真实的 WPF 泄漏：`PlaylistViewModel.SortedView`（`ICollectionView`）删除 —— `SortBy` 本来就把 `Queue` 物理重排，列表直接绑 `Queue` 即可，视图层没有任何东西需要"排序视图"这个中间概念；`Win32FileDialogService`（`Microsoft.Win32`）从 Core 搬回 WPF 壳，Core 只留 `IFileDialogService` 接口与消费点。**动机**：VM/Service 层早在 Phase 7 就已做到无 WPF 类型，物理隔离后第二壳才可能复用同一份逻辑而不引入 WPF 依赖。**WPF 侧行为零变化**是这一条的硬约束。（TFM 已于 Phase 28 抬至 net11.0）

28. **门禁走解决方案筛选器 `D-player.slnf`；WinUI 构建是 `-Full` 的第三道门禁（Phase 20 起，Phase 22 起正式并入）**：词汇只有三层，别再混——**① 门禁 1/2（每次改动，`-Fast`）**：`dotnet build D-player.slnf -c Debug --nologo -v q`（0 警告 0 错误）+ `dotnet test D-player.slnf -c Debug -v q --filter "Category!=AudioDevice"`（363 通过 / 0 失败；5 条真实音频集成由 `-Audio` 单独跑）；**② 门禁 3（`-Full`，每个 Phase 22 任务必跑）**：`dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q`（0/0），覆盖 WinUI 的 XAML 编译与 Core 公开面变化（构造参数、`IFileDialogService` 成员、`HandleDoubleClickPlay` 元数、`ViewedPlaylist` setter 等——这类改动让门禁 1/2 照样全绿，而壳已经编译不过），改到 WinUI 自身时同样跑；首次 restore 之后是增量构建，实测约 9 秒；**③ 整解 `D-player.sln` 构建 = 可选**，只为确认 IDE「Build Solution」没被打挂。`tools/verify-gates.ps1 -Fast` 跑门禁 1/2，`-Full` 跑门禁 1/2/3，`-Audio` 追加 5 条真实音频集成（逐步 fail-fast：任一步非零退出、**或两个构建步骤在 MSBuild 安静日志里报出任一行警告**即中止——口径是 **0 警告 0 错误**，而 `dotnet build` 只报警告时退出码仍是 0，光看退出码会把违约的构建印成 ok；`dotnet test` 那一步刻意不带 `--nologo`）。**筛选器只含 Core + WPF 壳 + Tests**，WinUI 刻意不放进 `.slnf`：Windows App SDK 一次 restore 拉 9 个子包、首次约 8.1 分钟、自包含输出目录很大，放进每次构建/测试会让"改一行共享层跑全量"的成本翻几十倍；代价是 **门禁 1/2 对 WinUI 完全无感**，所以每个 Phase 22 任务与任何 WinUI/Core 公开面改动都必须跑 `-Full` 的门禁 3。`Tests/Extensions/AddDPlayerCoreTests.cs` 只能钉住 Core 的 DI 图，钉不住壳忘记注册自己的 `IFileDialogService`（那种缺失要到第一次构造歌单才炸）。工作实例：A5 的"死 using"整理让门禁 1/2 全绿而 WinUI 已坏，只有单壳构建会发现。

29. **用户数据目录由 UI 壳注入（Phase 20）**：`DPlayerDataPaths(Root, FolderName)`（`Directory` 是组合属性）取代此前硬编码在三个持久化服务里的 `Environment.SpecialFolder.LocalApplicationData` + `"D-player"`；`AddDPlayerCore(IConfiguration, DPlayerDataPaths)` 因此多了第二个参数。**为什么必须分开**：两壳的 `queue.json`/`settings.json` 写入者之间没有任何跨进程协调，共用目录会互相覆盖对方的队列与音量。WPF 壳继续用 `D-player`（老用户数据不搬家，`LegacyDataMigration` 的 UmaPlayer 迁移也继续只属它），WinUI 壳用 `D-player-winui`。

30. **两壳共用同一条双击入口，不新增并行 API（Phase 20）**：WinUI 的双击走 `PlaylistsViewModel.HandleDoubleClickPlay(target, index)` —— 与 WPF `Views/Controls/PlaylistView.xaml.cs` 完全同一个方法。实施中曾按计划在 Core 新增过 `PlaylistViewModel.PlayIndexAsync(int)`，随后被推翻并删除：它直进 `PlayTrackAtAsync`，绕开了 `PlayTrackAt` 开头的 `_shuffleHistory.Clear()`（"视为新会话"），于是随机模式下两壳的双击语义会静默分叉 —— 而这正是本阶段要对比的那个手势。留下的纪律：**跨壳的同一手势必须走 Core 里同一个公开入口**；宁可壳侧少几行，也不要复制一份"看起来一样"的实现。

31. **每壳各自负责关闭落盘，且不得同步 Dispose 容器（Phase 20）**：WPF 用 `Window_Closing` 的 cancel-and-close（Phase 4 起）；WinUI 用 `AppWindow.Closing` → `args.Cancel = true` → `await MainViewModel.CleanupAsync()` → `Close()`，复刻同一形状（`D-player.WinUI/MainWindow.xaml.cs`）。少了这一步，最终断点位置只能靠 30 秒节流或"先暂停"才落盘。同时 WinUI **不**在关闭路径上同步 `Dispose` 它的 `ServiceProvider`：`MainViewModel` 只实现 `IAsyncDisposable`，同步 Dispose 会抛（WPF 壳 `App.OnExit` 那条既有缺陷正是如此），本阶段刻意不把它复制到第二壳。

32. **WinUI 的 Mica 依赖"根背景 Transparent"这条约定（Phase 20）**：`Window.SystemBackdrop = new MicaBackdrop()` 挂上后，材质只在**没有不透明背景刷**的表面后面可见；根 `Grid` 必须是 `Background="Transparent"`，否则整片材质被页面底色盖住，肉眼与截图都像"材质没生效"。并且 `DWMWA_SYSTEMBACKDROP_TYPE` 对组合器挂载的 backdrop **不是有效探针**（本项目实测恒为 0）—— 判据只能是"透明表面后的像素是否随窗外内容变化"。实测数据与三行对照已写进 `D-player.WinUI/MainWindow.xaml` 顶部注释。

33. **仓库根的兄弟目录必须进根工程的 glob 排除集（Phase 20）**：`D-player.csproj` 在仓库根，SDK 默认 `**/*.cs` / `**/*.xaml` 会把 `D-player.Core/**`、`D-player.WinUI/**`、`tools/**`（以及原有的 `Tests/**`）扫进 WPF 程序集，出现重复类型或不存在 `Microsoft.UI.Xaml` 导致的 MC3074/CS0234。每加一个仓库根级别的兄弟工程或工具目录，就要给它补一组 `Compile/Page/ApplicationDefinition/Resource/None/EmbeddedResource Remove`。

34. **TrackEnded 的推进权属于当前播放歌单，播放归属随实际出声歌单同步（2026-10-24，Phase 22 后修复）**：`IPlaybackService` 是全局单例，所有 `PlaylistViewModel` 都在 ctor 订阅 `TrackEnded`；`HandleTrackEnded` 入口必须以 `IsActivePlaylist` 守卫，否则曲目自然播完时所有歌单都会各自推进并调用全局 `LoadAsync/Play`，最后完成者抢走播放（Phase 22 冒烟实见非查看歌单 "1" 抢播）。同时 `PlayTrackAtAsync` 在开始播放前把 `Container.CurrentPlaylistId` 指向本歌单 —— 上一首/下一首已改绑 `CurrentPlaylist`（见第 35 条），但双击与"无已加载曲目时的首播"（`ViewedPlaylist.PlayCurrentCommand`）仍可从非当前歌单直达播放；不同步指针会让守卫在下次自然播完时推进旧歌单。启动断点续播同理，`FindPlaylistTrackByPath` 找到曲目所属歌单后同步指针。回归：`PlaylistViewModelTests.TrackEnded_WhenPlaylistIsInactive_DoesNotAdvanceOrPlay`、`PlaylistsViewModelTests.TrackEnded_RepeatOne_WhenPlaylistIsInactive_DoesNotReplay`、`PlaylistsViewModelTests.PlayTrackAt_FromViewedNonPlayingPlaylist_ClaimsCurrentPlaylistId`、`PlaybackResumeTests.InitializeAsync_RestoredTrackInAnotherPlaylist_ClaimsCurrentPlaylistId`。

35. **上一首/下一首作用于正在播放的歌单，不跟随查看项（2026-10-24）**：PlayerBar 的 Prev/Next 绑定 `Playlists.CurrentPlaylist`（由 `CurrentPlaylistId` 解析）—— WPF 走 XAML Command 绑定，WinUI 在点击处理器里取 `CurrentPlaylist` 并按它的 `HasCurrentTrack` 设置按钮可用性；`CurrentPlaylist` 在 Id 或歌单集合变化时发 `PropertyChanged`。这是 Phase 6 双指针模型的落实：切查看不打断播放，只有双击才跨歌单切换音频。绑 `ViewedPlaylist` 的后果是用户查看一个刚导入、`CurrentIndex = -1` 的歌单时按钮变灰，而且按键会作用于未播放队列。例外：无已加载曲目时 ▶ 仍走 `ViewedPlaylist.PlayCurrentCommand`（此时没有音频可打断，启动首播依赖它）。

---

## 5. 模块详解

> **Phase 20 起的路径说明**：下面 §5.1–§5.3（`Models` / `Services` / `ViewModels`）与 §4.2 的注册表都在 **`D-player.Core/`** 下；`Views` / `Converters` / `Themes` 在 WPF 壳（仓库根），`D-player.WinUI` 单独见 §5.7。小节标题沿用层名不带工程前缀。

### 5.1 `Models`

- **`Track`**：不可变 record。字段：`FilePath` / `Title` / `Artist` / `Album` / `Genre` / `Year` / `SampleRate` / `AlbumArt` / `Duration` / `TrackNumber`（元数据曲目号，Phase 12 continued 新增）。`Duration` 与 `SampleRate` 在加载时由 `NAudioPlaybackService.LoadAsync` 通过 `track with { Duration=..., SampleRate=... }` 补齐。`AlbumArt` 为原始字节数组，由 VM 转 `BitmapImage`（限 200px、`Freeze()` 跨线程安全）。**注意 record 的结构相等：** 两个 `CreateFallback("X.mp3")` 占位 Track 在结构上相等 —— 任何按相等性查找/去重的代码（`IndexOf` / 默认 `HashSet<Track>`）都会塌陷它们。Phase 5 重排算法因此改用引用身份（`ReferenceEquals` + `ReferenceEqualityComparer.Instance`）。
- **`PlayState`**：`Stopped / Playing / Paused`。
- **`RepeatMode`**：`Off / List / One`（Phase 2）。
- **`QueueState`**（Phase 4）：不可变 record；`SchemaVersion=3`（v2→v3 新增 `Playlist.SourceFolder`，无迁移：缺失字段反序列化为 null）/ `Playlists: IReadOnlyList<Playlist>` / `CurrentPlaylistId` / `ShuffleEnabled: bool` / `RepeatMode: RepeatMode`（Phase 12 continued：全局播放模式，所有歌单共享，从 Playlist 级别提升到 QueueState 根级别）/ `LibraryPlayback: Playlist?`（Phase 24：隐藏库播放队列，旧文件缺失为 null）。**只持久化路径与队列态**，不携带 Track 元数据或封面 —— 启动时由 `PlaylistsViewModel.Hydrate` 为每条路径创建占位 Track，用户首次播放时由 `PlayTrackAtAsync` 升级为完整元数据。
- **`MoveTracksArgs`**（Phase 5）：不可变 record；`SourceIndices: IReadOnlyList<int>`（升序无重复，每项 ∈ [0, Queue.Count)） / `TargetIndex: int`（∈ [0, Queue.Count]，i 表示插到 i 之前；Count 表示末尾）。由 View 层 Drop handler 构造，这些不变量由 View 保证（VM 信任入参，无校验代码）。
- **`LibraryCacheEntry`**（Phase 10）：不可变 record；`FilePath` / `Title` / `Artist` / `Album` / `Genre` / `Year` / `Duration` / `SampleRate` / `TrackNumber`（Phase 12 continued 新增，元数据曲目号）。不含 `AlbumArt` 字节——按需加载，保持缓存文件体积小。
- **`AudioDeviceInfo`**：`(Id, Name, IsDefault)`，目前仅类型存在。

### 5.2 `Services/NAudioPlaybackService`

| 成员 | 说明 |
|------|------|
| `LoadAsync(Track)` | **Phase 30 两阶段装载**（`_loadGate` 串行化，重活不持 `_chainGate`）：① 闸门内摘下旧链（置 `StopRequested`、记下 `_loadCancelVersion` 与音量/频谱/EQ 快照、`SetState(Stopped)`）；② 线程池 `Task.Run` 上拆除旧链并新建 `MediaFoundationReader` → `ToSampleProvider` → **`EqualizerSampleProvider`（Phase 14 均衡器中间件）** → **`SampleAggregator`（Phase 13 频谱中间件）** → `VolumeSampleProvider` → `WasapiPlayer`（Phase 19：`new WasapiPlayerBuilder().WithSharedMode().WithEventSync().WithLatency(100).Build()`，与迁移前的 `WasapiOut(Shared, 100ms)` 语义等价；`WasapiPlayer` 无公开构造函数，只能经 builder 建立，之后仍按 `IWavePlayer` 使用）；③ 回到闸门校验代际与 `_disposed` 后安装 `_current`，并按当前字段重放音量 / 频谱开关 / EQ（覆盖装载窗口内的实时改动）；装载期间 Unload/Dispose 递增代际，过时链直接释放。安装成功后**一次封送** PositionChanged(Zero) → DurationChanged → TrackChanged，并在 UI 线程复核当前链身份后才整组派发 · Phase 3：在 DurationChanged 之前先广播 PositionChanged(Zero)，防止切到时长更短的曲时旧 Position 与新 Duration 并存（"4:05 / 3:20" glitch） |
| `Play / Pause / Stop` | 委派给 `IWavePlayer`；`Stop` 同时将 `CurrentTime` 归零（保留底层资源，再 `Play()` 会重播同一首） |
| `Unload()` | **完全释放**底层 reader/wavePlayer，清掉 `_currentTrack`；之后 `Play()` 是 no-op。**Phase 3：同时广播 `TrackChanged(null) + DurationChanged(Zero) + PositionChanged(Zero)`** 让 VM 清屏（标题/封面/时长/进度全归零）。`PlaylistViewModel.UnloadCurrentTrack` 在清空队列/删当前曲时调用 |
| `Seek(TimeSpan)` | 写 `reader.CurrentTime` 后**主动广播 PositionChanged**（Phase 3：暂停态下 PollPositionAsync 已退出，否则进度条不刷新，看上去像"没跳转"）；通过 `ClampToDuration` 截到 [0, TotalTime] |
| `Volume { get; set; }` | `Math.Clamp(0..1)`；运行时写入 `VolumeSampleProvider.Volume` |
| `PollPositionAsync` | `Play()` 时以 `PeriodicTimer(33 ms)` 启动（fire-and-forget）；每 tick 检查播放会话代际（`_playSession`）、当前链与 `_state == Playing`，暂停/停止/切歌/卸载后最多一个 tick 退出；`PlaybackState` 不再是循环条件 · Phase 3：经 `ClampToDuration` 截断，避免解码器尾部浮点越界 |
| `OnPlaybackStopped` | 区分 (1) 异常 → `PlaybackError`；(2) 自然播完（距 `TotalTime` ≤ 200ms） → `TrackEnded`；(3) 用户 `Stop` → 仅 `Stopped`。Phase 30：先 `ReferenceEquals(Volatile.Read(ref _current), chain)` 判当前链，再判 `chain.StopRequested`；全程不取 `_chainGate` |
| `SpectrumConfig { get; set; }` | Phase 13：运行时可更新；setter 把 `Enabled` 传播到 in-flight `SampleAggregator`（禁用后分析线程不再取帧，不空耗 CPU） |
| `EqualizerConfig { get; set; }` | Phase 14：运行时可更新；setter 存字段 + `_current?.Equalizer.Update(value)` 把配置实时下发到在链 EQ provider（语义对齐 `SpectrumConfig`；链未建时仅存字段，装载安装时按当前字段重放） |
| `SpectrumDataAvailable` 事件 | Phase 13：`SampleAggregator.SpectrumDataReady`（Phase 30 起在后台分析线程 `DPlayer.SpectrumFft`，此前为音频线程）→ `OnSpectrumDataReady` 经 `_syncContext.Post` 封送到 UI 线程再广播 |
| `Dispose()` | Phase 30：在 `_chainGate` 内摘下链、递增 `_loadCancelVersion`，**锁外**执行 `PlaybackChain.Dispose()`（解订阅频谱 → 解订阅 `PlaybackStopped` → 停/释放输出 → `SampleAggregator.Dispose()` join FFT 线程 → 释放源）；`Unload` 形状相同并额外广播 `TrackChanged(null)`/`Duration=0`/`Position=0`/Stopped |

### 5.2a `Services/SampleAggregator` + `Models/SpectrumConfig`（Phase 13）

`SampleAggregator` 是实现 `ISampleProvider` 的**透明中间件**，插在 `EqualizerSampleProvider` 与 `VolumeSampleProvider` 之间：原样传递 PCM（`Read` 返回源读取数），同时把样本写进 SPSC 环形缓冲供后台线程做 FFT（Phase 30 起；Phase 13–29 是在音频 `Read` 内直接 FFT）。

处理管线（分析线程每跨过一个 hop 触发一帧）：
1. **多声道混单声道**：立体声逐帧取各声道均值，避免声道相位干扰频谱（在音频线程写环时完成）
2. **取帧**：环中截至当前写索引的最后 `FftSize` 个**真实时域样本**（hop = `FftSize/2`）
3. **汉宁窗**：对 FFT 工作缓冲加窗（`0.5*(1-cos(2πj/(N-1)))`，表在构造时预计算）
4. **FFT**：`NAudio.Dsp.FastFourierTransform.FFT`，`SpectrumConfig.FftSize`（当前 **8192**，历经 1024→2048→8192 提升低频分辨率）
5. **对数频率分组**：20Hz–16kHz 按对数均分到 `BarCount`（**32**）条柱（人耳对低频更敏感；bin 区间构造时预计算）
6. **RMS + dB + gamma**：每桶取均方根幅度 → 增益 8x → `20*log10` 映射到 -60~0dB → 归一化 [0,1] → `pow(x, 0.8)` gamma 增强对比
7. **复制数组触发事件**：`SpectrumDataReady?.Invoke(_spectrumData.ToArray())`（每帧新快照，防止订阅者篡改共享缓冲区）

`SpectrumConfig`（不可变 record）：`Enabled` / `BarCount=32` / `Sensitivity`（0.5~2.0）/ `Smoothing`（0.0~0.95）/ `FftSize=8192`。`Enabled` 运行时可切（`NAudioPlaybackService.SpectrumConfig` setter 传播到聚合器）。

**线程边界：** `SpectrumDataReady` 在**后台分析线程**触发（Phase 30 起为 `DPlayer.SpectrumFft`；Phase 13–29 为 NAudio 音频线程）；`NAudioPlaybackService.OnSpectrumDataReady` 负责封送到 UI 线程。灵敏度增益 + 指数平滑在 `PlayerViewModel.HandleSpectrumData`（UI 线程）做，**不在此重复 32-bar 映射**（聚合器已完成）。Phase 30 的 producer/consumer 细节见 §9「音频热路径的 Phase 30 结构」。

### 5.2b `Services/EqualizerSampleProvider` + `Models/EqualizerConfig`（Phase 14）

`EqualizerSampleProvider` 是实现 `ISampleProvider` 的**透明中间件**，插在 `MediaFoundationReader.ToSampleProvider()` 与 `SampleAggregator` 之间（**在 SampleAggregator 之前** → Phase 13 频谱可视化反映 EQ 处理后的信号）。10 段图形均衡器：ISO 倍频程中心频率 31/62/125/250/500/1k/2k/4k/8k/16k Hz，每段 ±12 dB 峰值滤波（`BiQuadFilter.PeakingEQ`，Q≈1.1 约一个倍频程带宽）+ preamp（−12~+12 dB）。

关键实现点：
1. **插入点（LoadAsync 建链）**：`ToSampleProvider → EqualizerSampleProvider → SampleAggregator → VolumeSampleProvider → WasapiPlayer`（Phase 19 起链尾是 `WasapiPlayer`）；EQ 在频谱聚合器之前，故频谱与 EQ 后听感一致。
2. **每声道独立滤波**：`BiQuadFilter?[][] _filters`，索引 `[channel][band]`；立体声左右声道各持一组滤波器，避免共享实例串扰滤波状态（x1/x2/y1/y2 延迟线）。
3. **preamp 线性增益**：`EqualizerConfig.PreampLinearGain = 10^(PreampDb/20)`，在逐级滤波前对样本统一缩放（多段提升时留余量防削波）。
4. **`Update` 就地 `SetPeakingEq`**：运行时改配置**不重建** `BiQuadFilter`，而是对已有实例调 `SetPeakingEq(...)` 就地重算系数；首次（slot 为 null）才 `PeakingEQ` 新建。**Phase 30 校正：`SetPeakingEq` 在 NAudio 3.3.0 会清零 x1/x2/y1/y2**（独立探针 0.5 → 0），Phase 14 起"保留状态、拖动无爆音"的说法是误记；保留的语义只有"缓冲边界换系数、不重建对象"，状态保持属后续候选。
5. **Nyquist 旁路**：中心频率 ≥ 采样率一半（`sampleRate/2`）的频段置 null slot 旁路 —— PeakingEQ 在 ≥Nyquist 时不稳定（如 44.1kHz 采样下 16kHz 段接近奈奎斯特，低采样率素材则直接旁路）。
6. **无锁配置下发（Phase 30）**：`Update`（UI 线程）只 `Volatile.Write` 一份不可变 `EqualizerConfig` 引用；音频线程每次 `Read` 开头 `Interlocked.Exchange(ref _pendingConfig, null)` 取走并在缓冲边界应用，**不再有 buffer 粒度 `lock`**。`_enabled` 用 `volatile bool` 且在配置应用时更新；0 dB（单位增益）频段不进级联（`SetPeakingEq` 复位语义下与全段级联在浮点容差内等价）。
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
- **`PlaylistFileEncoding`**（internal static）：字节 → 文本。探测链：**BOM（EF BB BF → UTF-8；FF FE → UTF-16LE；FE FF → UTF-16BE）→ 无 BOM 用"非法字节即抛"的严格 UTF-8 试解码 → `DecoderFallbackException` 回退 GBK(936)**。传统 `.m3u` 在中文 Windows 上多为 ANSI/GBK，只按 UTF-8 读会让整表中文路径变成 U+FFFD 进而被"文件缺失"过滤掉。CodePages provider 在**静态构造函数**注册（`Encoding.RegisterProvider` 幂等）—— `Encoding.GetEncoding(936)` 只出现在本类内部，注册永远早于解码，不依赖 App 启动顺序；csproj **不需要**加 `System.Text.Encoding.CodePages` 包（net11.0 仍框架隐含，显式引用触发 NU1510）。**Phase 28 实测结论**：net11.0 ref pack（`Microsoft.NETCore.App.Ref/11.0.0/ref/net11.0`）含 `System.Text.Encoding.CodePages.dll`，两个 csproj 均无该包引用，门禁 1 零警告（无 NU1510）；两条 GBK 单测各 1/1、GUI 端到端导入 GBK `.m3u` 中文路径无乱码（接受 2 / 缺失 0）——域 A 的 CodePages 风险在 net11.0 未触发。
- **`M3uParser`**（internal static）：逐行取"非 `#` 开头的非空行"；`#EXTM3U` / `#EXTINF` / 注释一律忽略（标题与时长只信 ATL 从音频文件读到的结果）。
- **`PlsParser`**（internal static）：INI 风格，只取 `File<N>=` 的值（key 大小写不敏感），**按出现顺序**返回而非按序号排序；段头 / `#`、`;` 注释 / `Title<N>` / `Length<N>` / `NumberOfEntries` / `Version` 全部忽略。
- **`PlaylistImportResult`**：服务层结果 record —— `SuggestedName`（列表文件名去后缀；取不到名时 "导入的歌单"）/ `AcceptedPaths`（已归一化为绝对路径，保留原顺序与重复，**不去重**）/ `TotalEntries` / `SkippedMissing` / `SkippedUnsupported`（三个计数互斥，一个条目只进一个桶）。
- **`PlaylistFileService`**：门面实现（Singleton，无状态），`ImportAsync` 编排：读全部字节 → `PlaylistFileEncoding.Decode` → 按后缀选解析器（`.pls` → PlsParser，其余 → M3uParser）→ `Classify`（URL 前缀 `http://`/`https://`/`mms://`/`rtsp://` → unsupported；相对路径以**列表文件所在目录**为 base 经 `Path.GetFullPath` 归一化，同时消掉 `..\`；后缀判定**先于**存在性判定，白名单引用 `AudioConstants.AudioExtensions`；`File.Exists` 为假 → missing）。
- **`M3u8Writer`**（internal static）：`Track` 列表 → extended M3U8 文本。形状：首行 `#EXTM3U`，每条两行 `#EXTINF:{秒},{Artist - Title}` + 绝对路径；UTF-8 **无 BOM**、`\r\n` 行尾。秒 = `(int)Math.Round(Duration.TotalSeconds)`，未知（≤ 0）写 `-1`；Artist 为空只写 Title（不留悬空 `" - "`）；Title 也为空写文件名；标题里的 `,` 无需转义（`#EXTINF` 只按第一个逗号切分）。

**读侧不抛 / 写侧抛的不对称（接口契约）**：`ImportAsync` 把 IO/权限/非法路径异常全部吞掉退化为空结果（用户仍有"没有可导入的条目"反馈而不是崩溃）；`ExportAsync` 必须让异常冒到 VM 转成错误文案，由 View 用 `ConfirmDialog.ShowError` 弹出。**不要**把 `ExportAsync` 包成不抛。

### 5.4 `ViewModels/MainViewModel`（Strict Facade）

当前公开面（Phase 6 起；Phase 24 增加库视图与导航状态）：

```csharp
public PlayerViewModel Player { get; }
public PlaylistsViewModel Playlists { get; }
public LibraryViewModel Library { get; }
public bool IsLibraryViewActive { get; }          // ObservableProperty
public void ShowLibrary(LibraryPage page);        // 艺术家/专辑页，置 IsLibraryViewActive=true
public void ShowPlaylist();                       // 切回队列视图
public Task<string?> InitializeAsync();           // 返回非 null = 断点恢复被跳过时的提示文案
public Task CleanupAsync();
```

构造由 DI 注入 `(PlayerViewModel, PlaylistsViewModel, LibraryViewModel, IPlaybackService, IPlaylistService, ISettingsPersistence)`。`InitializeAsync` 顺序固定：读 queue.json → `Hydrate`（含库播放队列）→ 订阅 `StateChanged` → `Library.InitializeAsync()` + `ApplyCachedMetadataToLibraryPlayback`（失败只留空库，不影响后续）→ 断点续播就位 → 两路后台扫描（文件夹歌单 + 音乐库）。debounce save 500ms 仍集中在本类（`StateChanged` → `ScheduleSave` → `SaveAfterDelayAsync`）。

`CleanupAsync()` 顺序固定：先 `Library.CaptureNavigation()` 记录导航快照 → 同时启动 `Library.CleanupAsync()` 与 `Playlists.CleanupAsync()`（封闭入口、取消扫描/保存并等待排空）→ 解绑 `StateChanged` → 取消 debounce → flush in-flight save → `BuildSnapshot` + `SaveAsync` → 遍历可见歌单 `PlaylistVM.Cleanup()` → `Playlists.LibraryPlayback?.Cleanup()` → 写入库导航状态 → `await Player.CleanupAsync()` → `_player.Dispose()`。**必须先解绑后 Dispose**，避免事件 handler 在底层资源销毁后被回调。`CleanupAsync` 幂等：重复调用返回同一个 Task。

**架构不变量（COUPLING.md §5）：** `PlayerViewModel` 与 `PlaylistViewModel` **互不持引用**，仅共享 `IPlaybackService` Singleton；本 Facade **不转发子 VM 的成员**，公开面只允许三类：(1) 子 VM 本体 `Player` / `Playlists` / `Library`；(2) 本类自己的生命周期 `InitializeAsync` / `CleanupAsync` / `DisposeAsync`；(3) 本类自己持有的壳级状态及其入口 `IsLibraryViewActive` / `ShowLibrary` / `ShowPlaylist`（导航状态存在 `MainViewModel`，不写进任何子 VM、也不从子 VM 读取）。在此之外再加成员——尤其是把子 VM 的属性/命令转出来——就倒退为"转发 Facade"反模式。

### 5.4a `ViewModels/PlayerViewModel`（Transport 子 VM）

源生成器属性：`_isSeeking`, `_position`, `_duration`, `_playState`, `_currentTrack`, `_albumArtBytes`（Phase 7：byte[]，非 BitmapImage）, `_volume`, `_isMuted`；**Phase 13 频谱**：`_spectrumData`(float[32]), `_spectrumEnabled`, `_spectrumSensitivity`, `_spectrumColorTheme`, `_spectrumSmoothing` + 私有 `_smoothedSpectrum`；**Phase 14 均衡器**：`_equalizerEnabled`（供 PlayerBar 🎚 按钮激活态高亮）。派生：`SampleRateText`、`PositionNormalized`（0..1）、`SpectrumColorThemes`（["紫色","蓝色","绿色","彩虹"]）。音量/静音图标由 View 层转换器提供，VM 不再暴露图标字符串。

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

### 5.4b `ViewModels/PlaylistViewModel`（队列子 VM / Phase 4 持久化 + PlayCurrent / Phase 10 SourceFolder + IsScanning / Phase 12 排序 + ImportFolderToCurrent / Phase 18 导入导出）

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

构造时订阅 `IPlaybackService.TrackEnded` 用于自动推进（**仅 `IsActivePlaylist` 的订阅者有权推进**，其余歌单在入口直接返回）；`Queue.CollectionChanged` 触发 Next/Prev/PlayCurrent 命令 `NotifyCanExecuteChanged`；构造尾段从 seed 恢复队列（`File.Exists` 过滤 + `MapCurrentIndexAfterFilter` 重映射）。

`[RelayCommand]`：`AddToQueue / RemoveTrack(int) / ClearQueue / PlayTrackAt(int) / NextTrack / PrevTrack / PlayCurrent` / `DropExternalFiles(IReadOnlyList<string>)`（Phase 5） / `MoveTracks(MoveTracksArgs)`（Phase 5） / `ImportFolderToCurrent`（Phase 12 continued）。

`ToRecord()`：把当前状态打包成 `Playlist` record（`ShuffleEnabled=false` / `RepeatMode=Off` 占位，实际全局状态由 `PlaylistsViewModel.BuildSnapshot` 负责）。

**Phase 5 新增成员：**
- `[RelayCommand] DropExternalFiles(IReadOnlyList<string> paths)`：与 `AddToQueue` 同语义入队管线（读元数据 → Queue.Add），不触发播放。路径白名单过滤由 View 层 `DragDropExtensions.FilterAudioPaths` 提前完成，VM 信任入参。
- `[RelayCommand] MoveTracks(MoveTracksArgs args)`：队列内重排算法（spec §4 八步算法）。**入口先 `_playToken++`** 顶替 in-flight `PlayTrackAtAsync`（同时偿还旧债 #5）。算法用对象身份（`ReferenceEquals` + `ReferenceEqualityComparer.Instance`）回找 `CurrentIndex` 与 `_shuffleHistory`，不做索引算术 —— Track record 的结构相等会让 `Queue.IndexOf(currentTrackObj)` 在出现重复占位时返回首个结构等价匹配而非原始那一个。不调 `_player.Unload()`，重排不中断播放。
- `RemoveTrack` 入口加 `_playToken++`（Phase 5 顺带还债 #5）：以前删除非当前曲不顶替 token，能让 in-flight `Queue[index] = meta` 写到错位；现已关闭。

**关键私有方法**（与旧 MainViewModel 等价）：
- `PlayTrackAtAsync(int, int skipCount=0)`：抢占 `_playToken` → **把 `Container.CurrentPlaylistId` 指向本歌单（归属同步）** → 读元数据 → `Queue[i] = meta` → `LoadAsync` → `Play`；每个 `await` 后校验 token，被顶替则静默退出；失败连续 3 次自动停止
- `CalculateNextIndex(int? failedIndex)`：纯算法，按 (Shuffle × RepeatMode) 4 种组合返回下一索引；Shuffle 用 `_shuffleHistory` 排除已播
- `HandleTrackEnded()`：入口先判 `IsActivePlaylist`（非播放歌单直接返回）；RepeatOne 重播当前，否则走 `CalculateNextIndex`
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

**Phase 24 新增成员（隐藏库播放队列）：**
- `public const string LibraryPlaybackId = "__library-playback__";` —— 保留 Id；`LibraryPlayback` 是私有字段 `_libraryPlayback` 的只读投影，**永远不加入 `Playlists` 集合**（侧栏拖拽重排按 `ListBox` 索引调用 `MovePlaylistCommand`，多塞一个隐藏项会破坏索引映射）
- `PlayLibraryTracksAsync(IReadOnlyList<Track> tracks, int startIndex, string contextName)` —— 懒创建/复用库队列（`Name` 用上下文名如专辑名，缺省 `音乐库播放`），替换队列内容、保留全局 Shuffle/Repeat、`CurrentPlaylistId` 指向库队列、不改 `ViewedPlaylist`，随后 `PlayTrackAtAsync(startIndex)` 出声；队列变化经既有 `StateChanged` → debounce 写 `queue.json`
- `IsLibraryPlaybackActive` —— `_libraryPlayback is not null && CurrentPlaylistId == _libraryPlayback.Id`；变化时 `OnPropertyChanged`，侧栏据此显示「音乐库」旁的 ▶
- `Hydrate` / `BuildSnapshot` —— 快照的 `QueueState.LibraryPlayback` 与隐藏队列互转；Hydrate 恢复库队列时**跳过**逐首读标签（启动不读盘），由 `ApplyCachedMetadataToLibraryPlayback(cache)` 用库缓存按路径回填标题/艺术家/时长/采样率/曲目号
- `CurrentPlaylist` 解析顺序：先库队列（Id 匹配时）→ 再可见歌单；`RecomputeIsActiveFlags` 同时覆盖库队列；`FindPlaylistTrackByPath` 也能找到库曲目（断点续播）
- 边界：删除最后一个可见歌单自动重建默认歌单时，若库队列正在播放**不**顶掉 `CurrentPlaylistId`

### 5.4d `ViewModels/LibraryViewModel`（Phase 24/25）

多根音乐库视图模型（Core、WPF-free、DI Singleton）。公开面：

```csharp
public ObservableCollection<LibraryArtist> Artists;
public ObservableCollection<LibraryAlbum> Albums;
public LibraryPage Page;                              // Artists / Albums / ArtistDetail / AlbumDetail / Search
public LibraryArtist? SelectedArtist;  public LibraryAlbum? SelectedAlbum;
public bool IsScanning; public string StatusText; public int UnavailableRootCount;
public bool HasLibraryRoots; public bool IsEmpty; public string EmptyStateText;
public string SearchQuery; public LibrarySearchResults SearchResults; public string SearchMessage;
public Task InitializeAsync();          // 读 settings → 逐根读缓存 → 建分组（不读标签）
public Task ScanInBackgroundAsync();    // 逐根 ScanFolderWithStatus → ComputeDiff → 只读 added 的标签 → 写缓存
public Task RefreshAsync();             // 强制重扫
public Task RefreshIfRootsChangedAsync(); // 设置保存后仅在根集合变化时重扫
public Task StartMonitoringAsync();     // WPF 壳显式启动 watcher
public Task CleanupAsync();             // 可等待排空，由 MainViewModel.CleanupAsync 调用
public void ShowArtists(); public void ShowAlbums();
public void OpenArtist(LibraryArtist); public void OpenAlbum(LibraryAlbum, bool fromArtist); public void Back();
public Task PlayAlbumAsync(LibraryAlbum); public Task PlayArtistAsync(LibraryArtist);
public Task PlayAlbumTrackAsync(LibraryAlbum, int index); public Task EnsureCoverAsync(LibraryAlbum);
public Task UpdateSearchAsync(string query); public void ClearSearch();
public void OpenSearchArtist(LibraryArtist); public void OpenSearchAlbum(LibraryAlbum);
public Task PlaySearchTrackAsync(Track);
public LibraryNavigationSnapshot CaptureNavigation();
public IReadOnlyList<PlaylistViewModel> Playlists;   // Phase 26：对话框选择集（容器集合的只读视图）
public bool AddTracksToPlaylist(PlaylistViewModel, IReadOnlyList<Track>); // Phase 26：转发容器规则
public PlaylistViewModel? AddTracksToNewPlaylist(string?, IReadOnlyList<Track>);
public IReadOnlyDictionary<string, LibraryCacheEntry> CachedEntriesByPath;
```

- 分组（`LibraryGrouping`，internal）：专辑 key = `(Album, 所在文件夹)`（空专辑 → `未知专辑`；同文件夹同专辑名不同 Artist 合并；同名不同文件夹是两张专辑）；专辑显示艺术家 = 曲目 Artist 唯一时显示它，否则 `多位艺术家`；艺术家列表按逐曲 `Track.Artist` 独立建（合辑的每位贡献者保留自己的艺术家页，并能看到该合辑）；曲目按 `TrackNumber ?? MaxValue` → `Title` → `FilePath`；未知组排最后。
- 扫描并发：根代际（`LibraryRefreshScheduler.RootEpoch`）、扫描代（`Generation`）、查询版本（`_queryVersion`）与库数据版本（`_libraryVersion`）分别失效；生命周期 CTS 让 `CleanupAsync` 取消初始化/封面任务。晚到结果在投递前与 UI 回调内都要复核，过期结果不写 UI、不写缓存。不可用根（`ScanFolderWithStatus` 返回 `ScanUnavailable`）跳过而不当空扫描，避免把离线盘/无权限目录的缓存清空。
- 封面：按专辑首曲读一次 `AlbumArt`（`SemaphoreSlim` 串行），只对用户看得到的 tile/详情头部 `Loaded` 触发；重建库时旧结果丢弃。
- 播放入口只调用 `PlaylistsViewModel.PlayLibraryTracksAsync`，不直接碰 `IPlaybackService`（库 VM → 播放容器单向依赖）。
- 库 → 歌单（Phase 26）：追加只允许普通歌单（文件夹歌单拒绝）、允许重复；新建路径不设置 `ViewedPlaylist` / `CurrentPlaylistId`，避免库视图下侧栏出现高亮歌单。两条入口都只转发容器规则，不复制守卫。

### 5.5 `Views`

- **`MainWindow`**：三行 Grid —— Row 0 `TitleBar`（Phase 17 自绘标题栏）+ Row 1 `ContentGrid`（5 列：Sidebar | Splitter | Playlist | Splitter | TrackInfo）+ Row 2 `PlayerBar`（底部，自适应高度）；Phase 17 起 `WindowStyle=None` + `WindowChrome` 无边框。`SidebarCol` 和 `TrackInfoCol` 各限制为窗口宽度一半（`ContentGrid_SizeChanged` + `DragDelta` 中到达上限直接锁死）。构造时同步读取窗口尺寸（`GetAwaiter().GetResult()`，启动阻塞 < 几 ms 可接受）；若持久化的 `WindowHeight < 500`（Phase 1 旧值）则一次性迁移到 650，避免列表不可见。关闭时采用 **cancel-and-close 模式**（Phase 4）：首次进入 `e.Cancel=true` + `_isClosing=true`，跑完 settings 写盘与 `MainViewModel.CleanupAsync()`（内部 flush queue 与库导航状态）后调 `Close()` 重新触发 Closing 直接放行；这是为了让 `async void` 多 await 链不被 `Application.Shutdown → Dispatcher.InvokeShutdown` 截断。
- **`PlayerBar`** *(UserControl)*：播放栏。两行 Grid：①`Position | Slider | Duration` 进度条；②⏮ ▶/⏸ ⏭ 🔀 ⇄/🔁/🔂 按钮组（居中）+ 🔊音量 + 🎚均衡器（Phase 14）+ ⚙设置（右对齐）。封面/信息已拆到 `TrackInfoView`。Phase 16：上述图标全部改为 `Themes/Icons.xaml` 矢量 `Path`（活跃态经 `BoolToAccentBrushConverter` 着 accent 色；音量/静音由 `BoolToVolumeIconConverter` 提供）。
  - Slider 的"单击跳转"由 `PreviewMouseLeftButtonDown` 手动从 `PART_Track` 计算比例并触发 `SeekCompletedCommand`；点击 Thumb 时不触发（通过 `FindAncestor<Thumb>` 检测，转交给原生 `DragStarted/DragCompleted`）。Thumb 默认 8px 圆点半透明，悬停/拖拽放大到 14px 不透明
  - `⏮` / `⏭` 通过 `{Binding DataContext.Playlists.CurrentPlaylist.<XxxCommand>, RelativeSource={RelativeSource AncestorType=Window}}` 跨级绑定到当前播放歌单（2026-10-24 起不跟随查看项）；`🔀` / `⇄/🔁/🔂` 绑到 `Playlists.ToggleShuffleCommand` / `Playlists.CycleRepeatCommand`
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
- **`PlaylistsSidebarView`** *(UserControl, Phase 6/9/10/12 continued/18)*：左侧歌单栏。`+` 按钮直接创建新歌单（Phase 12 continued 移除 ContextMenu 子菜单）；`-` 按钮删除选中歌单；**Phase 18 第三个 28×28 图标按钮**（`Icon.Import`，ToolTip「导入播放列表（M3U / M3U8 / PLS）→ 新建歌单」）→ `PlaylistImportUi.RunDialogAsync(_vm.ImportPlaylistFileAsync, ...)`（容器级 = 新建歌单）；ListBox 支持双击重命名、拖拽重排（Phase 9）。`▶` 标记由 `IsActivePlaylist` DataTrigger 驱动。文件夹绑定歌单显示 📂 图标 + 🔄 扫描指示。**Phase 18 拖拽**：`PlaylistList_DragOver/Drop` 接受外部 `.m3u/.m3u8/.pls`（`Effects = Copy`，**不显示重排插入线**——这不是重排；音频文件落在侧边栏无意义，不认），内部重排格式（`PlaylistItemsFormat`）优先；多个列表文件逐个导入（每个文件一个歌单）聚合成一份报告。导出**不放侧边栏**：侧边栏按钮作用于"选中项"，导出语义是"当前查看的歌单"，两个指针在键盘导航下可能不同步。Phase 24 起顶部固定「音乐库 / 艺术家 / 专辑」节点；2026-11-08 起监听 `MainViewModel.IsLibraryViewActive` 与 `Library.Page` 同步节点高亮（搜索态保留来源节点）。
- **`LibraryView`** *(UserControl, Phase 24/25/26)*：中间库视图。五页：艺术家列表 / 专辑网格 / 艺术家详情 / 专辑详情 / 搜索；搜索框常驻（Ctrl+F、200ms 去抖、清空/返回快照）；详情页按上下文播放全部、双击/Enter 播放、行首 ▶ 仅在库队列为当前上下文且路径匹配时显示；封面按 tile/详情 `Loaded` 惰性读取；600px 窄窗下详情按钮移至封面行下方、播放栏音量条 100→48px；搜索提示与输入框对齐，清空按钮使用次级按钮样式。Phase 26：五处右键菜单（专辑卡片 / 艺术家详情专辑 / 搜索结果专辑 / 曲目行 / 专辑详情头部）→ `AddToPlaylistDialog`；菜单显式使用本地 `LibraryContextMenuStyle`（全局 `MenuItem` 模板不会进入 `ContextMenu` 的 Popup，直接依赖会出现系统白色图标栏遮住文字）。
- **`TrackInfoView`** *(UserControl, Phase 12 continued/13)*：右侧曲目信息面板。`DataContext = PlayerViewModel`。`BackgroundSecondary` 背景 + 圆角 Border。两行 Grid：Row0（`*`）曲目信息 —— 封面用 `Viewbox MaxWidth/MaxHeight=250` 包裹自动缩放（内含 `Border` 180×180 + `Image Stretch="UniformToFill"`），文本元数据（标题/艺术家/专辑/采样率）居中，无曲目时 DataTrigger 显示"播放曲目以查看信息"占位；Row1（`Auto`）**Phase 13 `SpectrumView`**（高 120px，绑 `SpectrumData`/`SpectrumColorTheme`，`Visibility` 绑 `SpectrumEnabled`）。封面 Border 加 `ClipToBounds=True` 圆角裁切（ViewBox 缩放后内容溢出问题）。
- **`SettingsDialog`** *(Window, Phase 11/13/24)*：设置对话框。模态 ToolWindow（**420×660**）；三行 Grid（标题栏 / 可滚动内容 / 固定按钮），内容含通用音量、音频输出只读占位、音乐库根目录（列表 + 添加/移除）、频谱启用/灵敏度/主题/平滑度；EQ 在独立 `EqualizerDialog`。静态 `Show(Window?, ISettingsPersistence, IPlaybackService, PlayerViewModel?)` 工厂。构造注入 `IPlaybackService`（音量滑块实时调 `_playbackService.Volume`）+ 可选 `PlayerViewModel`（保存后即时同步频谱属性，免重启）。OnLoaded async 读盘加载音量 + 频谱设置并绑定滑块 ValueChanged 实时更新数值标签；Save_Click 通过 `UpdateAsync` 原子写盘后同步 VM —— **故意不 `ConfigureAwait(false)`，留在 UI 线程**才能直接写 `PlayerViewModel` 属性；失败弹 MessageBox（Phase 13）。Phase 17：`WindowStyle=None` + `WindowChrome` + `TitleBar(ShowMaximize=False)`（仅关闭键）。
- **`EqualizerDialog`** *(Window, Phase 14)*：均衡器对话框。复用 SettingsDialog 模式（静态 `Show(Window?, ISettingsPersistence, IPlaybackService, PlayerViewModel?)` 工厂 + 模态 `ShowDialog()`，返回 true = 用户保存）。模态 ToolWindow（**480×400**），三行 Grid：Row0 启用 CheckBox + 预设 ComboBox；Row1（`*`）频段滑块区 `BandsPanel`（UniformGrid）；Row2 恢复 Flat + 取消/保存。
  - **11 根竖直滑块由 code-behind 动态构建**：`BuildBands()` 在 `BandsPanel` 里生成 10 段 + preamp 共 11 列（每列：dB 值标签 / 竖直滑块 / 频率标签，`MakeSliderColumn` 三行 Grid）。滑块绑 `EqBandSlider` 样式（Min −12 / Max 12）
  - **自定义 `EqBandSlider` 竖直模板**：`Controls.xaml` 的隐式 Slider 模板是**横向专用**（Height=20 + 填充条 VerticalAlignment=Center），竖直滑块直接用会渲染错位；故对话框资源里自定义 `EqBandSlider`（`Orientation=Vertical` + Width=24 + 填充条 HorizontalAlignment=Center + 14px 圆形 Thumb）
  - **实时预览**：`BandSlider_ValueChanged` → `RefreshLabels()` 更新数值标签 + `EqualizerPresets.Match(CurrentGains())` 回推预设名（手动偏离即 Custom）→ `PushToService(preset)` 把 `EqualizerConfig.Create(...)` 写 `_playbackService.EqualizerConfig`（拖动即时听感）；`PresetCombo_SelectionChanged` 选预设→`ApplyGains` 写滑块 + 实时下发；`RestoreFlat_Click` 恢复平直（preamp=0）
  - **`_suppress` 拦截程序化回推**：程序设滑块/下拉时用 `_suppress` 窗口包住，避免 `ValueChanged`/`SelectionChanged` 回推；**尤其 `OnLoaded` 填充预设下拉必须在 `_suppress` 内** —— WPF ComboBox 向空集合添加首项会自动选中 index 0 并触发一次 `SelectionChanged`（Phase 14 code review 拦下的 Critical）
  - **取消回滚**：`_initialConfig` 取“打开瞬间的实时 `_playbackService.EqualizerConfig`”（权威，不受 LoadAsync 失败影响），再试读盘覆盖；`OnClosed` 若未 `_saved` 则把 `_playbackService.EqualizerConfig = _initialConfig` 撤销实时预览（回滚基准非二次读盘 —— 读盘失败会把基准误置为禁用平直）
  - **Save_Click**：`UpdateAsync` 原子写 settings.json（EqualizerEnabled/Preamp/Bands/Preset）→ 再确认一次链上配置 → 写 `PlayerViewModel.EqualizerEnabled`（按钮高亮即时更新）—— **故意不 `ConfigureAwait(false)`，留在 UI 线程**；失败弹 MessageBox
  - **Phase 17**：`WindowStyle=None` + `WindowChrome` + `TitleBar(ShowMaximize=False)`（仅关闭键）；标题栏占 Row 0，频段区/按钮区行号顺延
- **`AddToPlaylistDialog`** *(Window, Phase 26)*：库 → 歌单选择对话框。模态 ToolWindow（**360×460**），深色 WindowChrome + `TitleBar(ShowMaximize=False)`；列出可见歌单（名称 + 当前曲目数），文件夹歌单置灰并注明「文件夹歌单 · 由文件夹管理」，普通歌单双击/Enter/「加入」确认；预填「新歌单」+「新建并加入」，空名回落「新歌单」；`Show(Window?, IReadOnlyList<PlaylistViewModel>, int)` 返回 `AddToPlaylistResult(Target, NewPlaylistName)`，对话框本身不修改歌单或选中态。
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

### 5.7 `D-player.WinUI`（Phase 23 频谱/EQ 实现完成，验收通过 2026-11-02）

- 构造链：App 解析同一容器的 `IPlaybackService`，传入 `MainWindow(vm, settings, playback)`，再构造 `PlayerBar(player, playlists, playbackService, settings)`；不得新建第二份播放服务。
- `SpectrumView(player)` 位于 InfoPanel 末尾（120px），Canvas + 32 Rectangle；Loaded 订阅 Rendering/PropertyChanged，Unloaded 对称退订。逐帧只插值高度并同步 Top 保持底边，不逐帧建柱或画刷；尺寸变化才重布局，非正柱宽跳过。紫/蓝/绿/彩虹配色复用 WPF 频谱定义，属 Phase 23 可视化特例。
- 暂停守卫：Paused/Stopped 将目标置 2px；Loaded 非 Playing 不读取旧频谱；SpectrumData 通知只有 Playing 才更新，屏蔽暂停后晚到事件。A1 修复复测见 Task1 报告，不能用它替代最终组合回归。
- 设置新增启用、灵敏度 0.5–2、四主题、平滑度 0–0.95；初始化 `_ready` 守卫，之后写 VM 既有持久化链。
- EQ：底栏描边入口、10 段 + Pre（±12dB）、9 预设 + Custom、Flat 归零。打开时深拷贝实时 `IPlaybackService.EqualizerConfig`，不以 `LoadAsync` 的磁盘值覆盖。预览只写 service，通过 `PreviewChanged` 更新壳侧高亮；取消/未保存关闭恢复实时快照，不写 VM、不主动持久化。
- **计划勘误（保留原计划不改）**：计划 Task2 的预览/取消写 `Player.EqualizerEnabled` 是错误路径，Core setter 会下发并落盘。当前实现只在完整配置 `UpdateAsync` 成功后同步 VM；启用值变化时 VM setter 仍可能追加同值持久化，不能称为单次写盘。保存前冻结全部值，保存中防编辑/重复保存/ESC/关闭；失败留窗、内联提示并记 AppLog；主窗清理前有 EQ 保存守卫。失败/慢存储竞态尚无动态验证。
- EQ 由 PlayerBar 直接管理 ContentDialog 与事件退订，不经通用 `WinUiDialogService`；[验收记录](./PHASE23-ACCEPTANCE.md) 区分实现证据、历史 UI 证据与未验项。

WinUI 3 / Windows App SDK **2.6.5**（钉具体版本以保证可复现构建），unpackaged（`WindowsPackageType=None`）+ `WindowsAppSDKSelfContained=true`，TFM `net11.0-windows10.0.19041.0`，`<Platforms>x64</Platforms>` + `<Platform>x64</Platform>`（后者是必需的：`Platforms` 只声明支持面、不设默认值，裸 `dotnet build` / `dotnet run` 会因求值出的 `Platform=AnyCPU` 被 WASDK 的 `WindowsAppSDKSelfContained.targets` 直接拒掉）。**只消费 Core 的公开成员，不反向引用 WPF 壳，也不进 `D-player.slnf`。**

- `App.xaml.cs`：**构造函数里 `RequestedTheme = ApplicationTheme.Dark`**（spec §4.4 要求的那句，最终修复波 A1 才补上；不设 = 跟随系统主题，浅色 Windows 上两壳会像两个产品。类型注意：WinUI 3 的 `Application.RequestedTheme` 是 `ApplicationTheme`（只有 Light/Dark），不是 FrameworkElement 上的 `ElementTheme` —— 写错是 CS0266）。之后在 UI 线程 `AddDPlayerCore(configuration, new DPlayerDataPaths { FolderName = "D-player-winui" })` + `AddSingleton<IFileDialogService, WinUiFileDialogService>()` → 解析 `MainViewModel`、`ISettingsPersistence` 与同一 `IPlaybackService` → `new MainWindow(vm, settings, playback)` → `Activate()` → `_ = StartAsync(vm)`（水化 + 断点续播就位）。刻意**不**同步 `Dispose` 容器（见 §4.3 第 31 条）。Phase 22 起构造函数里先 `AppLog.Initialize(WinUiAppPaths.LogDirectory)` 并写一条 `WinUI shell starting`，再挂 `Application.UnhandledException` / `AppDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException`（只记录，不设 `Handled`）；`OnLaunched` 订阅 `IPlaybackService.PlaybackError` 写 `PlaybackError: ...`；`StartAsync` 的恢复跳过/异常改走 `AppLog`（不再是 `Debug.WriteLine`）。
- `MainWindow.xaml(.cs)`：三行布局 —— 自绘标题栏区（`ExtendsContentIntoTitleBar` + `SetTitleBar`，`Title = "D-player"`）/ `NavigationView` 左栏 + 中区 `ListView` / 底部播放器栏。`SystemBackdrop = new MicaBackdrop()`（该类型在 WASDK 2.5.1/2.6.4 里都住 `Microsoft.UI.Xaml.Media`，不是 `...Composition.SystemBackdrops` —— 文件顶部有逐条删除实测的注释），根 `Grid` 必须 `Background="Transparent"`（§4.3 第 32 条）。左栏是 `PlaylistsViewModel.ViewedPlaylist` 的**投影**：`SyncPlaylistMenu` 的选中优先级固定为 `ViewedPlaylist` → 重建前已选项 → 第一项，并有 `_syncingMenu` 重入守卫（设 `SelectedItem` 会绕回 `SelectionChanged`）；`Playlists.PropertyChanged` 回灌左栏（Core 的 `Hydrate` 在 `Playlists.Add` 之后才恢复 `ViewedPlaylist`）。重建时机 = `RootGrid.Loaded` + `Playlists.CollectionChanged`（歌单是异步水化的，比 `Loaded` 晚）。Phase 22 追加：启动时 `RestoreWindowGeometry()`（物理像素；任一维低于 800×500 整对回落 1200×800；中心点不在任何 `DisplayArea` 工作区时主屏居中；`DisplayArea.FindAll()` 在本机运行时崩溃，改用 `GetFromPoint(center, None)`），关闭时仅在 `OverlappedPresenterState.Restored` 保存几何；`_cleanupCompleted` 让收尾期间的二次关闭继续取消、收尾后的自家 `Close()` 放行；空格守卫扩展到「对话框开着或焦点在文本输入控件」时一律不抢；通用弹窗经 `WinUiDialogService`（Phase 23 EQ 例外：由 PlayerBar 直接管理并单独纳入守卫）。
- 双击：`TrackList_DoubleTapped` **先在可视化树里解析被点中的 `ListViewItem`**（`e.OriginalSource` → `FindAncestor<ListViewItem>` → `container.Content as Track`；`ListView.ContainerFromPoint`/`ContainerFromElement` 在 WinUI 3 的托管投影里不存在，CS1061），解析不出来（列表下方的空白区）就**什么都不做** —— 与 WPF `PlaylistView.xaml.cs:162-166` 同纪律（A3；切片版读 `SelectedItem`，空白区双击会把上次选中的那首再播一遍）。命中后按**引用身份**回找索引（`Track` 是 record，`Queue.IndexOf` 会塌陷重复占位，同 COUPLING §5 那条纪律）→ `await Playlists.HandleDoubleClickPlay(pl, index)`（与 WPF 同一个入口，§4.3 第 30 条）。
- 播放器栏：**三个** `x:Bind OneWay` 投影属性（`PlayPauseGlyph` / `NowPlayingText` / `TimeText`）+ 本类自实现 `INotifyPropertyChanged`（不加就 WMC1506 且值不刷新）；进度条**不在投影之列**——它直接绑 Core 的 `PlayerViewModel.PositionNormalized`（A4：壳侧那份同公式的 `PositionFraction` 已删，WPF 的 `PlayerBar.xaml` 绑的也是同一个成员）。`▶` 在无当前曲时走 `ViewedPlaylist.PlayCurrentCommand`、有曲时走 `Player.PlayPauseCommand`（与 WPF 的 DataTrigger 同语义，只是改成代码判断）。上一首/下一首自 2026-10-24 起走 `Playlists.CurrentPlaylist`，不跟随查看项；按钮可用性按当前播放歌单的 `HasCurrentTrack`。**拖动语义与 WPF 一致（A2）**：WinUI 3 的 `Slider` 不暴露 `DragStarted`/`DragCompleted`（XAML 属性 WMC0011 / 附加属性 WMC0010 / C# 订阅 CS1061，三条路实测全堵；WASDK 2.5.1 的托管投影里**不存在** WPF 那个 `SliderBase` 类型，这对事件只在模板内部的 `Thumb` 上——本段旧版写的"`SliderBase` 不暴露"是拿 WPF 的类层次命名一个不存在的类型，已按台账 P-23 更正），故从 `Slider` 模板的 `Thumb`（`HorizontalThumb`）上订阅同一对事件 —— 拖动期间 `IsSeeking` 为真、30 Hz 回写被 Core 抑制，松手才提交一次 Seek；`ValueChanged` 只承担"值变了才提交 Seek"这一半并跳过"程序写回值"那条路径（`_suppressSeek` 已删）。**单击定位在 2026-10-07 曾被用户实测为缺陷，当晚已量出根因并修掉**：现象是单击轨道靠近开头的半边跳到开头、靠近结尾的半边跳到结尾，位置**不落到点击处**。**当时的机制只是一条标注为假设的解释（`RangeBase.LargeChange` 默认值在 0..1 上等于整个量程，"一页"步进顶到端点），这次实测把它否了**：活的 `PositionSlider` 上 `LargeChange=10`、`SmallChange=1`、`StepFrequency=1`、`SnapsTo=StepValues`，而滑块停在 `Value=0` 时点它右边（x=0.10/0.25/0.50）`Value` **一动不动**——翻页解释说不通。真正的机制是原生"按按下点算分数后按 `StepFrequency` 量化"：`StepFrequency=1` 配 `SnapsTo=StepValues`（该枚举只有 `StepValues`/`Ticks`，**没有 `None`，量化关不掉**）在 0..1 的量程上只剩 {0,1} 两个落点，分界正是几何中点；把 `StepFrequency` 单独改成 0.05/0.001，同一批点击立刻落到 0.25/0.5/0.75 与 0.248/0.499/0.749。**修法（纯壳侧，Core 未改）**：原生处理是 `Slider` 自己的**类处理器**，挂在 `Slider` 上的托管处理器看到 `Value` 时它已被改成端点；托管子类覆盖 `OnPointerPressed` 在这个原生控件上不被调用（且 `DefaultStyleKey=typeof(Slider)` 会让模板变形）。所以 `MainWindow.xaml.cs` 在**比 `Slider` 更深的模板轨道**（`HorizontalTemplate`）上挂 `PointerPressed`：`e.Handled=true` 吃掉原生那步，落点自己按实测几何算（`clamp((x - thumbWidth/2)/(trackWidth - thumbWidth))`，本机轨道 904 px / 滑块 18 px → 滑块中心可行程 9..895，与 `Value=0.1637` 时实测中心 154.1 吻合），照拖动同一条纪律按住 `IsSeeking` 后经现成的 `SeekCompletedCommand` 提交一次；按在 `Thumb` 上仍旧让给拖动那条路（实测 `Value` 不动）。XAML 另外显式给了 `StepFrequency="0.001"`（≈0.9 px 一格）——单击不再靠控件算，但**拖动**靠它：实测默认配置下同样的拖动手势也会停在端点，这条一并修掉了。仪器复量（曲目载入但没出声）：点行程 25/50/75% → `Value` 0.2483/0.4989/0.7494、`Position` 01:07.5395/02:15.693/03:23.8465，每次点击恰好一对 `IsSeeking` True→False；**播放中点下去声音是否跟着跳已由 2026-10-07 第二轮复验报掉：会落到点击处、播放也跟着跳过去**（逐项记录在对比材料 §2.2）。**没顺手改的残留**：进度条有键盘焦点时按 → / PageUp 仍旧一步到头（`SmallChange=1`/`LargeChange=10` 未动，记在对比材料 §5 收尾清单）。此前这里还写过"`IsMoveToPointEnabled` 默认生效"，那是 **WPF** 的属性、WinUI 3 的 `Slider` 上没有（引用即 CS1061），假依据早已撤走。**残留风险**：这两条挂接现在依赖**两个**模板部件名（`HorizontalThumb` 与 `HorizontalTemplate`），而 WinUI 仍不进 `.slnf`、门禁 3 只覆盖编译 → 部件改名的运行时退化没有自动回归能发现（对比材料 §5 关切 5；退化方向已变温和：轨道找不到时退回原生 move-to-point，因 `StepFrequency=0.001` 落点仍在点上，只是精度约一个像素，不再退回饱和到端点）。另记一条实测量到的实现坑：`Slider.FindName` 在本壳取不到原生模板部件名（实测返回 null，`HorizontalThumb` 一直靠"向下找第一个 `Thumb`"兜底），所以轨道按"部件名 → 已挂接滑块的可视化父节点"两级找。逐项验收结果见对比材料 §2.2。
- Phase 22 播放控制：底栏补齐为单行 64px——`PrevButton` / `NextButton` / `ShuffleButton` / `RepeatButton` / `MuteButton` / `VolumeSlider`（0.05/0.1 键盘步进）+ `SettingsButton`，宽 ≥1100 显示音量滑块、<1100 收起（原三档随 2026-10-27 移除“正在播放”标签收敛为两档）；`SyncTransportVisuals` 以签名早退同步图标/强调色/ToolTip/UIA 名称。TrackList 工具栏（添加文件 / 递归导入文件夹 / M3U(M3U8)/PLS 追加 / M3U8 导出 / 清空）经 `RunImportAsync` 禁用按钮并显示忙碌环；NavRail 工具栏（新建 / 导入播放列表 / 删除）与双击重命名、▶ 活跃标记同属 Phase 22。2026-10-24 补：TrackList 行首 ▶ 当前曲标记 + 标题 Accent 色（判定 `ViewedPlaylist.IsActivePlaylist && index == CurrentIndex`，`ContainerContentChanging` + `CurrentIndex/IsActivePlaylist` 变化时重画）；行尾 × 移除按钮（确认后 `RemoveTrackCommand`，按 Track 引用定位索引；`DoubleTapped` 排除 Button 来源）；InfoPanel 在时长下显示 `Player.SampleRateText`；表头与行共用 `TrackListHorizontalPadding`（行容器在 `RailListViewItemStyle` 上补同值 Padding，否则标题列左移 12px、艺术家/时长列右移 12px）。2026-10-27 补：两颗折叠钮统一到各自栏头——右栏收起终值由 0 改为 48px（`InfoPanelCollapsedWidth`，`InfoPanelHeaderPadding` 与左栏同值；收起后栏头保留、钮仍可点），标题栏的 `InfoPanelToggle` 与其让位边距 `InfoPanelToggleMargin` 一并删除；手动点击发 `InfoPanel.ToggledManually` 置宿主手动优先。同日的底栏修正：去掉左侧“歌名 — 艺术家”文本（运输组从窗口左缘起排、进度条回收 180px），三档宽度状态收敛为两档（≥1100 显示音量）；进度/音量滑块拖动提示改挂 `ThumbToolTipValueConverter`，分别显示时间（mm:ss，≥1 小时为 h:mm:ss）与整数百分比，不再显示 0..1 小数。窄窗（<1100，横向音量条收起）时悬停静音钮弹出纵向音量条（Flyout + 纵向 Slider，与 `Player.Volume` 同步，提示同为整数百分比）；开关由 250ms 定时器按 `GetCursorPos` 实际位置驱动，不用 `PointerExited`（`ShowAt` 后 WinUI 会给锚点补一发 `PointerExited`，按它关会把弹层立刻收掉）。2026-10-29 修：弹层不再挂 `Button.Flyout`——Button 的点击/键盘激活会无条件打开它，宽窗点静音也弹层；改为构造时摘除、仅窄窗悬停 `ShowAt` 打开，点击仍是静音切换。同日第二轮：弹层打开时 light-dismiss 覆盖层会吞掉静音钮点击（点了不静音、弹层关了又重开），设 `VolumeFlyout.OverlayInputPassThroughElement = MuteButton` 让点击穿透，弹层保持打开时仍可切换静音。
- 关闭：`AppWindow.Closing` → cancel-and-close → `await SaveWindowGeometryAsync()`（仅 `OverlappedPresenterState.Restored`）→ `await MainViewModel.CleanupAsync()` → `Close()`；`_cleanupCompleted` 之前重复关闭继续取消，之后自家 `Close()` 放行（WPF 同形缺陷未修，两壳在此不对称）。
- `Services/WinUiFileDialogService.cs`（Phase 22）：`FileOpenPicker` / `FolderPicker` / `FileSavePicker` + `InitializeWithWindow.Initialize(picker, hwnd)`；HWND 未就绪、COM 异常、空虚拟路径、无法解析的保存过滤器都按取消语义返回空集合或 null；picker 跳过/失败写 `AppLog`。
- Phase 22 壳侧服务：`WinUiDialogService`（通用 `ContentDialog` 宿主，`IsOpen` 供空格守卫；Phase 23 EQ 由 PlayerBar 独立管理）、`WinUiAppPaths`（`D-player-winui` 数据目录 + `logs` 子目录）、`AppLog`（`app.log` 追加、1 MiB 轮转到 `app.log.1`、IO 失败静默）。这些服务只属 WinUI 壳，Core 不引用。
- WinUI 设置对话框：`SettingsDialog` 以 360px 内容宽度承载音量/频谱/输出/日志；三个滑块尾部已有数值，故设 `IsThumbToolTipEnabled=False`，音量数值列固定 48px 防止 `"99%"→"100%"` 变宽挤压滑块；`OnLoaded` 把 ContentDialog 模板的 `ContentScrollViewer` 改为垂直 `Auto`，低窗高时可滚动（2026-10-29）。
- 已知未验证 / 未做：Phase 22 的验收项与已知问题见 [`PHASE22-ACCEPTANCE.md`](./PHASE22-ACCEPTANCE.md)（验收已完成，验收 23 项全部 PASS（含用户 2026-10-24 确认的 C4/D3 与 D1 轮转））；Phase 23 频谱/EQ 实现完成、验收通过（2026-11-02，[验收记录](./PHASE23-ACCEPTANCE.md)）；仍缺拖拽 / 表头排序 / 文件夹绑定歌单管理（对比材料 §3；TrackList ▶ 与 InfoPanel 采样率已于 2026-10-24 补齐）。Phase 21 的视觉验收结论见 [PHASE21-ACCEPTANCE.md](./PHASE21-ACCEPTANCE.md)；对比材料 §2.2 是 Phase 20 历史验收。**WinUI 仍不进 `D-player.slnf`**：门禁 3（`-Full`）只保证它编译，XAML 运行时模板挂接、左栏逻辑、关闭落盘没有自动回归；本机读不到 Windows 应用程序日志 / WER，"没有崩溃记录"仍无第三方取证（应用侧已有 `AppLog`）。构建/运行命令见 §8.2。

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
    "WindowHeight": 450
  }
}
```

复制到输出目录（`<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>`），通过 `services.Configure<AppSettings>(configuration.GetSection("Player"))` 绑定为 `IOptions<AppSettings>`。

### 6.2 运行时持久化：`%LocalAppData%\D-player\settings.json`（WPF 壳）

> **Phase 20：数据目录由 UI 壳注入**，下面 §6.2/§6.3/§6.4 三个文件在两个壳里各有一份，落点由 `DPlayerDataPaths.FolderName` 决定：WPF 壳 = `%LocalAppData%\D-player\`，WinUI 壳 = `%LocalAppData%\D-player-winui\`。**两壳不得共用目录**（`JsonSettingsPersistence`/`JsonPlaylistService`/`JsonLibraryCache` 各自的 `SemaphoreSlim` 只在一个进程内生效，跨进程没有锁，共用会互相覆盖）。

由 `JsonSettingsPersistence` 读写，包含与 `AppSettings` 相同的字段；首次启动文件不存在时使用 record 默认值。

**更名数据迁移（UmaPlayer → D-player，只属 WPF 壳）**：数据目录从 `%LocalAppData%\UmaPlayer\` 改为 `%LocalAppData%\D-player\`。`App.OnStartup` 最早期调用 `LegacyDataMigration.MigrateIfNeeded(dataPaths)`（签名 Phase 20 起接受 `DPlayerDataPaths`；在任何持久化服务被 DI 构造前），把旧目录内文件逐个搬到新目录（新目录已有同名文件则跳过 → 幂等；失败静默吞掉不阻断启动，旧数据保留）。**WinUI 壳不做这条迁移**（它面对的是新用户与新目录，迁移逻辑一旦跑在两壳上会把同一份旧数据搬进两个目录并互相看不见了）。

**当前被持久化的字段**：`DefaultVolume`、`LibraryFolders`（Phase 24 音乐库根目录）、`WindowLeft/Top/Width/Height`、**`SpectrumEnabled/SpectrumSensitivity/SpectrumColorTheme/SpectrumSmoothing`（Phase 13）**、**`EqualizerEnabled/EqualizerPreamp/EqualizerBands/EqualizerPreset`（Phase 14）**、**`LastPlayedPath` / `LastPlayedPositionSeconds`（断点续播，2026-10-06）**、**`LibraryViewActive/LibraryViewPage/LibraryViewArtistName/LibraryViewAlbumTrackPath`（库导航，2026-11-03）**。
**已建模但未启用**：`OutputMode`、`PreferredDeviceId`。

**断点续播（2026-10-06）**：`LastPlayedPath` + `LastPlayedPositionSeconds` 记录"上次播放的曲目与位置"——`PlayerViewModel` 在暂停/停止/切歌时**立即**写、播放中由 `PositionChanged` 驱动但**最多每 30 秒写一次**、关闭时在 `CleanupAsync` 兜底写（在 `IPlaybackService.Dispose` 之前读出）。启动时 `MainViewModel.InitializeAsync` 读回，在已水化的歌单里按路径找到该曲及其所属歌单（`PlaylistsViewModel.FindPlaylistTrackByPath`，"正在播放"歌单优先），并把 `CurrentPlaylistId` 同步到该歌单（旧版本从 `ViewedPlaylist` 发起播放不会同步指针；不同步会让 ▶ 出声后的 `TrackEnded` 守卫推进错误歌单），`LoadAsync` + `Seek` **只就位不出声**——播放器栏显示曲目/时长/封面，按 `▶` 才从断点继续（`CurrentTrack != null` 后 ▶ 由 `PlayCurrent` 自动切回 `PlayPauseCommand`）。文件已缺失或该曲已不在任何歌单中时跳过恢复，由 `MainWindow` 弹一次 `ConfirmDialog.ShowInfo` 告知原因。恢复成功后立即回写一次，避免"启动后马上退出"把位置退回 0。无当前曲目时不写盘（清空队列/卸载后旧断点仍有效）。**两壳差异**：逻辑全在 Core，但"跳过恢复"的**提示出口**各壳自给 —— WPF 弹 `ConfirmDialog`，WinUI Phase 22 起写 `AppLog`（`断点续播被跳过：<原因>`，落 `%LOCALAPPDATA%\D-player-winui\logs\app.log`；此前切片期只 `Debug.WriteLine`，WinExe 下不可见）。

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
  "RepeatMode": "Off",
  "LibraryPlayback": {
    "Id": "__library-playback__",
    "Name": "专辑名",
    "Items": ["C:\\Music\\foo.flac"],
    "CurrentIndex": 0,
    "ShuffleEnabled": false,
    "RepeatMode": "Off",
    "SourceFolder": null
  }
}
```

Phase 12 continued: `ShuffleEnabled` / `RepeatMode` 提升到 QueueState 根级别（全局共享），Playlist 级别的同名字段保留但写入时固定为 `false` / `Off`（向后兼容）。Phase 24 增加 `LibraryPlayback`：隐藏库播放队列，`null` 表示尚未从库中播放过，不进入可见歌单集合。

写时机：`PlaylistsViewModel.StateChanged` → `MainViewModel` 500ms debounce 写盘；关闭时 `CleanupAsync` 再 flush 一次。读时机：`MainViewModel.InitializeAsync` 调 `IPlaylistService.LoadAsync`，随后 `Hydrate`。文件不存在/JSON 损坏/版本不匹配 → 静默 fallback 到空队列（保留旧文件供用户排查）。`Items` 中已被外部移动/删除的路径在加载时自动过滤；`CurrentIndex` 通过"向后滑、再向前回退"的算法映射到过滤后的位置（spec §5.1）。

### 6.4 元数据缓存：`%LocalAppData%\D-player\library-cache.json`（Phase 10）

由 `JsonLibraryCache` 读写。缓存文件夹绑定歌单与音乐库根扫描到的音频文件元数据，避免重复解析。Schema 为 `Dictionary<string, List<LibraryCacheEntry>>`，key 为规范化后的根目录路径（大写、去尾分隔符），value 是该根一次扫描的条目。`LibraryCacheEntry` 字段：`FilePath` / `Title` / `Artist` / `Album` / `Genre` / `Year` / `Duration` / `SampleRate` / `TrackNumber`；**不含封面字节与时间戳**——缓存有效性由共享根扫描事务与文件枚举结果决定。

读时机：文件夹歌单同步或 `LibraryRootScanner` 扫描前调 `ILibraryCache.LoadAsync(folderPath)`。写时机：扫描完成后在 `ILibraryRootScanner` 的同根事务内 `SaveAsync`；库 VM 与 `PlaylistsViewModel` 不得绕过事务直接写。`LoadAsync` 隐式契约：**绝不抛**（catch-all 空列表）；`SaveAsync` IO 失败抛出；实现用 `.tmp` + `Move` 原子覆盖。

---

## 7. 数据流：典型播放流程

### 7.1 启动 + 队列 schema 迁移

```
App.OnStartup 构建 DI → MainWindow Show → MainWindow.Loaded → MainViewModel.InitializeAsync()
→ IPlaylistService.LoadAsync 读 queue.json:
    SchemaVersion==1 → 自动迁移成单条 "默认歌单" + 立即覆盖写
    SchemaVersion==2 → 正常反序列化；v2→v3 补 Playlist.SourceFolder
    当前 SchemaVersion==3 → 正常反序列化（含可选 LibraryPlayback）
→ Hydrate 可见歌单 + 隐藏库队列；ViewedPlaylist 对齐 CurrentPlaylistId
→ Library.InitializeAsync 读库缓存并建分组；RestoreLibraryView 恢复上次库页签/详情（若有）
→ RestoreLastPlayedAsync 只就位不出声；同步 CurrentPlaylistId 到曲目所属歌单
→ 后台扫描文件夹歌单与音乐库根
→ MainWindow.Loaded 显式调用 Library.StartMonitoringAsync()（WinUI 壳不启动 watcher）
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
                                                              → EqualizerSampleProvider（Phase 14）
                                                              → SampleAggregator（Phase 13）
                                                              → VolumeSampleProvider
                                                              → WasapiPlayer(Shared + 事件同步, 100ms)
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
  → TrackEnded ─▶ 所有歌单 VM 的 HandleTrackEnded（共享播放器）
                  → !IsActivePlaylist：直接返回
                  → RepeatOne：PlayTrackAtAsync(CurrentIndex)
                  → 否则：CalculateNextIndex → PlayTrackAtAsync(next)
```

### 7.5 频谱数据流（Phase 13；Phase 30 改 producer/consumer）

```
WasapiPlayer 拉流 → VolumeSampleProvider → SampleAggregator.Read(buffer)
  → 混单声道 + 写 SPSC 环形缓冲（4×FftSize），跨 hop 发信号      ── 音频线程 ──▶
分析线程 DPlayer.SpectrumFft：取最后 8192 个真实时域样本（hop 4096）
  → 汉宁窗（预计算表）→ FFT → 对数分组 32 桶（预计算 bin 区间，RMS + gain8x + dB + gamma）
  → SpectrumDataReady(每帧新建 float[32] 快照)                   ── 分析线程 ──▶
NAudioPlaybackService.OnSpectrumDataReady
  → _syncContext.Post 封送 ── UI 线程 ──▶ SpectrumDataAvailable(data)
PlayerViewModel.HandleSpectrumData(data)
  → if (!SpectrumEnabled) return
  → 原地乘灵敏度增益 → 指数平滑(_smoothedSpectrum) → SpectrumData = smoothed.ToArray()
TrackInfoView → SpectrumView.SpectrumData (DP) → OnSpectrumDataChanged 算 _targetHeights
  → CompositionTarget.Rendering 每帧缓动 Rectangle.Height（60fps）

设置变更（SettingsDialog.Save_Click / PlayerViewModel.OnSpectrum*Changed）
  → UpdateAsync 写 settings.json + 同步 PlayerViewModel 属性
  → OnSpectrumEnabledChanged 额外回写 _player.SpectrumConfig.Enabled（禁用即停止取帧）
```

### 7.6 均衡器数据流（Phase 14）

```
启动：PlayerViewModel.Initialize → LoadEqualizerSettings(settings)
  → 先设 EqualizerEnabled observable（构造期 _isInitializing=true，OnEqualizerEnabledChanged 不写盘）
  → _player.EqualizerConfig = EqualizerConfig.Create(Enabled/Preamp/Bands/Preset)（存字段；在链则即时下发，装载窗口内待安装时重放）

播放：PlaylistViewModel.PlayTrackAtAsync → IPlaybackService.LoadAsync(track)
  → PlaybackChain 构建（Phase 30：_loadGate 串行化装载；建链在两道闸门外，安装只取 _chainGate 交换引用并重放实时配置）：ToSampleProvider → new EqualizerSampleProvider(sampleProvider, _equalizerConfig)
    → new SampleAggregator(equalizer, _spectrumConfig) → VolumeSampleProvider → WasapiPlayer
  （EQ 在 SampleAggregator 之前 → 频谱反映 EQ 后信号）

拖动/预设（实时）：EqualizerDialog.BandSlider_ValueChanged / PresetCombo_SelectionChanged / Enable_Changed
  → PushToService(preset)：_playbackService.EqualizerConfig = EqualizerConfig.Create(...)
  → setter：存字段 + _current?.Equalizer.Update(value)（Phase 30：只 Volatile.Write 一份不可变 config，不加锁）
  → 音频线程下一个 Read 开头 Interlocked.Exchange 取走，在缓冲边界对每声道每段 existing.SetPeakingEq(...) 就地重算系数
  → 下一个 Read 缓冲区即听感生效（且频谱同步反映；链未建时仅存字段待建链拾取）
  → 注意：SetPeakingEq 会清零 x1/x2/y1/y2（NAudio 3.3.0），换系数瞬间有复位型爆音风险，见 PHASE30-ACCEPTANCE.md §7

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

### 7.8 WinUI 壳的启动 / 双击 / 关闭（Phase 20，Phase 22 补功能层）

```
启动（全部在 UI 线程）：
App ctor
  → RequestedTheme = Dark
  → AppLog.Initialize(WinUiAppPaths.LogDirectory) + Write("WinUI shell starting")
  → 挂 Application.UnhandledException / AppDomain.UnhandledException / TaskScheduler.UnobservedTaskException（只记录）
App.OnLaunched
  → new DPlayerDataPaths { FolderName = "D-player-winui" }
  → services.AddDPlayerCore(configuration, dataPaths)
  → services.AddSingleton<IFileDialogService, WinUiFileDialogService>()   ← 各壳自己注册（Core 注册表里没有）
  → 订阅 IPlaybackService.PlaybackError → AppLog
  → BuildServiceProvider → GetRequiredService<MainViewModel> + ISettingsPersistence
  → new MainWindow(vm, settings, playback) → Activate()
      ctor：RestoreWindowGeometry()（物理像素；GetFromPoint 判可见性）+ MicaBackdrop（根 Grid Transparent）
            AppWindow.Closing += AppWindow_Closing
            Playlists.Playlists.CollectionChanged += SyncPlaylistMenu（歌单比 Loaded 晚到）
  → _ = vm.InitializeAsync()   → 水化 queue.json + Hydrate + 断点就位（只就位不出声）
                                   失败/跳过 → AppLog（WPF 是弹 ConfirmDialog）

双击（与 WPF 同一个 Core 入口）：
TrackList_DoubleTapped
  → IndexOfByReference(ViewedPlaylist.Queue, track)     ← 按引用身份，不用 Queue.IndexOf
  → await Playlists.HandleDoubleClickPlay(pl, index)
      → pl.Id != CurrentPlaylistId 时先切指针（RecomputeIsActiveFlags + StateChanged）
      → index 合法才 await pl.PlayTrackAtCommand → PlayTrackAt 里 _shuffleHistory.Clear() → PlayTrackAtAsync
                                                        （"视为新会话"，两壳语义一致）

关闭（cancel-and-close，复刻 WPF；Phase 22 补二次关闭守卫）：
AppWindow.Closing → _cleanupCompleted ? 放行自家 Close() : args.Cancel = true
  → _isClosing 已为真则继续取消（收尾期间的二次点击不绕过清理）
  → await SaveWindowGeometryAsync()      ← 仅 OverlappedPresenterState.Restored 时写位置/尺寸
  → await MainViewModel.CleanupAsync()   ← 写最终断点位置 + queue.json + 释放 WASAPI 设备
  → _cleanupCompleted = true → Close()
刻意不：同步 Dispose ServiceProvider（MainViewModel 现在用无操作 IDisposable 标记让容器继续释放单例；清理仍由 MainWindow.Closing 驱动）
```

---

## 8. 构建与运行

### 8.1 先决条件

- Windows 10/11（开发于 Windows 11 IoT Enterprise LTSC 2024）；WinUI 壳的 TFM 要求 build 19041（Windows 10 2004）及以上，self-contained 形态不需另装 Windows App SDK 运行时
- .NET 11 SDK（`net11.0-windows`）
- 构建 WinUI 壳需 nuget.org 可达（首次 restore 拉 `Microsoft.WindowsAppSDK` 2.6.5）；WinUI 工程只支持 x64
- 任意 IDE：Visual Studio 2026+ / JetBrains Rider / VSCode + C# Dev Kit

### 8.2 命令行构建与门禁

**门禁 1/2 = 两条命令，走解决方案筛选器 `D-player.slnf`（= `D-player.Core` + `D-player.csproj` + `Tests`），每次改动都跑，不是 `D-player.sln`**：

```bash
dotnet restore D-player.slnf
dotnet build   D-player.slnf -c Debug --nologo -v q      # 门禁 1：0 警告 0 错误
dotnet test    D-player.slnf -c Debug -v q --filter "Category!=AudioDevice"   # 门禁 2：363 通过 / 0 失败（默认排除音频）
dotnet test    D-player.slnf -c Debug -v q --filter "Category=AudioDevice"    # 5 条真实 WASAPI 集成测试（需音频设备）
dotnet run     --project D-player.csproj                 # 跑 WPF 壳
dotnet run     --project Tests/D-player.Tests.csproj -c Debug   # 跑测试（MTP，直接跑测试可执行程序）
```

**一次跑完（推荐，逐步 fail-fast，任一步非零退出即中止）**：

```bash
powershell -File tools/verify-gates.ps1 -Fast   # = 门禁 1 + 门禁 2（363，默认排除音频）
powershell -File tools/verify-gates.ps1 -Full   # = 门禁 1 + 门禁 2 + 门禁 3（WinUI 单工程构建）
powershell -File tools/verify-gates.ps1 -Full -Audio   # 再加 5 条真实音频集成（串行）
```

输出目录：`bin/Debug/net11.0-windows/`，可执行：`D-player.exe`。

**词汇只有三层（口径与 §4.3 第 28 条一致）**：**门禁 1/2**（上面两条，`-Fast`，自动化、每次改动必跑）／**门禁 3**（`-Full`，WinUI 单工程构建，每个 Phase 22 任务必跑；不在 `-Fast` 路径里）／**可选**（整解 `D-player.sln` 构建）。旧的"壳侧检查（不是门禁）"说法已作废：WinUI 构建现在是门禁 3，由 `-Full` 执行。

**为什么要筛选器**：WinUI 工程引用 `Microsoft.WindowsAppSDK`，把它留在每次构建/测试里会让"改一行共享层跑全量"从秒级变成十秒级、首次 restore 从秒级变成约 8 分钟。`D-player.slnf` 让 WinUI **不进日常门禁 1/2**；它的构建由 `-Full` 的门禁 3 单独覆盖（见 §4.3 第 28 条与 §9）。

WinUI 3 壳的**门禁 3**与运行：

```bash
dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q   # 门禁 3（-Full）：0 警告 0 错误
dotnet run   --project D-player.WinUI/D-player.WinUI.csproj -c Debug
```

**门禁 3 什么时候跑**：每个 Phase 22 任务必跑（`powershell -File tools/verify-gates.ps1 -Full`），以及任何改到 `D-player.Core` 公开面或 WinUI 本身的时候。理由就是 B1 的要害：**这类改动让门禁 1/2 照样全绿，而 WinUI 壳已经编译不过**。成本不是借口——首次 restore 之后它是增量构建，2026-10-07 实测 **9.31 秒**。

**`dotnet restore D-player.sln` 与 `dotnet build D-player.sln` 仍然存在，但不是门禁**：`.sln` 里四个工程全在，restore 会**首次**从 nuget.org 拉 Windows App SDK（当前 2.6.5，连带 9 个子包；8.1 分钟是 2.5.1 时的实测），且 self-contained 输出目录很大。只有这两种情况才需要它：① 要构建/跑 WinUI；② 要确认 IDE 的「Build Solution」不被 WinUI 打挂（Phase 20 实测整解构建 0 警告 0 错误）。

**测试栈（Phase 19 起；2026-11-11 升到 xunit.v3 4.0.3）**：`xunit.v3` 4.0.3（测试工程是**可执行程序**，`OutputType=Exe`）。runner 只有一个 —— Microsoft.Testing.Platform（MTP）；上面两条测试命令是同一个 runner 的两个入口，不是"MTP + VSTest 双 runner"。**测试总数 368（363 默认 + 5 音频集成；Phase 30 验收实跑默认 363/0/0、音频 5/0/0，新增 26 条默认测试）**（逐文件分解见 §3 的 Tests 树）。

> **测试过滤实测（2026-11-13 Phase 30）**：`--filter "Category!=AudioDevice"` 实跑 **363** 通过、0 失败、0 跳过；`Category=AudioDevice` 实跑 **5** 通过、0 失败、0 跳过。Phase 29 的 337 与更早读数（Phase 27 310、Phase 26/25/24）保留为历史；需要真实输出路径时用 `verify-gates.ps1 -Audio` 追加。

> `dotnet test` 之所以还能跑，靠仓库根 `global.json` 的 `{"test":{"runner":"Microsoft.Testing.Platform"}}`（.NET 11 SDK 的原生 opt-in，机制不变、Phase 28 已实测有效）。csproj 现在只保留 `xunit.v3` + `NSubstitute`（2026-11-02 移除 `Microsoft.NET.Test.Sdk`、`xunit.runner.visualstudio`、`coverlet.collector` 与 `TestingPlatformDotnetTestSupport`；IDE 测试发现不再保证，需要时再加回）。**删除 `global.json` 会让 `dotnet test D-player.slnf` 失败**（Microsoft.Testing.Platform 2.x 在 .NET 11 SDK 上不再支持 VSTest 目标）。
>
> **两条测试命令都不要加 `--nologo`**：MTP 不识别该参数，加上后一条测试都不会跑，摘要却打印 `成功: 0`（易被当成全绿；实际输出是"运行了零个测试"+ 退出码 8）。`--filter "FullyQualifiedName~X"` 照常可用。`--nologo` 只能用在 `dotnet build` 上。

音频集成测试（`NAudioPlaybackService*Tests`）真实占用 WASAPI 设备，并行度结论见 §9。

### 8.3 发布（独立可执行）

```bash
dotnet publish D-player.csproj -c Release -r win-x64 \
    --self-contained false /p:PublishSingleFile=true
```

发布形态未改：framework-dependent（`--self-contained false`）发布自 Phase 28 起要求目标机安装 **.NET 11 Desktop Runtime**（不再是 .NET 10）；WinUI 壳 self-contained 不受影响。

---

## 9. 已知约束与陷阱

- **格式限制**：仅支持 Windows Media Foundation 原生解码的格式；OGG/Vorbis 需用户系统安装第三方编解码器或后续切换 reader。
- **静默错误**：`HandlePlaybackError` 仅 TODO，未弹窗或写日志；调试期可通过断点检视。
- **启动时同步 IO**：`PlayerViewModel.Initialize()`、`MainWindow` 构造函数与 `PlaylistsViewModel` 的缓存回填（`ApplyCachedMetadataSync` / `LoadMetadataForNormalPlaylistSync`）仍使用 `LoadAsync().GetAwaiter().GetResult()`。三个持久化文件都极小（settings 几百字节、queue 视队列长度，典型 < 10KB），可接受；数据膨胀时需重构。
- **DI 解析跨线程**：`IPlaybackService` 必须由 UI 线程首次构造（依赖 `SynchronizationContext.Current` 捕获），目前由 `App.OnStartup` 保证。
- **UpdateAsync 跨线程读 DP**：`JsonSettingsPersistence.UpdateAsync` 内部 `.ConfigureAwait(false)` 把 mutator 调用 / 继续上下文带到 threadpool；若 mutator 闭包内读 WPF DependencyProperty（如 `Left/Top/Width/ActualHeight`）会抛 `InvalidOperationException`。**调用方必须先在 UI 线程把 DP 值捕获到局部变量再 await**。`MainWindow.xaml.cs:Window_Closing` 即采用此模式。
- **`async void Window_Closing` 多 await dispatcher race**（Phase 4）：超过 1-2 个 await 时第一个 await yield 后 WPF 立即继续关闭流程，`ShutdownMode.OnLastWindowClose` 触发 `Application.Shutdown → Dispatcher.InvokeShutdown`，后续 await 续延 post 到死 dispatcher 上**永不运行**。修复纪律：用 cancel-and-close 模式（首次 `e.Cancel=true` 加 `_isClosing` 标志，做完异步工作再 `Close()`）。Phase 4 加入 queue.json 写盘后从 2 个 await 涨到 4 个，settings 还能写但 queue 永远不更新；commit `9bae7ce` 用此模式修复（COUPLING.md §5）。
- **WPF inline Style 必须 `BasedOn` 隐式主题样式**：`<X.Style><Style TargetType="X">` 没有 `BasedOn="{StaticResource {x:Type X}}"` 会完全替换 `Themes/Controls.xaml` 中的隐式 Style，回退到 OS 原生外观（Button 白底灰框、Slider 灰色等）。Phase 4 ▶ 按钮加 DataTrigger 时漏 BasedOn → 按钮变白底；commit `ca66fa9` 修复。
- **WPF DP 优先级 `local > trigger setter > style setter`**（Phase 5）：要被 `Style.Triggers` 改的属性，**默认值必须放进 `Style.Setter`**，不能作为元素的 local attribute 写。Phase 5 拖拽边框高亮初次落地时 `<Border BorderBrush="Transparent">` 写成 local，让 `IsDragOver=True` 的 trigger setter 永远赢不了 → 高亮失效；commit `214d595` 把默认值挪进 Style.Setter 修复。Code review checklist：看到 inline Style.Trigger 改某 DP 时，对照查这个 DP 在元素自身上没有 local 写法。
- **WPF DragDrop RoutedEvent 冒泡 + Handled 拦截**（Phase 5）：`Drop` / `DragOver` 等都是冒泡事件；子元素设 `e.Handled = true` 后父元素的同名 handler 不再触发。Phase 5 验收时撞过：`QueueList_Drop` 处理完入队/重排设 `Handled=true`，原本想靠 `Root_Drop` 清高亮的逻辑被吃掉 → 高亮卡死。修复：清高亮（清 Adorner、清 IsDragOver）必须在两条路径都做（`QueueList_Drop` finally + `Root_Drop`），不能假定事件会冒泡上来。
- **WPF ListBox `PreviewMouseLeftButtonDown` 不消费事件 → 多选拖拽塌选**（Phase 5）：Preview 阶段不 `Handled=true` 时，ListBox 自身的选中处理仍会执行；用户 Ctrl+多选后再不带修饰键按下其中一项，ListBox 默认行为会立刻塌成单选，让随后启动的 DoDragDrop 拿到 `SelectedItems.Count==1`。修复：在按下点是"已选 + 多选 ≥2 + 无 Ctrl/Shift"时拦掉 `Handled=true`，没真正拖起来时再在 `MouseUp` 手动塌成单选模拟原行为；commit `af51dde`。
- **WPF record 结构相等会让 `Queue.IndexOf` / `HashSet<Track>` 塌陷重复占位**（Phase 5）：Track 是 `sealed record`；两个 `CreateFallback("X.mp3")` 在结构上相等。基于相等性的查找/去重会把它们认作同一个，重排时 `Queue.IndexOf(currentTrackObj)` 返回首个结构等价匹配而非原始那一个 → ▶ 跟到错的曲、Shuffle 历史塌陷。修复纪律：所有需要"找回原来那一个 Track 实例"的代码用 `ReferenceEquals` + `ReferenceEqualityComparer.Instance`（见 `PlaylistViewModel.MoveTracks` 与 `PlaylistView.QueueList_PreviewMouseMove`）。
- **ViewBox + CornerRadius + ClipToBounds（Phase 12 continued）**：`ViewBox` 缩放子元素时会突破父 `Border` 的 `CornerRadius` 圆角裁切区域，导致封面方形直角溢出圆角边框。修复：给封面 `Border` 加 `ClipToBounds=True`，让 WPF 裁切到 Border 边界内。同时封面 `Border` 不能有 `CornerRadius`（与播放时直角不一致），圆角仅在外层容器 Border 上设置。
- **频谱事件跨线程（Phase 13；Phase 30 改写）**：`SampleAggregator.SpectrumDataReady` 现在由**独立分析线程**（`DPlayer.SpectrumFft`，BelowNormal）触发，不再是 NAudio 渲染线程；`NAudioPlaybackService.OnSpectrumDataReady` 仍必须经 `_syncContext.Post` 封送到 UI 线程再广播 `SpectrumDataAvailable`；`PlayerViewModel.HandleSpectrumData` 直接在 UI 线程更新 `SpectrumData` 绑定属性。若跳过封送会跨线程触碰 DP 抛异常。`SettingsDialog.Save_Click` 同理故意不用 `ConfigureAwait(false)`，留在 UI 线程才能直接写 `PlayerViewModel` 属性。Phase 30 起每帧快照由聚合器新建（音频线程零分配），VM 可**原地**乘灵敏度，发布给绑定时再 `ToArray()` 一次（见下一条）。
- **EQ 实时更新的线程安全（Phase 14；Phase 30 改为无锁下发）**：`EqualizerSampleProvider` 不再用 `lock` 保护系数——`Update`（UI 线程）只 `Volatile.Write` 一份不可变 `EqualizerConfig` 引用，音频线程每次 `Read` 开头 `Interlocked.Exchange` 取走并**就地**应用（`SetPeakingEq` 重算系数），因此"配置在缓冲边界生效"这一语义保留、UI 不再能阻塞音频线程。**`SetPeakingEq` 在 NAudio 3.3.0 会把延迟线 `x1/x2/y1/y2` 清零**（独立探针：0.5 → 0）——Phase 14 起"保留延迟线状态/拖动不爆音"的注释与设计描述是**误记**；Phase 30 保留"不重建 `BiQuadFilter` 对象"，但**不保留跨缓冲滤波状态**，拖动 EQ 的复位型爆音是既有行为，状态保持/交叉淡化属后续候选（见 [PHASE30-ACCEPTANCE.md](./PHASE30-ACCEPTANCE.md) §7）。立体声必须每声道独立 `BiQuadFilter?[channel][band]`（左右共享实例会串扰滤波状态）；中心频率 ≥ 奈奎斯特（`sampleRate/2`）的频段置 null slot 旁路（PeakingEQ 在 ≥Nyquist 时不稳定）；0 dB（单位增益）频段不进级联（`SetPeakingEq` 复位语义下与全段级联在浮点容差内等价）。`EqualizerDialog.Save_Click` 同 `SettingsDialog` 故意不用 `ConfigureAwait(false)`，留在 UI 线程才能写 `PlayerViewModel.EqualizerEnabled`。
- **WPF ComboBox 向空集合添加首项会自动选中 index 0（Phase 14）**：`ComboBox` 在从空集合添加第一个项时会自动选中该项并触发一次 `SelectionChanged`。`EqualizerDialog.OnLoaded` 填充预设下拉必须在 `_suppress` 窗口内进行，否则打开对话框即误 push 一次 Flat/禁用配置，扰动正在播放的 EQ（Phase 14 code review 拦下的 Critical）。
- **播放链生命周期必须串行化；"停止"意图必须显式标记**（Phase 18 后修复，Phase 19 迁移到 WasapiPlayer，Phase 30 重构为 `PlaybackChain` + 两道闸门）：`NAudioPlaybackService.LoadAsync` 重建整条播放链，而 `Unload` / `Dispose` / 传输命令来自 UI 线程。缺串行化时，后一次重建会释放前一次正在使用或构建的链（早期的 `_reader` / `_volumeProvider` / `_wavePlayer` 三个共享字段被交错读写）→ 异常从链内部抛出并冒进 `async void` 事件处理器（进程崩溃）。Phase 30 的结构：**`_loadGate`（`SemaphoreSlim`）串行化装载**，打开 reader / 初始化输出这些重活以及旧链的 stop/dispose 都在 `_chainGate` **之外**完成；`_chainGate` 内只交换 `_current` 引用、`StopRequested` 标记与配置快照（持锁期间不 await）。唯一不取 `_chainGate` 的地方仍是 `OnPlaybackStopped`（播放线程回调）——持锁方可能正阻塞在输出类的 `Stop()` / `Dispose()` 上等待播放线程退出（`NAudio.Wasapi 3.3.0` 随包 XML 文档：`Dispose` 一条明确写着 "Stops playback (blocking) and releases all resources"，而 `Stop` 只有 "Stop playback and flush buffers"），回调里取锁即死锁，故其读取一律防御式（先 `ReferenceEquals(Volatile.Read(ref _current), chain)` 判当前链，再判 `chain.StopRequested`）。**NAudio 3 的 guarded dispose 不使这条失效**：它只保护 NAudio 自己的对象，管不到我们自己的链字段。同源的第二条陷阱：用户停止与自然播完都只表现为一次 `PlaybackStopped`，输出类不告知发起方，仅凭播放头位置无法区分（曲尾 200ms 内按停止会被误判为播完并自动推进下一首）；修复是**每链** `PlaybackChain.StopRequested` 标记（`Stop`/`Unload`/`Dispose`/切歌摘下旧链时置位、`Play` 清零、`OnPlaybackStopped` 命中即提前返回）——该不确定性不随实现变化，故换类后依然必要。回归测试（证据强度不同，别混着引用）：Phase 30 新增的 `NAudioPlaybackServiceDeterministicTests`（14 条，门闩编排）是**确定性**证据——`Load_BlocksInInit_UnloadCancelsInstall` / `TwoOverlappingLoads_Serialized_LastChainWins` / `Stop_RaisesPlaybackStoppedSynchronously_DoesNotDeadlock` / `NaturalEnd_RaisesTrackEndedOnce` / `ReplacedChain_LatePlaybackStopped_IsIgnored` / `ChainDispose_UnsubscribesBeforeStop` 覆盖闸门、代际与停止意图；闸门侧原 `NAudioPlaybackServiceConcurrencyTests` 的 `LoadAsync_TwoOverlappingCalls_DoNotThrow` / `Unload_DuringLoadAsync_DoesNotThrow` 以 1–5 ms 错位窗口断言"不抛异常"，是真实但**随机**的证据（窗口未命中时只是没有触发竞态），Phase 30 保留它们作为集成证据而不再承担主要论证；意图标记侧真机仍只有 `NAudioPlaybackServiceStopSemanticsTests.Stop_WhenPlayheadIsAtTrackEnd_DoesNotRaiseTrackEnded` 一条，同类中的 `Unload_WhenPlayheadIsAtTrackEnd_DoesNotRaiseTrackEnded` 钉住的是"解订阅早于停止"这条顺序；反方向由 `NAudioPlaybackServiceConcurrencyTests.PlayToNaturalEnd_ThenAdvance_DoesNotThrow` 钉住（真自然播完仍须发 `TrackEnded`——注意它在闸门侧那个类里，不在 `…StopSemanticsTests`）。
- **音频热路径的 Phase 30 结构（2026-11-13）**：改音频代码前先读这三条。
  - **频谱**：音频线程 `Read` 只做「读源 → 混音为单声道 → 写 SPSC 环形缓冲（`4 × FftSize`）→ 跨 hop 发信号」，不 FFT、不分配、不取锁、不等待；FFT/加窗/对数分桶/快照在后台线程 `DPlayer.SpectrumFft`（`IsBackground`、BelowNormal、`Dispose` join 上限 2 s）。**重叠帧必须用真实时域样本**：环中截至 `writeIndex` 的最后 `FftSize` 个样本，hop = `FftSize/2`。Phase 13 的实现在 FFT 后把频域缓冲的前半段当"重叠尾"复用，导致第二帧起前半段是上一帧的频域残留（只污染频谱、不污染 PCM），Phase 30 已修——不要按"就地复用 `_fftBuffer`"的老写法改回去。落后超过 `ring.Length - FftSize` 时丢帧不排队（内存有界）；`FftSize`/`BarCount` 只在建链时读，运行时只支持 `Enabled`（非法配置构造期抛 `ArgumentOutOfRangeException`）；`SampleAggregator` 新增 `IDisposable`，`PlaybackChain.Dispose` 的顺序是「解订阅频谱 → 解订阅 `PlaybackStopped` → 停/释放输出 → `Aggregator.Dispose()`（join FFT 线程）→ 释放源」。
  - **快照契约**：每帧仍新建一份 `float[BarCount]` 快照（128 B）再发事件，订阅者不能篡改下一帧；这份分配属 FFT 线程，**不计入**"音频线程零分配"。`PlayerViewModel.HandleSpectrumData` 自 Phase 30 起**原地**乘灵敏度（快照归它所有，不再二次 `ToArray()`），只在发布给 WPF 绑定时复制一次；同一份数组被重复消费是破坏性契约。
  - **性能口径**：`tools/AudioBench` 是人工基线工具（不进 `D-player.slnf`、不进日常门禁），读数见 [PHASE30-AUDIO-BASELINE.md](./PHASE30-AUDIO-BASELINE.md)（改前）与 [PHASE30-AUDIO-AFTER.md](./PHASE30-AUDIO-AFTER.md)（改后）。改写后 `aggregator` 行只剩 producer `Read` 成本，**不能**与基线的 `aggregator` 行相减；`full-chain` 行才是音频线程总成本主对照。`thread alloc` 地板恒为 40 B/run（baseline 的 `eq-off` 也是 40），属 harness；`process alloc` 含 FFT 线程每帧快照。
- **音频集成测试的并行度**（Phase 19）：仓库有 5 条测试真实占用 WASAPI 设备（`NAudioPlaybackServiceConcurrencyTests` 3 条 + `NAudioPlaybackServiceStopSemanticsTests` 2 条）。迁到 xunit v3 时在默认并行下实测：结论是 **level 0 —— 沿用 xunit v3 的默认并行，既没有加 `[Collection("AudioDevice")]`，也没有 `Tests/AssemblyInfo.cs` / `DisableTestParallelization`**。证据：10 次 MTP 运行 + 3 次 `dotnet test` 运行，每次都是 161 通过 / 0 失败 / 0 跳过（含那 5 条真设备用例），单次墙钟 5.875–6.249 s、runner 内 1.69–1.95 s。改这两个测试类或新增音频测试时沿用同一约束；若真出现设备争用/时序漂移，再按设计稿 §5.3 的顺序收紧（先给音频测试类加同一个 `[Collection("AudioDevice")]`，仍不稳才全局禁用并行）。
- **音频集成测试已从默认门禁拆出（2026-11-02）**：5 条测试现在都带 `[Trait("Category","AudioDevice")]` + `[Collection("AudioDevice")]`；默认门禁用 `Category!=AudioDevice`（Phase 25 收尾 283、Phase 26 验收 294、Phase 27 实现 310、Phase 29 实现 337 为历史；Phase 30 实现实跑 **363**），`verify-gates.ps1 -Audio` 单独串行跑这 5 条。Phase 19 的默认并行结论保留为历史。**计数口径见 §8.2 的实测块**：总数 368 = 363 默认 + 5 音频（Phase 30 新增 26 条默认测试）。
- **库播放队列不得加入 `Playlists` 集合（Phase 24）**：`PlaylistsViewModel` 私有持有 `_libraryPlayback`（Id `__library-playback__`），只通过 `LibraryPlayback` 投影暴露。侧栏拖拽重排按 `ListBox` 索引调用 `MovePlaylistCommand`，把隐藏项塞进集合会让索引与歌单错位；同时它也不该出现在 `queue.json` 的可见歌单数组里（`QueueState.LibraryPlayback` 是独立字段）。只有 `CurrentPlaylistId` 能指向它。
- **不可用库根必须与空根分开（Phase 24）**：`LibraryScannerService.ScanFolder` 会吞掉 Unauthorized/IO 异常并返回空集合；`LibraryViewModel` 必须走 `ScanFolderWithStatus` 的 `ScanUnavailable`，跳过该根的缓存写入，否则一次离线的移动硬盘会被当成"所有文件已删除"而清空 `library-cache.json`。手工刷新路径同理。
- **库封面与网格的规模边界（Phase 24）**：封面只按专辑首曲惰性读取（`EnsureCoverAsync`），tile/详情头部 `Loaded` 才触发；专辑页是非虚拟化 `WrapPanel`。不要改成"扫描时读全部封面"（几千张字节数组常驻内存）或依赖虚拟化——后者是独立子系统，本阶段明确不做（设计 §9）。
- **库视图导航持久化（2026-11-03 修订；2026-11-08 补侧栏选中）**：关闭时 `MainViewModel.CleanupAsync` 把 `LibraryViewActive` / `LibraryViewPage` / 艺术家名 / 专辑首曲路径写入 `AppSettings`；启动在库缓存水化后恢复最后页签与专辑/艺术家详情，找不到目标时回落到专辑页/艺术家页。`IsLibraryViewActive` 置 true 时必须清空 `Playlists.ViewedPlaylist`（`OnIsLibraryViewActiveChanged`），否则 `Playlists.Hydrate` 选中的第一个歌单会与库页同时高亮；启动恢复不经过点击路径，`PlaylistsSidebarView` 监听 `IsLibraryViewActive` / `Library.Page` 补亮「艺术家/专辑」节点（搜索态保持来源节点）。回归 `PlaybackResumeTests.InitializeAsync_RestoresLibraryView_ClearsViewedPlaylist`；侧栏视觉由合成库重启走查覆盖。`QueueState` 仍不记录 `LibraryPage`；库播放队列继续按 `CurrentPlaylistId` 恢复。
- **Phase 29 删除了 `PlaylistsViewModel` 的测试专用 Null Object/internal 2 参构造器（Phase 29）**：现在只有公开 8 参构造器；给 `IPlaybackService` 加成员只需同步生产实现与测试替身（NSubstitute），不再有手写 `NullPlaybackService` 需要补成员。
- **矢量图标经 `Application.Current.FindResource` 解析（Phase 16）**：`PlayStateToIconConverter` / `RepeatModeToIconConverter` / `BoolToVolumeIconConverter` 返回的 `Geometry` 依赖 `Icons.xaml` 已在 `App.xaml` 合并；若漏合并会运行时抛异常。新增图标须同步 `Icons.xaml` 键与使用处（COUPLING.md §5 Phase 16 契约）。▶ 标记元素类型为 `Path`：`FindChildByName<Path>(container, "PART_Marker"/"PART_SidebarMarker")`，改回 TextBlock 或改泛型会静默失效。
- **WindowChrome 自绘按钮必须 `IsHitTestVisibleInChrome=True`（Phase 17）**：caption 高度（32px）区域内的自绘按钮不加此 attached 属性会被 caption 拖动吞掉点击；改动标题栏按钮/新增标题栏控件时必查。
- **无边框窗最大化边距必须用常量式工作区计算（Phase 17）**：`TitleBar.ApplyWindowState` 给根内容加「`WorkArea` 偏移 + `WindowResizeBorderThickness`」边距；不要改回"读窗口实际边界"的实现 —— 布局时序会让 right/bottom 边距偏大导致大片留空（commit `d86694b` 修）。
- **WPF ComboBox 深色模板的 ToggleButton 用 `ClickMode=Press`（Phase 17）**：ComboBox 本体由自绘 `ToggleButton` 承载点击（展开/收起），`ContentPresenter` 设 `IsHitTestVisible=False` 叠加显示选中值；点击必须落在 toggle 表面才能开合下拉（commit `dd435bd` 修）。
- **单击进度条跳转必须 `handledEventsToo: true` 挂接（2026-10-05 修复）**：隐式 Slider 样式开启 `IsMoveToPointEnabled`（Phase 13 `781d35d`）后，Slider 类处理器按轨道时会先置 `e.Handled=true`，XAML 属性挂接的实例处理器（不接收已处理事件）被静默跳过 → 单击跳转整体失效。`PlayerBar` 构造函数改用 `SeekBar.AddHandler(PreviewMouseLeftButtonDownEvent, ..., handledEventsToo: true)`。音量滑块 TwoWay 不受影响；进度条 Value 是 OneWay，类处理器的本地改值不回传 VM 且被 30Hz 轮询覆盖。
- **事件 `OriginalSource` 可能是 `ContentElement` 而非 `Visual`（2026-11-08 修复）**：库列表文字是 TextBlock 内的 `Run`，对其调用 `VisualTreeHelper.GetParent` 会抛 `InvalidOperationException` 并让进程退出（实测崩溃堆栈在 `LibraryView.FindAncestor`）。从 `e.OriginalSource` 向上找容器时必须区分 `Visual`/`Visual3D` 与 `ContentElement`（后者走 `ContentOperations.GetParent` 或 `FrameworkContentElement.Parent`）；`PlayerBar.FindAncestor` 也用了同一模式，改动事件路由时要复查。
- **自定义 TextBox 模板不得把 `Padding` 再绑成 `PART_ContentHost` 的 `Margin`（2026-11-08 修复）**：`TextBox` 自己会按 `Padding` 缩进文本与光标，模板再绑一次等于内边距翻倍——Phase 25 搜索框的提示文字因此停在 9px、输入文字与光标在 16px，相差约 8px。模板只保留 `VerticalAlignment="Center"`；无边框搜索框的提示文字 `Margin` 要与 `Padding` 左值一致（8）。修掉模板后控件自然高度变小，需要保持原视觉高度的用法处显式给 `MinHeight`（搜索框 30、`PromptDialog` 输入框 28），不要靠重复 Padding 撑高。
- **WinUI 3 的进度条单击必须挂在比 `Slider` 更深的模板元素上，且 `StepFrequency` 要显式给小（2026-10-07 修复，只属 WinUI 壳）**：三条都是本机注入鼠标 + 临时探针量出来的，与上面那条 WPF 的坑形似而机制不同 —— **WinUI 3 的 `Slider` 没有 `IsMoveToPointEnabled`**（WPF 属性，引用即 CS1061）。① 原生"按下点 → 值"的处理是 `Slider` 自己的**类处理器**：挂在 `Slider` 上的托管处理器运行时 `Value` 已经被改掉（实测 `[value] 0 -> 1` 记录在 `[slider-press]` 之前），在那里 `e.Handled=true` 太晚；**挂在模板轨道元素（`HorizontalTemplate`，`Slider` 的可视化下游）上才赶得上**，实测同一位置的端点事件完全消失。② 想用托管子类覆盖 `OnPointerPressed` 拦它是死路：那个 override 在本壳上**根本不被调用**（原生控件不走托管 override 链），而且 `DefaultStyleKey=typeof(Slider)` 会让模板变形（丢 `FocusBorder`、滑块从 18×18 变 8×24）。③ `Slider.FindName("HorizontalTemplate"/"HorizontalThumb")` 在本壳实测返回 null（原生控件的模板名字不在托管 namescope 里）——模板部件要靠"向下找第一个 `Thumb`"或"已挂接滑块的可视化父节点"来取。④ **`StepFrequency` 默认 1 + `SnapsTo=StepValues`（该枚举只有 `StepValues`/`Ticks`，没有 `None`）在 `Minimum=0 Maximum=1` 的进度条上把整条轨道量化成 {0,1}**：单击与拖动的手势都只能落在两端，分界是几何中点（用户 2026-10-07 报的"点左半跳开头、点右半跳结尾"就是这条；当时表里标注为假设的 `LargeChange` 翻页解释被实测否掉——滑块停在 0 时点它右边 `Value` 一动不动）。程序写入 `Value` 不参与量化，所以壳侧自己算落点那条路是准的；而**拖动**这条路由原生算，只能靠把 `StepFrequency` 显式给小（本壳给 0.001 ≈ 0.9 px 一格）才停得住手指处。
- **`UnloadCurrentTrack` 必须以 `IsActivePlaylist` 守卫（2026-10-05 修复）**：`IPlaybackService` 是全局单例——清空/删除非播放中歌单的曲目不得停掉正在播放的歌（`ClearQueue`/`RemoveTrack` 经此方法）。只有当前播放歌单才有权 `_player.Unload()`；`_playToken++` 与 `CurrentIndex=-1` 保持无条件。
- **显式引用 `System.Text.Encoding.CodePages` 会触发 NU1510（Phase 18 于 net10.0 记录；Phase 28 复核 net11.0 仍框架隐含）**：该包在 net11.0 上仍框架隐含（framework-implicit）——Phase 28 实测：net11.0 ref pack（`Microsoft.NETCore.App.Ref/11.0.0/ref/net11.0`）含 `System.Text.Encoding.CodePages.dll`，`D-player.csproj` 与 `D-player.Core.csproj` 均无该包引用，门禁 1 零警告（无 NU1510），`Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` + `Encoding.GetEncoding(936)` 开箱可用（两条 GBK 单测各 1/1、GUI 端到端导入 GBK `.m3u` 中文路径无乱码）。对框架隐含包再写显式 `PackageReference` 会被 SDK 判定冗余并给 NU1510 警告，破坏本项目 0 警告门禁。`D-player.csproj` 因此**没有**该包引用，不要"补依赖"加回去。
- **GBK 解码不得搬出 `PlaylistFileEncoding`（Phase 18）**：CodePages provider 在该类型的**静态构造函数**里注册，`Encoding.GetEncoding(936)` 只出现在该类内部，"注册永远早于解码"是类型不变量而非启动顺序约定（`App.xaml.cs` 无需改动）。把 GBK 解码搬到别的类型就等于把注册时机重新变成一条口头约定（COUPLING §5）。
- **GBK 编码探测存在可接受的误判（Phase 18）**：严格 UTF-8 试解码成功即认定 UTF-8；极少数 GBK 字节序列恰好是合法 UTF-8 时会被误判并产生乱码路径——后果是这些条目落入"文件缺失"计数，用户能从导入报告里看出来，不会静默错乱。
- **Core 必须 WPF-free（Phase 20）**：`D-player.Core/` 下不得出现 `System.Windows.*` / `PresentationFramework` / `ICollectionView` / `CollectionViewSource`，否则第二壳被拖回 WPF。可 grep 自检（四个类型名一个都不能少，命令与上面这条规则同口径）：`grep -rn "System.Windows\|PresentationFramework\|ICollectionView\|CollectionViewSource" D-player.Core --include=*.cs | grep -v "/obj/\|/bin/"` → **应无命中，2026-10-07 实跑确实零命中**。注意 `BitmapImage` **不在这个自检的模式里**：Core 有三行注释提到它（`Models/Track.cs:13`、`ViewModels/PlayerViewModel.cs:18`/`:47`，说的是 WPF 壳侧 converter 的行为），那是散文不是类型引用，加进模式只会让自检长期"命中注释"而失去意义。
- **仓库根的兄弟目录必须进 `D-player.csproj` 的 glob 排除集（Phase 20）**：WPF SDK 会生成 `*_wpftmp.csproj` 并从仓库根重新 glob `**/*.cs` / `**/*.xaml`；`Tests/**`、`D-player.Core/**`、`D-player.WinUI/**`、`tools/**` 四组各有一份 `Compile/Page/ApplicationDefinition/Resource/None/EmbeddedResource Remove`。漏掉的后果分别是重复编译进主程序集（AssemblyInfo 重复、xunit 引用缺失）与 `MC3074`/`CS0234`（WPF 侧不存在 `Microsoft.UI.Xaml`）。新增仓库根级工程或工具目录时必须同步补一组，并**重跑门禁**确认隔离仍然成立。
- **WinUI 不进 `D-player.slnf`，构建由门禁 3（`-Full`）覆盖（Phase 20 的设计决策，Phase 22 升为门禁 3）**：`D-player.slnf` 只含 Core + WPF + Tests，所以门禁 1/2 不编译 WinUI；`powershell -File tools/verify-gates.ps1 -Full` 的门禁 3 会构建 WinUI 单工程（0 警告 0 错误），每个 Phase 22 任务与任何 Core 公开面/WinUI 改动都必须跑。它仍不覆盖 WinUI 的**行为**（左栏逻辑、关闭落盘、XAML 运行时模板挂接没有自动回归），那部分靠真机手测与验收清单。`Tests/Extensions/AddDPlayerCoreTests.cs` 钉住 Core 的 DI 图，但**钉不住 WinUI 壳忘记注册自己的 `IFileDialogService`** —— 那种缺失要到第一次构造歌单才炸。要再收紧，就得把 WinUI 放进 `.slnf`，那会带来 restore/构建成本（见 §8.2），仍是一条要单独拍板的决策。
- **WinUI 的 Mica 可见性依赖"根 Grid 背景必须是 `Transparent`"这条约定（Phase 20）**：材质挂在窗口上，但只在没有不透明背景刷的表面后面可见。以后任何人往根 Grid 或某个铺满的容器上加不透明背景刷，材质就"看起来消失"。护栏写在 `D-player.WinUI/MainWindow.xaml` 顶部注释（含三行像素对照）。反向陷阱：`DWMWA_SYSTEMBACKDROP_TYPE` 对组合器挂载的 backdrop **不是判据**（本项目实测恒为 0），基于它的自动化断言必然得出错结论 —— 实施过程中就真的被它误导过一次，把已生效的 Mica 判成了"未挂载"。
- **两壳不得共用数据目录（Phase 20）**：三个持久化服务的 `SemaphoreSlim` 只在单进程内生效，跨进程无协调；`D-player` 与 `D-player-winui` 必须分开。`LegacyDataMigration` 的 UmaPlayer 迁移**只属 WPF 壳**（跑两遍会把同一份旧数据搬进两个目录，此后两边互相看不见）。
- **`IFileDialogService` 已异步（Phase 22 已解 Phase 20 的同步/异步障碍）**：三个方法返回 `Task`；WPF 实现仍在 UI 线程弹同步 `OpenFileDialog`/`OpenFolderDialog`/`SaveFileDialog` 后包成 `Task.FromResult`，WinUI 用真实 picker + `InitializeWithWindow`，取消/失败返回空集合或 null。Core 里不得出现 WinUI/WPF 类型；不要在 Core 侧改回同步签名，否则两个壳的 picker 路径都会退回"在 UI 线程阻塞等待模态 picker"的死锁边缘。
- **WinUI 壳的运行期异常现在写入 `AppLog`（Phase 22）**：`%LOCALAPPDATA%\D-player-winui\logs\app.log` 记录启动、断点恢复跳过、picker 跳过、`PlaybackError`、未处理异常与几何失败；1 MiB 轮转到 `app.log.1`，IO 失败静默（日志永远不能成为崩溃源）。2026-10-24 起 `SettingsDialog`（打开日志目录失败）与 `PlayerBar`（设置对话框异常）也走 `AppLog`，WinUI 壳不再有 `Debug.WriteLine` 出口。本机读取 Windows 应用程序日志 / WER 仍被安全策略拒绝，所以"没有崩溃记录"依旧没有第三方佐证，但应用侧至少有了可读的日志出口。

---

## 10. 文档索引

**当前文档**
- [README](../README.md) — 入口、快速开始、当前限制
- PROJECT.md（本文）— 架构、模块、数据流
- [COUPLING.md](./COUPLING.md) — 耦合分析与隐式契约
- [README.md](./README.md) — 文档地图
- [PHASE30-ACCEPTANCE.md](./PHASE30-ACCEPTANCE.md) — Phase 30 音频实时路径性能重写验收（当前；core + GUI 验收完成 2026-11-13）
- [PHASE30-AUDIO-BASELINE.md](./PHASE30-AUDIO-BASELINE.md) / [PHASE30-AUDIO-AFTER.md](./PHASE30-AUDIO-AFTER.md) — Phase 30 改写前/后 AudioBench 读数（含 `-RUN2` 复跑）
- [PHASE29-ACCEPTANCE.md](./PHASE29-ACCEPTANCE.md) — Phase 29 非音频路径现代化验收（上一阶段）
- [PHASE28-ACCEPTANCE.md](./PHASE28-ACCEPTANCE.md) — Phase 28 升级到 .NET 11 验收（上一阶段）
- [PHASE27-ACCEPTANCE.md](./PHASE27-ACCEPTANCE.md) — Phase 27 多碟专辑归并验收（历史）
- [PHASE26-ACCEPTANCE.md](./PHASE26-ACCEPTANCE.md) — Phase 26 库→歌单验收（历史）
- [PHASE25-ACCEPTANCE.md](./PHASE25-ACCEPTANCE.md) — Phase 25 搜索/监听验收（历史）
- [PHASE24-ACCEPTANCE.md](./PHASE24-ACCEPTANCE.md) — Phase 24 库视图验收

**历史验收与决策**
- [PHASE23-ACCEPTANCE.md](./PHASE23-ACCEPTANCE.md)
- [PHASE22-ACCEPTANCE.md](./PHASE22-ACCEPTANCE.md)
- [PHASE21-ACCEPTANCE.md](./PHASE21-ACCEPTANCE.md)
- [PHASE20-COMPARISON.md](./PHASE20-COMPARISON.md)

**当前设计稿（Phase 14–30，按需查阅）**
- `docs/superpowers/specs/` — Phase 14–30 的 D-player 设计稿；Phase 24/25 库视图与搜索/监听、Phase 26 库→歌单、Phase 28 net11 升级、Phase 29 非音频现代化、Phase 30 音频实时路径设计稿是当前实现的直接依据。
- [Phase 30 设计：音频实时路径性能重写](./superpowers/specs/2026-11-13-d-player-phase30-audio-realtime-design.md) — AudioBench 基线与目标、确定性安全网、频谱离线程/EQ 无锁/播放链重构、验收与已知边界（§2.1/§4.2/§7.3/§10.4/§11 含 2026-11-13 对 NAudio `SetPeakingEq` 状态保持误记的校正）。
- [Phase 29 设计：非音频路径现代化](./superpowers/specs/2026-11-12-d-player-phase29-modernization-design.md) — RootGates/树查找修复、JsonFileStore/源生成、union/closed 模型、生命周期与验收边界（含 Phase 28 附录 C 的复核修正）。
- [Phase 28 设计：升级到 .NET 11](./superpowers/specs/2026-11-12-d-player-phase28-net11-upgrade-design.md) — 四工程抬 net11.0 的直接依据（含三个失败域与回退、Rider 红线定性、C# 15 / .NET 11 BCL 核实附录、Phase 29/30 移交盘点）。
- 设计稿不回改；与最终实现不一致处，以实现与验收记录为准。

**归档（历史执行材料）**
- `docs/archive/specs/` — UmaPlayer 时代（Phase 1–13）设计稿。
- `docs/archive/plans/` — Phase 1–23 实现计划（执行清单，已被代码、验收记录与 git 历史覆盖）。
- 四份 Phase 24–30 计划刻意未跟踪，保留在 `docs/superpowers/plans/` 本地工作区，不属于仓库发布面。

**查历史提交**：`git log --oneline --all`；按阶段查用 `git log --grep "phase24"` / `git log --grep "phase25"` / `git log --grep "phase26"`。细节不再复制到文档里。
