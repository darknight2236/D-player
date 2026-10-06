# Phase 20 对比材料：WPF 壳 vs WinUI 壳

> 文档日期：2026-10-06 · 最后更新：2026-10-07（最终修复波） · 代码基线：`dc67f13`（A1-A5 壳侧修复）+ 本波文档/工具提交 · 对应分支：`master`
> 关联设计稿：[`specs/2026-10-06-d-player-phase20-winui-shell-design.md`](./superpowers/specs/2026-10-06-d-player-phase20-winui-shell-design.md)
> 关联实现计划：[`plans/2026-10-06-d-player-phase20-winui-shell-implementation.md`](./superpowers/plans/2026-10-06-d-player-phase20-winui-shell-implementation.md)

**这份材料怎么用（请先读这一段）**

Phase 20 交付了一条 WinUI 3 的第二 UI 壳（第一条纵向切片），它**能不能替代 WPF 壳不由代码判定，由你的手感判定**。下面两张表：

1. **功能等价核对表**（第 2 节）——客观项，已经按"实测/未实测"如实填好。**标 ❓ 的行必须由你去确认**（取证替不了你：需要你的曲库、你的耳朵，或需要多歌单数据）；标 🟡 的行已经取到证、只剩你顺眼复核一下。两类都不等于"没问题"。
2. **六维评分表**（第 4 节）——主观项，**分数栏是空的，留给你填**。每个维度 1–5 分（5 = 非常满意），备注栏已预先放了已知事实，供你打分时参考，也可以改写。

**填完请把结论（续投 WinUI / 停止并保留 Core 抽取 / 其它）告诉我，我会把结论记进本文档末尾、`docs/PROJECT.md` 的阶段状态，并同步进项目记忆。** 第 5 节是决策门的记录位。

**本文档刻意不含结论。** 决策门尚未发生，任何"已经选定 WinUI"的表述都是错的。

> **本表基线已更新到最终修复波**：代码基线 `dc67f13`（A1-A5 五处壳侧修复）+ 本波文档/工具提交。A1-A4 改变了"这一格该填什么"——深色主题改由壳强制、拖动进度条不再连续 Seek、空白区双击不再回播上次选中的曲目、进度值不再由壳侧抄一份公式。**修复之前记录的那些差异已经不再是差异**，本表按修复后的源码重写；仍缺的能力照旧列在 §3。

---

## 1. Phase 20 到底交付了什么

| 交付物 | 内容 | 校验位置 |
|---|---|---|
| `D-player.Core` | WPF-free 的共享类库（Models / Services / ViewModels / Configuration / Extensions），`net10.0-windows`，不开 `UseWPF`；两个壳都引用它 | 在 `D-player.slnf` 内 |
| `D-player`（WPF 壳） | 既有应用。**行为与外观按设计零变化**；只多了"数据目录由壳注入"与 `SortedView` 的删除（排序在界面上怎么变，见 §2 的"排序"行；`SortedView` 为什么删、删后靠什么排序，见 [`PROJECT.md`](./PROJECT.md) §4.3 关键决策 27 与 §5.4b 的 `SortBy`。§3 只列 WinUI 侧缺的能力，与这条无关） | 在 `D-player.slnf` 内 |
| `Tests` | 180 条单测，引用目标从 WPF 壳改为 Core。**这 180 条覆盖 Core + WPF + Tests，WinUI 壳一条都不覆盖** | 在 `D-player.slnf` 内 |
| `D-player.WinUI` | WinUI 3 壳（unpackaged + self-contained，x64），第一条纵向切片：歌单导航 + 曲目列表 + 播放器栏 + 真机可播 + 断点续播 | **不进门禁**。它有一条**需要手跑**的"壳侧构建检查"（`dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q`），**触发条件 = `D-player.Core` 的公开面一变就跑**（增量后实测约 9 秒）。见 [`README.md`](../README.md)「构建与运行」与 PROJECT §4.3 第 28 条 |
| `D-player.slnf` | **门禁**筛选器（Core + WPF + Tests）：门禁一构建 + 门禁二测试，每次改动必跑。把 Windows App SDK 的重型工具链挡在每次构建/测试之外 | — |
| `tools/verify-gates.ps1` | 把上面两条门禁（`-Fast`）与"门禁 + 壳侧检查"（`-Full`）一次跑完，逐步 fail-fast | 手跑 |

一句话：**共享层已经拿到并且对 WPF 零行为影响；新壳拿到了"能跑起来、能出声"的最小证据，但远未达到功能等价。**

---

## 2. 功能等价核对表

图例（**每格只取一个值**，取值规则：看这一行**最需要你出力**的那部分——所以 🟡 与 ❓ 都算"要你参与"，区别只在取证强度）：
✅ 已具备且取证已闭合（不需要你再确认）｜🟡 已具备、有代码级/截图级取证，只差你顺眼复核｜❓ **没有任何取证能替你判定**，必须你在真机上用曲库/耳朵确认｜❌ 本阶段刻意不做｜⚠️ 已具备但有**已知缺陷**（不影响使用，代价写在备注里）。

| 功能项 | WPF | WinUI | 备注（如实记录验证状态） |
|---|---|---|---|
| **启动与窗口** | ✅ | 🟡 | 两壳都能起窗。**深色现在由壳强制**：`App` 构造函数里 `RequestedTheme = ApplicationTheme.Dark`（spec §4.4 那句，A1 补上的——之前根本没有这行，于是壳跟随系统主题，浅色 Windows 上两壳会像两个产品）。本波的取证强度要分清（原文"3 次重新起窗都取到证 + 采样 #202020/#272727"把两件事混成了一件）：**UIA 读数**（`UIA_NAME=D-player`、`WinUIDesktopWin32WindowClass`、22 节点树）来自 `final-fix-evidence-boot.txt` / `boot2` / `boot3` / `boot4` 四次起窗；**深色像素读数只有一份**——`final-fix-evidence-mica-ab.txt`，而且是**同一次运行的三个窗口位置**（`#202020` 标题栏/导航栏、`#272727` 内容区/播放器栏），`boot.txt` / `boot2` / `boot4` 的 `PIXELS` 行是空的、`boot3` 的五个采样点全是 `#000000`（归档里没写原因，不替它编），所以"整片深色"另由未遮挡截图 `final-fix-evidence-window-topmost.png` 一张撑着，不是三次独立的像素对照。**但"浅色 Windows 上仍然深色"这半边本机造不出条件**（这台机器 `AppsUseLightTheme=0`，本来就是深色）→ 那半边 **NOT VERIFIED**，需要你在浅色系统下看一眼。Mica 的两条前提照旧：① 根 `Grid` 必须 `Background="Transparent"`；② `DWMWA_SYSTEMBACKDROP_TYPE` **不是探针**（恒为 0）。另记一条本波的新读数：Mica 的 A/B/C 位置像素对照在四个采样点上**位置不变**，没能复现 Task 4 当年的变化签名（采样点可能全落在不透明层上，内容区 `#272727` 就是 `NavigationView` 的内容卡片），所以本表**沿用 Task 4 的 Mica 取证、本波未复现**（见 §5 关切 6）。已知缺口不变：WinUI 不设定窗口尺寸、不读写 `settings.json` 的窗口几何、没有 WPF 的"窗口位置自愈"。 |
| **歌单导航** | ✅ | ❓ | WPF：侧边栏增删/重命名/拖拽重排/文件夹绑定图标/▶ 活跃标记。WinUI：`NavigationView` 左栏列出歌单、点击切换查看项，本波重新起窗又在 UIA 里看到 `默认歌单`（`ControlType.ListItem`）与强调色选中条。**必须你确认的那一半**：多歌单时的恢复态——左栏高亮是否等于持久化的 `CurrentPlaylistId`、是否等于中间列表正在显示的那个。这条在单歌单启动上**不可观测**，而 WinUI 不进门禁、没有自动化能替它说话，只能你走（见 §2.1 的 ②）。 |
| **曲目列表** | ✅ | ❓ | WPF：▶ 当前曲标记 + `#`（TrackNumber）/标题/艺术家/专辑/时长列 + 表头 + × 删除按钮 + Delete 键。WinUI：`ListView` 只显示**标题 + 艺术家**两列，无 `#`/专辑/时长列、**无 ▶ 当前曲标记**、无删除入口；`ItemsSource` 走代码后置赋值（`{x:Bind ViewModel.ViewedPlaylist.Queue}` 会让 XamlCompiler 在 MarkupCompilePass1 抛 WMC9999）。**取证只到空状态**（切片无导入入口，而我按纪律不读写 `%LOCALAPPDATA%`）：有曲目之后列表怎么渲染、长标题怎么截断，只能你看（见 §2.1 的 ②）。 |
| **播放与暂停** | ✅ | ❓ | WPF：▶/⏸、上一首/下一首、随机、循环三态、双击跨歌单播放。WinUI：只有 ▶/⏸ 与双击播放；**上一首/下一首、随机、循环按钮本阶段没有**（见 §3）。双击**与 WPF 共用同一条 Core 入口** `PlaylistsViewModel.HandleDoubleClickPlay(target, index)`，因此"切当前歌单指针 + 清空已播历史（视为新会话）"的语义两壳一致（计划里曾打算新增 Core 侧 `PlayIndexAsync`，实施中被推翻并删除，理由见计划勘误）。**A3 已修掉一处真差异**：双击改为在可视化树里解析被点中的 `ListViewItem` 容器（`e.OriginalSource` → `FindAncestor<ListViewItem>` → `container.Content`），**空白区双击什么都不做**——切片版读 `SelectedItem`，会把上一次选中的那首再播一遍，现在与 WPF 同纪律。本波取证：▶ 按钮 Invoke 一次后进程存活、文本无变化（空队列下是正确的 no-op）。**双击是否真出声、进度是否推进 = 只能靠你的耳朵**（我没有任何真实曲目可播）。 |
| **进度显示** | ✅ | ❓ | WPF：≈30 Hz 位置刷新 + 时长 + 拖拽与单击跳转（**只在拖动结束时** Seek，`Views/Controls/PlayerBar.xaml.cs:70-74` 挂 `Thumb.DragStarted/DragCompleted`）。WinUI：**A2 之后与 WPF 同语义**——拖动期间 `IsSeeking` 为真、30 Hz 回写被 Core 抑制（`PlayerViewModel.cs:180-186`），**松手才提交一次 Seek**；`ValueChanged` 只承担"值变了才提交 Seek"这一半（单击轨道**是否真会改变 `Value`** 取决于 WinUI 的默认行为，本波没测 → 见下面 ②），并且会跳过"程序把值写回来"那一路径。**切片期记录的"每次 ValueChanged 都 Seek（拖动中连续定位）"这条差异已经不存在**（不是被接受，是被修掉了）。实现方式与 WPF 同源：WinUI 3 的 `Slider` **不暴露** `DragStarted/DragCompleted`（WASDK 2.5.1 的托管投影里**根本没有 `SliderBase` 这个类型**，本表旧版写"SliderBase 不暴露"是拿 WPF 的类层次去命名一个不存在的类型；这对事件只存在于模板内部的 `Thumb` 上。三条路实测都失败：XAML 属性 → WMC0011、附加属性 → WMC0010、C# 订阅 → CS1061），所以事件从 `Slider` 模板的 `Thumb`（`HorizontalThumb`）上取——正是 WPF 订阅的那同一对事件。进度值也不再由壳侧抄公式（A4：删掉 `PositionFraction`，直接绑 Core 的 `PlayerViewModel.PositionNormalized`，与 WPF 的 `PlayerBar.xaml` 同一个成员）。**仍真实存在的两点差别**：① 这条挂接依赖模板部件名，找不到时**静默退回**"连续 Seek"的旧行为，而 WinUI 不在门禁里 → 没有任何自动回归能发现它退化（本波实测：拖动滑块进程存活、窗口完好，但**没有曲目，落点是否停在手指处无法判定 → NOT VERIFIED**）；② WPF 的单击跳转要 `handledEventsToo` 才生效（Phase 13 起的历史坑）；WinUI 侧的机制**未确认**——过去这里写的"`Slider` 默认的 `IsMoveToPointEnabled`"是**假依据**：那是 **WPF** 的属性（本仓库在 `Themes/Controls.xaml:106`、`Views/Controls/PlayerBar.xaml:145` 显式设它），WinUI 3 的 `Slider` 上没有它（2026-10-07 在本壳里引用 `PositionSlider.IsMoveToPointEnabled` → **CS1061**；WASDK 2.5.1 的 Slider 属性面里也没有这一项，投影里更不存在 `SliderBase` 类型）。既然机制不成立，"结果同类"也就无从谈起，而且**本波根本没测过单击跳转**：切片没有曲目、`Duration = 0`，`Position_Changed` 在时长守卫处就返回了。所以这一格现在的结论是 **待用户确认**（有曲目后单击轨道中段，看播放位置是否跟着跳；见 §2.1），不再声称与 WPF 同类。**播放中进度是否推进 = ❓ 需要你的耳朵。** |
| **断点续播** | ✅ | ❓ | 逻辑在 Core（同一份），写入时机与"只就位不出声"的恢复语义完全相同。WinUI 侧本阶段补上了关闭落盘：`AppWindow.Closing` → `await MainViewModel.CleanupAsync()`（cancel-and-close，复刻 `Views/MainWindow.xaml.cs:82-112`），并刻意**不**同步 Dispose 它的 `ServiceProvider`。**必须"正在播放时关窗"**才走得到这条新 flush（在 30 秒节流点或暂停后关闭会绕过它）；随后重开按 ▶ 是否从断点继续 = ❓。已知缺口：恢复被跳过时 WinUI 只写 `Debug.WriteLine`（WinExe 无控制台，等于看不见），WPF 是弹 `ConfirmDialog`。数据落在 `%LocalAppData%\D-player-winui\`。 |
| **排序** | ✅ | ❌ | WPF：表头点击物理重排 `Queue`（`#` / 标题 / 艺术家 / 专辑 / 时长），同列再点切升降序，`CurrentIndex` 跟随当前播放曲。WinUI：**没有可点的表头，因此该能力在界面上不可达**——但底层 `SortBy` 是 Core 的同一个方法，已被单测覆盖（Phase 20 Task 1 补的两条 `SortBy` 事实）。补上表头属第二阶段。 |
| **曲目信息面板** | ✅ | ❌ | WPF：右侧独立面板 `Views/Controls/TrackInfoView.xaml:30-47`——**内嵌封面**（`AlbumArtBytes` → `BytesToBitmapImage` 转换器，ViewBox 缩放 + 圆角裁切）、**标题 / 艺术家 / 专辑**三行、**采样率读数**（`PlayerViewModel.SampleRateText`），面板下半部才是频谱（同文件 `:70` 的 `SpectrumView`）。WinUI：**整个面板没有对应物**——没有封面、没有专辑行、没有采样率，也不显示当前曲的完整元数据（底部栏只有 `标题 — 艺术家` 一行，`MainWindow.xaml` 的 `NowPlayingText`）。数据全在 Core（`Track.AlbumArtBytes`/`Album`、`PlayerViewModel.SampleRateText`），**缺的是壳侧 UI**，属第二阶段。 |
| **退出与收尾** | ⚠️ | 🟡 | **这是 WinUI 客观更好的一轴，而且是唯一一轴——但取到的证只覆盖"空会话"那一半。** WinUI：`CloseMainWindow()` 后 25 秒内退出、**ExitCode = 0**、无残留进程（本波归档的 `boot.txt` / `boot2` / `boot3` / `boot4` 四次都写了 `ExitCode=0` 与 `REMAINING=0`；`mica-ab` 那次只写了 `EXITED=True ExitCode=0`；收尾这一轮又起窗关窗一次，同样 `ExitCode=0`、`REMAINING_PROCESSES=0`）。**这几次全是"没有曲目、没有出声"的会话**，而新加的关闭落盘恰恰只在**正在播放时关窗**才走得到（正是 §2.1 的 ⑤ 要留给你的那半边）——所以"播放中关窗仍干净退出 + 断点已落盘"未验证，本格由 ✅（取证已闭合）改标 🟡（有证、但覆盖不到要你确认的那一半）。WPF：数据落盘正常，但 `App.OnExit`（`App.xaml.cs:61-67`）**同步** Dispose 容器，而容器里住着只实现 `IAsyncDisposable` 的 `MainViewModel` → 抛 `InvalidOperationException`、**进程以非 0 退出码收场**。这是 Phase 20 之前就存在的缺陷（台账 Task 1 "parked" 项），本阶段刻意**没有**把它复制进第二壳，也**没有**顺手修它——修它要单独决定（两壳的关闭路径一起看才对，见 §5 的收尾任务清单）。 |

### 2.1 需要你确认的项（标 ❓ 的格子，以及 🟡 里点名的那几处）

**先做这一步（手工测试数据，不是产品功能，代码里没有任何东西替你做它；也必须由你自己做——实施方按纪律不读写 `%LOCALAPPDATA%`）**：
在资源管理器里把 `%LocalAppData%\D-player\queue.json` 复制一份到 `%LocalAppData%\D-player-winui\`（目录没有就新建），并让里面的曲目路径指向你机器上真实存在的音频文件，**并且保留/构造至少两个歌单、且 `CurrentPlaylistId` 指向的不是第一个**。切片期没有导入入口，不这么做就只能看到空状态。
**同一目录里还要有 `library-cache.json`**：只拷 `queue.json` 的话，文件夹歌单的元数据缓存是冷的，WinUI 首启会对每个文件重读一遍元数据——于是"操作手感"这一维会被拿去跟一次 WPF 从来不用付的冷缓存启动比较。把这份缓存一起拷过去（或先让 WinUI 空跑一次把缓存焐热再计时）才叫公平对照。

```
② 左侧歌单与曲目列表正确显示         ← ❓（需要多歌单数据，上面那一步是它的前置）
③ 双击曲目出声、进度条推进           ← ❓ NOT VERIFIED（需你的耳朵；我没有任何真实曲目可播）
④ 暂停/继续手感                      ← ❓ NOT VERIFIED（需手感）
⑤ 关闭后重开按 ▶ 从断点续播          ← ❓ NOT VERIFIED（务必在"正在播放"时关窗，才 exercise 新加的关闭落盘）
＋ 浅色 Windows 下 WinUI 是否仍深色   ← ❓ NOT VERIFIED（A1 的可见差异本机造不出条件，见 §2 第一行）
```

已经在真机上取证到的部分（不是推测）：窗口能起、`UIA_TITLE=D-player`、左栏列出 seed 歌单 `默认歌单`、空状态文案渲染、播放器栏显示 `未在播放` / `00:00 / 00:00`、▶ 按钮可 Invoke 且空队列下正确 no-op、拖动滑块进程存活、关窗退出码 0 且无残留进程（本波归档的 `boot`/`boot2`/`boot3`/`boot4` 四次都写了 `ExitCode=0` + `REMAINING=0`，`mica-ab` 那次只写了退出码；收尾这一轮又复现一次）。**这些全是空队列、没出声的会话**——有曲目之后是否还这样，见 §2"退出与收尾"行与下面的 ⑤。**未取证的**：Windows 应用程序日志/WER 的读取被本机安全策略拒绝，所以"没有崩溃记录"这一条没有取证；运行期异常在 WinUI 侧没有可见出口（见 §5 关切 3）。

三条顺手请感受一下（前两条在 A2/A3 之后从"已知差异"变成了"待你复核的修复"，第三条是本轮收尾新让出来的）：
- 拖动进度条：松手后是否**停在手指放下的位置**（修复前会边拖边跳）；如果它退回"连续跳"，说明模板 `Thumb` 没挂上——这条**没有任何自动回归能发现**，只能靠你的手；本波的取证只到"空队列下拖动不崩"，**没有证据能说明部件被找到并挂上了**；
- 在列表下方的**空白区**双击：应当什么都不播放（修复前会把上一次选中的那首再播一遍）。
- **单击**进度条轨道中段：播放位置是否跟着跳。WinUI 侧这条**机制未确认**（`IsMoveToPointEnabled` 是 WPF 的属性，WinUI 3 的 `Slider` 上没有它 → 引用即 CS1061），而且本波在 `Duration = 0` 下从未测过，所以本表不再声称它"与 WPF 结果同类"，**待你确认**。

---

## 3. 本阶段 WinUI 侧刻意没有的能力

这不是遗漏，是设计稿的 Non-Goals（Phase 20 只做第一条纵向切片 + 决策门）。**评估"视觉观感/操作手感"时请把这一节当作已知前提**，但评估"维护与演进成本/生态"时可以把它当作工作量证据。

| 能力 | WPF 现状 | WinUI 现状 | 接过去需要动什么 |
|---|---|---|---|
| **曲目信息面板**（内嵌封面 / 专辑 / 采样率） | ✅ `Views/Controls/TrackInfoView.xaml:30-47`：封面（`AlbumArtBytes` → `BytesToBitmapImage` 转换器，ViewBox 缩放 + 圆角裁切）+ 标题 / 艺术家 / 专辑三行 + **采样率读数**（`PlayerViewModel.SampleRateText`） | ❌ **整个面板没有对应物**：无封面、无专辑行、无采样率；壳侧关于当前曲的全部信息就是底部栏那一行 `标题 — 艺术家`（`MainWindow.xaml` 的 `NowPlayingText`） | 数据全在 Core（`Track.AlbumArtBytes` / `Track.Album` / `PlayerViewModel.SampleRateText`），**缺的只是壳侧 UI**；但 WPF 的 `BytesToBitmapImageConverter` 属 WPF 壳（`System.Windows.Media.Imaging`），WinUI 要自己写一份到 `SoftwareBitmapSource` 的转换。**注意从属关系：频谱就嵌在这个面板的下半部**（同文件 `:70` 的 `SpectrumView`），所以下一行其实是本行的子部件——过去只列"缺频谱"，把整块面板、封面与采样率读数都漏在了表外 |
| 频谱可视化（上面板的下半部） | ✅ 32 柱 FFT（Phase 13） | ❌ | `SpectrumView` 是 WPF `Control` + `CompositionTarget.Rendering`，需按 WinUI 重写一套绘制控件；数据源 `PlayerViewModel.SpectrumData` 已在 Core，无需改共享层 |
| 拖拽（外部入队 + 队列内重排） | ✅（Phase 5） | ❌ | WinUI 的 DragDrop API 与 WPF 不同（无 `AdornerLayer`），插入线要换别的高亮机制；VM 侧命令 `DropExternalFiles` / `MoveTracks` 已在 Core，可直接复用 |
| 文件/文件夹对话框 | ✅ `Win32FileDialogService` | ❌ **切片期是空实现**（三个方法各返回"用户取消"） | 已知真障碍：`IFileDialogService` 是**同步**接口，WinUI picker 只有异步 API，用同步接口驱动异步 picker 会在 UI 线程死锁。要么把 Core 接口改异步（跨壳改动，需单独决策），要么壳侧走异步 + 完成后回填 |
| 均衡器（10 段 EQ + 预设 + 对话框） | ✅（Phase 14） | ❌ | 数据与实时下发都在 Core（`IPlaybackService.EqualizerConfig`），缺的只是 WinUI 侧的对话框与竖直滑块模板 |
| 设置对话框 | ✅（Phase 11/13） | ❌（`NavigationView` 的 `IsSettingsVisible=False`） | 同上：读写字段都在 Core 的 `AppSettings`/`ISettingsPersistence`，缺 UI |
| 播放列表文件导入/导出 | ✅ M3U/M3U8/PLS 导入 + M3U8 导出（Phase 18） | ❌ | 服务与两个 VM 方法都在 Core；文案格式化器也在 Core（`PlaylistImportReportFormatter`，Phase 20 从 View 层搬过去），缺对话框入口与报告展示 |
| 音量与静音 | ✅ 滑块 + 一键静音 | ❌ | `PlayerViewModel.Volume` 已在 Core，纯 UI 活 |
| 上一首/下一首、随机、循环三态 | ✅ | ❌ | 命令已在 Core（`NextTrack` / `PrevTrack` 在 `PlaylistViewModel`，`ToggleShuffle` / `CycleRepeat` 在 `PlaylistsViewModel`；WPF 侧的绑定路径就是这两个命令），纯 UI 活 |
| 歌单增删/重命名/拖拽重排、文件夹绑定歌单、列表区 ▶ 标记 | ✅ | ❌ 只读导航 | 容器级方法都在 Core，缺 UI 与侧栏交互 |
| 窗口几何持久化 + 可见性自愈 | ✅ | ❌ | 纯壳侧工作 |

**判据提示**：上面这些"缺 UI 不缺逻辑"的行，是 Phase 20 抽出 `D-player.Core` 的直接收益——共享层已经可复用，缺的部分全在壳的表现层。反过来，**对话框（同步 vs 异步）**与**拖拽（Adorner 机制）**是两处真需要动跨壳契约或换实现方式的地方，估工作量时别按"顺手接一下"算。

---

## 4. 六维评分表（请填分数）

打分口径：**1 = 明显不如另一侧 / 5 = 明显优于另一侧**，同分写 3/3 也行。备注栏里预先放的是**已核实的事实**，不是结论；你可以直接划掉改写。

| 维度 | WPF 分 | WinUI 分 | 备注（已知事实，供打分参考） |
|---|---|---|---|
| 视觉观感 |  |  | WPF：19 个阶段打磨出的深色自绘主题（`Themes/Controls.xaml` 全量隐式模板 + `Icons.xaml` 矢量图标集 + WindowChrome 自绘标题栏）。WinUI：原生 Fluent 深色 + Mica 半透明材质 + `NavigationView`，一眼是"新一点的 Windows 应用"；Mica 这一格要说清强度：**是否仍生效，本波没能复现**——§5 关切 6 记了本波的 A/B/C 三位置复探，四个采样点全部位置不变，没复现出 Task 4 当年的变化签名，所以这里沿用的是 **Task 4 的归档取证 + "本波未复现"** 这个标注，别拿它加分也别拿它减分；已确认的只有"材质依赖根背景 `Transparent` 这一约定"（不 Transparent 就被页面底色盖住，见 `MainWindow.xaml` 顶部注释）。要判定它，按 Task 4 的**三行对照协议**重做（材质+不透明底 / 材质+Transparent / 无材质+Transparent），本机给不出全部条件，列为"续投 WinUI 的第一件工作"。**切片只有三个区域，控件覆盖度远低**（无对话框、无滑块主题、无列表列）。请就你肉眼看到的窗口做判断。 |
| 操作手感 |  |  | 需要你的真机操作才能填。未验证项：③ 双击出声与进度推进、④ 暂停/继续、⑤ 断点续播、多歌单恢复态、**松手后进度条是否停在手指处**（A2 修的就是这件事，但没有曲目我无从判定落点）。可对照的事实：两壳共用同一个 `PlaylistsViewModel.HandleDoubleClickPlay` 双击入口，且 A3 之后空白区双击两壳都不动作；**切片期"拖动会连续跳"与"空白区双击会重播上次选中"这两条已知差异已被修掉**，不再是扣分项；随机/循环/上一首/下一首在 WinUI 侧根本没有按钮。 |
| 性能 |  |  | 尚未做两侧对比测量（没有启动耗时/内存/帧率的量化数据，别按印象打分，可注明"未测"）。**已知的一侧成本是包体与恢复时间**：WinUI 走 unpackaged + self-contained，`Microsoft.WindowsAppSDK` 2.5.1 会拉 9 个子包、首次 restore 实测约 8.1 分钟、自包含输出目录明显大于 WPF 侧；WPF 壳只依赖 NAudio 两子包 + ATL，`dotnet restore D-player.sln`（全量、可选）与两条门禁（走 `D-player.slnf`）的差别就在这里。另记一条与本维度无关但客观存在的差别：**退出码**（见 §2 最后一行——WPF 因既有 `App.OnExit` 缺陷非 0，WinUI 为 0）。 |
| 开发体验 |  |  | **门禁 = 两条**（`D-player.slnf` 构建 0 警告 + 测试 **180 通过**），**这 180 条只覆盖 Core + WPF + Tests，WinUI 壳一条都不覆盖**；WinUI 侧唯一的自动化级保护是一条**需要手跑**的"壳侧构建检查"（0 警告），**触发条件 = `D-player.Core` 的公开面一变就跑**，一键跑法 `powershell -File tools/verify-gates.ps1 -Fast|-Full`（增量后实测约 9 秒）。这条触发条件不是形式主义：本波就有一次"死 using 整理"报错了对象，而 `.slnf` 门禁对 WinUI 完全无感（详见 PROJECT §4.3 第 28 条）。WPF：一条 `dotnet run --project D-player.csproj` 就能跑；XAML 编译器报错信息成熟。WinUI：`dotnet build D-player.WinUI/D-player.WinUI.csproj` / `dotnet run --project …` 裸命令可用（靠 csproj 里 `<Platforms>x64</Platforms>` + `<Platform>x64</Platform>`，`Platforms` 只声明支持面、不设默认值，裸构建会报 "requires a supported Windows architecture"）。**WinUI 刻意不进门禁**，所以每次改动它都要单独构建；XamlCompiler 崩过 WMC9999（`x:Bind` 经可空中间段），这类错误没有可读信息、只能靠逐块剥离法定位；`Slider` 缺 `DragStarted/DragCompleted`（WMC0011 / WMC0010 / CS1061 三条路实测都堵；投影里也没有 WPF 那个 `SliderBase` 类型可查）也是同一类"文档没说、只能试"的摩擦。 |
| 维护与演进成本 |  |  | 已经付掉的成本：Core 抽取 + 数据目录参数化对 WPF 零行为影响，两壳从此共享一份 VM/Service。WinUI 侧新增的长期约定：每壳各自注册 `IFileDialogService`（`Tests/Extensions/AddDPlayerCoreTests.cs` 钉住了 Core 图，但**钉不住壳忘注册自己的那一个**）；每壳各自的数据目录（`D-player` vs `D-player-winui`，UmaPlayer 更名迁移仍只属 WPF）；每壳各自的关闭落盘（await `CleanupAsync`，不得同步 Dispose 容器）。未解风险：WinUI 不进门禁 → XAML 编译、左栏逻辑、关闭落盘**没有自动回归**，而手跑的壳侧检查只在有人记得跑时才跑；**A2 的拖动语义挂在模板部件名 `HorizontalThumb` 上，部件找不到时是静默退回"连续 Seek"的旧行为**——没有任何自动回归能发现这种退化；`IFileDialogService` 同步/异步死锁；Mica 依赖 Transparent 约定；已知既有缺陷（WPF `App.OnExit` 同步 Dispose 容器，表现为退出码非 0）刻意没被复制到 WinUI，也刻意没在本阶段修（见 §5 的收尾任务清单）。 |
| 生态与可扩展性 |  |  | WPF：.NET 内置、无额外 SDK 依赖，但控件生态停止演进，本项目已自建全套深色模板与矢量图标。WinUI：官方 Fluent/Win32 控件、Mica、`NavigationView` 等开箱即用，未来可 MSIX 打包与自更新；代价是 Windows App SDK 版本与运行时耦合（本项目钉在 2.5.1 + TFM `net10.0-windows10.0.19041.0`），且 unpackaged/self-contained 形态带来体积与 restore 时间成本（见"性能"行）。请结合你后续想做的功能（频谱/拖拽/EQ/打包分发）判断哪一侧扩建更便宜。 |

---

## 5. 决策门记录位（用户填写后由助手写入）

```
结论：（续投 WinUI / 停止 / 其它）
理由摘要：
对 §2.1 四项未验证项的实测结果：
后续阶段范围（若续投）：
```

**若结论是"停止"**：Phase 20 的 Task 1–2（`D-player.Core` 抽取 + 数据目录参数化）**建议保留**——它们对 WPF 版零行为影响，且让未来任何新壳都更省事；`D-player.WinUI` 工程的去留由你另行决定。

**已知关切（决策时值得计入）**

1. 切片无法自助走通：没有导入入口 + 首启零曲目，不手工放 `queue.json` 就只能验收 §2.1 之外的几项。
2. **WinUI 不进门禁**（设计决策，为了让两条门禁不被 Windows App SDK 工具链拖慢），所以新壳的正确性目前主要靠真机手测 + **一条需要人记得跑的壳侧构建检查**。本波为此把触发条件写死（Core 公开面一变就跑）并给了 `tools/verify-gates.ps1 -Full` 一条命令——但"有规则"和"会执行"之间仍然隔着一只手。
3. 运行期异常在 WinUI 侧没有可见出口（WinExe 无控制台，`Debug.WriteLine` 不进任何流），第二阶段建议先给壳加一条最小崩溃/日志出口。
4. `IFileDialogService` 的同步契约 vs WinUI 异步 picker 是第二阶段的第一个真决策点（改跨壳接口需要单独拍板）。
5. A2 的拖动语义依赖 `Slider` 模板部件 `HorizontalThumb`，**部件找不到时静默退回旧行为**；这类退化没有自动回归能发现（§4"维护与演进成本"）。
6. **Mica 的本波复探没能复现 Task 4 的位置变化签名**：三个窗口位置下四个采样点全部位置不变（`#202020/#202020/#272727/#272727`）。这**不足以判定材质消失**——采样点很可能都落在不透明层上（内容区 `#272727` 就是 `NavigationView` 的内容卡片层），而 Task 4 当年是挑着"能看见窗外内容"的点采的；但它意味着本表里的 Mica 一行**沿用旧取证、本波未复现**。续投的话，第一件事就该按 Task 4 的三行对照协议（材质+不透明底 / 材质+Transparent / 无材质+Transparent）重做一次，而不是继续引用旧表。
7. 本阶段遗留的其它小项已记在实现台账，见下面那份指名道姓的清单。

**收尾任务清单（本波判定为"记录、不修"，逐项指名）**

*需要单独拍板的（跨壳 API / 两壳对称性）*
- **WPF `App.OnExit` 的同步 Dispose 缺陷**（`App.xaml.cs:61-67`：容器里住着只实现 `IAsyncDisposable` 的 `MainViewModel` → 抛异常 → 退出码非 0）。**这是 §2"退出与收尾"一行里 WPF 那侧的 ⚠️**，Phase 20 之前就存在、刻意没复制进第二壳、也刻意没在本波修：数据已落盘，属"难看但真"的缺陷，修它要连着看两壳的关闭路径。
- **`DPlayerDataPaths` 的空 `FolderName` 逃生门与构造形态**（整支分支评审的 Important 6，以及它吸收掉的若干台账 minor）。这是 Core 的 API 重设计，会同时波及两个壳，放在一个以"WPF 零变化"为前提的阶段末尾做，风险不对称 → 独立任务。
- **第二次关闭请求可能截断 flush**（`MainWindow.xaml.cs` 的 `if (_isClosing) return;` 未 cancel 第二次请求）——**与 WPF 的 `Views/MainWindow.xaml.cs:85` 同形**，所以只修一个壳会制造新的不对称；要修就两壳一起（`_cleanupDone` 标志）。

*第二阶段结构性工作（不影响本次去留判断）*
- 左栏每次点击整栏重建（pane rebuild-per-click）；`MainWindow.xaml.cs` 单文件承载过多职责 → 抽 `PlayerBar`；左栏改声明式（数据驱动）；`NavigationViewItem.Content` 的重命名快照陈旧问题；VM 订阅无解绑路径；`Debug.WriteLine` 作为"恢复被跳过"的唯一信号（WinExe 里等于没有）；`Directory.Build.props` 统一包版本与 glob 排除集（现在是三处副本）。
