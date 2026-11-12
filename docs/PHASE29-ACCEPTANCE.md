# Phase 29 验收记录（非音频路径现代化）

- 日期：2026-11-12
- 分支：`phase29-modernization`
- 基线：`master` / `a1700af`；设计提交 `302f6f9`、边界修正 `12b00cf`
- 设计：[2026-11-12-d-player-phase29-modernization-design.md](./superpowers/specs/2026-11-12-d-player-phase29-modernization-design.md)
- 实施提交区间：`3236398..2921daa`（逐条见 `git log`；设计文档另有两笔 docs 提交）
- 范围：非音频路径现代化；**音频实时路径与 WinUI 壳零文件改动**

## 1. 门禁读数

入口：`powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-gates.ps1 -Full -Audio`，末行 `all requested steps passed.`

| 关 | 命令 | 实测 |
|---|---|---|
| 门禁 1 | `dotnet build D-player.slnf -c Debug --nologo -v q` | **0 警告 / 0 错误**（1.28s） |
| 门禁 2 | `dotnet test D-player.slnf -c Debug -v q --filter "Category!=AudioDevice"` | **336 通过 / 0 失败 / 0 跳过**（1.743s） |
| 音频 | `dotnet test D-player.slnf -c Debug -v q --filter "Category=AudioDevice"` | **5 通过 / 0 失败 / 0 跳过**（3.822s） |
| 门禁 3 | `dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q` | **0 警告 / 0 错误**（16.55s） |

- 全量测试口径：**341 = 336 默认 + 5 音频**；Phase 29 新增 26 条默认测试。
- 测试命令未加 `--nologo`；MTP 零匹配退出码 8 未被当作成功。

## 2. 新增测试（26 条）

| 文件 | 新增 | 覆盖 |
|---|---|---|
| `Tests/Services/LibraryRootScannerTests.cs` | 4 | 根闸门归零、异根并行、持有中撤销、预取消等待者不泄漏 |
| `Tests/Services/JsonPlaylistServiceTests.cs` | 1 | v1 fixture → v3 迁移（钳制/枚举/落盘） |
| `Tests/Services/JsonFileStoreTests.cs` | 2 | 原子写替换 + tmp 清理；锁定目标失败保留旧文件 |
| `Tests/Services/JsonSettingsPersistenceTests.cs` | 1 | settings 写失败保留旧设置 |
| `Tests/ViewModels/OneShotCleanupTests.cs` | 3 | 同 Task/只跑一次、异常传播、晚到完成丢弃 |
| `Tests/Models/AudioConstantsTests.cs` | 9 | 音频后缀大小写、空/无后缀拒绝、对话框过滤器 |
| `Tests/Services/JsonContractParityTests.cs` | 3 | QueueState / AppSettings / 缓存字典的反射 vs 源生成全量输出对照 |
| `Tests/Services/DisposalTests.cs` | 2 | Json 服务双 Dispose 不抛；watcher 双 Dispose 不抛 |
| `Tests/ViewModels/PlaybackResumeTests.cs` | 1 | `MainViewModel` 的 `IDisposable` 标记幂等 |

## 3. 独立核对项

- **Core WPF-free**：`grep -rn "System.Windows\|PresentationFramework\|ICollectionView\|CollectionViewSource" D-player.Core --include=*.cs` → **零命中**（排除 obj/bin）。
- **抑制项**：`#pragma warning disable` / `NoWarn` 在 Core/WPF/WinUI/Tests → **零命中**。
- **耦合审计**：`tools/coupling-audit/Invoke-CouplingAudit.ps1` → **0 cycles / 0 violations**。审计脚本新增对“无源文件命名空间”的空邻接保护（提交 `2921daa`），否则 `DPlayer.WinUI.Theme` 会让 BFS 抛空键异常；这是工具修复，不改变审计图口径。
  - M5 大文件读数（非门禁）：`PlaylistsViewModel.cs` 832、`PlaylistViewModel.cs` 760、WinUI `PlayerBar.xaml.cs` 665（冻结壳）、`LibraryViewModel.Scanning.cs` 616。
  - M6：`IAudioDeviceManager` / `IAudioOutputFactory` 仍为预注册未消费 Stub，属明示预留。
- **发布**：`dotnet publish D-player.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true` 成功；`bin/Release/net11.0-windows/win-x64/publish/D-player.exe` 3,219,538 bytes（2026-11-12 18:50:42），publish 目录 4 个文件。
- **输出目录**：`bin/Debug/net11.0-windows/D-player.exe` 73,728 bytes（18:48:53）；GUI 走查启动的是该路径的产物，不是旧 `net10.0-windows`。
- **Rider 红线**：代理侧不读取 IDE 诊断面板；**待用户在 Rider 内重新观察确认**（Phase 28 的红线机制未定性，不能沿用旧结论）。

## 4. GUI 走查（Computer Use + 主代理复核）

- 环境：Windows 11；合成库 `%TEMP%\phase29-synthetic-library`（5 个 20 秒 WAV、中文路径、`歌手甲/专辑二 (Disc 1|2)` 多碟目录、GBK(936) 编码 `中文歌单.m3u`）。
- 执行方式：Computer Use 子代理执行，主代理逐帧复核窗口树/截图；桌面锁屏期间停止注入，解锁后重跑。
- 结果：

| 项 | 结果 |
|---|---|
| 库加载 | 1 位未知艺术家、4 张专辑、5 首（WAV 无标签，按文件夹分组，符合预期） |
| 四级钻取 | 艺术家列表 → 艺术家详情（4 专辑）→ 专辑详情（曲目）→ 曲目 |
| 播放/暂停 | 20 秒样本播放正常；暂停后 1.2s 进度保持 0:11 不变 |
| 进度单击 | 暂停态点击进度条从 0:11 跳到 0:14 |
| 上一首/下一首 | 夜雨 → 下一首 第一碟 → 上一首 夜雨 |
| 音量/静音 | 滑块 0.8→0.686；点击扬声器静音到 0；再点击+滑块回 0.686 |
| 频谱 | 播放时频谱柱可见 |
| EQ | 启用；1k 段 +8 dB；预设变 Custom；保存后 settings 为 `EqualizerEnabled=True`、`EqualizerPreset=Custom`、`EqualizerBands=0,0,0,0,0,8,0,0,0,0` |
| 搜索 | 输入“远方”得到 1 条曲目结果 |
| 库右键 | 曲目右键“加入歌单…”→ 选“测试歌单”→ 加入成功 |
| GBK 导入 | `中文歌单.m3u` 导入成功：新歌单“中文歌单”，5 首入列，中文路径无乱码 |
| 拖拽重排 | “01 - 晨光”从第 1 位拖到第 5 位，顺序变为夜雨/第一碟/第二碟/远方/晨光 |
| 歌单新建/重命名/删除 | 新建“新歌单”→ 双击重命名为“测试重命名”→ 删除确认后侧栏只剩测试歌单/中文歌单 |
| 导入文件夹 | “导入文件夹”按钮把 5 首追加到当前歌单（5→10）；**文件夹绑定歌单的独立创建入口未在本轮单独验证**（明示覆盖缺口） |
| 关闭/重启续播 | 关闭时 `LastPlayedPath=远方.wav`、`Position=12.58s`；重启后恢复到 0:12/0:20 |
| 关闭中刷新/保存 | 库刷新后立即关闭，2.5s 内进程退出，无挂起 |

## 5. 数据保护

- 走查前备份 `%LOCALAPPDATA%\D-player` 下三份 JSON 到 `%TEMP%\phase29-data-backup`，SHA-256：
  - `library-cache.json` `4758EA35A977CABC12211E21537434EE9B37A8AF6209B4EC00DC0A6564C60BCF`
  - `queue.json` `810EA1BBB61B7D1374E36D7F5DDC7CE40D30DCD663BB3F8648460165E2F3B9C2`
  - `settings.json` `8FF849CA447F205841F5824B44F517DE53A877CEAA80EAD9C341C6E6E33AC236`
- 走查结束后按字节还原，三份 SHA-256 与备份**完全一致**。
- 合成库、备份、临时探针工程与脚本已删除；未修改用户音乐文件。

## 6. 探针结论

- **union**：设计稿里的字面探针 `union Probe(string Name, int Count)` 报 **CS1073**（union case 列表只接收类型名，不是参数声明）；修正为目标形状 `ArtistsNavigation/AlbumsNavigation/ArtistDetailNavigation(string ArtistName)/AlbumDetailNavigation(...)` 后在 `net11.0-windows` 编译并运行通过。`LibraryNavigationSnapshot` 最终采用 union；`_searchReturn` 用 `is { } pending` 解包；union 包装器没有旧 record 的值相等语义（全仓无消费者依赖）。
- **Search partial property**：CommunityToolkit.Mvvm 8.4.2 + `net11.0-windows` 下 `[ObservableProperty] public partial string ... { get; private set; } = ""` 编译通过；`SearchQuery`/`SearchMessage` 已替换，`SearchResults` 保留手写以保证“每次必通知”。
- **非音频 Lock**：8 个非音频 `object` gate 改为 `System.Threading.Lock`；音频 `EqualizerSampleProvider._lock` 与 `NAudioPlaybackService._chainGate` 保持 `object`，`SemaphoreSlim` 未动。

## 7. 已知边界

- **WinUI 只有编译覆盖**：壳已冻结，XAML 运行时模板挂接、`NavigationView`、`AppWindow.Closing` 落盘没有自动回归；Core 改动流入 WinUI 的运行时行为未走查。
- **音频实时路径零文件改动**：`NAudioPlaybackService`/`EqualizerSampleProvider`/`SampleAggregator` 未改；5 条真实音频测试通过，但音频性能与随机回归留给 Phase 30。
- **settings 写失败异常类型变化**：Windows 覆盖被锁文件时由 `IOException` 变为 `UnauthorizedAccessException`；当前调用方宽泛捕获或 fire-and-forget，无调用方依赖具体类型。
- **`LibraryRefreshScheduler` 仍未 Dispose**（既有缺口）；`LibraryViewModel` 的 `_queryCts`/`_lifetimeCts` 在极端关闭窗口存在 disposed-before-null/guard-token 窗口；刷新 fire-and-forget 与 watcher gate 释放存在极窄竞争，失败被 `_cleaned` 捕获吸收，不崩溃、不丢数据。
- **测试覆盖缺口**：ArtistDetail 导航持久化臂无专测；源生成 parity 测试不覆盖三个服务的 options 接线；`FilterAudioPaths`/`ImportFolderToCurrent` 无专测；文件夹绑定歌单的独立创建入口未在 GUI 单独走查。
- **union 探针字面 snippet 与 Search partial property 的细节**以本节为准，不要把字面 CS1073 误读为“union 不可用”。
- **Rider 红线待用户确认**；不为 IDE 诊断改产品代码、不加 `pragma`/`NoWarn`。

## 8. 结论

Phase 29 的非音频路径现代化已完成并通过三关门禁、5 条真实音频、合成库 GUI 走查与数据还原核对。实现范围与设计一致；音频路径与 WinUI 壳零文件改动。推送等用户明确指示。
