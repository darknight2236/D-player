# Phase 20：WinUI 3 第二套 UI（切片验证 + 对比决策门）设计

> 设计日期：2026-10-06 · 对应分支：`master` · 状态：**待实现**
>
> 目标：用 **WinUI 3（Windows App SDK）** 重做一套界面，与现有 WPF 界面**并存**，最终由用户对比后决定保留哪一套。本阶段**不追求功能等价**：先把工程切分与工具链跑通，做出**第一条可对比的纵向切片**，再设决策门——续投与否由用户在真机对比后拍板。

---

## 0. 背景与动机

现有应用是单工程（`D-player.csproj`，`net10.0-windows` + `UseWPF`），Models/Services/ViewModels/Configuration/Views 全在一个 exe 里。要在其上验证 WinUI 3，必须先解决"两套 UI 如何共享同一份业务代码"的问题。

评估期勘察结论（§3 有证据表）：

- **共享层几乎已经是 WPF-free**：VM/Service/Model 只在 **1 处** 使用 WPF 类型（`PlaylistViewModel.SortedView` 的 `ICollectionView`），另有 1 个服务（`Win32FileDialogService`）用 WPF 的文件对话框。Phase 7 已把 `BitmapImage` 从 VM 清出，Phase 19 前后也未新增耦合。
- **UI 面较大**：`MainWindow` + 6 个控件（标题栏/侧边栏/歌单列表/播放器栏/频谱/曲目信息）+ 4 个对话框，共 16 个 XAML，且含 WPF 专有机制（`WindowChrome` 自绘标题栏、`DropInsertionAdorner` 拖拽指示、`CompositionTarget.Rendering` 频谱、`Icons.xaml` Geometry 图标、`IsMoveToPointEnabled` 单击定位）。
- **工具链不确定**：本机 NuGet 缓存只有 `Microsoft.WindowsAppSDK` 1.6 / 1.7，而 .NET 10 SDK（10.0.201 / 10.0.401 已装）配 WASDK 需要更新的版本 —— 必须先验证，不能纸面假设。

## 1. 目标与范围

### Goals

- **G1 工程切分**：抽出 `D-player.Core`（类库，**无 WPF**），WPF 壳与 WinUI 壳各自引用；WPF 版行为**零变化**、172 测试保持全绿。
- **G2 WinUI 工程骨架**：`D-player.WinUI` 能构建、能跑（unpackaged），并**实测确认** WASDK × net10 的组合可用。
- **G3 第一条纵向切片**：在**新视觉语言**下做出"主窗口 + 左侧歌单导航 + 曲目列表 + 播放器栏 + 真机播放/暂停/断点续播"这一条贯通路径。
- **G4 数据隔离**：WinUI 版读写独立数据目录 `%LocalAppData%\D-player-winui\`，与 WPF 版互不影响。
- **G5 决策材料**：产出"功能等价核对表 + 主观评分表"，用户真机对比后拍板续投/停止。

### Non-Goals（本阶段明确不做）

- **不做功能等价**：频谱可视化、拖拽（含跨列表拖拽与插入指示）、EQ 对话框、设置对话框、导入/导出播放列表、播放列表文件拖放、自定义标题栏的全部细节打磨——一律留到**决策门之后的第二阶段**（若续投，另出 spec/plan）。
- **不动 WPF 版的行为与视觉**：本阶段对 WPF 侧只做"取代码到 Core"的搬迁与两处必要清理，不重构、不美化。
- **不做 MSIX 打包**（用户已定 unpackaged）。
- **不做 WinUI 版的数据迁移**：`LegacyDataMigration`（UmaPlayer→D-player）只由 WPF 壳执行。
- **不引入第三套 UI 框架**、不改播放链与并发不变量（Phase 19 的契约原样保留）。
- 不删 WPF 代码——保留哪一套是决策门之后的事。

## 2. 决策记录

| # | 议题 | 决定 | 理由 |
|---|------|------|------|
| 1 | 目标形态 | **借机做新视觉设计**（结构三区保留、控件语言换 Fluent） | 用户拍板：不只移植。保留"侧栏/列表/播放器栏"三区便于逐项功能对照，换材质、控件、标题栏与动效 |
| 2 | 共享层切分 | **抽 `D-player.Core` 类库** | 两壳共享同一份 VM/Service/Model；唯一实质障碍是 1 处 `ICollectionView`，成本可控 |
| 3 | 数据目录 | **独立目录** `D-player-winui` | 对比期互不影响、可随时删掉重来；代价是不能直接验证"两套操作同一份真实数据" |
| 4 | 部署形态 | **非打包 unpackaged + WASDK self-contained** | `dotnet run`/双击 exe 即可跑，免装运行时；对比阶段最省事 |
| 5 | 打法 | **垂直切片先行**（A 方案） | 每步可停、每步有可运行产物；最早撞上 WinUI 的三块硬骨头 |
| 6 | 决策门 | **切片完成后由用户拍板**续投/停止 | 避免"一口气做到功能等价"的长周期与返工风险 |
| 7 | WASDK 版本 | **不通就取新版**（不退回 `net9.0` 壳） | 用户拍板；nuget.org 可用，取最新稳定版优先于降 TFM |
| 8 | 门禁隔离 | **解决方案筛选器**：`D-player.sln` 含全部工程，`D-player.slnf` 只含 Core+WPF+Tests，主门禁走筛选器 | 用户拍板；让 WinUI 工具链不进入既有构建/测试路径。**已实测**：`dotnet build probe.slnf` 0 警告 0 错误、`dotnet test probe.slnf` 总计 172 / 失败 0（在本仓现有 2 个工程上验证，2026-10-06） |

## 3. 勘察证据（现状）

| 项 | 证据 | 结论 |
|---|---|---|
| 工程形态 | `D-player.csproj`：`OutputType=WinExe`、`net10.0-windows`、`UseWPF=true`；`InternalsVisibleTo("D-player.Tests")`；Tests 以 `ProjectReference` 引用它 | 需拆层；`InternalsVisibleTo` 随 Core 走 |
| VM/Service WPF 耦合 | `grep "System.Windows\|CollectionViewSource\|BitmapImage"` 命中：`PlaylistViewModel.cs:5,134`（唯一实质依赖）、`PlayerViewModel.cs`（仅注释）、`Models/Track.cs`（仅注释） | **共享层已基本 WPF-free** |
| 排序机制 | `SortBy(column)` **物理重排 `Queue`**（clear + 排序 + 重映射 `CurrentIndex`，`:161-190`）；`SortedView` 只是 `CollectionViewSource.GetDefaultView(Queue)`（`:134`），XAML 唯一绑定在 `PlaylistView.xaml:123` | `SortedView` 冗余，删除即达成 WPF-free，排序语义零变化 |
| 文件对话框 | `Services/Win32FileDialogService.cs:1` `using Microsoft.Win32`（PresentationFramework） | 移入 WPF 壳；Core 只留 `IFileDialogService` |
| 数据目录硬编码 | `JsonSettingsPersistence.cs:23`、`JsonPlaylistService.cs:30`、`JsonLibraryCache.cs:27,36` 均 `Path.Combine(appData, "D-player")`；`LegacyDataMigration.cs:20-21` 写死 UmaPlayer/D-player | 需参数化（§4.3） |
| 纯字符串 View 层工具 | `Views/Controls/PlaylistImportReportFormatter.cs`（有 5 条单测） | 挪进 Core，使测试工程只需依赖 Core |
| 工具链 | `dotnet --list-sdks` → 10.0.201 / 10.0.401；缓存 `microsoft.windowsappsdk` 1.6.241114003、1.7.250909003；`microsoft.windows.sdk.buildtools` 10.0.22621.756 | WASDK 版本需实测，可能需联网取新版 |

## 4. 技术设计

### 4.1 工程与切分

```
D-player.Core      (net10.0-windows, 类库, UseWPF 关闭)   ← Models/ Services/ ViewModels/ Configuration/
D-player           (net10.0-windows, WinExe, UseWPF)      ← App.xaml, Views/, Converters/, Themes/, Win32FileDialogService
D-player.WinUI     (net10.0-windows10.0.19041.0, WASDK)   ← 全新；App/ MainWindow/ Views/
D-player.Tests     → 改为引用 Core（断言零改动）
```

- **Core 用 `net10.0-windows`（不加版本号、不开 `UseWPF`）**：既无 PresentationFramework 依赖，又能满足 `NAudio.Wasapi` 的 Windows 平台标注（避免 CA1416），且两个壳（含 `net10.0-windows10.0.19041.0`）都能引用。这是"WPF-free"的**准确**含义——不是平台无关，而是不含 WPF UI 框架。
- **DI 装配**：Core 暴露 `AddDPlayerCore()`（注册服务/VM/持久化）；各壳再补自己的 UI 服务（`IFileDialogService` 实现、窗口相关）。现有 `Extensions/ServiceCollectionExtensions.cs` 按此拆分。
- **`InternalsVisibleTo("D-player.Tests")`** 随 Core；WPF 壳若还需暴露内部成员给测试，再加一条。
- **`Tests` 工程属性保持不变**（`net10.0-windows` + `UseWPF=true` + `OutputType=Exe` + MTP 配置），只把 `ProjectReference` 改指 Core（若 View 层测试仍需 WPF 壳，则同时保留对 WPF 壳的引用）。
- **顺序安全**：20-1 只做 Core 提取 + 两处清理 + WPF 壳适配，产物是"行为零变化的 WPF 版 + 全绿测试"，可独立提交交付。

### 4.1.1 解决方案与门禁隔离

| 文件 | 内容 | 用途 |
|---|---|---|
| `D-player.sln` | Core + WPF + WinUI + Tests（四个工程全含） | IDE 里一次看到全部；不用于门禁 |
| `D-player.slnf` | Core + WPF + Tests | **主门禁**：`dotnet build D-player.slnf -c Debug`、`dotnet test D-player.slnf -c Debug` |
| WinUI 构建 | 直接构建 csproj：`dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug` | 只在做 WinUI 时用；不额外做第二个筛选器 |

- **实测依据**（2026-10-06，在本仓现有工程上以 `probe.slnf` 验证）：`dotnet build <slnf>` 0 警告 0 错误；`dotnet test <slnf>` 总计 172 / 失败 0 —— 说明 `.slnf` 在 xunit.v3 + MTP + `global.json` 这套组合下可用。
- 因门禁命令由 `.sln` 变为 `.slnf`，**PROJECT/README/COUPLING 与项目记忆里的命令必须同步改**（否则照旧命令跑会连带构建 WinUI，隔离失效）。

### 4.2 共享层的两处实质改动

| 位置 | 现状 | 目标 | 风险 |
|---|---|---|---|
| `PlaylistViewModel.SortedView` | `public ICollectionView SortedView`（`private set`）+ `using System.Windows.Data` + `CollectionViewSource.GetDefaultView(Queue)`；`SortBy` 末尾 `OnPropertyChanged(nameof(SortedView))` | 删除该属性与 using；`SortBy` 的物理重排与 `CurrentIndex` 重映射**一字不动**；`PlaylistView.xaml:123` 的 `ItemsSource` 改绑 `Queue`（`ObservableCollection` 自带变更通知，clear/add 已经会驱动列表刷新） | 低。需在实施时全仓 grep `ICollectionView`/`CollectionViewSource`/`SortedView` 确认无其他消费点 |
| `Services/Win32FileDialogService.cs` | WPF 的 `Microsoft.Win32.OpenFileDialog` | 整体移入 WPF 壳工程（命名空间不变）；WinUI 壳另写 `FileOpenPicker` 版（`InitializeWithWindow` 拿 HWND）——切片阶段**不新增**对话框路径，仅保证 Core 干净 | 低 |

`PlaylistImportReportFormatter.cs` 从 `Views/Controls/` 移入 Core（纯字符串函数，5 条单测随之只依赖 Core）。

### 4.3 数据目录与线程契约

- Core 新增 `DPlayerDataPaths` 记录（`Root = %LocalAppData%`，`FolderName` 由壳注入）并注册进 DI。WPF 壳传 `"D-player"`，WinUI 壳传 `"D-player-winui"`。四个持久化点（`JsonSettingsPersistence` / `JsonPlaylistService` / `JsonLibraryCache` 两处 / `LegacyDataMigration`）改为注入该记录。
- **`LegacyDataMigration` 只由 WPF 壳调用**；WinUI 版不做迁移（目录从零开始）。
- **线程契约原样复用**：`NAudioPlaybackService` 在构造时捕获 `SynchronizationContext` 用于事件封送 → 两个壳都必须在 **UI 线程**构造 DI 容器（既有契约，PROJECT 已写明）。WinUI 的 `DispatcherQueueSynchronizationContext` 满足该前提。
- 断点续播（2026-10-06 特性）对两个壳一视同仁：Core 里实现，WinUI 版天然具备（数据写各自目录）。

### 4.4 WinUI 切片的新视觉方向

- 材质与主题：**Fluent 深色 + Mica 背景**（保留 D-player 深色身份）；`Application.RequestedTheme = Dark`。
- 窗口：`ExtendsContentIntoTitleBar` + `AppWindowTitleBar`（Fluent 标题栏按钮），替代 WPF 的自绘 `WindowChrome`。
- 导航：`NavigationView` 左栏承载歌单（替代自绘 sidebar）。
- 列表：`ListView` + Fluent 项样式（含 hover 播放按钮）。
- 播放器栏：底部 bar，用原生 `Slider`/`Button`/`FontIcon`。
- 结构：仍保留"侧栏 / 列表 / 播放器栏"三区，便于与 WPF 版逐项功能对照。
- **视觉细节在真机上迭代**：spec 只定到"材质 + 控件选型 + 布局分区"这一层，不逐像素承诺。

### 4.5 测试与验收

- **Core 切分（20-1）**：`dotnet build D-player.sln` 0 警告；`dotnet test D-player.sln -c Debug`（**不加 `--nologo`**）172 通过；WPF 版手动冒烟（播放 / 导入导出 / 断点续播）。
- **WinUI 切片（20-3）真机清单**（交用户逐项确认）：
  1. 启动即出窗口（Mica/深色生效）
  2. 左侧歌单与曲目列表正确显示
  3. 双击曲目**出声**，进度条推进
  4. 暂停/继续可用
  5. 关闭后重开按 ▶ **从断点续播**（数据落在 `%LocalAppData%\D-player-winui\`）
  6. 干净退出，无异常
- **门禁命令（已隔离，见 §4.1.1）**：`dotnet build D-player.slnf -c Debug` 0 错误 0 警告；`dotnet test D-player.slnf -c Debug`（**不加 `--nologo`**）172 通过 / 0 失败。WinUI 工程不进主门禁，其工具链问题不会污染既有绿灯；但 20-2（骨架验证）仍排在切片之前——切片依赖它。若 WASDK×net10 组合不通，取 nuget.org 最新稳定版 WASDK（若仍不通则停在 20-2 并如实报告，不降级、不伪造）。
- 无 WinUI XAML 层自动化测试（项目惯例）；VM/Service 的测试全部在 Core 侧，两个壳共享同一份覆盖率。

### 4.6 对比材料与决策门

- **功能等价核对表**（逐项打勾，只列切片已覆盖与明确缺失两大类）：启动与窗口、歌单导航、曲目列表显示、播放/暂停、进度显示、断点续播 + 明确标注"频谱/拖拽/EQ/设置/导入导出：本阶段不在 WinUI 侧"。
- **主观评分表**（1-5 分 + 备注）：视觉观感 / 操作手感 / 启动与播放性能 / 开发体验（改一处 UI 的难易）。
- 决策门输出：用户给出"续投（进入第二阶段：功能等价）/ 停止（保留或删除 WinUI 代码）"的结论，记入文档与项目记忆。

### 4.7 步骤划分（供实施计划展开）

| 步 | 内容 | 交付物 | 可停点 |
|---|---|---|---|
| 20-1 | 抽 `D-player.Core` + 两处清理 + WPF 壳适配 + 测试改引用 | 行为零变化的 WPF 版、172 绿 | ✅ 可独立交付 |
| 20-2 | 建 `D-player.WinUI`（unpackaged + self-contained）并跑通最小窗口；**实测记下可用的 WASDK 版本** | 能起窗口的空壳 exe | ✅ 工具链定论 |
| 20-3 | 第一条纵切（新视觉：主窗口 + NavigationView + 列表 + 播放器栏 + 播放/暂停/断点续播） | 可对比的 WinUI 切片 | ✅ |
| 20-4 | 对比材料 + 决策门；文档同步（README/PROJECT/COUPLING） | 决策记录 | ✅ 本阶段终点 |

**第二阶段（功能等价：频谱/拖拽/对话框/EQ/设置/导入导出）不在本 spec 范围**，续投后另出 spec + plan。

## 5. 文档与契约同步

| 文档 | 改动 |
|---|---|
| `docs/COUPLING.md` §5 | 新增契约：**Core 必须 WPF-free**（不得引用 `System.Windows.*` / `PresentationFramework`）；两壳共享同一份 VM/Service，业务行为只有一份实现；数据目录由壳注入（`D-player` vs `D-player-winui`，跨进程互不写同一份文件） |
| `docs/COUPLING.md` §7 | 新增 ❌：不要在 Core 里引用 WPF 类型（含 `ICollectionView`）；不要给 WinUI 壳做 UmaPlayer 迁移；不要让两个壳共用同一数据目录（当前无跨进程锁）；**不要把 WinUI 工程加进主门禁筛选器**（`D-player.slnf` 只含 Core+WPF+Tests） |
| `docs/PROJECT.md` | 工程结构（三个工程 + 两个解决方案文件）、依赖清单、数据目录（新增 winui 目录）、**构建/测试命令改为 `dotnet build/test D-player.slnf`**、Phase 20 状态；`SortedView` 相关描述若存在需改 |
| `README.md` | 项目结构树补两个新工程与 `D-player.slnf`；构建/测试命令改 `.slnf`；补 WinUI 壳的启动命令；阶段表加 Phase 20 行 |
| 本 spec + 实施计划 | 按惯例落盘并勾选 |

## 6. 验收标准

1. **20-1**：新增 `D-player.Core` 与 `D-player.slnf`（只含 Core+WPF+Tests）；`dotnet build D-player.slnf -c Debug` 0 错误 0 警告；`dotnet test D-player.slnf -c Debug` **172 通过 / 0 失败**（断言零改动）；WPF 版真机冒烟通过；`grep -rn "System.Windows\|CollectionViewSource" D-player.Core/` 无命中（Core WPF-free 可 grep 验证）。
2. **20-2**：`D-player.WinUI` 构建 0 警告、能起窗口；**记录实际可用的 WASDK 版本与 TFM 组合**（写进 PROJECT 与提交信息）。
3. **20-3**：4.5 的真机清单 6 项由用户逐项确认通过（未做/未验的项如实标 NOT VERIFIED，不得凭空报通过）。
4. **20-4**：对比材料（核对表 + 评分表）产出；用户给出续投/停止结论并记录；三份文档同步完成。
5. 全阶段：WPF 版行为与视觉零变化（只搬迁不重构）；不做 MSIX；不删 WPF 代码。

## 7. 风险与边界

| 风险 | 影响 | 处置 |
|---|---|---|
| WASDK × net10 组合不通 | 20-2 阻塞 | 取 nuget.org 最新稳定 WASDK（用户已定：不退回 net9 壳）；仍不通则停在 20-2 并如实报告 |
| `D-player.WinUI` 工程把 WinUI 工具链拖进既有构建/测试路径 | 影响既有绿灯流程 | **已消解**：主门禁走 `D-player.slnf`（只含 Core+WPF+Tests），WinUI 只以 csproj 单独构建；`.slnf` 支持性已在 20-1 前实测（§4.1.1）。文档与记忆里的门禁命令须同步改为 `.slnf` |
| 无 `Adorner` 的拖拽插入指示 | 第二阶段风险（本阶段不涉及） | 第二阶段用 overlay 指示线方案，届时单独验证 |
| 频谱渲染 | 第二阶段风险 | WinUI 3 有 `Microsoft.UI.Xaml.Media.CompositionTarget.Rendering`；必要时上 Win2D |
| Core 提取引入行为差异 | 影响 WPF 版稳定性 | 20-1 以"172 测试 + 真机冒烟"收口；`SortBy` 语义逐行对照，不做"顺手优化" |
| 两壳共存导致文档/依赖漂移 | 长期可维护性 | COUPLING 登记三条新契约（§5）；PROJECT 明确两目录与两命令 |

## 8. 参考

- WinUI 3 / Windows App SDK：`ExtendsContentIntoTitleBar`、`AppWindowTitleBar`、`NavigationView`、`ContentDialog`、Mica 背景、unpackaged 部署（`WindowsPackageType=None` + `WindowsAppSDKSelfContained`）
- 本仓相关：`D-player.csproj`、`ViewModels/PlaylistViewModel.cs:134,161-190`、`Views/Controls/PlaylistView.xaml:123`、`Services/Win32FileDialogService.cs`、`Services/Json*.cs`、`docs/COUPLING.md` §5/§7、`docs/PROJECT.md` §5–§9、`docs/superpowers/specs/2026-10-06-d-player-phase19-dependency-migration-design.md`（工程与门禁先例）
