# Phase 22 WinUI 功能补齐验收清单

**状态**：**23 项全部 PASS**。A1–A7、B1–B6、C1–C4、D1–D3、E1–E3 均已 PASS；C4/D3 与 D1 的 1 MiB 轮转由用户 2026-10-24 手测确认。可选的冷启动空态复验见第 5 项。A6 的“删最后一个歌单重建默认”与 B3 的“单曲循环自然播完”已由用户 2026-10-24 手测确认。收尾修复：首启空态提示缺失已修复并提交为 `adc0fbf`（fix(winui): show the empty-state hint on first launch）；修复后 `-Full` 三门禁复跑通过（0/0、180/0、WinUI 0/0，证据 `%TEMP%\p22-acc\gates-postfix.log`）。空态在会话内新建空歌单时已实测可见（`ct-fix-01-created.txt`）；“冷启动即空歌单”的复验仍交用户，原因见「已知问题 / 交给用户」第 5 项。

执行者：acceptance agent（自动化部分）+ 用户（手测部分）。
规则：不修改任何产品文件；只追加本验收记录。证据截图/UIA dump 位于 `%TEMP%\p22-acc\`。
格式：`项 | PASS/FAIL/NOT VERIFIED | 证据方式 + 实测读数 | 原始证据路径`。

**交付基线**：Phase 22 实现与文档提交 `18f6486` → `9cc28ba`；验收记录另有两个 docs 提交。WinUI 单工程构建是 `tools/verify-gates.ps1 -Full` 的第三道门禁（`-Fast` 只跑门禁 1/2）。

**本轮已交付（验收对象）**：
- Core 的 `IFileDialogService` 三个方法改为返回 `Task`；WPF 用同步对话框 + `Task.FromResult`，WinUI 用 `FileOpenPicker` / `FolderPicker` / `FileSavePicker` + `InitializeWithWindow`；取消/失败返回空集合或 null。
- 歌单宿主 `WinUiDialogService`；左栏新建 / 双击重命名 / 删除（含确认）与 ▶ 活跃标记；删除最后一个歌单由 Core 自动重建默认歌单。
- TrackList 工具栏：添加文件（多选）、递归导入文件夹、导入 M3U/M3U8/PLS 到当前歌单（含导入/跳过计数报告）、导出 M3U8、清空（含确认）、导入忙碌环。
- 64px 单行底栏：上一首 / 下一首 / 随机 / 循环三态 / 音量 / 静音；宽 ≥1100 / 中 ≥860 / 窄 <860 三档收起音量与“正在播放”标签；音量滑块键盘步进 0.05 / 0.1。
- 设置对话框（基础版）：音量立即生效、音频输出只读、日志目录入口；**没有**频谱 / EQ 设置。
- 窗口几何：物理像素保存与恢复、最大化不覆盖普通几何、非法尺寸回落 1200×800、中心点不在任何显示器工作区时主屏居中。
- 本地日志：`%LOCALAPPDATA%\D-player-winui\logs\app.log`，1 MiB 轮转到 `app.log.1`；启动、断点恢复跳过、picker 跳过、`PlaybackError`、未处理异常与几何失败写入。

**仍未实现（不在本清单内，Phase 23/24）**：频谱可视化与频谱设置、10 段 EQ、拖拽（外部入队 / 队列内重排 / 歌单重排）、表头排序、文件夹绑定歌单的创建/刷新/管理。TrackList 的 ▶ 当前曲标记与 InfoPanel 采样率已在 2026-10-24 的交付后快赢切片补齐（见文末）。

---

## A 导入与歌单管理

| 项 | 结果 | 证据（方式 + 实测读数） | 原始证据路径 |
|---|---|---|---|
| A1 添加文件（多选）→ 曲目/时长/序号正确显示。 | PASS | 真实 picker 多选 3 个 WAV → 列表出现 3 行 `toneA30s / toneB30s / toneC30s`，时长均 `00:30`，艺术家列空（无标签）。`#` 列对无 `TrackNumber` 的文件按设计显示 `-`（`TrackList.xaml` 注释「TrackNumber null 时显示 "-"」）；带标签的 12 曲歌单上 `#` 列显示 `1..12` 连续数字，两列行为都被实测到。 | `%TEMP%\p22-acc\t08-a1-added.txt`、`t09-a1-after-open.txt`、`t41-multiselect4.png`、`s01.txt` |
| A2 导入文件夹（递归）→ 只收 5 种音频后缀，追加到当前歌单。 | PASS | 已有 3 行时选 `%USERPROFILE%\Music\p22-acc-tmp\acc-audio`（含 `toneA30s.wav`、`toneB30s.wav`、`cover.jpg`、`notes.txt`、`nested\toneD30s.wav`）→ 追加 3 行（共 6 行），`toneA30s/toneB30s/toneD30s` 全部入列，`cover.jpg` 与 `notes.txt` 未被收集，`nested` 子目录被递归。 | `%TEMP%\p22-acc\t11-folder-selected.txt`、`t12-a2-after-folder.txt`、`s27-a2.png` |
| A3 导入 M3U/M3U8/PLS 到当前歌单 → 追加 + 报告含导入/跳过计数。 | PASS | 报告原文 `已导入「append-one.m3u8」→ 歌单「acc-list」1 首入列，跳过 1 条（文件缺失 0 / 格式不支持 1）`；新建路径报告为 `已导入「acc-list.m3u8」→ 新歌单「acc-list」2 首入列，跳过 2 条（文件缺失 1 / 格式不支持 1）`。 | `%TEMP%\p22-acc\s33-a3.txt`、`s29-a3.txt`、`s34-a3.png` |
| A4 左栏导入播放列表 → 新建歌单并切换查看。 | PASS | 左栏 `导入播放列表` 选 `acc-list.m3u8` → 新行 `acc-list` 出现在歌单末尾并被选中，列表切到该歌单（2 行）；截图与导入后的树一致。 | `%TEMP%\p22-acc\s30-a4.png`、`s31-a4-after.txt` |
| A5 导出当前歌单 M3U8 → 文件存在，`#EXTINF` 与绝对路径正确。 | PASS | `export\acc-list-export.m3u8`：3 行 `#EXTINF`、3 行非注释、`#EXTM3U` 头、CRLF；3 条路径全部为反斜杠绝对路径且磁盘上存在（2 条 `%TEMP%\p22-acc\audio\*`、1 条 `%USERPROFILE%\Music\p22-acc-tmp\acc-audio\*`）。 | `%TEMP%\p22-acc\export\acc-list-export.m3u8`、`s38-a5.txt` |
| A6 新建/重命名/删除歌单；删最后一个自动重建默认歌单；删当前播放歌单后播放器停止。 | PASS | 新建 `A6acc` → 立即选中并显示空态；双击行打开 `重命名歌单` 对话框，确认后左栏标签即时变为 `A6tmpRenamed`（`OneWay` 绑定修复）；删除确认框原文 `确定删除歌单「A6acc」吗？此操作不可撤销。`（按钮 `删除` / 默认 `取消`）；确认删除后该行消失（`A6acc` 计数 0），播放器回落 `未在播放`×2、进度 `00:00 / 00:00` 且进度条 `enabled=False`，播放确实停止。「删最后一个歌单 → 自动重建默认歌单」由用户 2026-10-24 手测确认通过。 | `%TEMP%\p22-acc\a6-02-created.txt`、`t04-a6-rename-dlg.txt`、`t05-a6-renamed.txt`、`a6-07-deletedlg.txt`、`a6-08-after-delete.txt`、`a6-09-after-delete.png`；用户手测 2026-10-24 |
| A7 ▶ 活跃标记随播放歌单切换；折叠态仍可见。 | PASS | 播放 `A7a` 中的 `toneA30s` 后左侧 8px 三角 ▶ 出现在 `A7a` 行（展开态裁切图、48px 折叠态裁切图各一张，均见蓝色 ▶ 位于 24px 徽标左侧内边距）；切换到其它歌单后标记移走（`A6tmpRenamed` 行带 ▶、`A7a` 行无）。说明：▶ 是 8px `FontIcon`，无 UIA 名称，故以裁切截图为准。 | `%TEMP%\p22-acc\a7-crop-expanded-play.png`、`a7-crop-collapsed-play.png`、`t28-crop-rail-expanded.png`、`t32-crop-rail-48.png`、`t30-crop-viewother.png` |

## B 播放控制

| 项 | 结果 | 证据（方式 + 实测读数） | 原始证据路径 |
|---|---|---|---|
| B1 上一首/下一首在当前曲目为空时禁用，有曲目时切歌正确。 | PASS | 空歌单/无当前曲目时 `上一首`、`下一首` 均 `enabled=False`（`播放/暂停` 亦禁用）；播放 `tlong-A` 后两者变 `enabled=True`，连点下一首依次到 `tlong-D`，再点上一首回 `tlong-C`、`tlong-B`，标题/时间同步刷新且每次从 `00:02` 重新开始。 | `%TEMP%\p22-acc\t55-b1-playing.txt`、`t57-b1-next3.txt`、`t58-b1-prev.txt`、`t59-b1-prev2.txt`、`t56-b1-playing.png` |
| B2 随机开/关：图标强调态、ToolTip、实际随机行为。 | PASS | UIA 名称/即时 ToolTip 由 `随机播放：开` 切到 `随机播放：关`（同一字符串同时驱动 ToolTipService）；开启后图标由次级灰变强调蓝（裁切图像素对比）；实际行为：4 曲歌单从 `tlong-A` 出发连点 3 次下一首得到 `B → D → C`，非顺序推进，证明随机生效。走查后已还原为 `随机播放：关`。 | `%TEMP%\p22-acc\t61-shuffle-state.txt`、`t60-b2-shuffle-on.png`、`t60-crop.png`、`b2-next1.txt`–`b2-next3.txt`、`b2-final.txt` |
| B3 循环三态：Off/List/One 图标与 ToolTip 正确；单曲循环播完重播。 | PASS | 连点 `循环` 按钮三次，UIA 名称即 ToolTip 依次为 `循环：关` → `循环：列表` → `循环：单曲` → `循环：关`；裁切截图显示 List 为灰色 E8EE 环形箭头、One 为强调蓝 E8ED（带 “1”）字形，Off 为灰色。**单曲循环播完重播**由用户 2026-10-24 手测确认“可以正常重播”（代理未用短音频强制触发自然结束，避免 Core 既有 `TrackEnded` 竞态干扰其他歌单）。 | `%TEMP%\p22-acc\b3-05-buttons.txt`、`b3-07-one.txt`、`b3-09-off.txt`、`b3-crop-list.png`、`b3-crop-one.png`；用户手测 2026-10-24 |
| B4 音量滑块与静音：即时生效、图标状态正确、重启后恢复。 | PASS | UIA `RangeValue` 读数 `1 → 0.85`（Left×3，malformed rounding 显示 `0.85000002`），静音后名称 `静音`→`取消静音`、滑块读数立刻 `0`、图标 E767→E74F（裁切图像素可见）；取消静音回到 `0.85`。关闭壳（WM_CLOSE）后重新启动，UIA 读回 `音量 = 0.850000023841858`，与关闭前一致，证明 `DefaultVolume` 已落盘并在启动时回灌。走查后 Right×3 还原为 `1.0000`（原始值，见 `t01-volume-orig.txt`）。 | `%TEMP%\p22-acc\b4-01-vol85.png`、`b4-03-muted.png`、`b4-crop-muted.png`、`b4-crop-unmuted.png`、`b4-04-unmuted.txt`、`t01-volume-orig.txt` |
| B5 底栏 64px 实测；宽/中/窄三档无重叠、无横向滚动。 | PASS | 引用 Task 4 真机像素测量：底栏 y = 693..756（含）共 **64 px**，y = 757 为 1px 窗口边框；UIA 顺序 `上一首/播放暂停/下一首/随机/循环/播放进度/时间/静音/音量/设置`。响应式：外框 1000px（客户区 986）时 `音量` 滑块从树中消失、`正在播放`标签保留；外框 800px（客户区 786）时两者均收起，底栏无重叠、无横向滚动。本轮未重测，直接引用。 | Task 4 report §Manual smoke 1 / 5（`task-4-report.md`） |
| B6 设置对话框：音量立即生效；音频输出只读；日志入口可打开日志目录。 | PASS | 引用 Task 5 真机结果：`设置` 打开对话框，UIA 暴露 `设置 / 音量 / VolumeSlider / VolumePercent / 音频输出 / 日志 / LogPathText / 打开日志文件夹 / 完成`；音量 `1.0000 / 100%` → Left → `0.0000 / 0%` 即时生效且关闭重开仍为 `0%`；`音频输出` 是不可编辑文本；点击 `打开日志文件夹` 弹出标题为 `...\D-player-winui\logs - 文件资源管理器` 的 Explorer。 | Task 5 report §Manual smoke 1 / 5（`task-5-report.md`） |

## C 持久化与窗口

| 项 | 结果 | 证据（方式 + 实测读数） | 原始证据路径 |
|---|---|---|---|
| C1 移动/缩放窗口 → 关闭 → 重开：位置与尺寸恢复。 | PASS | `SetWindowPos` 把外框移到 `(300,200) 1000x640`（关闭前读数 `outer 300,200 1000x640 client 984x632 zoomed=False`），用标题栏 `Close` 按钮关闭（点击 `1269,216`），3 秒后进程表为空；重新启动同一 exe 后 UIA 读数 `outer 300,200 1000x640 client 984x632 zoomed=False uia=300,200,1000x640`，位置与尺寸逐像素一致。走查后已还原为 `(200,80) 1200x800` 并再次关闭/重开验证（读数 `outer 200,80 1200x800`）。 | `%TEMP%\p22-acc\c1-01-moved.txt`、`c1-02-geom-before-close.txt`、`c1-03-closeclick.txt`、`c1-04-relaunch-geom.txt`、`c1-05-restore-move.txt`、`c1-06-restore-geom.txt` |
| C2 最大化关闭 → 重开：回到上次的普通几何。 | PASS | 先处于普通几何 `(200,80) 1200x800`，点标题栏 `Maximize` 按钮（按钮随即改名 `Restore`）→ `outer -8,-8 1936x1064 client 1920x1048 zoomed=True`；在最大化态点 `Close`（`1897,16`）关闭，重开后读数为 `outer 200,80 1200x800 client 1184x792 zoomed=False`，即回到最大化前的普通几何而非最大化尺寸，证明最大化态未落盘。 | `%TEMP%\p22-acc\c2-01-maxclick.txt`、`c2-02-geom-maximized.txt`、`c2-03-close.txt`、`c2-04-relaunch-geom.txt` |
| C3 断点续播回归：播放中关窗 → 重开按 ▶ 从断点继续。 | PASS | 临时歌单 `C3resume` 加入 `%TEMP%\p22-acc\audio\toneA30s.wav`（30s），双击播放后暂停并把进度条设为 `0.25` → 时间文本 `00:07 / 00:30`（`RangeValue=0.25`，静止 2 秒读数不变，确认已暂停）；点 `Close` 关闭，重开后时间栏即显示 `00:07 / 00:30`（断点已回灌，非 `00:00`），点 `▶` 后 3 秒读数为 `00:11 / 00:30`（`RangeValue=0.4049992`），播放从断点继续。全程在 30s 片段结束前停止，规避既有 `TrackEnded` 竞态。 | `%TEMP%\p22-acc\c3-24-pause.txt`、`c3-27-seek25.txt`、`c3-29-pos-check.txt`、`c3-31-relaunched.txt`、`c3-32-playclick.txt`、`c3-33-playing-after.txt`、`c3-34-pos-after.txt` |
| C4 屏外自愈（用户手测）：窗口中心不在任何显示器内 → 主屏居中。 | PASS | 用户 2026-10-24 手测：窗口移到副屏 → 正常关闭 → 断开副屏（仅电脑屏幕）→ 重开，窗口在主屏工作区居中且完整可见。 | 用户手测 2026-10-24 |

## D 日志与门禁

| 项 | 结果 | 证据（方式 + 实测读数） | 原始证据路径 |
|---|---|---|---|
| D1 启动后日志文件存在且含启动记录；损坏音频触发 `PlaybackError`；断点文件缺失/失效走 `断点续播被跳过`；日志 IO 失败不影响应用。 | PASS | 用户 2026-10-24 读取 `%LOCALAPPDATA%\D-player-winui\logs\app.log` 报告：① `2026-10-24 10:09:43.656 [T2] WinUI shell starting`；② `2026-10-24 10:13:59.543 [T2] PlaybackError: Media Foundation error 0xC00D3E84`；③ `2026-10-24 10:09:44.911 [T2] 断点续播被跳过：上次播放的曲目已不在任何歌单中，未恢复播放位置：C:\Users\darknight2236\Music\p22-acc-tmp\toneA30s.wav`。日志 IO 失败不影响应用由代码路径确认；④ 1 MiB 轮转：用户 2026-10-24 将 `app.log` 填充到 1,058,923 字节后启动，`app.log.1` = 1,058,923 字节，新 `app.log` = 54 字节且含 `2026-10-24 13:16:10.867 [T2] WinUI shell starting`。 | `%LOCALAPPDATA%\D-player-winui\logs\app.log`（用户读取，代理未读取） |
| D2 `powershell -File tools/verify-gates.ps1 -Full` → 三道门禁全绿。 | PASS | `mode: Full (3 gates…)`；gate 1/3 `-> ok (exit 0, 0 warning lines)`，gate 2/3 `总计 180 / 失败 0`，gate 3/3 `-> ok (exit 0, 0 warning lines)`，结尾 `all requested steps passed.`（进程退出码 0）。修复后（`adc0fbf`）复跑同样全绿；中途一次门禁 3 失败是测试进程锁住 `D-player.Core.dll`（MSB3027），关闭进程后复跑通过，不是代码缺陷。 | `%TEMP%\p22-acc\gates.log`、`gates-postfix.log` |
| D3 WPF 壳的导入/导出/文件夹对话框行为不变（用户手测）。 | PASS | 用户 2026-10-24 手测：WPF 壳的添加文件 / 导入文件夹 / 导出 M3U8 对话框行为正常，标题/过滤器/取消与 Phase 21 一致。 | 用户手测 2026-10-24 |

> D1 口径补充：断点文件缺失/失效走的是 `断点续播被跳过：<原因>` 记录，不是 `PlaybackError`；`PlaybackError` 由播放失败（如损坏音频）触发。两者都写同一个 `app.log`。

## E 回归

| 项 | 结果 | 证据（方式 + 实测读数） | 原始证据路径 |
|---|---|---|---|
| E1 `.slnf` 180 通过 / 0 失败；WinUI 单壳 0 警告 0 错误。 | PASS | 逐条实跑：`dotnet build D-player.slnf -c Debug --nologo -v q` → `0 个警告 0 个错误`（1.80 s，exit 0）；`dotnet test D-player.slnf -c Debug -v q`（无 `--nologo`）→ `总计 180 / 失败 0 / 成功 180 / 已跳过 0`（exit 0）；`dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q` → `0 个警告 0 个错误`（13.60 s，exit 0）。 | `%TEMP%\p22-acc\gates.log` |
| E2 Phase 21 的折叠、Tab 顺序、空格播放/暂停、方向键步进、9 项动效抽样不回退。 | PASS | 折叠 200→48→200：e2-41 轮询在 ~340ms 落到 48；e2-43 展开后歌单列表恢复 200x655；e2-44 新建歌单按钮回归。Tab 顺序（e2-09）：播放/暂停→下一首→随机→循环→进度→静音→音量→设置→信息栏→折叠→新建歌单→导入→删除→歌单项→添加文件。空格（e2-16/17/18）：窗格焦点时进度 0.3185→0.3275（播放），再按停在 0.3512、播放图标回到 ▶；音量滑块与对话框文本框内空格不切换播放。方向键（e2-30/31/33）：0.3512→0.3550→0.3600（≈0.005/次）；PageUp（e2-35）→0.4100（≈0.05）。动画抽样：e2-playanim-35ms.png / e2-playanim-130ms.png 可见播放/暂停交叉淡入；e2-fold4-29ms.png / e2-fold4-291ms.png 可见宽度变化。最终态 e2-61：窗口 1200x800@200,80、音量 1、随机/循环关。 | `%TEMP%\p22-acc\e2-41-foldpoll.txt`, `e2-43-restored.txt`, `e2-44-actionsback.txt`, `e2-09-taborder.txt`, `e2-16..18`, `e2-23/24`, `e2-26/27`, `e2-30/31/33/35`, `e2-playanim-35ms.png`, `e2-playanim-130ms.png`, `e2-fold4-29ms.png`, `e2-fold4-291ms.png`, `e2-61-state.txt` |
| E3 Core 除 `IFileDialogService` 外无公开面变化；WPF 用户可见行为零变化。 | PASS | `git diff 9cc28ba..HEAD -- D-player.Core` **无输出**（交付基线之后 Core 未再改动）。整个 Phase 22 窗口 `git diff 18f6486..HEAD -- D-player.Core` 只触及 3 个文件：`IFileDialogService.cs`（唯一公开面变化：三个同步方法 → `Task<...>` 的 `OpenFilesAsync` / `OpenFolderAsync` / `SaveFileAsync`）、`PlaylistViewModel.cs` / `PlaylistsViewModel.cs`（仅 `await` 调用点与私有嵌套 `NullFileDialogService` 的实现，无其它 public/protected 成员增删）。WPF 工程只改了 `D-player/Services/Win32FileDialogService.cs`：原同步方法体原样保留为 `private static`，公开方法改为 `Task.FromResult(...)` 包装，对话框类型/过滤器/标题/默认扩展名/OverwritePrompt 全部未变。WPF 的肉眼确认属 D3（用户手测）。 | `git diff 9cc28ba..HEAD -- D-player.Core`（空）、`git diff 18f6486..HEAD -- D-player.Core`、`git diff 18f6486..HEAD -- D-player/Services/Win32FileDialogService.cs` |

---

## 交付后快赢切片（2026-10-24）

| 项 | 结果 | 证据 / 方式 |
|---|---|---|
| TrackList ▶ 当前曲标记 + 标题强调色 | PASS | 真机：正在播放的当前曲行首显示 Accent ▶、标题转 Accent 色；切到非播放歌单查看时标记隐藏、标题恢复主色。判定与 WPF 一致：`ViewedPlaylist.IsActivePlaylist && index == CurrentIndex`；`ContainerContentChanging` + `CurrentIndex`/`IsActivePlaylist` 变化重画。 |
| InfoPanel 采样率 | PASS | 真机 InfoPanel 在时长下显示 `48,000 Hz`；无曲目/无采样率时该行折叠。 |
| 两处 `Debug.WriteLine` 迁入 `AppLog` | PASS（静态）/ 运行时异常路径 NOT VERIFIED | 两处调用点已改 `AppLog.Write(..., ex)`，WinUI 构建 0 警告、壳内已无 `Debug.WriteLine`。未人为制造“日志目录不可写 / 设置对话框异常”，故实际写入未复验。 |
| NavRail 删除查看项后的选中不一致 | NOT REPRODUCED | 两次真机复现：① 只删除正在查看的临时歌单（播放歌单保持为另一歌单）；② 删除正在查看且正在播放的临时歌单。每次重启后左栏选中项与中间列表内容一致（以默认歌单的两首曲目区分）。未改 NavRail 代码，问题保留观察。 |
| TrackList 表头/行对齐（用户 2026-10-24 报告） | PASS | 根因：表头有 12px 左右内边距，行容器 `RailListViewItemStyle.Padding=0`，行内容比表头宽 24px → 标题列左移 12px、艺术家/时长列右移 12px。修复：行样式补 `TrackListHorizontalPadding`（与表头同 token）。加入行尾操作列后 UIA 复量：`#` 右缘 832=832、标题 844=844、艺术家 1179=1179、时长右缘 1483=1483。 |
| TrackList 行尾移除按钮（用户 2026-10-24 报告缺失） | PASS（UI/确认框）/ 实际删除路径由 Core 单测覆盖 | 行尾 24px ×（Segoe E711），点击弹「移除曲目」确认框（含曲名），确认后 `RemoveTrackCommand`；`TrackList_DoubleTapped` 排除 Button 来源。真机验证：按钮出现在每行末尾、确认框文案正确、取消后曲目仍在；临时曲目添加受 picker 自动化限制，未端到端复验“确认后移除”，该路径由 `PlaylistViewModelTests` 的 4 条 `RemoveTrack` 单测覆盖。 |

证据：`%TEMP%\p22-acc\nr-base.png`（标记/采样率正向）、`nr-negative.png`（非播放歌单无标记）、`nr-after-add.txt`、`nr-playing-a.txt`、`nr-restart-c.txt`（重启后左栏选中 + 中间列表曲目）、`nr-sel.ps1`（选中态读取脚本）。

---

## 交付后布局一致性修正（2026-10-27）

用户报告：WinUI 歌单栏的折叠钮在栏内，信息栏的折叠钮却挂标题栏，视觉不协调。按用户拍板统一为“都放各自栏内”：右栏收起终值由 0 改为 48px（`InfoPanelCollapsedWidth`），`InfoPanelHeaderPadding` 与左栏同值，标题栏的折叠钮及 `InfoPanelToggleMargin` 删除。

| 项 | 结果 | 证据 / 方式 |
|---|---|---|
| 两颗折叠钮都在各自栏头 | PASS | UIA：左钮 `592,335,24x24`、右钮 `1759,335,24x24`，同一水平线且都在标题栏（32px）之下；标题栏无按钮。`%TEMP%\p22-acc\fold-before.png` / `fold-final.png`。 |
| 右栏收起终值 48px 且钮仍可点 | PASS | 点右钮后信息栏 Pane `48x465`，钮仍在 `1759,335`；中列表宽度 `755→967`（+212 = 260-48）。再次点击恢复 `260x465`、228px 封面与文本完整。`fold-right-collapsed.txt` / `fold-right-expanded-again.txt`。 |
| 自动收起/恢复与手动优先不回退 | PASS | 窄窗启动（900×602，<960）→ 右栏 48；拉宽到 1200 → 260。手动展开后在 900 宽窗口仍保持 260（手动优先），重启后自动口径重新接管。`fold-auto-narrow-start.txt`、`fold-auto-wide.txt`、`fold-manual-override-narrow.txt`。 |
| 门禁 | PASS | `.slnf` 0/0、测试 186/0、WinUI 0/0、`verify-gates.ps1 -Full` 3/3。 |

副作用：窗口几何最终恢复为 1231×602@572,294（与验证前一致）；只做了折叠/展开与窗口缩放，未编辑歌单或曲目，应用已关闭。

### 底栏精简与滑块提示（2026-10-27）

| 项 | 结果 | 证据 / 方式 |
|---|---|---|
| 底栏去掉“歌名 — 艺术家” | PASS | UIA 底栏已无该文本；运输组从窗口左缘 12px 起排，进度条宽度 551→739（+188 = 原 180 文本 + 8 间距）；900 宽时音量滑块自动收起、进度条仍有 500px。WPF 底栏本无此文本。`bar-no-title.txt`、`bar-top.png`。 |
| 进度/音量拖动提示显示用户单位 | PASS | 真机合成拖拽并读 UIA 弹出层：进度提示 `02:00`（Value 0.595 / 3:23），音量提示 `72%`（Value 0.72）；释放后底栏时间同步为 `02:00 / 03:23`。`drag-pos2.txt`、`drag-vol.txt`、`drag-pos2.png`、`drag-vol.png`。 |
| 门禁 | PASS | `.slnf` 0/0、测试 186/0、WinUI 0/0、`verify-gates.ps1 -Full` 3/3。 |

副作用：验证时把进度拖到 02:00、音量拖到 72%，关闭前已用 UIA 还原为 00:29 / 0.55；窗口几何未变，应用已关闭。

### 窄窗悬停音量弹层（2026-10-27）

用户报告：窗口 <1100 时横向音量条被隐藏，此时无法调节应用内音量，要求悬停音量图标至少给出纵向音量条。

| 项 | 结果 | 证据 / 方式 |
|---|---|---|
| 窄窗悬停弹出纵向音量条 | PASS | 1000 宽（<1100）悬停静音钮，UIA 出现 `音量（弹出）` 纵向 Slider（`1484,670,32x140`）且保持打开；宽窗（1231）悬停不弹。`vol-pos-hover.txt`、`vol-flyout-open.png`。 |
| 弹层可调音量、与主音量同步 | PASS | 纵向拖动拇指至 25%，提示 `25%`、`Slider.Value=0.25`；与横向音量条同源（`Player.Volume`，拉宽后读数一致）。`drag-vol-popup.txt`、`drag-vol-popup.png`。 |
| 移出关闭、拉宽关闭 | PASS | 指针离开钮与弹层后自动关闭；窗口从 1000 拉到 1231 时弹层自动收起。诊断日志确认 `button enter → timer show → panel enter/exit → timer hide` 序列（日志仅用于排障，已从代码移除）。 |
| 门禁 | PASS | `.slnf` 0/0、测试 186/0、WinUI 0/0、`verify-gates.ps1 -Full` 3/3。 |

副作用：验证时把音量拖到 25%，关闭前已还原为 0.55；窗口几何恢复 1231×602@572,294，应用已关闭。排障过程中曾两次出现“弹层刚开就被关”，根因是 `ShowAt` 后 WinUI 给锚点补 `PointerExited`，已改为按真实鼠标位置驱动（见 COUPLING）。

交付后补记（2026-10-29）：本节的“宽窗悬停不弹”只覆盖悬停；用户实测宽窗**点击**静音钮也会弹出纵向音量条——`Button.Flyout` 在点击/键盘激活时无条件打开，绕过了窄窗判定。已摘除 `MuteButton.Flyout`，弹层只由窄窗悬停 `ShowAt` 打开，详见 [PHASE23-ACCEPTANCE](./PHASE23-ACCEPTANCE.md) 同日缺陷记录。同日第二轮：摘除后窄窗弹层打开时点击静音钮，点击被 light-dismiss 覆盖层吞掉、弹层关了又重开；已设 `OverlayInputPassThroughElement = MuteButton`，点击可切换静音且弹层保持。同日设置对话框修正（拖动提示、99%→100% 回跳、低窗滚动）见 [PHASE23-ACCEPTANCE](./PHASE23-ACCEPTANCE.md) 同日记录。

---

## 已知问题 / 交给用户

以下问题在 Phase 22 的 Task 2–6 真机冒烟中被发现或确认。第 1、3 项已在交付后修复；第 2 项两次复现未重现（保留观察）；其余未在本阶段修复。Task 8 走查时不要把它们误判为新增回归。

1. **Core `TrackEnded` 竞态 —— 已于 2026-10-24 修复（Phase 22 交付后）**：每个 `PlaylistViewModel` 都在构造时订阅共享播放器的 `TrackEnded`，曲目自然播完后所有歌单 VM 都会尝试推进，可能让非查看中的歌单（冒烟中见到的 `1`）抢走播放。修复：`HandleTrackEnded` 入口以 `IsActivePlaylist` 守卫；`PlayTrackAtAsync` 开始播放前把 `Container.CurrentPlaylistId` 指向自己（双击/首播等直达入口仍可能从非当前歌单进入）。新增 4 条 TrackEnded/归属单测（含启动断点续播把 `CurrentPlaylistId` 同步到曲目所属歌单），另加 2 条 `CurrentPlaylist` 单测，全量 186 通过 / 0 失败。验收时 B3/C3 用长音频规避该竞态的步骤保留为历史记录。同日追加（用户冒烟发现）：新建歌单导入曲目后，查看该歌单时上一首/下一首因 `CurrentIndex=-1` 变灰；按用户拍板，传输按钮改绑 `Playlists.CurrentPlaylist`（不跟随查看项，落实双指针模型）。**用户 2026-10-24 真机复验竞态修复**：双击歌单 1 起播 → 左栏切回默认歌单查看 → 把歌单 1 当前曲拖到接近结尾自然播完，播放切到歌单 1 的第二首，默认歌单未抢播。
2. **删除当前查看歌单后的 NavRail/列表选中不一致（既有缺陷，2026-10-24 NOT REPRODUCED）**：原记录为“删除正在查看的歌单并重启后，左栏高亮与中间列表显示可能是两个不同歌单；点击一次左栏即恢复同步”。2026-10-24 两次真机复现（只删查看项 / 删除查看+播放项）后，重启时左栏选中与中间列表内容均一致；未改 NavRail 代码，保留观察，若后续再现需记录三指针（`ViewedPlaylist` / `SelectedItem` / `CurrentPlaylistId`）快照。
3. **两个 `Debug.WriteLine` 未接入 `AppLog` —— 已于 2026-10-24 修复**：`SettingsDialog`（打开日志文件夹失败）与 `PlayerBar`（设置对话框 `ShowAsync` 异常）已改 `AppLog.Write(..., ex)`；壳内不再有 `Debug.WriteLine`。
4. **文件夹选择器“输入路径”等效取消**：在 Windows `FolderPicker` 的路径框里键入路径并确认，可能返回 `StorageFolder.Path == ""`；`WinUiFileDialogService.OpenFolderAsync` 按既有契约把空路径映射为 null（取消）。选择列表中的真实文件夹项工作正常，A2 请按“选中文件夹项”的方式操作。
5. **可选的补充复验**：首启空态提示的冷启动（清单 23 项已全部 PASS；1 MiB 轮转已由用户 2026-10-24 确认）。代理按纪律不读写 `%LOCALAPPDATA%\D-player-winui\*`，此项交用户按需确认。空态冷启动的代理尝试受两点限制：应用启动恢复的是“当前播放歌单”而非“最后查看的空歌单”，且文件 picker 在本会话无法前台驱动，未能构造出“冷启动即空歌单”的条件；会话内新建空歌单已实测空态可见。
6. **冒烟副作用（透明披露）**：Task 3/4/5 的真机冒烟可能改变了持久化的“查看歌单”指针、某歌单的当前曲索引与窗口几何（Task 5 结束时记录为 1200×800 @ (360,124)）；所有用户歌单的**内容**未被编辑。
7. **本轮走查副作用**：创建并删除了 AccProbe、list3、验收临时、acc-list、A6tmpRenamed、A7a、Btmp、C3resume、SpaceProbe 等一次性歌单；用户歌单 新歌单/幻灯/1 的内容未被编辑。走查结束时窗口 1200×800@(200,80)、音量 1.0、随机/循环关，查看歌单切回 幻灯。断点续播可能指向已删除的临时 WAV（`%TEMP%\p22-acc\audio\*`），下次启动会走“断点续播被跳过”。
8. **2026-10-24 快赢切片冒烟副作用**：创建并删除了 NRProbeA / NRProbeB / NRProbeC / NRDel 等临时歌单；NRProbeA 曾加入 `%TEMP%\p22-acc\audio\tlong-A.wav` 并播放后随歌单删除。行尾删除按钮验证时对默认歌单的一行点了 × 并取消，未实际删除用户曲目。默认歌单被双击播放过一次（当前播放歌单变为默认歌单，LastPlayedPath 指向其中第一首）；用户歌单内容未被编辑，窗口几何/音量未改，应用已关闭。

---

## D1 子项：1 MiB 轮转测试（已完成，保留为复现步骤）

2026-10-24 已按此步骤实测通过：`app.log.1` = 1,058,923 字节，新 `app.log` = 54 字节且含启动行。以下保留为复现步骤。

前置：关闭 WinUI 壳，确保日志文件没有被占用。路径：`%LOCALAPPDATA%\D-player-winui\logs\`。

1. 打开 PowerShell，把 `app.log` 填充到 1 MiB 以上：

   ```powershell
   $dir = Join-Path $env:LOCALAPPDATA 'D-player-winui\logs'
   $log = Join-Path $dir 'app.log'
   New-Item -ItemType Directory -Force $dir | Out-Null
   Add-Content -LiteralPath $log -Value ('x' * 1049600) -Encoding UTF8
   (Get-Item $log).Length
   ```

   读数应大于 `1048576`。

2. 启动 D-player.WinUI，等 2–3 秒，点标题栏 ✕ 正常关闭。
3. 查看目录与日志尾部：

   ```powershell
   Get-ChildItem $dir | Select-Object Name,Length,LastWriteTime
   Get-Content -LiteralPath $log -Tail 3
   ```

   预期：`app.log.1` 存在且约 1 MiB+（就是刚才填充的旧内容）；`app.log` 很小，只有新启动的几行，尾部含 `WinUI shell starting`。

4. 清理（可选）：`Remove-Item -LiteralPath (Join-Path $dir 'app.log.1')`；保留也不影响下次轮转。

说明：轮转在每次写日志前检查，达到 1 MiB（含）就把旧文件改名为 `app.log.1`（覆盖旧 `.1`），新启动必然触发一次写入，所以这一步能确定性触发。
