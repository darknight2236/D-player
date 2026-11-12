# Phase 28 验收记录：升级到 .NET 11

- 日期：2026-11-12
- 代码基线：分支 `phase28-net11-upgrade`，设计稿 commit `cb4066d`；升级落地 commit `53b594f`（父 `cb4066d`，未 push）
- 范围：四个工程 TFM 抬 net11.0（Core / WPF / Tests → `net11.0-windows`；WinUI → `net11.0-windows10.0.19041.0`，OS build 19041 不变）；`CommunityToolkit.Mvvm` 由浮动 `8.*` 钉死 `8.4.2`；**零行为变更意图**——不引入任何 .NET 11 新 API、不使用任何 C# 15 新语法；WinUI 壳继续冻结，只参与门禁 3 编译
- 设计：[Phase 28 设计](superpowers/specs/2026-11-12-d-player-phase28-net11-upgrade-design.md)
- 升级 diff（`cb4066d..53b594f`）：6 文件、8 增 8 删——4 处 TFM + 1 处版本号 + 3 处注释；唯一的 `.cs` 改动是 `D-player.WinUI/Views/CoverArtLoader.cs:26` 的一行注释。**未新增任何测试**，现有 310 条默认测试即回归网。
- **自包含说明**：本记录的**全部承重读数**（门禁输出、测试摘要、产物字节数与时间戳、SHA-256、音频回环指标、崩溃原文、复现输出）均已**内联或写全**，核对结论**不需要访问任何外部文件或目录**。文中 `Task N Step X` / `§Y` 形态的标注是对 Phase 28 实施过程记录（未随仓库发布）的出处标签，仅供追溯；走查截图、回环 WAV、queue 快照等视觉/音频原件已在核对后按项目既有验收惯例丢弃，本记录不以它们作为论据。

## 1. 自动化门禁

最终执行命令：`powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-gates.ps1 -Full -Audio`（exit=0，末行判据句逐字匹配 `all requested steps passed.`）。完整 50 行输出原文内联于 [§8.A](#a-门禁日志原件full-audiolog50-行)。

| 门禁 | 结果（抄自真实输出） |
|---|---|
| 1/3 解决方案筛选器构建（Core + WPF + Tests） | 通过，**0 警告 / 0 错误**，0.91 s，`-> ok (exit 0, 0 warning lines)` |
| 2/3 默认测试（`Category!=AudioDevice`） | **310 通过 / 0 失败 / 0 跳过**，1s 815ms，`-> ok`；dll `Tests\bin\Debug\net11.0-windows\D-player.Tests.dll (net11.0\|x64)` |
| 3/3 WinUI 3 构建 | 通过，**0 警告 / 0 错误**，9.92 s，`-> ok (exit 0, 0 warning lines)`（编译级证据口径见下） |
| 音频（`Category=AudioDevice`，真实 WASAPI） | **5 通过 / 0 失败 / 0 跳过**，3s 784ms，`-> ok`（实跑，非「未执行」） |

计数口径：升级前基线（SDK 11 + net10.0 TFM）已实测门禁 1 = 0 警告 / 0 错误、门禁 2 = 310 / 0 / 0（Task 1 Step 3，net10.0 路径）；升级后数字一致，零行为变更意图成立。本阶段**未新增任何测试**。真实音频 5 条本次**两次实跑一致**：Task 2 Step 3 独立跑 5/5（3s 820ms）、Step 4 `-Full -Audio` 内 5/5（3s 784ms），无设备争用迹象。

**域 B 编译级证据口径（承重区分，必须写明）**：门禁 3 的「net11.0 首次真编译」证据以 **Task 2 Step 1** 为准——`dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q` 单独首跑 0 警告 / 0 错误、26.51 s、exit=0，WinUI 产物落在新目录 `D-player.WinUI/bin/x64/Debug/net11.0-windows10.0.19041.0/`（`.dll` 297,472 B / `.exe` 212,992 B，LastWriteTime 2026-11-12 00:57:40.438 +0800），net11 obj 树的 XAML `.g.cs` 生成物时间戳 00:57:29–37 为「确实重新编译」之证。**Step 4 `-Full -Audio` 里的 gate 3 是增量空跑**：WinUI obj dll 时间戳仍是 Step 1 的 00:57:40、未被重写；`tools/verify-gates.ps1:118-120` 自陈「全增量构建不重编译就报不出警告」。因此 gate 3 的 0 警告判据由 Step 1 的首次真编译承重，Step 4 的 9.92 s 增量绿只是复核，不能单独当作「net11.0 编译通过」的证据。WASDK 2.6.5 的 TFM `>= 6.0` 下限静态取证由此被端到端实测坐实（XAML 编译 / CsWinRT 投影 / WinRT.SourceGenerator 在 net11.0-windows10.0.19041.0 上全部通过）。

## 2. 独立核对项

| 项 | 结果 | 证据 |
|---|---|---|
| GBK `.m3u` 解码（单测） | `Decode_GbkBytes_FallsBackTo936` **1/1**、`Import_GbkEncodedChinesePaths_DecodeAndResolve` **1/1**，两条真实退出码均 0（非 8，filter 确实匹配） | Task 1 Step 11；`Tests/Services/PlaylistFileEncodingTests.cs:71`、`Tests/Services/PlaylistFileServiceTests.cs:195`；dll 路径 net11.0-windows |
| GBK `.m3u` 导入（端到端） | 走查第 14 项：报告「2 首入列」，**接受 2 / 缺失 0**；中文曲目 FilePath/Title/Artist/Album 无乱码 | Task 3 §4 第 14 项；导入报告原文与队列新增项见 §3 第 14 行 |
| 输出目录与 exe | `bin/Debug/net11.0-windows/D-player.exe`，**73,728 B**，LastWriteTime **2026-11-12 00:59:11.492 +0800**；FileVersion `1.0.0.0`、ProductVersion `1.0.0+53b594f3ea69c117f27f3eb72a8086ddec2e5318`；runtimeconfig `tfm: net11.0` + `Microsoft.NETCore.App 11.0.0` + `Microsoft.WindowsDesktop.App 11.0.0` | Task 2 Step 8。时间戳被 Step 3/4 伴随构建重写（落在 Step 4 gate 1 窗口），**字节数与提交身份均与 Task 1 产物一致**；旧 `net10.0-windows/` 目录仍在盘上未删，走查严禁从旧路径启动 |
| publish 产物 | `bin/Release/net11.0-windows/win-x64/publish/D-player.exe`，**3,156,050 B**（≈3.01 MB），2026-11-12 01:00:38；同目录 `D-player.Core.pdb` 111,756 B / `D-player.pdb` 91,164 B / `appsettings.json` 234 B。framework-dependent 单文件（`--self-contained false`），**目标机现需 .NET 11 Desktop Runtime**；只验证能发布，未运行该产物 | Task 2 Step 5（exit=0）。Git Bash 下需 `MSYS_NO_PATHCONV=1` 让 `/p:PublishSingleFile=true` 逐字到达 dotnet（shell 适配，非产品/构建失败，见 §5） |
| Core WPF-free 自检 | 四类型名 grep（`System.Windows\|PresentationFramework\|ICollectionView\|CollectionViewSource`，排除 obj/bin）**无输出，exit=1** | Task 2 Step 6；Core 保持 WPF-free |
| 耦合审计 | **官方脚本崩溃，无 M1–M6 官方读数**；`0 cycles / 0 violations` 仅来自**明确标注的非官方原口径只读复现**（105 文件 / 14 命名空间 / 40 边）。**不得读作「耦合审计通过」** | Task 2 Step 7；详见 §5 与 §8.B–E（崩溃原文 + 复现输出 + 两个复现脚本全文内联） |
| queue.json SchemaVersion | **仍为 3**（抬 TFM 未改持久化 schema） | Task 3 §7；走查期 queue.json 原文转录（SchemaVersion=3） |
| MVVM 钉版生效 | `D-player.Core/obj/project.assets.json` 实际解析 `"CommunityToolkit.Mvvm/8.4.2"` | Task 1 Step 5（grep 取证） |
| CoverArtLoader 注释断言 | `D-player.WinUI/Views/CoverArtLoader.cs:26`「本机 net11.0-windows10.0.19041 + WASDK 2.6.5 编译通过」**已坐实**（门禁 3 绿） | Task 2 §1 |

## 3. WPF 交互验收

环境：Windows 11 / **.NET 11**（runtimeconfig 11.0.0）/ Debug；渲染端点 MOONDROP MM3A。执行方式按 controller 裁决：**implementer 亲自用 Computer Use（node_repl + @qoder-space/computer-use）驱动真实窗口，不派任何子代理**；「独立复核」由 controller（读关键截图 + queue.json 原文）与 task reviewer 承担（执行与复核分离）。起窗经显式路径 `D:\CodingProjects\D-player\bin\Debug\net11.0-windows\D-player.exe`，进程核对 PID 26888、Responding=True、Path 为 net11.0-windows；**未启动旧 net10.0 exe**。

临时合成库 **5 首**带标签 FLAC（ffmpeg 生成：2 位艺术家 / 3 张专辑，含 1 个中文文件名曲目）+ 1 个 **GBK 编码 `.m3u`**（`#EXTM3U` + 2 条相对路径、其中 1 条中文文件名，GBK + CRLF，确认非 UTF-8 → 端到端必走 CodePages 回退分支）。验收前备份 `settings.json` / `queue.json` / `library-cache.json` 并记 SHA-256，**三文件全部移走隔离（含 settings.json）**；结束后按字节还原，三文件 SHA-256 逐一全等；临时库、备份、脚本与截图已删除；未修改用户音乐文件（否定性声明口径见下）。

**16 项结果：14 PASS / 1 清单偏差（#8）/ 1 需用户确认（#9 听感子项；其客观持久化子项 PASS）。**

| # | 操作 | 结果 | 观测与覆盖限制 |
|---|---|---|---|
| 1 | 起窗 | PASS | 800×650 正常渲染：空库、侧栏仅「默认歌单」、播放栏/设置钮齐全，无崩溃；进程路径核实 net11.0-windows（PID 26888，Responding=True）。空库证明 settings 隔离生效 |
| 2 | 设置加合成库根 → 库视图 | PASS | 专辑网格 3 卡片（Album One/Artist A、Album Three/Artist B、Album Two/Artist A）；底栏「2 位艺术家 · 3 张专辑 · 5 首」 |
| 3 | 艺术家页四级钻取 | PASS | 艺术家列表（Artist A 2 专辑·3 首 / Artist B 1 专辑·2 首）→ 双击 Artist A → 详情（Album One+Two、播放全部、BackButton）→ 双击 Album One → 专辑详情 2 行曲序 01→02；单击仅选中、双击钻取 |
| 4 | 播放全部 + 真的出声 | PASS | 播放栏 Track One 播放中、进度前进、信息面板 44,100 Hz。**客观音频**：WASAPI 回环 5s = AUDIO_PRESENT（peak 0.099993、RMS 0.070695、5 活跃秒、逐秒 RMS 恒 0.0707）；暂停对照 = SILENT（RMS 0）。非 UI 推断 |
| 5 | 暂停/继续/下一首/上一首 | PASS | 暂停冻结 seek；继续 seek 前进（0.0744→0.1044）；下一首→Track Two；上一首→Track One（重置曲首）；再暂停冻结 |
| 6 | 进度条单击跳转 + 拖动 | PASS | 单击 x=400 → seek 0:16（value 0.540，历史坏点本次正常）；从 thumb 拖到 x=150 → seek 0:03。首次拖动因起点不在 thumb 上未生效，改从 thumb 起拖后生效（**操作问题，非产品缺陷**） |
| 7 | 音量滑块 + 静音钮 | PASS | 拖动音量 thumb 0.8→≈0.207；静音→VolumeSlider=0；再点静音→恢复 0.207（round-trip 正确） |
| 8 | 频谱 | **清单偏差（非 net11 回归）** | 判据「暂停归 2px」不满足：播放时 32 柱渲染（maxBarHeight=71px、90 列≥3px），空闲=0px（2px 基线），**暂停 2.5s 与 6s 后仍 71px/84 列、未归 2px**（冻结末帧）。源码核对：WPF `Views/Controls/SpectrumView.xaml.cs` 无 Paused/Stopped 归零路径（既有设计）。**「32 柱跳动」未验**——纯正弦刺激下稳态频谱时不变，两播放帧隔 700ms 像素相同，动态物理不可判；渲染 32 柱有证据、动态跳动未验。详见 §5 |
| 9 | EQ 打开→拖动→保存 | **客观子项 PASS；听感子项未验（用户未确认）** | EQ 对话框 11 根竖直滑块渲染正确（31…16k+Pre）、全 0、Flat；启用后拖 500Hz→即时显示 +3、preset 变 Custom；保存→重开仍 +3 且启用 → **持久化 PASS**。「拖动即时听感变化、无爆音」为主观听感，**未验（用户未确认）**，不得写成 PASS；DSP 数学由单测覆盖 |
| 10 | EQ 打开→拖动→取消 | PASS | 在已保存（500=+3）基础上拖 1k→+8；取消→重开：1k 回 0、500 仍 +3 → 回滚到打开瞬间实时配置，正确 |
| 11 | 库搜索 "Track" + 清空 | PASS（测试数据说明） | "Track"→曲目组 4 行（中文曲目不含 Track 正确排除）。brief 期望「三组」但 "Track" 只匹配曲目（无艺术家/专辑名含 Track）——**测试数据命名问题，非应用缺陷**；另用补充词 "r" 验证三组布局：艺术家(2)/专辑(3)/曲目(5) 均渲染。清空按钮→搜索框回 placeholder、结果列表移除 |
| 12 | 库内右键专辑→加入歌单→新建并加入 | PASS | 右键 Album One→「加入歌单…」→对话框「将加入 2 首曲目」；点名称框→列表高亮清空且「加入」禁用（Phase 26 焦点一致性保持）；填「走查歌单」→新建并加入；**库视图保持、侧栏无高亮错位**；打开走查歌单含 Track One+Track Two |
| 13 | 歌单内拖拽重排 + ▶ 跟随 | PASS（输入限制） | 播放中把 Track One 从 idx0 拖到 idx1 → 队列变 [Track Two, Track One]；**当前曲高亮/▶ 跟随到 Track One 的新行**（信息面板仍 Track One、seek 继续前进）→ 引用身份而非结构相等；`queue.json` `CurrentIndex=1` 佐证。**覆盖限制**：Computer Use click API 无 modifier 参数，无法注入 Ctrl/Shift 多选点击，故以**单行拖拽（同一重排代码路径）**验证；字面「多选」拖拽未直接注入 |
| 14 | 导入 gbk.m3u | PASS | 导入报告「已导入「gbk.m3u」→ 歌单「走查歌单」/ 2 首入列」（**接受 2、缺失 0**）；队列新增 中文曲目（FilePath `…\01 中文曲目.flac`、Title 中文曲目、Artist B/Album Three，**无乱码**）+ Track Five。CodePages GBK 回退在 net11.0 端到端成立。**状态说明（防误读）**：导入后队列显示**自然播完到队列尾停止**（信息面板 Track Two、seek 0:03/0:03、无当前曲标记），与第 13 项的播放态不同；第 15 项显式重置 Track One——**这不是引用身份塌缩** |
| 15 | 关闭应用 | PASS | 先置 Track One 暂停 ~0:01 再点 CloseButton；窗口消失、`Get-Process` 无输出（正常退出、无崩溃）；关闭路径落盘 settings/queue/library-cache（cancel-and-close 生效） |
| 16 | 重启断点续播 | PASS | 重启后队列恢复（走查歌单 4 首、▶/高亮在 Track One）、信息面板 Track One、seek 恢复 ~0:01、**播放钮为三角形=未自动播放**、频谱平直；回环 2s = NO_DATA（**只就位不出声**客观佐证）；库完整（3 专辑、底栏 2/3/5）、双歌单在侧栏 |

**客观音频判别力**（WasapiLoopbackCapture 探针输出，端点 MOONDROP MM3A）：空闲基线 NO_DATA、播放 AUDIO_PRESENT（peak 0.099993 / RMS 0.070695，5 活跃秒）、暂停 SILENT（peak 0 / RMS 0）、重启就位 NO_DATA（captured-bytes 0）→ 判别成立。第 4 项「真的出声」以客观端点证据判 PASS，未凭 UI 播放状态推断。reviewer 另对捕获的 WAV 逐样本复算 peak/RMS，与转录零偏差（录制原件未保留）。

**独立复核**：implementer 自读关键截图（Read 工具，非仅采信操作日志）——库网格、播放中、重排后、导入结果、EQ 拖动共 5 帧，均与上表观测一致；并 Read `queue.json` 原文（走查结束、还原前）：SchemaVersion=3、`LibraryPlayback.Items`=[01 Track One, 02 Track Two]（曲序正确）/`CurrentIndex=1`、走查歌单 Items=[02 Track Two, 01 Track One, 01 中文曲目, 02 Track Five]（重排后顺序）/`CurrentIndex=1`（引用身份）、中文路径无乱码（解码后为「中文曲目」，原文以 JSON Unicode 转义保存）。controller 另读关键截图与 queue.json 原文复核；reviewer 亲读 12+ 张截图逐条吻合、对活文件重算 SHA-256 仍全等、确认 Temp 已删与无残留进程。

**数据还原 SHA-256 核对**（两组逐一全等）：

| 文件 | 第一组（备份） | 第二组（还原后活文件） | 相等 |
|---|---|---|---|
| library-cache.json | 4758EA35A977CABC12211E21537434EE9B37A8AF6209B4EC00DC0A6564C60BCF | 4758EA35A977CABC12211E21537434EE9B37A8AF6209B4EC00DC0A6564C60BCF | 相等 |
| queue.json | 810EA1BBB61B7D1374E36D7F5DDC7CE40D30DCD663BB3F8648460165E2F3B9C2 | 810EA1BBB61B7D1374E36D7F5DDC7CE40D30DCD663BB3F8648460165E2F3B9C2 | 相等 |
| settings.json | 8FF849CA447F205841F5824B44F517DE53A877CEAA80EAD9C341C6E6E33AC236 | 8FF849CA447F205841F5824B44F517DE53A877CEAA80EAD9C341C6E6E33AC236 | 相等 |

还原后尺寸复核 96695 / 11245 / 919 B，与原始一致；核对全等后才删备份。

**SHA-256 出处口径（不得写成「已保全原件可复算」）**：上表两组哈希均为 **Task 3 报告转录**，`Get-FileHash` 原件未保全；**第一组备份已随 Step 7 删除，不可复算**。其可信度由两条旁证支撑：① controller 于 2026-11-12 约 13:30 亲自取证 Temp 备份**完好且双份**（settings 919 B / queue 11245 B / library-cache 96695 B，各自 `.aside` 原件同字节数）；② reviewer 在清理后**对活文件重算 SHA-256，仍与两组全等**。

**否定性声明口径（无法直接证明，按旁证写）**：「未读写 `D-player-winui/`、未修改用户真实音乐文件」属否定性声明，无法直接证明；旁证为走查 `queue.json` 全部 FilePath 落在临时合成库（Temp SynthLib，走查后已删）、且 settings/library-cache 还原后哈希与原始全等。本记录按此口径陈述，不写成已直接证明。

## 4. Rider 红线定性

**用户确认结论（2026-11-12，controller 直接问得）**：Rider 中 `LibraryViewModel` / `PlaylistViewModel` 的「`NotifyPropertyChangedForAttribute` 特性重复」红线**已消失**——抬 TFM + 工程重载后自行消失。

**升级前误报证据链（设计稿 §2.3，保留存档）**：
- CLI 两次构建 0 警告 / 0 错误（含 `--no-incremental` 强制全量重建）→ Roslyn 11 接受这份代码。
- 用户确认 Rider 内**可以正常构建并运行**，只是编辑器红线。
- .NET 11 ref pack 中**不存在** `NotifyPropertyChangedForAttribute`（grep 10.0.15 与 11.0.0 的 ref 目录均无命中）→ 排除「BCL 新增同名特性造成冲突」。
- 代码用传统写法：`[ObservableProperty]` 打在字段上，`LibraryViewModel.cs:44-51` 的 `_page` 叠 8 个 `[NotifyPropertyChangedFor]`、`PlaylistViewModel.cs:68-71` 的 `_currentIndex` 叠 3 个 `[NotifyCanExecuteChangedFor]`；这些特性本就是 `AllowMultiple`。
- Rider 2026.3 的 `idea.log` / `RoslynWorker` / `MsBuildTask` 日志中无源生成器异常、无错误码记录。

**明确声明：未改任何产品代码去迎合它。** 不为消红线改 `[NotifyPropertyChangedFor]` 写法（合并特性 / 改 partial property 等）、不加 `#pragma warning disable`（仓库当前 pragma 数为 **0**，登记在册的纪律）、不用 `NoWarn` 压警告。红线随工程重载自行消失，处置顺序（Invalidate Caches / Restart 等）未实际动用。

**具体机制未定性**：只定性到「IDE 侧、非编译错误」这一层；确切错误码与文本未取得（用户未提供、日志中也没有），误报的具体机制（ReSharper 检查误判 vs Roslyn 工作区重复跑生成器）仍未定性。

## 5. 发现与处理

| 发现 | 级别 | 处理 |
|---|---|---|
| 耦合审计官方脚本 `tools/coupling-audit/Invoke-CouplingAudit.ps1:97` 抛 `ContainsKey(null)` / ArgumentNullException，**无 M1–M6 官方读数** | **既有工具缺陷（非升级引入）** | 不在 Phase 28 修脚本（超出零行为变更范围）。`0 cycles / 0 violations` 仅采信**明确标注的非官方原口径只读复现**；**不得写成「耦合审计通过」**。承重论据见下；崩溃原文 + 复现输出 + 两个脚本全文内联于 §8.B–E。**移交 Phase 29** |
| 走查第 8 项判据「暂停归 2px」不满足 | **清单偏差（非 net11 回归）** | 「暂停归 2px」是 **WinUI 壳 Phase 23 契约**（`docs/COUPLING.md:237`），被走查清单误套到 WPF。WPF `Views/Controls/SpectrumView.xaml.cs:32,157-166` 无 Paused/Stopped 归零路径（WinUI 那份 `D-player.WinUI/Views/SpectrumView.xaml.cs:40-43,67-73` 才有 `Array.Fill(_targetHeights, MinBarHeight)`）。升级 diff 未触碰任何 SpectrumView → 非 net11 回归。**不改产品代码**；记为清单偏差 + **WPF/WinUI 暂停行为不对称观察项**（是否统一属独立决策） |
| 第 8 项「32 柱跳动」 | **未验（动态物理不可判）** | brief 强制纯正弦刺激使两播放帧（隔 700ms）像素相同，稳态频谱时不变 → 动态跳动不可判。**渲染 32 柱有证据、动态跳动未验**。建议未来频谱动态验收改用**瞬态信号** |
| 第 11 项查询词 "Track" 只命中曲目组 | 清单偏差 / 测试数据 | "Track" 不匹配任何艺术家/专辑名，属合成库命名与 brief 词不匹配，**非缺陷**；已用补充词 "r" 验证三组布局（艺术家 2 / 专辑 3 / 曲目 5） |
| 第 13 项字面「多选」拖拽 | **覆盖限制** | Computer Use click API 无 modifier 参数，无法注入 Ctrl/Shift 多选；以单行拖拽（同一重排代码路径）验证重排 + ▶ 引用身份（`queue.json` `CurrentIndex=1` 佐证）。字面多选拖拽未直接注入 |
| 第 9 项 EQ 即时听感 / 无爆音 | **未验（用户未确认）** | 主观听感，代理边界外；客观持久化子项 PASS，DSP 数学由单测覆盖。不得写成 PASS |
| 前一次 implementer 进程异常消失 | **原因不明、未复现** | 本次走查全程（起窗→16 项→两次正常关窗）未复现异常退出/崩溃/无响应；查 Application 事件日志近 14 小时 `Message -match 'D-player'` **零命中**，无崩溃记录。**不淡化为正常、不夸大为崩溃回归**；前次终止原因仍不明，不编结论 |
| 第 6 项首次拖动未生效 | 走查操作 | 起点不在 thumb 上，改从 thumb 起拖后生效；操作问题，非产品缺陷 |
| publish 命令在 Git Bash 下首次 MSB1008 | shell 适配 | MSYS 路径转换吃掉 `/p:` 前导斜杠；用 `MSYS_NO_PATHCONV=1` 重试使语义与 brief 完全一致，发布形态未改。非产品/构建失败 |
| IME 候选窗 / 模态对话框 Ctrl+F / 退出排空 / 网络根 | **未测（用户手动确认或环境不可测）** | 属用户手动确认项或环境不可测项，本阶段未测，明写为覆盖缺口 |

**耦合审计崩溃的承重论据（「输入图未变」，不是复现脚本）**：`cb4066d..53b594f` 的 diff 只含 4 行 TFM + 1 行版本号 + 3 处注释，唯一 `.cs` 改动是 `CoverArtLoader.cs` 的一行注释；审计脚本读 `using DPlayer.*` 建图 → **扫描集与 using 图升级前后逐字节相同** → 崩溃在升级前 HEAD `cb4066d` 上必然同样发生。根因：`D-player.WinUI/Theme/`（Motion.cs / TokenResource.cs / VisualTree.cs，声明 `namespace DPlayer.WinUI.Theme;`）**不在脚本的七个 `$layerDirs`**（Models/Services/ViewModels/Views/Configuration/Extensions/Converters）里 → `DPlayer.WinUI.Theme` 不是 `$uses` 的键 → 四个 View（PlayerBar/NavRail/TrackList/InfoPanel）的 `using DPlayer.WinUI.Theme;` 成悬空边 → `Test-SelfReachable` 的 BFS 对 dequeued 节点取 `$adj[$n]` 得 `$null`，`@($null)` 含一个 null 元素，`$seen.ContainsKey($null)` 抛 ArgumentNullException。**日期归因（悬空 using 引入于 2026-10-07 的 `e339ea3` / `69f41d0`，早于 Phase 28 一个多月）只作附注**，不作承重论据。旁证：`docs/COUPLING.md:294` 早已记录同一崩溃机制（Phase 20 搬迁后 BFS 撞「被引用但未被扫描」的命名空间），当时靠补第四个 scan root 修复；本次是同一脆弱点的另一个触发器（Theme 目录不在 layerDirs）。

**M4/M5/M6 本轮无读数（必须明写）**：非官方复现脚本只覆盖 **M1 层边 / M2 环 / M3 层级违规**；**M4/M5/M6 本轮无读数**。**M6 的两个预留 Stub（`IAudioDeviceManager` / `IAudioOutputFactory`）是既有登记项（COUPLING §4），不因本轮无读数而消失**。M1 层边计数相对 Phase 20 有历史漂移（如 `WinUI->Services` 1→3，系 Phase 21+ 功能开发所致，且已证明升级前后图相同），不在 0 cycles / 0 violations 判据范围内。修脚本收集口径（把 Theme 纳入 layerDirs）会重置 M1/M3/M5 基线，属独立决策，移交 Phase 29。

## 6. 限制

- **发布形态未改**：framework-dependent（`--self-contained false`）现要求目标机装 **.NET 11 Desktop Runtime**（不再是 .NET 10）；WinUI 壳 self-contained 不受影响。本机验证仅到「能产出」，未运行 publish 产物。
- **门禁 3 只覆盖编译**：WinUI 的 XAML 运行时模板挂接、`NavigationView` 左栏逻辑、`AppWindow.Closing` 落盘无自动回归；本阶段未做 WinUI 真机走查（壳已冻结）——**明示的覆盖缺口**。
- **耦合审计无官方读数**：官方脚本崩溃（既有工具缺陷），M1–M6 官方读数缺失；0/0 仅来自非官方原口径复现，M4/M5/M6 本轮无读数（见 §5）。
- **Rider 红线的具体机制未定性**：只到「IDE 侧、非编译错误」这一层，确切错误码未取得。
- **未使用任何 .NET 11 新 API / C# 15 新语法**（零行为变更意图）。
- **GUI 走查覆盖限制**：第 13 项字面多选拖拽未注入 modifier；第 8 项动态跳动未验（纯正弦稳态）；第 9 项听感/无爆音未验（用户未确认）；IME 候选窗 / 模态对话框 Ctrl+F / 退出排空 / 网络根未测（用户手动确认或环境不可测）。
- **SHA-256 第一组备份已删不可复算**：两组哈希为报告转录，旁证为 controller 13:30 双份备份尺寸取证 + reviewer 清理后对活文件重算仍全等（见 §3）。
- **否定性声明无法直接证明**：「未读写 `D-player-winui/`、未改用户真实音乐文件」按旁证口径陈述（queue 全路径在 Temp SynthLib + settings/cache 哈希与原始全等），非直接证明。

## 7. 移交

- **Phase 29（非音频路径现代化）**：设计稿附录 A（C# 15 核实 + 纠正清单）/ B（.NET 11 BCL 新 API 证据分级，B 级用前必须再核实）/ C（代码库候选盘点，含 `RootGates` 无界增长、`FindAncestor` 三份副本未处理 `ContentElement` 两个真缺陷）。**并移交耦合审计脚本修复**：null 防御 和/或 把 `Theme` 纳入收集——后者改口径、重置 M1/M3/M5 基线，属独立决策。
- **Phase 30（音频实时路径性能重写）**：设计稿 §9.3；**必须先量基线 + 把随机证据升级为确定性证据**（闸门侧回归以 1–5ms 错位窗口断言「不抛异常」，是真实但随机的证据），再谈重写。不可动的纪律见 COUPLING §5/§7。
- **观察项（独立决策，不在本阶段处置）**：WPF/WinUI 暂停频谱行为不对称（WPF 冻结末帧、WinUI 归 2px）是否统一；未来频谱动态验收改用瞬态信号。

## 8. 证据内联（原件未随仓库保留）

**口径**：Task 2 / Task 3 的视觉与音频原件（走查截图 54 张、回环 WAV、queue 快照、门禁日志副本、复现脚本）已在核对后按项目既有验收惯例丢弃，**本记录不引用任何外部文件或目录作为证据出处**。核对结论所需的全部内容以下方**逐字内联**形式保存在本文件内：门禁日志 50 行原文、耦合崩溃原文与复现输出、两个复现脚本全文——**读者只读本文件即可复核，无需访问任何外部路径**。

Temp 的 `dplayer-phase28/`（合成库、备份、脚本、截图、探针）已按 Task 3 Step 7 `rm -rf` 删除，**本记录不引用任何 Temp 路径作为证据出处**。`coupling-audit.log` 实测 **0 字节**（PowerShell 错误流未落盘），**本记录不引用它**；耦合崩溃引文以控制台捕获与下方内联原件为准。下文 A 的 `full-audio.log` 逐字内联，其中 `/flp:logfile=…Temp…` 是门禁脚本自身的瞬态 MSBuild 文件日志参数（原始输出的一部分），非证据引用路径。

### A. 门禁日志原件（`full-audio.log`，50 行）

```text
mode: Full (3 gates: solution filter build + test + WinUI shell build)
audio: enabled (real WASAPI device tests appended)

--- gate 1/3: build the solution filter (Core + WPF + Tests), 0 warnings required
    dotnet build D-player.slnf -c Debug --nologo -v q /flp:logfile=C:\Users\darknight2236\AppData\Local\Temp\dplayer-verify-gates-62792734d5cb49148d768b3f7ff8121d.log;verbosity=quiet;errorandwarningonly=true

已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:00.91
    -> ok (exit 0, 0 warning lines)

--- gate 2/3: test the solution filter, non-audio (no --nologo here)
    dotnet test D-player.slnf -c Debug -v q --filter Category!=AudioDevice
正在从以下位置运行测试 D:\CodingProjects\D-player\Tests\bin\Debug\net11.0-windows\D-player.Tests.dll (net11.0|x64)
D:\CodingProjects\D-player\Tests\bin\Debug\net11.0-windows\D-player.Tests.dll (net11.0|x64) 已通过 [+310/x0/?0] (1s 554ms)

测试运行摘要: 已通过!
  总计: 310
  失败: 0
  成功: 310
  已跳过: 0
  持续时间: 1s 815ms
    -> ok

--- audio: real-WASAPI integration tests (Category=AudioDevice)
    dotnet test D-player.slnf -c Debug -v q --filter Category=AudioDevice
正在从以下位置运行测试 D:\CodingProjects\D-player\Tests\bin\Debug\net11.0-windows\D-player.Tests.dll (net11.0|x64)
D:\CodingProjects\D-player\Tests\bin\Debug\net11.0-windows\D-player.Tests.dll (net11.0|x64) 已通过 [+5/x0/?0] (3s 555ms)

测试运行摘要: 已通过!
  总计: 5
  失败: 0
  成功: 5
  已跳过: 0
  持续时间: 3s 784ms
    -> ok

--- gate 3/3: build the WinUI 3 project, 0 warnings required
    dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q /flp:logfile=C:\Users\darknight2236\AppData\Local\Temp\dplayer-verify-gates-b54ff3b3940f4ed6b5772fb721d209bf.log;verbosity=quiet;errorandwarningonly=true

已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:09.92
    -> ok (exit 0, 0 warning lines)

all requested steps passed.
```

### B. 耦合审计官方脚本崩溃原文（UTF-8 控制台，exit=1，无 M1–M6 读数）

命令：`powershell -NoProfile -ExecutionPolicy Bypass -File tools/coupling-audit/Invoke-CouplingAudit.ps1`

```text
Where-Object : 使用“1”个参数调用“ContainsKey”时发生异常:“键不能为 null。
参数名: key”
所在位置 D:\CodingProjects\D-player\tools\coupling-audit\Invoke-CouplingAudit.ps1:97 字符: 40
+ ... es.Keys | Sort-Object | Where-Object { Test-SelfReachable $_ $uses })
+                             ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    + CategoryInfo          : NotSpecified: (:) [Where-Object], MethodInvocationException
    + FullyQualifiedErrorId : ArgumentNullException,Microsoft.PowerShell.Commands.WhereObjectCommand
exit=1
```

只读诊断脚本 `diag-coupling.ps1`（全文见 §8.D）定位悬空边，输出：

```text
scanned file count: 105
namespace keys: 14
== dangling using-targets (target not among namespace keys) ==
DANGLING: DPlayer.WinUI.Views -> DPlayer.WinUI.Theme
    in file: D:\CodingProjects\D-player\D-player.WinUI\Views\PlayerBar.xaml.cs
    in file: D:\CodingProjects\D-player\D-player.WinUI\Views\NavRail.xaml.cs
    in file: D:\CodingProjects\D-player\D-player.WinUI\Views\TrackList.xaml.cs
    in file: D:\CodingProjects\D-player\D-player.WinUI\Views\InfoPanel.xaml.cs
== files NOT tracked by git (informational) ==
（空——105 个被扫描文件全部被 git 跟踪，无生成文件混入）
```

### C. 非官方原口径只读复现输出（**非官方脚本输出**，明确标注）

复现脚本 `diag-coupling-m2m3.ps1`（全文见 §8.E）：收集规则、命名空间解析、建图、`Get-Layer`、forbidden 列表与官方脚本逐字一致；唯一差异是把「被引用但未被扫描」的命名空间按脚本显然的本意处理为**无出边**（而非抛异常）。口径未放宽、未新增扫描目录。reviewer 已逐行比对两脚本确认「原口径」自我声明为真。

```text
== REPLICATION (original caliber, null-safe) ==
scanned files: 105; namespace keys: 14; namespace edges: 40
== M2 cycles ==
0 cycles
== M3 layer violations ==
0 violations
== M1 layer edges (for reference) ==
Converters->Models : 1
Extensions->Configuration : 1
Extensions->Services : 2
Extensions->ViewModels : 1
Root->Configuration : 1
Root->Extensions : 1
Root->Services : 1
Root->ViewModels : 1
Services->Configuration : 1
Services->Models : 2
ViewModels->Configuration : 1
ViewModels->Models : 1
ViewModels->Services : 2
Views->Configuration : 1
Views->Models : 2
Views->Root : 1
Views->Services : 4
Views->ViewModels : 3
WinUI->Models : 2
WinUI->Services : 3
WinUI->ViewModels : 2
exit=0
```

**复现读数：0 cycles / 0 violations**——与 Phase 20 基线（`COUPLING.md:292`）的判据项一致。仅覆盖 M1/M2/M3；M4/M5/M6 本轮无读数（见 §5）。

### D. 只读诊断脚本 `diag-coupling.ps1` 全文

```powershell
# Read-only diagnostic: replicate Invoke-CouplingAudit.ps1 collection + namespace graph,
# find dangling using-targets (namespaces referenced by `using DPlayer.X;` but never declared
# by any scanned file). Does NOT modify anything.
$ErrorActionPreference = 'Stop'
$RepoRoot = 'D:\CodingProjects\D-player'
$layerDirs = @('Models','Services','ViewModels','Views','Configuration','Extensions','Converters')
$scanRoots = @(
    (Join-Path $RepoRoot 'D-player.Core'),
    (Join-Path $RepoRoot 'D-player.WinUI'),
    (Join-Path $RepoRoot 'D-player'),
    $RepoRoot
)
$files = @()
foreach ($root in $scanRoots) {
    foreach ($d in $layerDirs) {
        $p = Join-Path $root $d
        if (Test-Path $p) { $files += @(Get-ChildItem -Path $p -Filter *.cs -Recurse -File) }
    }
}
$files += @(Get-ChildItem -Path $RepoRoot -Filter *.cs -File)
"scanned file count: $($files.Count)"

$uses = @{}
$fileNs = @{}
foreach ($f in $files) {
    $text = Get-Content -LiteralPath $f.FullName -Raw
    $m = [regex]::Match($text, '(?m)^namespace\s+(DPlayer(?:\.[A-Za-z0-9_]+)*)\s*;')
    $ns = if ($m.Success) { $m.Groups[1].Value } else { 'DPlayer' }
    $fileNs[$f.FullName] = $ns
    if (-not $uses.ContainsKey($ns)) { $uses[$ns] = [System.Collections.Generic.HashSet[string]]::new() }
    foreach ($u in [regex]::Matches($text, '(?m)^using\s+(DPlayer(?:\.[A-Za-z0-9_]+)*)\s*;')) {
        $t = $u.Groups[1].Value
        if ($t -ne $ns) { [void]$uses[$ns].Add($t) }
    }
}
"namespace keys: $($uses.Keys.Count)"

"== dangling using-targets (target not among namespace keys) =="
$found = $false
foreach ($ns in ($uses.Keys | Sort-Object)) {
    foreach ($t in ($uses[$ns] | Sort-Object)) {
        if (-not $uses.ContainsKey($t)) {
            $found = $true
            "DANGLING: $ns -> $t"
            foreach ($fn in $fileNs.Keys) {
                if ($fileNs[$fn] -eq $ns) {
                    $txt = Get-Content -LiteralPath $fn -Raw
                    if ($txt -match ('(?m)^using\s+' + [regex]::Escape($t) + '\s*;')) { "    in file: $fn" }
                }
            }
        }
    }
}
if (-not $found) { "none" }

"== files NOT tracked by git (informational) =="
$tracked = & git -C $RepoRoot ls-files
$trackedSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($t in $tracked) { [void]$trackedSet.Add((Join-Path $RepoRoot ($t -replace '/','\'))) }
foreach ($f in $files) {
    if (-not $trackedSet.Contains($f.FullName)) { "UNTRACKED/IGNORED: $($f.FullName)" }
}
```

### E. 非官方原口径复现脚本 `diag-coupling-m2m3.ps1` 全文

```powershell
# Read-only REPLICATION of Invoke-CouplingAudit.ps1 M2/M3 at the ORIGINAL collection caliber.
# Collection rules + namespace graph construction are verbatim-identical to the official script.
# ONLY difference: a using-target namespace absent from the scan set is treated as having no
# outgoing edges (the script's evident intent) instead of throwing ContainsKey(null).
# This is NOT official script output. It modifies nothing.
$ErrorActionPreference = 'Stop'
$RepoRoot = 'D:\CodingProjects\D-player'
$layerDirs = @('Models','Services','ViewModels','Views','Configuration','Extensions','Converters')
function Get-Layer([string]$ns) {
    $parts = $ns -split '\.'
    if ($parts.Count -ge 2) { return $parts[1] }
    return 'Root'
}
$scanRoots = @(
    (Join-Path $RepoRoot 'D-player.Core'),
    (Join-Path $RepoRoot 'D-player.WinUI'),
    (Join-Path $RepoRoot 'D-player'),
    $RepoRoot
)
$files = @()
foreach ($root in $scanRoots) {
    foreach ($d in $layerDirs) {
        $p = Join-Path $root $d
        if (Test-Path $p) { $files += @(Get-ChildItem -Path $p -Filter *.cs -Recurse -File) }
    }
}
$files += @(Get-ChildItem -Path $RepoRoot -Filter *.cs -File)
$uses = @{}
foreach ($f in $files) {
    $text = Get-Content -LiteralPath $f.FullName -Raw
    $m = [regex]::Match($text, '(?m)^namespace\s+(DPlayer(?:\.[A-Za-z0-9_]+)*)\s*;')
    $ns = if ($m.Success) { $m.Groups[1].Value } else { 'DPlayer' }
    if (-not $uses.ContainsKey($ns)) { $uses[$ns] = [System.Collections.Generic.HashSet[string]]::new() }
    foreach ($u in [regex]::Matches($text, '(?m)^using\s+(DPlayer(?:\.[A-Za-z0-9_]+)*)\s*;')) {
        $t = $u.Groups[1].Value
        if ($t -ne $ns) { [void]$uses[$ns].Add($t) }
    }
}
$nsEdges = @()
foreach ($ns in $uses.Keys) { foreach ($t in $uses[$ns]) { $nsEdges += [pscustomobject]@{ From=$ns; To=$t } } }
$layerEdges = @{}
foreach ($e in $nsEdges) {
    $lf = Get-Layer $e.From; $lt = Get-Layer $e.To
    if ($lf -ne $lt) { $k = "$lf->$lt"; $layerEdges[$k] = ([int]$layerEdges[$k]) + 1 }
}
# M2: null-safe BFS (missing key => no neighbors)
function Test-SelfReachableSafe([string]$start, $adj) {
    $seen = @{}
    $q = [System.Collections.Generic.Queue[string]]::new()
    $init = if ($adj.ContainsKey($start)) { @($adj[$start]) } else { @() }
    foreach ($nb in $init) { if ($null -ne $nb -and -not $seen.ContainsKey($nb)) { $seen[$nb]=$true; $q.Enqueue($nb) } }
    while ($q.Count -gt 0) {
        $n = $q.Dequeue()
        if ($n -eq $start) { return $true }
        $nbs = if ($adj.ContainsKey($n)) { @($adj[$n]) } else { @() }
        foreach ($nb in $nbs) { if ($null -ne $nb -and -not $seen.ContainsKey($nb)) { $seen[$nb]=$true; $q.Enqueue($nb) } }
    }
    return $false
}
$cyclic = @($uses.Keys | Sort-Object | Where-Object { Test-SelfReachableSafe $_ $uses })
$forbidden = @(
    'Services->ViewModels','Services->Views',
    'ViewModels->Views',
    'Models->Services','Models->ViewModels','Models->Views','Models->Configuration','Models->Extensions','Models->Converters',
    'Configuration->Services','Configuration->ViewModels','Configuration->Views','Configuration->Extensions','Configuration->Converters',
    'Converters->Services','Converters->ViewModels','Converters->Views','Converters->Extensions'
)
$violations = @($layerEdges.Keys | Where-Object { $forbidden -contains $_ } | Sort-Object)
"== REPLICATION (original caliber, null-safe) =="
"scanned files: $($files.Count); namespace keys: $($uses.Keys.Count); namespace edges: $($nsEdges.Count)"
"== M2 cycles =="
if ($cyclic.Count -eq 0) { "0 cycles" } else { $cyclic | ForEach-Object { "cycle involves: $_" } }
"== M3 layer violations =="
if ($violations.Count -eq 0) { "0 violations" } else { $violations }
"== M1 layer edges (for reference) =="
$layerEdges.GetEnumerator() | Sort-Object Name | ForEach-Object { "{0} : {1}" -f $_.Key, $_.Value }
```
