# D-player

> 本地优先的 Windows 音乐播放器（.NET 10 + NAudio）。共享层 `D-player.Core` 为 WPF-free；**WPF 主壳**承载完整功能，WinUI 3 壳自 2026-11-02 冻结，仅由门禁 3 编译防腐烂。原名 UmaPlayer，C# 命名空间为 `DPlayer`。

## 特性

- **播放**：播放 / 暂停 / 上一首 / 下一首 / 随机 / 循环；进度条拖拽与单击跳转（≈30 Hz 节流）；音量滑块与一键静音。
- **格式**：MP3 / WMA / FLAC / AAC / WAV（Windows Media Foundation 原生解码）。
- **元数据**：标题 / 艺术家 / 专辑 / 流派 / 年份 / 采样率 / 曲目号 / 内嵌封面（z440.atl.core）。
- **队列与歌单**：内存队列（多选入队、删除、清空、自动推进、表头排序）；多命名歌单（Viewed/Current 双指针）；文件夹绑定歌单（递归扫描 + 后台增量同步 + 元数据缓存）。
- **音乐库**（Phase 24/25/26）：多库根目录；艺术家列表 / 专辑网格 / 艺术家详情 / 专辑详情四级钻取；按专辑惰性读取封面；独立的隐藏库播放队列；断点续播与重启恢复；全局搜索（艺术家/专辑/曲目三组、200ms 去抖、CJK 字面匹配、Ctrl+F）与按库根实时监听（800ms 静默窗口 / 5s 上限、删除/改名不打断播放）；库内单曲 / 专辑可右键加入普通歌单（Phase 26）。
- **播放列表文件**：导入 `.m3u` / `.m3u8` / `.pls`（相对路径、UTF-8/UTF-16(BOM)/GBK 探测、严格过滤与跳过计数）；导出 `.m3u8`（绝对路径 + `#EXTINF`）。
- **拖拽**：外部音频文件拖入入队；队列内单/多选拖拽重排（插入线 + 高亮）。
- **持久化**：窗口几何、音量、队列、歌单、频谱/EQ、库根与库导航、断点续播均落盘。
- **音频可视化**：32 条频谱柱（8192 点 FFT + 汉宁窗 + 50% 重叠 + 对数分组 20 Hz–16 kHz + RMS/gamma）；4 种主题；灵敏度/平滑度可调。
- **均衡器**：10 段图形 EQ（31 Hz–16 kHz，±12 dB + preamp）；9 个内置预设 + Custom；拖动实时生效。
- **界面与主题**：全量描边矢量图标；无边框自定义标题栏；内置深色主题（深紫强调色），主要控件均已深色化。

## 技术栈

| 层 | 选型 |
|---|---|
| 运行时 | .NET 10（Core/WPF 为 `net10.0-windows`；WinUI 为 `net10.0-windows10.0.19041.0` + x64） |
| UI | WPF 主壳（`UseWPF=true`）· WinUI 3 / Windows App SDK 2.6.4（冻结，unpackaged + self-contained） |
| 共享层 | `D-player.Core`：Models / Services / ViewModels / Configuration / DI，**不得出现 WPF 类型** |
| MVVM / DI / 配置 | CommunityToolkit.Mvvm 8.x · Microsoft.Extensions.DependencyInjection 10.x · Configuration.Json + `IOptions<AppSettings>` |
| 音频 | NAudio 3.3.0（`NAudio.Core` + `NAudio.Wasapi`；`MediaFoundationReader` 解码，`WasapiPlayer` Shared 输出） |
| 标签 / 测试 | z440.atl.core 7.18 · xUnit v3（MTP）+ NSubstitute；5 条真实 WASAPI 集成测试标记 `Category=AudioDevice` |

## 构建与运行

环境：Windows 10 2004（build 19041）及以上、.NET 10 SDK；构建 WinUI 壳需 nuget.org 可达。

### 门禁 1/2（每次改动）

```bash
dotnet restore D-player.slnf
dotnet build   D-player.slnf -c Debug --nologo -v q      # 门禁 1：0 警告 0 错误
dotnet test    D-player.slnf -c Debug -v q --filter "Category!=AudioDevice"   # 门禁 2：294 通过 0 失败
dotnet test    D-player.slnf -c Debug -v q --filter "Category=AudioDevice"    # 5 条真实音频（需设备）
dotnet run     --project D-player.csproj                 # 运行 WPF 壳
```

- `D-player.slnf` 只含 Core + WPF + Tests；WinUI 刻意不进日常门禁。
- 当前 **294 默认测试 + 5 真实音频 = 299**；默认门禁实跑 294，`-Audio` 才执行 5 条音频测试。
- 两条测试命令**不要加 `--nologo`**：MTP 不识别该参数，会零测试运行却打印“成功: 0”（退出码 5）。`--nologo` 只用于 `dotnet build`。
- `dotnet test` 依赖仓库根 `global.json` 的 `{"test":{"runner":"Microsoft.Testing.Platform"}}`，不要删除。

### 门禁 3（WinUI 编译）

```bash
dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q   # 0 警告 0 错误
dotnet run   --project D-player.WinUI/D-player.WinUI.csproj -c Debug
```

### 一键脚本与发布

```bash
powershell -File tools/verify-gates.ps1 -Fast          # 门禁 1 + 2
powershell -File tools/verify-gates.ps1 -Full          # 门禁 1 + 2 + 3
powershell -File tools/verify-gates.ps1 -Full -Audio   # 再加 5 条真实音频

dotnet publish D-player.csproj -c Release -r win-x64 \
    --self-contained false /p:PublishSingleFile=true
```

## 项目结构

```text
D-player/                    # 仓库根 = WPF 壳工程
├── App.xaml(.cs)            # DI 容器、数据迁移、主窗口
├── Views/ Converters/ Themes/
├── D-player/Services/       # 仅 WPF 壳：Win32FileDialogService
├── D-player.Core/           # 共享层（WPF-free）：Models / Services / ViewModels / Configuration
├── D-player.WinUI/          # WinUI 3 壳（冻结；不进 .slnf，由门禁 3 编译）
├── Tests/                   # xUnit v3，只引用 Core
├── tools/                   # verify-gates.ps1、coupling-audit/
├── appsettings.json         # 启动默认值
├── D-player.slnf            # 门禁 1/2 的解决方案筛选器
└── docs/                    # 当前文档 + archive/（历史设计与计划）
```

## 配置与数据

- **启动默认值**：`appsettings.json` 的 `"Player"` 节（两壳共用同一份）。
- **运行时数据目录**（由各壳注入，不能共用）：
  - WPF：`%LocalAppData%\D-player\`
  - WinUI：`%LocalAppData%\D-player-winui\`
- **文件**：
  - `settings.json` — 窗口几何、默认音量、频谱/EQ、库根目录、库导航、断点续播。
  - `queue.json` — 可见歌单、当前播放指针、全局 Shuffle/Repeat，以及隐藏的 `LibraryPlayback` 库队列（Schema v3）。
  - `library-cache.json` — 文件夹歌单与音乐库根共用的元数据缓存（按根目录分组；不含封面字节）。

## 开发阶段

| 阶段 | 内容 |
|---|---|
| 1–3 | 单曲播放骨架、内存队列、ViewModel 拆分与技术债清算 |
| 4–9 | 队列持久化、拖拽、多命名歌单、VM 测试体系、侧栏重排 |
| 10–14 | 文件夹绑定歌单、设置、UI 重构、FFT 频谱、10 段 EQ |
| 15–19 | 耦合审计、矢量图标、无边框深色标题栏、播放列表文件、依赖迁移 |
| 20–23 | WinUI 3 第二壳：共享 Core 抽取、视觉打磨、功能补齐、频谱/EQ；**2026-11-02 起冻结** |
| 24 | **音乐库视图（WPF）**：多库根、四级钻取、隐藏库播放队列、断点续播；验收完成 2026-11-02 |
| 25 | **音乐库搜索与实时监听（WPF）**：三组搜索、Ctrl+F、按根 watcher、可等待清理；验收完成 2026-11-06 |
| 26 | **音乐库 → 歌单互通（WPF）**：右键加入普通歌单、新建并加入；验收完成 2026-11-11 |

WinUI 壳保留但不再新增功能；后续开发在 Core + WPF。Avalonia 全量重构仅为未来可能（未排期、未选型）。

## 已知限制

- 仅支持 Windows Media Foundation 原生解码的格式；OGG/Vorbis 需系统额外编解码器。
- WASAPI Exclusive / ASIO 与多设备切换接口已预留、尚未实现。
- 播放列表导出仅 M3U8（绝对路径）；不导出 PLS、不做相对路径导出。
- 列表文件中的网络流条目导入时计入“格式不支持”，不支持流媒体播放；`#EXTINF` / PLS `Title=` 元数据刻意忽略。
- 音乐库专辑身份为 `(Album, 所在文件夹)`：同文件夹合辑会合并，多碟子文件夹仍可能拆开；同文件夹同名不同专辑（罕见）会合并。
- 库搜索为内存字面匹配（`OrdinalIgnoreCase` 子串，不做拼音/模糊/正则/分词），查询与结果不持久化。
- 实时监听为根级 best-effort：只监听文件名/目录名变化，不保证网络根、复制中文件、缓冲区溢出场景的固定延迟；删除/改名不主动停止当前播放。
- 库播放队列没有独立管理界面，也不出现在侧栏。
- 专辑封面只读该专辑第一首曲目的内嵌图；专辑网格未做 UI 虚拟化，大库首次进入可能较慢。
- 进入库视图会清空当前查看的歌单（`ViewedPlaylist`）；库视图期间侧栏 `−` 与 PlayerBar 的无曲回落不可用，需点选一个可见歌单回到队列视图。
- 库内曲目 / 专辑只能加入普通歌单（文件夹歌单由目录管理）；不支持拖拽入歌单。
- WinUI 壳冻结且没有自动行为回归：它只有门禁 3 编译保护，XAML 运行时行为仍需真机手测；拖拽、表头排序、文件夹绑定管理在 WinUI 侧仍未实现。

## 文档

- [项目架构与模块详解](docs/PROJECT.md)
- [耦合分析与隐式契约](docs/COUPLING.md)
- [文档地图](docs/README.md) · [历史归档](docs/archive/README.md)
- 最近验收：[Phase 26](docs/PHASE26-ACCEPTANCE.md) · [Phase 25](docs/PHASE25-ACCEPTANCE.md) · [Phase 24](docs/PHASE24-ACCEPTANCE.md)
- 历史验收与 WinUI 决策：[Phase 22](docs/PHASE22-ACCEPTANCE.md) · [Phase 21](docs/PHASE21-ACCEPTANCE.md) · [Phase 20 对比](docs/PHASE20-COMPARISON.md)
