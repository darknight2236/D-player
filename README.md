# D-player

> 一个轻量级、本地优先的 Windows 音乐播放器（WPF + .NET 10 + NAudio）。
> 灵感来源于 foobar2000。原名 UmaPlayer。

---

## 特性

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

---

## 技术栈

| 层 | 选型 |
|----|------|
| 运行时 | .NET 10（`net10.0-windows`） |
| UI 框架 | WPF（`UseWPF=true`） |
| MVVM | [CommunityToolkit.Mvvm 8.x](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) |
| DI 容器 | `Microsoft.Extensions.DependencyInjection` 10.x |
| 配置 | `Microsoft.Extensions.Configuration.Json` + `IOptions<AppSettings>` |
| 音频引擎 | [NAudio 3.1.0](https://github.com/naudio/NAudio)（引用收窄为 `NAudio.Core` + `NAudio.Wasapi` 两个子包；`MediaFoundationReader` 解码 + `WasapiPlayer` WASAPI Shared 输出） |
| 元数据/标签 | [z440.atl.core 7.18](https://github.com/Zeugma440/atldotnet) |
| 测试 | xUnit v3（Microsoft.Testing.Platform）+ NSubstitute + Coverlet |

---

## 环境要求

- Windows 10 / 11
- .NET 10 SDK

## 构建与运行

```bash
dotnet restore D-player.sln
dotnet build   D-player.sln -c Debug
dotnet run     --project D-player.csproj
```

可执行文件输出于 `bin/Debug/net10.0-windows/D-player.exe`。

发布独立单文件：

```bash
dotnet publish D-player.csproj -c Release -r win-x64 \
    --self-contained false /p:PublishSingleFile=true
```

## 测试

```bash
# xunit v3 的测试工程是可执行程序，runner 是 Microsoft.Testing.Platform（MTP）
dotnet run --project Tests/D-player.Tests.csproj -c Debug   # 直接跑可执行程序
dotnet test D-player.sln -c Debug                           # 同一个 runner 的另一条命令
```

两条命令跑的是**同一个 runner（MTP）**，不是两套 runner：`dotnet test` 之所以仍可用，靠的是仓库根 `global.json` 里的 `{"test":{"runner":"Microsoft.Testing.Platform"}}`（.NET 10 SDK 的原生 opt-in）——**删掉 `global.json` 这条命令就失败**。两条都**不要加 `--nologo`**：MTP 不认这个参数，加上后一条测试都不会跑，摘要却打印 `成功: 0`（易被当成全绿；实际是"运行了零个测试" + 退出码 5）；`--filter "FullyQualifiedName~X"` 照常可用。

当前共 **161** 个单元测试（Models / Services / ViewModels 全覆盖 + View 层纯字符串函数 `PlaylistImportReportFormatter`；其余 View 层代码按项目惯例不做单测，由手动验收把关）。

---

## 项目结构

```
D-player/
├── App.xaml(.cs)        # 应用入口：构建 DI 容器、加载主窗口
├── Configuration/       # AppSettings 强类型配置 record
├── Models/              # 不可变 record 数据模型（Track / Playlist / QueueState / EqualizerConfig …）
├── Services/            # 业务与基础设施服务（接口 + 实现：播放/持久化/元数据/扫描/缓存/播放列表文件读写）
├── ViewModels/          # MVVM ViewModel（MainViewModel 门面 + Player/Playlist/Playlists 子 VM）
├── Views/               # XAML 视图、对话框（Settings/Equalizer/Prompt）、自定义控件
├── Converters/          # 值转换器
├── Themes/              # 深色主题资源字典（Colors / Fonts / Controls）
├── Extensions/          # DI 注册扩展（AddDPlayerServices）
├── Tests/               # xUnit v3 测试项目（可执行程序，跑 MTP）
└── docs/                # 项目文档、耦合登记册、各阶段设计稿与实现计划
```

## 配置与数据

- **构建时默认值**：`appsettings.json` 的 `"Player"` 节（启动快照）。
- **运行时数据**（`%LocalAppData%\D-player\`）：
  - `settings.json` — 窗口几何、默认音量、频谱与 EQ 设置
  - `queue.json` — 歌单与队列快照（Schema v3）
  - `library-cache.json` — 文件夹歌单的元数据缓存

## 架构概览

严格分层、单向依赖：`View → ViewModel → Service → Model`；跨边界一律走接口；DI 容器集中注册（无 Service Locator、无 static 单例）。

播放链（`NAudioPlaybackService`）：

```
MediaFoundationReader → EqualizerSampleProvider → SampleAggregator → VolumeSampleProvider → WasapiPlayer
                        （10 段 EQ，Phase 14）      （FFT 频谱，Phase 13）        （Shared + 事件同步 + 100ms，Phase 19）
```

核心抽象：`IPlaybackService`、`IPlaylistService`、`ISettingsPersistence`、`ITrackMetadataReader`、`ILibraryScannerService`、`ILibraryCache`、`IPlaylistFileService`（Phase 18 播放列表文件读写门面：导入绝不抛、导出抛给 VM 转文案）。

> 完整的模块详解、数据流、隐式契约登记册见 [`docs/PROJECT.md`](docs/PROJECT.md) 与 [`docs/COUPLING.md`](docs/COUPLING.md)。

---

## 开发阶段（Phase 1–18）

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

每个阶段的设计稿与实现计划归档于 [`docs/superpowers/`](docs/superpowers/)（`specs/` 与 `plans/`）。

---

## 已知限制

- 仅支持 Windows Media Foundation 原生解码的格式；OGG/Vorbis 需系统额外编解码器。
- 多设备 / 输出模式切换（WASAPI Exclusive / ASIO）接口已预留、尚未实现。
- 播放列表导出仅 M3U8（绝对路径）；不导出 PLS、不做相对路径导出。
- 列表文件里的网络流条目（http:// 等）导入时计入"格式不支持"跳过，不支持流媒体播放；`#EXTINF` / PLS `Title=` 元数据刻意忽略（标题与时长只信音频文件）。
- 按艺术家/专辑组织的音乐库视图尚未实现。
