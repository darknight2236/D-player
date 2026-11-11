# Phase 27 验收记录：多碟专辑归并

- 日期：2026-11-11
- 代码基线：Phase 27 工作树改动（`LibraryIdentity` / `LibraryGrouping` / 测试 / 现状文档；验收时未提交）
- 范围：共享 Core + WPF 主壳；WinUI 壳继续冻结，只参与门禁 3 编译
- 设计：bounded 短设计（聊天内批准）；识别范围经用户拍板为「只认文件夹名」，不读 DiscNumber 标签

## 1. 自动化门禁

最终执行命令：

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-gates.ps1 -Full
```

| 门禁 | 结果 |
|---|---|
| 1/3 解决方案筛选器构建（Core + WPF + Tests） | 通过，0 警告 / 0 错误 |
| 2/3 默认测试（`Category!=AudioDevice`） | **310/310 通过**，0 失败，0 跳过 |
| 3/3 WinUI 3 构建 | 通过，0 警告 / 0 错误 |

计数口径：

- Phase 26 收尾默认 **294**；Phase 27 新增 **16** 条（9 个测试方法，含 8 个命名变体 theory 用例），当前默认 **310**。
- 红→绿证据：实现前默认门禁 310 总 / **13 失败**（全部为归并/排序新行为；3 条「不许误并」守卫用例前后皆绿），实现后 310/310。
- 真实音频集成仍为 **5** 条；本次未执行 `-Audio`，不把 315 写成实跑结果。
- 未修改 `D-player.slnf`、WinUI 工程或任何持久化 schema（`queue.json` SchemaVersion 仍为 3，走查中实测）。

新增覆盖（`LibraryGroupingTests`）：

| 用例 | 覆盖 |
|---|---|
| 双碟归并为一 / 碟序+曲号排序 | `Disc 1` + `Disc 2` 同标签归并；跨碟曲号从 1 重启时整张顺序正确 |
| 命名变体 theory（8） | `Disc 1` / `CD1` / `disc 1` / `Disc3` / `Disk 1` / `碟1` / `ディスク 1` / `Disc-1` |
| 误并守卫（3） | 专辑标签不同不并；`Discography` / `CD Backup` / `Disc`（无数字）不匹配；不同父目录不并 |
| 艺术家页跨碟曲序 / 键一致性 / 根目录+碟子文件夹混排 | `AlbumOf(album)` 与成员 track 键一致；根目录曲目排在碟前 |

## 2. WPF 交互验收

环境：Windows 11（10.0.26300）/ .NET 10 / Debug；Computer Use 子代理驱动真实窗口，主代理独立复核。临时合成库 18 首带标签 FLAC（1 位艺术家 Synth Artist；4 首 30 秒 + 14 首 1 秒）：标准双碟、CD/日文/中文变体各一、三类守卫（无数字目录 / 标签不同 / 父目录不同）、普通单文件夹回归。验收前备份 `settings.json` / `queue.json` / `library-cache.json` 并记录 SHA-256，queue/cache 移走隔离真实数据；结束后按字节还原，三文件 SHA-256 全部相等；临时库、备份、脚本与截图已删除；未修改用户音乐文件。

| # | 结果 | 操作与观测 |
|---|---|---|
| 1 | PASS | 专辑网格 11 张卡片，"Multi Disc Album" 恰好 1 张；底栏「1 位艺术家 · 11 张专辑 · 18 首」。 |
| 2 | PASS | 守卫拆分：Guard Album 2 张、Same Name Album 2 张、Tag Split One / Tag Split Two 各 1 张。 |
| 3 | PASS | Multi Disc Album 详情 4 行，顺序 Disc1 Track1 → Disc1 Track2 → Disc2 Track1 → Disc2 Track2；曲号列显示 1,2,1,2（各碟编号保留）。 |
| 4 | PASS | 「播放全部」→ 播放栏 Disc1 Track1 播放中；`queue.json` SchemaVersion=3，`LibraryPlayback.Items` 顺序 Disc 1/01 → Disc 1/02 → Disc 2/01 → Disc 2/02；「下一首」→ Disc1 Track2。 |
| 5 | PASS | Variant（CD One→CD Two）、Japanese（JP Disc1→JP Disc2）、Chinese（CN Disc1→CN Disc2）各归并为 1 张 2 行。 |
| 6 | PASS（注） | 艺术家页 Synth Artist 摘要「11 张专辑 · 18 首」，专辑列表 Multi Disc Album 仅 1 次。注：艺术家详情页按 Phase 24 既有设计**只有专辑列表**（`LibraryView.xaml` 绑定 `SelectedArtist.Albums`），无曲目列表 UI；艺术家层跨碟曲序由单测 `Build_MultiDisc_ArtistTracksFollowDiscOrder` 覆盖。 |
| 7 | PASS | 搜索 "Multi Disc"：专辑组 1 条、曲目组 4 条按碟序；双击专辑结果 → 详情 4 行；清空按钮生效。 |
| 8 | PASS | 右键专辑卡片 →「加入歌单…」→ 对话框「将加入 4 首曲目」→ 名称框输入 Phase27 List →「新建并加入」→ 库视图保持；侧栏新歌单 4 首按碟序。 |
| 9 | PASS | Disc1 Track2 播放中（30 秒曲）右键 Variant Album 加入 Phase27 List（2 首）；对话框关闭后播放栏仍 Disc1 Track2、进度前进、未暂停；歌单变 6 首。 |
| 10 | PASS（观察） | 关闭重启：Phase27 List 6 首、网格 11 张、Multi Disc 仍 4 首；SchemaVersion=3。观察：落地视图为队列视图（`CurrentPlaylistId` 指向隐藏库队列时 UI 回落首个歌单），属既有导航行为，库数据完整、侧栏可达。 |

独立复核：主代理读取 01-album-grid / 02-multidisc-detail / 07-playlist 三张截图与 `queue.json` 原文，卡片计数、详情碟序、歌单顺序、schema 均与子代理报告一致。

## 3. 发现与处理

| 发现 | 级别 | 处理 |
|---|---|---|
| 走查清单第 6 项「艺术家曲目列表」过度指定 | 清单偏差 | 艺术家详情页无曲目列表 UI 是 Phase 24 既有设计，非 Phase 27 缺陷；不改 UI，碟序由单测覆盖，验收措辞已修正。 |
| 重启落地队列视图而非库视图 | 既有行为 | 隐藏库队列作为当前指针时的既有回落逻辑，与归并无因果；记为观察，不修。 |
| 走查侧误点静音键致测试 settings 音量漂移 | 走查事故 | 测试 settings 整体按字节还原，SHA-256 相等，无残留。 |

## 4. 限制

只认文件夹名（`Disc N / CD N / Disk N / 碟N / ディスクN`，大小写/空格/连字符不敏感、必须带数字）；乱命名碟文件夹与专辑标签变体（如 "Album (Disc 1)"）仍拆开；无碟分隔 UI 与碟数标注；嵌套碟文件夹只上提一层；不读 DiscNumber 标签，Track / 缓存 / 元数据读取器零改动。不改变 `queue.json` / `settings.json` / `library-cache.json` schema。