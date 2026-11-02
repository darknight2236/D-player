# Phase 24 验收记录：艺术家 / 专辑库视图

- 日期：2026-11-02
- 范围：共享 Core + **WPF 主壳**（音乐库 = 可配置多根目录、艺术家/专辑分组、专辑/艺术家详情、隐藏的专用库播放队列、断点续播）。WinUI 壳自 2026-11-02 冻结，本阶段**不**实现库 UI，只要求 `-Full` 门禁 3 继续编译通过。
- 状态：**实现完成 / 验收完成**。9 项实测项 PASS；6 项未单独走查（见「未单独走查」一节，均为同路径、单测或结构不变项）。
- 依据：[设计](superpowers/specs/2026-11-02-d-player-phase24-library-view-design.md)、计划（`docs/superpowers/plans/2026-11-02-d-player-phase24-library-view-implementation.md`，**该计划文件刻意未入库**，只在本地工作区，链接不可用）、各任务报告（`.superpowers/sdd/2026-11-02-d-player-phase24-library-view-implementation/`，**SDD 工作区被 Git 忽略，链接与报告仅本地可用**）。
- 历史验收：[Phase 21](./PHASE21-ACCEPTANCE.md) / [Phase 22](./PHASE22-ACCEPTANCE.md) / [Phase 23](./PHASE23-ACCEPTANCE.md) 的 PASS 不自动成为 Phase 24 的 PASS。

## 实现提交

```
dc249e4 docs(phase24): add the artist/album library view design
81bdd0e feat(core): add library folders and playback queue schema
5bc9cbb feat(playlists): add a dedicated library playback queue
6316b12 test(playlists): harden library queue lookup coverage
ff95415 test(playlists): make the no-tag-read hydrate test discriminating
7e001fe feat(library): scan and group a multi-root music library
550197b fix(library): distinguish unavailable roots and harden scanning
a059299 feat(shell): wire library initialization and navigation state
c69d857 fix(shell): harden library startup and resume coverage
61ed56b feat(settings): manage music library folders
2d4c686 fix(settings): make the library section scrollable and clean the test warning
69e1617 feat(wpf): add the artist and album library views
011dfb4 fix(wpf): keep library buttons on the dark theme
```

验收记录本身（`docs/PHASE24-ACCEPTANCE.md` + README/PROJECT/COUPLING）为收尾文档提交，不含代码改动。Task 2 的两轮测试加固（`6316b12`、`ff95415`）与 Task 3/4/5/6 的 fix 轮都在上面的范围内。

## 门禁

最终门禁由代理在 2026-11-02 实跑（非转录）：

```bash
powershell -File tools/verify-gates.ps1 -Full
```

实际输出摘要（末行 `all requested steps passed.`）：

| 门禁 | 命令 | 实际结果 |
|---|---|---|
| 门禁 1/3 | `dotnet build D-player.slnf -c Debug --nologo -v q` | 0 警告 / 0 错误 |
| 门禁 2/3 | `dotnet test D-player.slnf -c Debug -v q --filter Category!=AudioDevice` | **总计 215 / 成功 215 / 失败 0 / 跳过 0** |
| 门禁 3/3 | `dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q` | 0 警告 / 0 错误 |

本记录只记计数与退出状态：构建/测试耗时随机器与缓存状态变化，不复述秒数。

补充的音频分档实跑：

```bash
dotnet test D-player.slnf -c Debug -v q --filter "Category=AudioDevice"   # 总计 5 / 成功 5 / 失败 0
```

`tools/verify-gates.ps1` 本任务**未修改**。

## 测试过滤与计数说明（实测，2026-11-02）

| 命令 | 总计 | 说明 |
|---|---|---|
| `dotnet test D-player.slnf -c Debug -v q`（无过滤） | **220** | 212 个 `[Fact]` + 1 个 `[Theory]` 展开的 8 条 `[InlineData]` |
| `... --filter "Category!=AudioDevice"`（门禁 2） | **215** | 默认门禁口径 |
| `... --filter "Category=AudioDevice"` | **5** | 真实 WASAPI 集成（`NAudioPlaybackServiceConcurrencyTests` 3 + `NAudioPlaybackServiceStopSemanticsTests` 2，均 `[Trait("Category","AudioDevice")]` + `[Collection("AudioDevice")]`） |

- 三者互差正好 5 条，说明**本次运行中否定过滤器确实排除了音频测试**：默认门禁 215 不需要音频设备，需要覆盖真实输出路径时用 `verify-gates.ps1 -Audio` 追加 5 条。
- 验收走查阶段曾记录一条相反观察（"否定过滤器未排除音频测试，215 条里含 5 条设备测试"）。本任务两次独立实跑（上面的单项命令 + `-Full` 的门禁 2）均**未复现**；记录在此以免后人把 215 误解成"含音频测试"。文档的计数以本表实测为准：**总数 220 = 215 默认 + 5 音频**。
- MTP 纪律照旧：两条 `dotnet test` 命令都**不能**加 `--nologo`（加上后一条测试都不跑，摘要却打印成功、退出码 5）。

## 验收环境与数据

- 临时音乐库：`%TEMP%\dplayer-phase24-acceptance`，含 3 个专辑文件夹（`Album1` / `Album2` / `Album3`）与 9 个真实 FLAC 文件（从用户既有队列复制，每张专辑 3 首、均带内嵌封面）。验收后该临时目录已删除。
- 用户数据保护：验收前备份 `settings.json` / `queue.json` / `library-cache.json`，验收后**逐字节还原**（三者 SHA256 与备份相同），临时库目录已移除。
- 壳：`bin\Debug\net10.0-windows\D-player.exe`（WPF 主壳）；播放检查期间系统音量静音。
- 走查方式：Computer Use 驱动真实窗口（UIA 树读数 + 截图/像素检查 + 磁盘 JSON 字段核对），不是"读代码推断"。
- 隐私：本记录只用通用标签（`Album1..3`、3/3/9 等计数）、通用指代（如「一个可见歌单」）与字段名，**不含**用户的真实歌单名、艺术家名、专辑名、曲目名，也**不含**任何绝对路径。

## 验收项

| # | 项目 | 预期 | 结果 | 证据类型 |
|---|---|---|---|---|
| 1 | 设置添加 / 保存 / 重开 / 移除库根目录 | 音乐库区可经 Win32 文件夹选择器添加根；保存写入 `settings.json`；重开设置可见；移除后保存生效 | **PASS**：添加 → 保存 → 重开可见 → 两个根均移除并保存；随后库空态显示 `还没有音乐库文件夹` / `在设置中添加一个或多个音乐文件夹` / `打开设置`；移除两个根之后，正在播放的库音频仍在继续播放（删除根不影响已在播放的库队列，设计 §8） | UIA 树 + `settings.json` 字段 + 播放栏读数 |
| 2 | 多根扫描与分组 | 状态行与列表数量正确；艺术家行显示 `N 张专辑 · M 首`；专辑 tile 渲染内嵌封面 | **PASS**：状态行与艺术家列表 **3 位艺术家 · 3 张专辑 · 9 首**；每个艺术家行 `1 张专辑 · 3 首`；三张专辑 tile 均渲染内嵌封面 | UIA 树 + 视觉检查 |
| 3 | 专辑详情 | 封面、标题、艺术家、`播放全部`、按曲目号排序的曲目与时长 | **PASS**：一张专辑打开后显示封面/标题/艺术家/`播放全部` 与 3 首曲目（含曲目号与时长） | UIA 树 |
| 4 | 播放（播放全部 / 下一首 / 双击 / 上一首） | 从正确索引起播；上一首/下一首在同一上下文内推进；播放栏元数据正确 | **PASS**：`播放全部` → 第 1 首（播放栏 0:02）；下一首 → 第 2 首（0:01）；双击第 3 首 → 该曲（0:01）；上一首 → 第 2 首（0:01）；播放栏标题/艺术家/专辑/采样率正确 | UIA 树 + 播放栏读数 |
| 5 | 隐藏库播放队列 | `queue.json` 使用保留 Id，恰好 3 首；可见歌单集合不变；库队列不进侧栏列表 | **PASS**：`CurrentPlaylistId = "__library-playback__"`、`LibraryPlayback.Id = "__library-playback__"`、`LibraryPlayback.Items.Count = 3`、`Playlists.Count = 5`（可见歌单未变，侧栏不出现库队列） | 文件字段（`queue.json`） |
| 6 | 侧栏 ▶ 与双指针 | 库播放时「音乐库」显示 ▶；点可见歌单切回队列视图但不断音；点库节点回到库视图 | **PASS**：库播放期间 音乐库 旁出现 ▶，点击一个可见歌单后 ▶ 仍在；中间面板切回该歌单的队列视图而库音频继续播放；点库节点回到库视图 | UIA 树 + 视觉检查 |
| 7 | 重启 / 断点续播 | 关闭重启后恢复最后一首库曲目（暂停、不出声）；▶ 与 `CurrentPlaylistId` 仍指向库队列；缓存分组立即可见 | **PASS**：重启后播放栏恢复最后库曲目于 0:18（暂停）、音乐库 ▶ 仍在、`CurrentPlaylistId` 仍指向库队列；点 艺术家 立即显示 3/3/9（来自缓存） | UIA 树 + 文件字段 |
| 8 | 多根去重（嵌套根） | 同时加入父根与嵌套子根并重扫，计数不重复 | **PASS**：加入 `...\Album2`（父根的子目录）后重扫仍为 3/3/9 | UIA 树 |
| 9 | 主题与布局（Task 6 修复回归） | 侧栏库节点与 `播放全部` 走深色模板（非 Aero）；活跃节点在悬停时仍可见 accent；设置内容可滚动、`取消`/`保存` 固定 | **PASS**：侧栏库节点与 `播放全部` 为深色模板；活跃节点悬停时像素仍为 `#5C2DCF`（`AccentPressed`），空闲节点 `#2A2A3C`（与侧栏同色，无 Aero 底/边框），悬停 `#3E3E52`（`BackgroundTertiary`）；设置在窗口高度不足时内容滚动而按钮行固定 | 像素采样（Task 6 报告）+ 视觉检查 |

## 未单独走查

以下项在这次 GUI 走查中**没有**单独执行，不记为 PASS（实现路径与单测覆盖写在右列，属于证据边界而非缺陷结论）：

| 项 | 状态 | 覆盖情况 |
|---|---|---|
| 艺术家详情的 `播放全部` / 艺术家范围双击曲目 | NOT SEPARATELY WALKED | 与专辑详情共用同一播放方法（`LibraryViewModel` → `PlaylistsViewModel.PlayLibraryTracksAsync`），分组与"艺术家全部曲目"由 `LibraryViewModelTests` / `LibraryGroupingTests` 覆盖 |
| 窄窗口布局（侧栏/库视图不重叠、可滚动） | NOT SEPARATELY WALKED | 走查窗口为标准尺寸；布局为既有 5 列 Grid + `WrapPanel`，无新隔离容器 |
| 侧栏歌单拖拽重排回归 | NOT SEPARATELY WALKED | 库队列不在 `Playlists` 集合内，拖拽索引映射未变；拖拽会重写 `queue.json`，本次按"不改用户数据"原则未动 |
| 不可用根目录（手动路径） | NOT SEPARATELY WALKED（GUI） | `ScanFolderWithStatus` 的不可用判定与"跳过而非当空扫描"由单测覆盖（`LibraryScannerServiceTests` / `LibraryViewModelTests`），GUI 未构造无权限/离线根 |
| 设置 `取消` 语义 | NOT SEPARATELY WALKED | Task 5 手工做了"添加/保存/重开/移除"，未在本次走查中验证"改动后取消不落盘"；实现在对话框草稿上修改、仅保存时写 `LibraryFolders` |
| 库上下文下的 Shuffle/Repeat | NOT SEPARATELY WALKED | 隐藏库队列复用 `PlaylistsViewModel` 的全局 Shuffle/Repeat（与可见歌单同一组字段，`PlayLibraryTracksAsync` 只替换队列内容、保留模式），推进算法由既有单测覆盖；本次走查只用顺序播放验证了 `播放全部`/上一首/下一首/双击，没有在库上下文下开关 Shuffle/Repeat，故不记为 PASS |

## 已知限制（与设计 §9 一致）

- 专辑按 `(Album, Artist)` 分组；**未引入 `AlbumArtist`**，合辑/原声带可能被拆成多张专辑。后续要修需缓存 schema 迁移，本轮刻意不做。
- 不做搜索；库很大时只能滚动浏览。
- 不做文件系统实时监听；库变化在启动、手动刷新、设置保存后生效。
- 专辑封面只读该专辑第一首曲目的内嵌图；封面只在后续曲目时显示占位。
- 库播放队列不出现在侧栏，也没有独立的队列管理界面；控制方式就是播放栏的上一首/下一首/Shuffle/Repeat。
- 专辑封面网格未做 UI 虚拟化（`WrapPanel`），大库首次进入专辑页会逐张惰性读封面，首屏可能较慢。
- 库视图页签不持久化：重启后回到队列视图（库播放队列仍按 `CurrentPlaylistId` 恢复）。
- 进入库视图会清空 `ViewedPlaylist`：库视图打开期间侧栏 `−` 按钮与 PlayerBar ▶ 回落不可用，要回到队列视图需点选一个可见歌单。

## 数据还原与副作用

- 临时库 `%TEMP%\dplayer-phase24-acceptance` 已删除；真实 FLAC 仅复制、未改动原件。
- `settings.json` / `queue.json` / `library-cache.json` 在验收前备份、验收后逐字节还原（SHA256 三者均与备份一致；临时目录已移除）。
- 走查期间系统静音用于播放检查；未改动 EQ/频谱设置。
- 其他环境事实：验收使用的临时库与备份位于 `%TEMP%` 与 SDD 工作区，均不在 Git 发布面；本文件不引用其中任何真实名称或绝对路径。
