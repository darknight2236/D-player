# Phase 20 对比材料：WPF 壳 vs WinUI 壳

> 文档日期：2026-10-07 · 代码基线：`71613f2`（Phase 20 Task 4 收口） · 对应分支：`master`
> 关联设计稿：[`specs/2026-10-06-d-player-phase20-winui-shell-design.md`](./superpowers/specs/2026-10-06-d-player-phase20-winui-shell-design.md)
> 关联实现计划：[`plans/2026-10-06-d-player-phase20-winui-shell-implementation.md`](./superpowers/plans/2026-10-06-d-player-phase20-winui-shell-implementation.md)

**这份材料怎么用（请先读这一段）**

Phase 20 交付了一条 WinUI 3 的第二 UI 壳（第一条纵向切片），它**能不能替代 WPF 壳不由代码判定，由你的手感判定**。下面两张表：

1. **功能等价核对表**（第 2 节）——客观项，已经按"实测/未实测"如实填好，你只需要用真机操作把标了 ❓ 的格子确认掉。
2. **六维评分表**（第 4 节）——主观项，**分数栏是空的，留给你填**。每个维度 1–5 分（5 = 非常满意），备注栏已预先放了已知事实，供你打分时参考，也可以改写。

**填完请把结论（续投 WinUI / 停止并保留 Core 抽取 / 其它）告诉我，我会把结论记进本文档末尾、`docs/PROJECT.md` 的阶段状态，并同步进项目记忆。** 第 5 节是决策门的记录位。

**本文档刻意不含结论。** 决策门尚未发生，任何"已经选定 WinUI"的表述都是错的。

---

## 1. Phase 20 到底交付了什么

| 交付物 | 内容 | 门禁位置 |
|---|---|---|
| `D-player.Core` | WPF-free 的共享类库（Models / Services / ViewModels / Configuration / Extensions），`net10.0-windows`，不开 `UseWPF`；两个壳都引用它 | 在 `D-player.slnf` 内 |
| `D-player`（WPF 壳） | 既有应用。**行为与外观按设计零变化**；只多了"数据目录由壳注入"与 `SortedView` 的删除（详见 §3） | 在 `D-player.slnf` 内 |
| `Tests` | 180 条单测，引用目标从 WPF 壳改为 Core | 在 `D-player.slnf` 内 |
| `D-player.WinUI` | WinUI 3 壳（unpackaged + self-contained，x64），第一条纵向切片：歌单导航 + 曲目列表 + 播放器栏 + 真机可播 + 断点续播 | **刻意不在任何门禁里**，只单独构建 |
| `D-player.slnf` | 主门禁筛选器（Core + WPF + Tests）。把 Windows App SDK 的重型工具链挡在每次构建/测试之外 | — |

一句话：**共享层已经拿到并且对 WPF 零行为影响；新壳拿到了"能跑起来、能出声"的最小证据，但远未达到功能等价。**

---

## 2. 功能等价核对表

图例：✅ 已具备并已被验证（自动化或代码级）｜🟡 已具备但**未真机验证**｜❌ 本阶段刻意不做｜❓ 需要你在真机上确认

| 功能项 | WPF | WinUI | 备注（如实记录验证状态） |
|---|---|---|---|
| **启动与窗口** | ✅ | 🟡 | 两壳都能起窗。WinUI 侧已自动化取证：深色 Fluent 外观、自绘标题栏区（`ExtendsContentIntoTitleBar` + 系统 Minimize/Maximize/Close 三键）、`UIA_TITLE=D-player`、Mica 材质（判据见下）。**Mica 有两条前提**：① 根 `Grid` 必须 `Background="Transparent"`，否则整扇材质被页面底色盖住；② `DWMWA_SYSTEMBACKDROP_TYPE` 对组合器挂载的 backdrop **不是有效探针**（实测恒为 0），任何用它做的自动化断言都会得出错结论。已知缺口：WinUI 既不设定窗口尺寸，也不读写 `settings.json` 里的窗口几何，更没有 WPF 的"窗口位置自愈"（外接屏拔掉后回退主屏居中）——真机取证时看到的位置与尺寸是取证脚本挪动过的，不代表产品行为。 |
| **歌单导航** | ✅ | 🟡 | WPF：侧边栏增删/重命名/拖拽重排/文件夹绑定图标/▶ 活跃标记。WinUI：`NavigationView` 左栏列出歌单、点击切换查看项，真机已看到 seed 歌单"默认歌单"与强调色选中条。**未验证**：多歌单时的恢复态（左栏高亮是否等于持久化的 `CurrentPlaylistId`、是否等于中间列表正在显示的那个）——这条在单歌单启动上不可观测，WinUI 又不进门禁，只能由你走（见 §2.1 的 ②）。 |
| **曲目列表** | ✅ | 🟡 | WPF：▶ 当前曲标记 + `#`（TrackNumber）/标题/艺术家/专辑/时长列 + 表头 + × 删除按钮 + Delete 键。WinUI：`ListView` 只显示**标题 + 艺术家**两列，无 `#`/专辑/时长列、**无 ▶ 当前曲标记**、无删除入口；`ItemsSource` 走代码后置赋值（brief 的 `{x:Bind ViewModel.ViewedPlaylist.Queue}` 会让 XamlCompiler 在 MarkupCompilePass1 抛 WMC9999）。真机只取证到**空状态**（切片无导入入口），有曲目后的列表渲染待你肉眼确认（见 ②）。 |
| **播放与暂停** | ✅ | ❓ | WPF：▶/⏸、上一首/下一首、随机、循环三态、双击跨歌单播放。WinUI：只有 ▶/⏸ 与双击播放；**上一首/下一首、随机、循环按钮本阶段没有**（见 §3）。双击**与 WPF 共用同一条 Core 入口** `PlaylistsViewModel.HandleDoubleClickPlay(target, index)`，因此"切当前歌单指针 + 清空已播历史（视为新会话）"的语义两壳一致（计划里曾打算新增 Core 侧 `PlayIndexAsync`，实施中被推翻并删除，理由见计划勘误）。**双击是否真出声、进度是否推进 = ❓ 需要你的耳朵**。 |
| **进度显示** | ✅ | 🟡/❓ | WPF：≈30 Hz 位置刷新 + 时长 + 拖拽与单击跳转（**只在拖动结束时** Seek）。WinUI：位置/时长文本与进度滑块已渲染（真机取证到 `未在播放` 与 `00:00 / 00:00`），但播放中的推进 = ❓。已知差异：切片期滑块**每次 `ValueChanged` 都 Seek**，拖动过程中可能连续定位——第二阶段才对齐 WPF 的"只在拖完定位"。 |
| **断点续播** | ✅ | ❓ | 逻辑在 Core（同一份），写入时机与"只就位不出声"的恢复语义完全相同。WinUI 侧本阶段补上了关闭落盘：`AppWindow.Closing` → `await MainViewModel.CleanupAsync()`（cancel-and-close，复刻 `Views/MainWindow.xaml.cs:82-112`），并刻意**不**同步 Dispose 它的 `ServiceProvider`。**必须"正在播放时关窗"**才走得到这条新 flush（在 30 秒节流点或暂停后关闭会绕过它）；随后重开按 ▶ 是否从断点继续 = ❓。已知缺口：恢复被跳过时 WinUI 只写 `Debug.WriteLine`（WinExe 无控制台，等于看不见），WPF 是弹 `ConfirmDialog`。数据落在 `%LocalAppData%\D-player-winui\`。 |
| **排序** | ✅ | ❌ | WPF：表头点击物理重排 `Queue`（`#` / 标题 / 艺术家 / 专辑 / 时长），同列再点切升降序，`CurrentIndex` 跟随当前播放曲。WinUI：**没有可点的表头，因此该能力在界面上不可达**——但底层 `SortBy` 是 Core 的同一个方法，已被单测覆盖（Phase 20 Task 1 补的两条 `SortBy` 事实）。补上表头属第二阶段。 |

### 2.1 需要你确认的四项（标 ❓ 的格子）

**先做这一步（手工测试数据，不是产品功能，代码里没有任何东西替你做它；也必须由你自己做——实施方按纪律不读写 `%LOCALAPPDATA%`）**：
在资源管理器里把 `%LocalAppData%\D-player\queue.json` 复制一份到 `%LocalAppData%\D-player-winui\`（目录没有就新建），并让里面的曲目路径指向你机器上真实存在的音频文件，**并且保留/构造至少两个歌单、且 `CurrentPlaylistId` 指向的不是第一个**。切片期没有导入入口，不这么做就只能看到空状态。

```
② 左侧歌单与曲目列表正确显示         ← 需要多歌单数据（上面那一步是它的前置）
③ 双击曲目出声、进度条推进           ← NOT VERIFIED（需你的耳朵）
④ 暂停/继续手感                      ← NOT VERIFIED（需手感）
⑤ 关闭后重开按 ▶ 从断点续播          ← NOT VERIFIED（务必在"正在播放"时关窗，才 exercise 新加的关闭落盘）
```

已经在真机上取证到的部分（不是推测）：窗口能起、深色 Fluent chrome + Mica、`UIA_TITLE=D-player`、左栏列出 seed 歌单、空状态文案渲染、播放器栏显示 `未在播放` / `00:00 / 00:00`、关窗退出码 0 且无残留进程。**未取证的**：Windows 应用程序日志/WER 的读取被本机安全策略拒绝，所以"没有崩溃记录"这一条没有取证；运行期异常在 WinUI 侧没有可见出口（见 §5 关切）。

两条顺手请感受一下：
- 拖动进度条是否连续跳（切片期每次 ValueChanged 都 Seek）；
- 随机模式下双击切歌的推进手感是否与 WPF 一致（两壳现在走同一条入口，理论上应当一致）。

---

## 3. 本阶段 WinUI 侧刻意没有的能力

这不是遗漏，是设计稿的 Non-Goals（Phase 20 只做第一条纵向切片 + 决策门）。**评估"视觉观感/操作手感"时请把这一节当作已知前提**，但评估"维护与演进成本/生态"时可以把它当作工作量证据。

| 能力 | WPF 现状 | WinUI 现状 | 接过去需要动什么 |
|---|---|---|---|
| 频谱可视化 | ✅ 32 柱 FFT（Phase 13） | ❌ | `SpectrumView` 是 WPF `Control` + `CompositionTarget.Rendering`，需按 WinUI 重写一套绘制控件；数据源 `PlayerViewModel.SpectrumData` 已在 Core，无需改共享层 |
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
| 视觉观感 |  |  | WPF：19 个阶段打磨出的深色自绘主题（`Themes/Controls.xaml` 全量隐式模板 + `Icons.xaml` 矢量图标集 + WindowChrome 自绘标题栏）。WinUI：原生 Fluent 深色 + Mica 半透明材质 + `NavigationView`，一眼是"新一点的 Windows 应用"；Mica 生效但依赖根背景 Transparent 这一约定。**切片只有三个区域，控件覆盖度远低**（无对话框、无滑块主题、无列表列）。请就你肉眼看到的窗口做判断。 |
| 操作手感 |  |  | 需要你的真机操作才能填。未验证项：③ 双击出声与进度推进、④ 暂停/继续、⑤ 断点续播、多歌单恢复态、拖动进度条是否连续跳（切片已知差异）。可对照的事实：两壳共用同一个 `PlaylistsViewModel.HandleDoubleClickPlay` 双击入口，随机/循环/上一首/下一首在 WinUI 侧根本没有按钮。 |
| 性能 |  |  | 尚未做两侧对比测量（没有启动耗时/内存/帧率的量化数据，别按印象打分，可注明"未测"）。**已知的一侧成本是包体与恢复时间**：WinUI 走 unpackaged + self-contained，`Microsoft.WindowsAppSDK` 2.5.1 会拉 9 个子包、首次 restore 实测约 8.1 分钟、自包含输出目录明显大于 WPF 侧；WPF 壳只依赖 NAudio 两子包 + ATL，`dotnet restore D-player.sln` 全量 restore 与门禁三件套（走 `D-player.slnf`）的差别就在这里。 |
| 开发体验 |  |  | 主门禁两侧都是 0 警告 / 180 测试。WPF：一条 `dotnet run --project D-player.csproj` 就能跑；XAML 编译器报错信息成熟。WinUI：`dotnet build D-player.WinUI/D-player.WinUI.csproj` / `dotnet run --project …` 裸命令可用（靠 csproj 里 `<Platforms>x64</Platforms>` + `<Platform>x64</Platform>`，`Platforms` 只声明支持面、不设默认值，裸构建会报 "requires a supported Windows architecture"）。**WinUI 刻意不进 `D-player.slnf`**，所以每次改动它都要单独构建；XamlCompiler 崩过 WMC9999（`x:Bind` 经可空中间段），这类错误没有可读信息、只能靠逐块剥离法定位。 |
| 维护与演进成本 |  |  | 已经付掉的成本：Core 抽取 + 数据目录参数化对 WPF 零行为影响，两壳从此共享一份 VM/Service。WinUI 侧新增的长期约定：每壳各自注册 `IFileDialogService`（`Tests/Extensions/AddDPlayerCoreTests.cs` 钉住了 Core 图，但**钉不住壳忘注册自己的那一个**）；每壳各自的数据目录（`D-player` vs `D-player-winui`，UmaPlayer 更名迁移仍只属 WPF）；每壳各自的关闭落盘（await `CleanupAsync`，不得同步 Dispose 容器）。未解风险：WinUI 不在门禁里 → XAML 编译、左栏逻辑、关闭落盘都**没有自动回归**；`IFileDialogService` 同步/异步死锁；Mica 依赖 Transparent 约定；已知既有缺陷（WPF `App.OnExit` 同步 Dispose 容器）刻意没被复制到 WinUI。 |
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

1. 切片无法自助走通：没有导入入口 + 首启零曲目，不手工放 `queue.json` 就只能验收 §2.1 之外的三项。
2. WinUI 不在任何门禁里，这是设计决策（为了让主门禁不被 Windows App SDK 工具链拖慢），但它意味着新壳的正确性目前主要靠真机手测。
3. 运行期异常在 WinUI 侧没有可见出口（WinExe 无控制台，`Debug.WriteLine` 不进任何流），第二阶段建议先给壳加一条最小崩溃/日志出口。
4. `IFileDialogService` 的同步契约 vs WinUI 异步 picker 是第二阶段的第一个真决策点（改跨壳接口需要单独拍板）。
5. 本阶段遗留的其它小项（左栏整栏重建、`MainWindow.xaml.cs` 单文件承载过多职责、空状态里那句开发者口吻的提示文案、订阅无解绑路径、第二次关闭请求可能截断 flush）已记在实现台账，属第二阶段结构性工作。
