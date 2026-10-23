# Phase 22 WinUI 功能补齐验收清单

**状态**：**部分执行（Task 8，记录-复核轮）**。本清单覆盖 Phase 22 已交付的功能层（异步文件对话框 + 真实 picker、导入/导出、歌单增删改与 ▶ 标记、播放控制、基础设置、窗口几何、本地日志）。已填写：A1–A7、B1–B6、D2、E1、E3；仍待填写：C1–C4、D1、D3、E2（由后续走查代理与用户完成）。

执行者：acceptance agent（自动化部分）+ 用户（手测部分）。
规则：不修改任何产品文件；只追加本验收记录。证据截图/UIA dump 位于 `%TEMP%\p22-acc\`。
格式：`项 | PASS/FAIL/NOT VERIFIED | 证据方式 + 实测读数 | 原始证据路径`。

**交付基线**：Task 1–6 已完成并评审（`570c00d` → `e98b369`）。WinUI 单工程构建是 `tools/verify-gates.ps1 -Full` 的第三道门禁（`-Fast` 只跑门禁 1/2）。

**本轮已交付（验收对象）**：
- Core 的 `IFileDialogService` 三个方法改为返回 `Task`；WPF 用同步对话框 + `Task.FromResult`，WinUI 用 `FileOpenPicker` / `FolderPicker` / `FileSavePicker` + `InitializeWithWindow`；取消/失败返回空集合或 null。
- 歌单宿主 `WinUiDialogService`；左栏新建 / 双击重命名 / 删除（含确认）与 ▶ 活跃标记；删除最后一个歌单由 Core 自动重建默认歌单。
- TrackList 工具栏：添加文件（多选）、递归导入文件夹、导入 M3U/M3U8/PLS 到当前歌单（含导入/跳过计数报告）、导出 M3U8、清空（含确认）、导入忙碌环。
- 64px 单行底栏：上一首 / 下一首 / 随机 / 循环三态 / 音量 / 静音；宽 ≥1100 / 中 ≥860 / 窄 <860 三档收起音量与“正在播放”标签；音量滑块键盘步进 0.05 / 0.1。
- 设置对话框（基础版）：音量立即生效、音频输出只读、日志目录入口；**没有**频谱 / EQ 设置。
- 窗口几何：物理像素保存与恢复、最大化不覆盖普通几何、非法尺寸回落 1200×800、中心点不在任何显示器工作区时主屏居中。
- 本地日志：`%LOCALAPPDATA%\D-player-winui\logs\app.log`，1 MiB 轮转到 `app.log.1`；启动、断点恢复跳过、picker 跳过、`PlaybackError`、未处理异常与几何失败写入。

**仍未实现（不在本清单内，Phase 23/24）**：频谱可视化与频谱设置、10 段 EQ、拖拽（外部入队 / 队列内重排 / 歌单重排）、表头排序、文件夹绑定歌单的创建/刷新/管理、TrackList 的 ▶ 当前曲标记、InfoPanel 的采样率读数。

---

## A 导入与歌单管理

| 项 | 结果 | 证据（方式 + 实测读数） | 原始证据路径 |
|---|---|---|---|
| A1 添加文件（多选）→ 曲目/时长/序号正确显示。 | PASS | 真实 picker 多选 3 个 WAV → 列表出现 3 行 `toneA30s / toneB30s / toneC30s`，时长均 `00:30`，艺术家列空（无标签）。`#` 列对无 `TrackNumber` 的文件按设计显示 `-`（`TrackList.xaml` 注释「TrackNumber null 时显示 "-"」）；带标签的 12 曲歌单上 `#` 列显示 `1..12` 连续数字，两列行为都被实测到。 | `%TEMP%\p22-acc\t08-a1-added.txt`、`t09-a1-after-open.txt`、`t41-multiselect4.png`、`s01.txt` |
| A2 导入文件夹（递归）→ 只收 5 种音频后缀，追加到当前歌单。 | PASS | 已有 3 行时选 `%USERPROFILE%\Music\p22-acc-tmp\acc-audio`（含 `toneA30s.wav`、`toneB30s.wav`、`cover.jpg`、`notes.txt`、`nested\toneD30s.wav`）→ 追加 3 行（共 6 行），`toneA30s/toneB30s/toneD30s` 全部入列，`cover.jpg` 与 `notes.txt` 未被收集，`nested` 子目录被递归。 | `%TEMP%\p22-acc\t11-folder-selected.txt`、`t12-a2-after-folder.txt`、`s27-a2.png` |
| A3 导入 M3U/M3U8/PLS 到当前歌单 → 追加 + 报告含导入/跳过计数。 | PASS | 报告原文 `已导入「append-one.m3u8」→ 歌单「acc-list」1 首入列，跳过 1 条（文件缺失 0 / 格式不支持 1）`；新建路径报告为 `已导入「acc-list.m3u8」→ 新歌单「acc-list」2 首入列，跳过 2 条（文件缺失 1 / 格式不支持 1）`。 | `%TEMP%\p22-acc\s33-a3.txt`、`s29-a3.txt`、`s34-a3.png` |
| A4 左栏导入播放列表 → 新建歌单并切换查看。 | PASS | 左栏 `导入播放列表` 选 `acc-list.m3u8` → 新行 `acc-list` 出现在歌单末尾并被选中，列表切到该歌单（2 行）；截图与导入后的树一致。 | `%TEMP%\p22-acc\s30-a4.png`、`s31-a4-after.txt` |
| A5 导出当前歌单 M3U8 → 文件存在，`#EXTINF` 与绝对路径正确。 | PASS | `export\acc-list-export.m3u8`：3 行 `#EXTINF`、3 行非注释、`#EXTM3U` 头、CRLF；3 条路径全部为反斜杠绝对路径且磁盘上存在（2 条 `%TEMP%\p22-acc\audio\*`、1 条 `%USERPROFILE%\Music\p22-acc-tmp\acc-audio\*`）。 | `%TEMP%\p22-acc\export\acc-list-export.m3u8`、`s38-a5.txt` |
| A6 新建/重命名/删除歌单；删最后一个自动重建默认歌单；删当前播放歌单后播放器停止。 | PASS（最后歌单重建为 NOT VERIFIED，用户手测） | 新建 `A6acc` → 立即选中并显示空态；双击行打开 `重命名歌单` 对话框，确认后左栏标签即时变为 `A6tmpRenamed`（`OneWay` 绑定修复）；删除确认框原文 `确定删除歌单「A6acc」吗？此操作不可撤销。`（按钮 `删除` / 默认 `取消`）；确认删除后该行消失（`A6acc` 计数 0），播放器回落 `未在播放`×2、进度 `00:00 / 00:00` 且进度条 `enabled=False`，播放确实停止。「删最后一个歌单 → 自动重建默认歌单」会销毁用户歌单，标记 NOT VERIFIED 交用户。 | `%TEMP%\p22-acc\a6-02-created.txt`、`t04-a6-rename-dlg.txt`、`t05-a6-renamed.txt`、`a6-07-deletedlg.txt`、`a6-08-after-delete.txt`、`a6-09-after-delete.png` |
| A7 ▶ 活跃标记随播放歌单切换；折叠态仍可见。 | PASS | 播放 `A7a` 中的 `toneA30s` 后左侧 8px 三角 ▶ 出现在 `A7a` 行（展开态裁切图、48px 折叠态裁切图各一张，均见蓝色 ▶ 位于 24px 徽标左侧内边距）；切换到其它歌单后标记移走（`A6tmpRenamed` 行带 ▶、`A7a` 行无）。说明：▶ 是 8px `FontIcon`，无 UIA 名称，故以裁切截图为准。 | `%TEMP%\p22-acc\a7-crop-expanded-play.png`、`a7-crop-collapsed-play.png`、`t28-crop-rail-expanded.png`、`t32-crop-rail-48.png`、`t30-crop-viewother.png` |

## B 播放控制

| 项 | 结果 | 证据（方式 + 实测读数） | 原始证据路径 |
|---|---|---|---|
| B1 上一首/下一首在当前曲目为空时禁用，有曲目时切歌正确。 | PASS | 空歌单/无当前曲目时 `上一首`、`下一首` 均 `enabled=False`（`播放/暂停` 亦禁用）；播放 `tlong-A` 后两者变 `enabled=True`，连点下一首依次到 `tlong-D`，再点上一首回 `tlong-C`、`tlong-B`，标题/时间同步刷新且每次从 `00:02` 重新开始。 | `%TEMP%\p22-acc\t55-b1-playing.txt`、`t57-b1-next3.txt`、`t58-b1-prev.txt`、`t59-b1-prev2.txt`、`t56-b1-playing.png` |
| B2 随机开/关：图标强调态、ToolTip、实际随机行为。 | PASS | UIA 名称/即时 ToolTip 由 `随机播放：开` 切到 `随机播放：关`（同一字符串同时驱动 ToolTipService）；开启后图标由次级灰变强调蓝（裁切图像素对比）；实际行为：4 曲歌单从 `tlong-A` 出发连点 3 次下一首得到 `B → D → C`，非顺序推进，证明随机生效。走查后已还原为 `随机播放：关`。 | `%TEMP%\p22-acc\t61-shuffle-state.txt`、`t60-b2-shuffle-on.png`、`t60-crop.png`、`b2-next1.txt`–`b2-next3.txt`、`b2-final.txt` |
| B3 循环三态：Off/List/One 图标与 ToolTip 正确；单曲循环播完重播。 | PASS（单曲播完重播为 NOT VERIFIED） | 连点 `循环` 按钮三次，UIA 名称即 ToolTip 依次为 `循环：关` → `循环：列表` → `循环：单曲` → `循环：关`；裁切截图显示 List 为灰色 E8EE 环形箭头、One 为强调蓝 E8ED（带 “1”）字形，Off 为灰色。**单曲循环播完重播未验证**：需让 30s 短音频自然结束，而 Core 既有 `TrackEnded` 竞态会让其它歌单 VM 抢走播放（见“已知问题 1”），短音频还会干扰用户歌单，故标记 NOT VERIFIED；该分支由 Core 单元测试覆盖。 | `%TEMP%\p22-acc\b3-05-buttons.txt`、`b3-07-one.txt`、`b3-09-off.txt`、`b3-crop-list.png`、`b3-crop-one.png` |
| B4 音量滑块与静音：即时生效、图标状态正确、重启后恢复。 | PASS | UIA `RangeValue` 读数 `1 → 0.85`（Left×3，malformed rounding 显示 `0.85000002`），静音后名称 `静音`→`取消静音`、滑块读数立刻 `0`、图标 E767→E74F（裁切图像素可见）；取消静音回到 `0.85`。关闭壳（WM_CLOSE）后重新启动，UIA 读回 `音量 = 0.850000023841858`，与关闭前一致，证明 `DefaultVolume` 已落盘并在启动时回灌。走查后 Right×3 还原为 `1.0000`（原始值，见 `t01-volume-orig.txt`）。 | `%TEMP%\p22-acc\b4-01-vol85.png`、`b4-03-muted.png`、`b4-crop-muted.png`、`b4-crop-unmuted.png`、`b4-04-unmuted.txt`、`t01-volume-orig.txt` |
| B5 底栏 64px 实测；宽/中/窄三档无重叠、无横向滚动。 | PASS | 引用 Task 4 真机像素测量：底栏 y = 693..756（含）共 **64 px**，y = 757 为 1px 窗口边框；UIA 顺序 `上一首/播放暂停/下一首/随机/循环/播放进度/时间/静音/音量/设置`。响应式：外框 1000px（客户区 986）时 `音量` 滑块从树中消失、`正在播放`标签保留；外框 800px（客户区 786）时两者均收起，底栏无重叠、无横向滚动。本轮未重测，直接引用。 | Task 4 report §Manual smoke 1 / 5（`task-4-report.md`） |
| B6 设置对话框：音量立即生效；音频输出只读；日志入口可打开日志目录。 | PASS | 引用 Task 5 真机结果：`设置` 打开对话框，UIA 暴露 `设置 / 音量 / VolumeSlider / VolumePercent / 音频输出 / 日志 / LogPathText / 打开日志文件夹 / 完成`；音量 `1.0000 / 100%` → Left → `0.0000 / 0%` 即时生效且关闭重开仍为 `0%`；`音频输出` 是不可编辑文本；点击 `打开日志文件夹` 弹出标题为 `...\D-player-winui\logs - 文件资源管理器` 的 Explorer。 | Task 5 report §Manual smoke 1 / 5（`task-5-report.md`） |

## C 持久化与窗口

| 项 | 结果 | 证据（方式 + 实测读数） | 原始证据路径 |
|---|---|---|---|
| C1 移动/缩放窗口 → 关闭 → 重开：位置与尺寸恢复。 |  |  |  |
| C2 最大化关闭 → 重开：回到上次的普通几何。 |  |  |  |
| C3 断点续播回归：播放中关窗 → 重开按 ▶ 从断点继续。 |  |  |  |
| C4 屏外自愈（用户手测）：窗口中心不在任何显示器内 → 主屏居中。 |  |  |  |

## D 日志与门禁

| 项 | 结果 | 证据（方式 + 实测读数） | 原始证据路径 |
|---|---|---|---|
| D1 启动后日志文件存在且含启动记录；损坏音频触发 `PlaybackError`；断点文件缺失/失效走 `断点续播被跳过`；日志 IO 失败不影响应用。 |  |  |  |
| D2 `powershell -File tools/verify-gates.ps1 -Full` → 三道门禁全绿。 | PASS | `mode: Full (3 gates…)`；gate 1/3 `-> ok (exit 0, 0 warning lines)`，gate 2/3 `总计 180 / 失败 0`，gate 3/3 `-> ok (exit 0, 0 warning lines)`，结尾 `all requested steps passed.`（进程退出码 0）。 | `%TEMP%\p22-acc\gates.log` |
| D3 WPF 壳的导入/导出/文件夹对话框行为不变（用户手测）。 |  |  |  |

> D1 口径补充：断点文件缺失/失效走的是 `断点续播被跳过：<原因>` 记录，不是 `PlaybackError`；`PlaybackError` 由播放失败（如损坏音频）触发。两者都写同一个 `app.log`。

## E 回归

| 项 | 结果 | 证据（方式 + 实测读数） | 原始证据路径 |
|---|---|---|---|
| E1 `.slnf` 180 通过 / 0 失败；WinUI 单壳 0 警告 0 错误。 | PASS | 逐条实跑：`dotnet build D-player.slnf -c Debug --nologo -v q` → `0 个警告 0 个错误`（1.80 s，exit 0）；`dotnet test D-player.slnf -c Debug -v q`（无 `--nologo`）→ `总计 180 / 失败 0 / 成功 180 / 已跳过 0`（exit 0）；`dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q` → `0 个警告 0 个错误`（13.60 s，exit 0）。 | `%TEMP%\p22-acc\gates.log` |
| E2 Phase 21 的折叠、Tab 顺序、空格播放/暂停、方向键步进、9 项动效抽样不回退。 |  |  |  |
| E3 Core 除 `IFileDialogService` 外无公开面变化；WPF 用户可见行为零变化。 | PASS | `git diff 9cc28ba..HEAD -- D-player.Core` **无输出**（交付基线之后 Core 未再改动）。整个 Phase 22 窗口 `git diff 18f6486..HEAD -- D-player.Core` 只触及 3 个文件：`IFileDialogService.cs`（唯一公开面变化：三个同步方法 → `Task<...>` 的 `OpenFilesAsync` / `OpenFolderAsync` / `SaveFileAsync`）、`PlaylistViewModel.cs` / `PlaylistsViewModel.cs`（仅 `await` 调用点与私有嵌套 `NullFileDialogService` 的实现，无其它 public/protected 成员增删）。WPF 工程只改了 `D-player/Services/Win32FileDialogService.cs`：原同步方法体原样保留为 `private static`，公开方法改为 `Task.FromResult(...)` 包装，对话框类型/过滤器/标题/默认扩展名/OverwritePrompt 全部未变。WPF 的肉眼确认属 D3（用户手测）。 | `git diff 9cc28ba..HEAD -- D-player.Core`（空）、`git diff 18f6486..HEAD -- D-player.Core`、`git diff 18f6486..HEAD -- D-player/Services/Win32FileDialogService.cs` |

---

## 已知问题 / 交给用户

以下问题在 Phase 22 的 Task 2–6 真机冒烟中被发现或确认，已记录、未在本阶段修复。Task 8 走查时不要把它们误判为新增回归。

1. **Core `TrackEnded` 竞态（既有缺陷，Phase 22 范围外）**：每个 `PlaylistViewModel` 都在构造时订阅共享播放器的 `TrackEnded`，曲目自然播完后所有歌单 VM 都会尝试推进，可能让非查看中的歌单（冒烟中见到的 `1`）抢走播放。修复属于 Core，需要单独拍板；本阶段只记录。走查建议用足够长的测试音频，避免短音频自然结束干扰其他项。
2. **删除当前查看歌单后的 NavRail/列表选中不一致（既有缺陷）**：删除正在查看的歌单并重启后，左栏高亮与中间列表显示的可能是两个不同歌单；点击一次左栏即恢复同步。Phase 22 未改 NavRail 的选中恢复逻辑，记录待后续 NavRail 梳理。
3. **两个 `Debug.WriteLine` 未接入 `AppLog`**：`SettingsDialog`（打开日志文件夹失败）与 `PlayerBar`（设置对话框 `ShowAsync` 异常）仍用 `Debug.WriteLine`，WinExe 下不可见。Task 6 只迁移了 brief 指定的调用点，未顺手扩大范围。
4. **文件夹选择器“输入路径”等效取消**：在 Windows `FolderPicker` 的路径框里键入路径并确认，可能返回 `StorageFolder.Path == ""`；`WinUiFileDialogService.OpenFolderAsync` 按既有契约把空路径映射为 null（取消）。选择列表中的真实文件夹项工作正常，A2 请按“选中文件夹项”的方式操作。
5. **只能由用户确认的项**：日志文件内容、真实 `PlaybackError` 记录、1 MiB 轮转、屏外自愈（C4）。代理按纪律不读写 `%LOCALAPPDATA%\D-player-winui\*`，这些项交用户在 Task 8 走查时确认。
6. **冒烟副作用（透明披露）**：Task 3/4/5 的真机冒烟可能改变了持久化的“查看歌单”指针、某歌单的当前曲索引与窗口几何（Task 5 结束时记录为 1200×800 @ (360,124)）；所有用户歌单的**内容**未被编辑。
