# Phase 20：WinUI 3 第二套 UI（切片 + 决策门）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 抽出 WPF-free 的 `D-player.Core`，用它驱动一个全新的 WinUI 3 壳，做出"主窗口 + 歌单导航 + 曲目列表 + 播放器栏 + 真机播放/断点续播"的第一条纵向切片，并把对比材料交给用户拍板去留。

**Architecture:** 共享层（Models/Services/ViewModels/Configuration）搬进类库 `D-player.Core`（`net10.0-windows`，不开 `UseWPF`）；WPF 壳与 WinUI 壳各自引用它、各自注册自己的 UI 服务。主门禁走解决方案筛选器 `D-player.slnf`（只含 Core+WPF+Tests），WinUI 工程不进主门禁、只单独构建。数据目录由壳注入（`D-player` vs `D-player-winui`）。

**Tech Stack:** .NET 10（`net10.0-windows` / `net10.0-windows10.0.19041.0`）· WPF（既有壳）· WinUI 3 / Windows App SDK（unpackaged + self-contained）· CommunityToolkit.Mvvm · NAudio 3.1.0 · xunit.v3 + MTP

**Spec:** [`docs/superpowers/specs/2026-10-06-d-player-phase20-winui-shell-design.md`](../specs/2026-10-06-d-player-phase20-winui-shell-design.md)

**验证基线：** 起点 `92b8412`（工作树干净）；当前 **172 测试全绿 / 构建 0 警告**。每个 Task 结束都以"0 警告 + 172（或更多）测试绿"收尾。
> **勘误**：起点数字对、提交号不对——计划写 `92b8412`，实施台账记录的 BASE 是 `3f6a340`（即"把本计划归档"那次提交，内容等价、在其之后）。"172（或更多）"的"更多"后来一路涨到 180，逐 Task 的真实值见下一条（Global Constraints 计数勘误）。

## Global Constraints

- **门禁命令**：`dotnet build D-player.slnf -c Debug --nologo -v q` → **0 错误 0 警告**；`dotnet test D-player.slnf -c Debug -v q` → **172 通过 / 0 失败**（Task 2 之后为 **174**）。**`dotnet test` 一律不加 `--nologo`**（加了会静默跑 0 条并打印「成功: 0」；`--nologo` 只能用在 `dotnet build` 上）。
  > **勘误（实施后被代码推翻 · 裁定 P-2 + P-8 + P-19）**：这一行的两个数都不成立。真实轨迹是 **174（Task 1 之后）→ 177（Task 2 之后）→ 177（Task 3 之后，门禁未变）→ 180（Task 4 之后）**。
  > - **172 → 174**：Task 1 评审指出"本 Task 唯一用户可感的改动"（`SortedView` 删除、列表改绑 `Queue`，即表头点击排序）**零测试覆盖**，172 条测试对它无感；于是补了两条 `SortBy` 事实（`dc3b0cc`）。基线整体 +2（P-8）。
  > - **括号里的"174"本身是 175 的笔误**（P-2）：Task 2 Step 7 与 Task 3 Step 4 都写 175（= 172 + 新增 3 条 `DPlayerDataPathsTests`），计划自相矛盾，裁定以 175 为准；叠加上面的 +2，真实值 177。
  > - **180 的分解**（P-19，逐步累加，每步给出中间值）：**172** → +2（Task 1 补的 `SortBy` 两条事实）→ **174** → +3（Task 2 `DPlayerDataPathsTests`）→ **177** → +1（Task 4 首轮新增的 `PlayIndexAsync` 事实）、+1（R-5 壳路径钉桩）→ **179** → −1（那条 `PlayIndexAsync` 事实随该 API 一起删除，与上一步的 +1 相抵为**净零**）、+1（越界事实改落在共享入口上）、+1（DI 图解析）→ **180**。此前这里漏写了 `+1（PlayIndexAsync 事实）` 却保留了它的 `−1`，所以链条只加到 179。计划正文其余出现 **176** 的地方（Task 4 Step 3 的 Expected、Task 5 Step 2 的"测试计数改 176"、Step 3 的 Expected）同样作废，一律按 180 读。
- **Core 必须 WPF-free**：`D-player.Core/` 下不得出现 `System.Windows.*` / `PresentationFramework` / `ICollectionView` / `CollectionViewSource`。可 grep 验证（四个类型名一个都不能少，与 `docs/PROJECT.md` §9 的自检同口径；Step 10 用的是同一条命令）：`grep -rn "System.Windows\|PresentationFramework\|ICollectionView\|CollectionViewSource" D-player.Core --include=*.cs | grep -v "/obj/\|/bin/"` 无命中。
- **WPF 版行为与视觉零变化**：Task 1-2 只做搬迁与依赖注入改造，不改任何业务逻辑、不改 XAML 外观、不动播放链与并发不变量（Phase 19 契约）。
- **WinUI 工程不得进入主门禁**：`D-player.slnf` 只含 `D-player.Core`、`D-player`、`Tests/D-player.Tests`；WinUI 只以 csproj 单独构建。
  > **勘误（最终修复波 · 词汇与触发条件）**：本行"单独构建"留了一个空洞——**谁在什么时候跑它**没写，于是后续文档把它叫成"门禁三"/"门禁三件套（含单壳构建）"，把手跑命令写成了必然发生的事实，README 甚至把触发方向写反了（"整解构建只在改到 WinUI 时跑"）。收口后的唯一口径（`README.md`「构建与运行」、`docs/PROJECT.md` §4.3 第 28 条与 §8.2、`docs/COUPLING.md` §5）是三层词汇：**门禁 = `.slnf` 构建 + `.slnf` 测试，每次改动必跑**；**WinUI 单工程构建 = 壳侧检查，不是门禁，触发条件是 `D-player.Core` 的公开面一变就跑**（外加任何改到 WinUI 本身的时候；增量后实测约 9 秒）；**整解 `.sln` 构建 = 可选**。一键：`powershell -File tools/verify-gates.ps1 -Fast|-Full`。本文件保留"主门禁"等原措辞作为历史归档，读到它们时按上面的三层词汇理解。
- **数据隔离**：WPF 壳用 `%LocalAppData%\D-player\`，WinUI 壳用 `%LocalAppData%\D-player-winui\`；两壳不得共用目录（无跨进程锁）。
- **CLI 构造契约**：两个壳都必须在 **UI 线程**构造 DI 容器（`NAudioPlaybackService` 构造时捕获 `SynchronizationContext` 用于事件封送）。
- 行尾纪律：仓库 `core.autocrlf=true`；**Edit 工具会把 CRLF 静默转 LF** → 改完 `git ls-files --eol <file>` 必须是 `i/lf    w/crlf`，否则 `unix2dos <file>`；**禁用 `sed -i`**。
- 提交风格 `type(scope): subject` + 要点式 body；每个 Task 至少一次提交。
- **不自动推送**：每次推送都要用户单独发话。工作直接在 `master` 上做（用户既定习惯）。
- 联网：`Microsoft.WindowsAppSDK` 需要一次从 nuget.org 的 restore（本机缓存只有 1.6/1.7）；nuget.org 可用，失败重试即可。
- **不做**：MSIX 打包、WinUI 侧的数据迁移、功能等价（频谱/拖拽/对话框/EQ/设置/导入导出）、删除 WPF 代码。

---

## 文件结构

| 路径 | 责任 | Task |
|---|---|---|
| `D-player.Core/D-player.Core.csproj`（新） | 共享层类库（`net10.0-windows`，无 `UseWPF`） | 1 |
| `D-player.Core/{Models,Services,ViewModels,Configuration}/`（搬入） | 原样搬迁的共享代码 | 1 |
| `D-player.Core/Extensions/ServiceCollectionExtensions.cs`（搬入并拆） | `AddDPlayerCore(configuration, paths)`：服务/VM 注册（不含 UI 相关） | 1, 2 |
| `D-player.Core/ViewModels/PlaylistImportReportFormatter.cs`（搬入并改命名空间） | 导入报告文案（纯字符串，原在 `Views/Controls`） | 1 |
| `D-player.Core/Configuration/DPlayerDataPaths.cs`（新） | 数据目录根 + 文件夹名（`Directory` 组合属性） | 2 |
| `D-player/Services/Win32FileDialogService.cs`（搬回 WPF 壳） | WPF 文件对话框实现 | 1 |
| `D-player.csproj`（改） | 引用 Core；包引用收敛 | 1 |
| `Views/Controls/PlaylistView.xaml`（改 1 行） | `ItemsSource` 由 `SortedView` 改绑 `Queue` | 1 |
| `Tests/D-player.Tests.csproj`（改） | 引用目标改 Core | 1 |
| `D-player.slnf`（新） | 主门禁筛选器（Core+WPF+Tests） | 1 |
| `D-player.WinUI/`（新） | WinUI 壳：csproj / App / MainWindow / 最小文件对话框实现 | 3, 4 |
| `docs/PHASE20-COMPARISON.md`（新） | 功能等价核对表 + 六维评分表（用户填） | 5 |
| `README.md` / `docs/PROJECT.md` / `docs/COUPLING.md`（改） | 工程结构、命令改 `.slnf`、数据目录、新契约 | 5 |
| `tools/coupling-audit/Invoke-CouplingAudit.ps1`（改） | 层目录移到 Core 后要改扫描根 | 5 |

---

### Task 1: 抽出 `D-player.Core`（共享层搬家 + DI 拆分 + 解决方案筛选器）

**Files:**
- Create: `D-player.Core/D-player.Core.csproj`、`D-player.slnf`
- Move: `Models/`、`Services/`（`Win32FileDialogService.cs` 除外）、`ViewModels/`、`Configuration/`、`Extensions/ServiceCollectionExtensions.cs` → `D-player.Core/`
- Move: `Views/Controls/PlaylistImportReportFormatter.cs` → `D-player.Core/ViewModels/`（改命名空间 `DPlayer.Views.Controls` → `DPlayer.ViewModels`）
- Move: `Services/Win32FileDialogService.cs` → `D-player/Services/`（留在 WPF 壳，命名空间不变）
- Move: `Tests/Views/PlaylistImportReportFormatterTests.cs` → `Tests/ViewModels/`（改 using）
- Modify: `D-player.csproj`、`Tests/D-player.Tests.csproj`、`Views/Controls/PlaylistImportUi.cs`、`Views/Controls/PlaylistView.xaml`（1 行）、`ViewModels/PlaylistViewModel.cs`（删 `SortedView`）、`App.xaml.cs`（DI 调用改名）

**Interfaces:**
- Consumes: 无（首个 Task）
- Produces:
  - `DPlayer.Extensions.ServiceCollectionExtensions.AddDPlayerCore(this IServiceCollection, IConfiguration)`（Task 2 会加第三个参数）
  - WPF 壳里：`services.AddSingleton<IFileDialogService, Win32FileDialogService>()`
  - `D-player.slnf`：主门禁命令载体

- [x] **Step 1: 建 Core 工程文件**

`D-player.Core/D-player.Core.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net10.0-windows</TargetFramework>
        <RootNamespace>DPlayer</RootNamespace>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <UseWPF>false</UseWPF>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="NAudio.Core" Version="3.1.0" />
        <PackageReference Include="NAudio.Wasapi" Version="3.1.0" />
        <PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />
        <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.*" />
        <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.*" />
        <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="10.*" />
        <PackageReference Include="z440.atl.core" Version="7.18.0" />
    </ItemGroup>

    <ItemGroup>
        <InternalsVisibleTo Include="D-player.Tests" />
    </ItemGroup>

</Project>
```

说明：`net10.0-windows` 而不带版本号——既无 `PresentationFramework` 依赖，又满足 `NAudio.Wasapi` 的 Windows 平台标注（避免 CA1416）。

- [x] **Step 2: 用 `git mv` 搬迁（保留历史）**

```bash
mkdir -p D-player.Core
git mv Models D-player.Core/Models
git mv Services D-player.Core/Services
git mv ViewModels D-player.Core/ViewModels
git mv Configuration D-player.Core/Configuration
git mv Extensions D-player.Core/Extensions
git mv D-player.Core/Services/Win32FileDialogService.cs D-player/Services/Win32FileDialogService.cs
git mv Views/Controls/PlaylistImportReportFormatter.cs D-player.Core/ViewModels/PlaylistImportReportFormatter.cs
git mv Tests/Views/PlaylistImportReportFormatterTests.cs Tests/ViewModels/PlaylistImportReportFormatterTests.cs
```

（`D-player/Services/` 是 WPF 壳自己的目录，需要先 `mkdir -p D-player/Services`；`Views/`、`Converters/`、`Themes/`、`App.xaml*` 留在 WPF 壳。）

- [x] **Step 3: 改 WPF 壳的 csproj**

`D-player.csproj`：加 `<ProjectReference Include="D-player.Core\D-player.Core.csproj" />`；从 `PackageReference` 移除 `NAudio.Core`、`NAudio.Wasapi`、`CommunityToolkit.Mvvm`、`z440.atl.core`（已随 Core 传递）；保留 `Microsoft.Extensions.DependencyInjection` 与 `Microsoft.Extensions.Configuration.Json`（`App.xaml.cs` 直接使用）。保留原有 `Compile Remove="Tests/**"` 等排除项与 `appsettings.json` 拷贝项。
> **勘误（实施前就被预检扫出的真实缺陷 · 裁定 P-1，本步原样执行会炸）**："保留原有排除项"漏了致命一半。`D-player.csproj` 位于**仓库根**，SDK 的默认 glob 是 `**/*.cs` / `**/*.xaml`，所以兄弟工程目录必须**逐个显式排除**：本步要补 `D-player.Core/**` 的一组，Task 3 再补 `D-player.WinUI/**` 的一组（共两组新增）。漏掉的后果是 WPF 程序集把 Core/WinUI 的源文件一起编进去 → 重复类型、以及 WPF 侧根本解析不了的 `Microsoft.Win32` / `Microsoft.UI.Xaml` 引用（`MC3074` / `CS0234`）。csproj 里 `Tests/**` 那组排除项自带的注释就是这个隐患在本仓库存在过的直接证据；另外 WPF SDK 的 `*_wpftmp.csproj`（XAML 编译临时工程）重新求值时同样从仓库根 glob，所以这组排除项对 XAML 路径也必要。计划起草时本节没有任何"新增排除项"的指令，是预检阶段拦下来的。

- [x] **Step 4: 拆 DI 注册**

`D-player.Core/Extensions/ServiceCollectionExtensions.cs`：方法改名 `AddDPlayerCore`，**删除** `services.AddSingleton<IFileDialogService, Win32FileDialogService>();` 一行，其余（配置绑定、全部服务、三个 VM 与工厂）原样保留。文件头注释补一句"UI 相关服务（文件对话框等）由各壳自行注册"。

`App.xaml.cs`：`services.AddDPlayerServices(configuration)` → `services.AddDPlayerCore(configuration)` 后紧跟 `services.AddSingleton<IFileDialogService, Win32FileDialogService>();`。

- [x] **Step 5: 清掉排序视图的 WPF 依赖**

`D-player.Core/ViewModels/PlaylistViewModel.cs`：删除 `using System.Windows.Data;`（第 5 行）、删除 `public ICollectionView SortedView { get; private set; }`（第 65 行）与 `SortedView = CollectionViewSource.GetDefaultView(Queue);`（第 134 行）及 `SortBy` 末尾的 `OnPropertyChanged(nameof(SortedView));`（第 189 行）。`SortBy` 的物理重排与 `CurrentIndex` 重映射**逐行保留**。

`Views/Controls/PlaylistView.xaml:123`：`ItemsSource="{Binding SortedView}"` → `ItemsSource="{Binding Queue}"`。

先全仓确认无其他消费点：`grep -rn "SortedView" --include=*.cs --include=*.xaml . | grep -v "/obj/\|/bin/"` → 只应命中上面这些行。

- [x] **Step 6: 改命名空间与引用（formatter）**

- `D-player.Core/ViewModels/PlaylistImportReportFormatter.cs`：`namespace DPlayer.Views.Controls;` → `namespace DPlayer.ViewModels;`；文件内 `using DPlayer.ViewModels;` 删除。
- `Views/Controls/PlaylistImportUi.cs`：确保有 `using DPlayer.ViewModels;`（原本已有则可直接用）。
- `Tests/ViewModels/PlaylistImportReportFormatterTests.cs`：`using DPlayer.Views.Controls;` → `using DPlayer.ViewModels;`（命名空间声明 `DPlayer.Tests.Views` → `DPlayer.Tests.ViewModels` 一并改，保持一致）。

- [x] **Step 7: 改测试工程的引用**

`Tests/D-player.Tests.csproj`：`<ProjectReference Include="..\D-player.csproj" />` → `<ProjectReference Include="..\D-player.Core\D-player.Core.csproj" />`。其余属性（`OutputType=Exe`、MTP 两属性、`UseWPF`、包引用）保持不变。

- [x] **Step 8: 建解决方案筛选器并挂进 sln**

`D-player.slnf`（内容与已实测通过的一致）：

```json
{
  "solution": {
    "path": "D-player.sln",
    "projects": [
      "D-player.Core\\D-player.Core.csproj",
      "D-player.csproj",
      "Tests\\D-player.Tests.csproj"
    ]
  }
}
```

```bash
dotnet sln D-player.sln add D-player.Core/D-player.Core.csproj
```

- [x] **Step 9: 构建 + 全量测试**

```bash
dotnet build D-player.slnf -c Debug --nologo -v q
dotnet test  D-player.slnf -c Debug -v q
```

Expected：构建 **0 错误 0 警告**；测试 **总计 172 / 失败 0**。
> **勘误**：本步真实收尾值是 **174**，不是 172——评审要求给"本 Task 唯一用户可感改动"（表头排序）补覆盖，加了 2 条 `SortBy` 事实（P-8）。
> 另记一条 Step 12 的措辞教训（P-9）：本步那段提交信息里的 "the sort/playlist flows **were smoke-tested** on the real app" 说过头了——真机冒烟当时**有意没做**"点击表头后列表可见地重排"这一半（会永久改写用户真实歌单顺序）。历史不重写（仓库纪律：只追加、不改写），由后续提交 `dc3b0cc` 的正文显式更正。Task 4/5 的提交信息因此只写实际确认过的部分。

- [x] **Step 10: 验证 Core 确实 WPF-free**

```bash
grep -rn "System.Windows\|PresentationFramework\|ICollectionView\|CollectionViewSource" D-player.Core --include=*.cs | grep -v "/obj/\|/bin/"
```

Expected：**无输出**（`PlayerViewModel.cs` / `Models/Track.cs` 里提到 `BitmapImage` 的只是注释，若 grep 命中注释行，人工确认后放行并在报告里写明）。

- [x] **Step 11: WPF 版真机冒烟（行为零变化的证据）**

```bash
dotnet run --project D-player.csproj -c Debug
```

逐项确认：① 窗口起来、深色主题与自绘标题栏正常 ② 歌单列表显示 ③ 双击播放出声 ④ 列表表头点击排序仍生效（`SortBy` 改动的唯一可感点）⑤ 关闭再开，断点续播仍在。无法自动化的项如实标 NOT VERIFIED 交用户。
> **勘误（读这个勾之前先看）**：本步的 `[x]` 只代表"真机冒烟跑过"，**不代表五项逐项确认**——其中 **④（点击表头后列表可见地重排）有意没验证**（会永久改写用户真实歌单的顺序），详情与理由在上面 Step 9 的勘误块（裁定 P-9）。台账明确记录的未验证项只有 ④；其余各项的确认情况以 Task 1 报告为准，别把这个勾读成"五项都已由用户确认"。

- [x] **Step 12: 提交**

```bash
git add -A
git commit -m "refactor: extract a WPF-free D-player.Core shared by the UI shells

Models, services, view models and configuration move into a class
library (net10.0-windows, no UseWPF) so a second UI shell can share them.
The only real WPF leaks are removed on the way: PlaylistViewModel drops
its SortedView property (SortBy already reorders the queue physically, so
the list binds to Queue directly) and the Win32 file dialog moves into the
WPF shell, leaving IFileDialogService in Core.

The main gates move to a solution filter (D-player.slnf: Core + WPF +
Tests) so the upcoming WinUI project cannot drag the Windows App SDK
toolchain into every build and test run. WPF behaviour is unchanged:
same build output, 172 tests green, and the sort/playlist flows were
smoke-tested on the real app."
```

---

### Task 2: 数据目录参数化（`DPlayerDataPaths`）

**Files:**
- Create: `D-player.Core/Configuration/DPlayerDataPaths.cs`
- Modify: `D-player.Core/Services/JsonSettingsPersistence.cs`、`JsonPlaylistService.cs`、`JsonLibraryCache.cs`、`LegacyDataMigration.cs`、`D-player.Core/Extensions/ServiceCollectionExtensions.cs`、`App.xaml.cs`（WPF 壳传 `"D-player"`）
- Test: `Tests/Configuration/DPlayerDataPathsTests.cs`（新）、`Tests/Services/JsonLibraryCacheTests.cs`（改构造）

**Interfaces:**
- Consumes: Task 1 的 `D-player.Core` 工程与 `AddDPlayerCore`
- Produces: `DPlayer.Configuration.DPlayerDataPaths(string Root, string FolderName)`，属性 `string Directory => string.IsNullOrEmpty(FolderName) ? Root : Path.Combine(Root, FolderName)`；`AddDPlayerCore(this IServiceCollection, IConfiguration, DPlayerDataPaths)`（**签名变更**，两个壳都要传）

- [x] **Step 1: 写失败测试**

`Tests/Configuration/DPlayerDataPathsTests.cs`：

```csharp
using System.IO;
using DPlayer.Configuration;
using Xunit;

namespace DPlayer.Tests.Configuration;

public sealed class DPlayerDataPathsTests
{
    [Fact]
    public void Directory_CombinesRootAndFolder()
        => Assert.Equal(Path.Combine(@"C:\root", "D-player"),
            new DPlayerDataPaths(@"C:\root", "D-player").Directory);

    [Fact]
    public void Directory_EmptyFolder_IsRoot()
        => Assert.Equal(@"C:\root", new DPlayerDataPaths(@"C:\root", string.Empty).Directory);

    [Fact]
    public void Default_HasNoFolderName()
        => Assert.Equal(string.Empty, new DPlayerDataPaths { Root = @"C:\root" }.FolderName);
}
```

- [x] **Step 2: 运行确认失败**

Run: `dotnet test D-player.slnf -c Debug -v q --filter "FullyQualifiedName~DPlayerDataPathsTests"`
Expected: 编译失败（`DPlayerDataPaths` 不存在）。

- [x] **Step 3: 实现 `DPlayerDataPaths`**

```csharp
namespace DPlayer.Configuration;

/// <summary>
/// 用户数据目录（settings.json / queue.json / library-cache.json 的落点）。
/// 文件夹名由 UI 壳注入：WPF 壳 "D-player"，WinUI 壳 "D-player-winui"——
/// 两个壳各写各的目录，互不覆盖（无跨进程锁，共用目录会互相踩）。
/// </summary>
public sealed record DPlayerDataPaths
{
    public string Root { get; init; } =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public string FolderName { get; init; } = string.Empty;

    /// <summary>数据目录绝对路径；FolderName 为空时即 Root（测试用）。</summary>
    public string Directory => string.IsNullOrEmpty(FolderName) ? Root : Path.Combine(Root, FolderName);
}
```

（`Path` 需要 `using System.IO;`；`Configuration` 目录已在 Core 内。）

- [x] **Step 4: 三个持久化点改为注入**

- `JsonSettingsPersistence`：删无参构造器，新增 `public JsonSettingsPersistence(DPlayerDataPaths paths)`，内部 `var dir = paths.Directory; Directory.CreateDirectory(dir); _path = Path.Combine(dir, "settings.json");`。类注释里 `%LocalAppData%\D-player\settings.json` 改为"由 `DPlayerDataPaths` 决定（WPF 壳为 `%LocalAppData%\D-player\`）"。
- `JsonPlaylistService`：同上，文件名 `queue.json`。
- `JsonLibraryCache`：删 `public JsonLibraryCache()` 与 `internal JsonLibraryCache(string? overrideDir)`，改为单一 `public JsonLibraryCache(DPlayerDataPaths paths)`（`library-cache.json`）。
- `LegacyDataMigration`：`MigrateIfNeeded()` → `MigrateIfNeeded(DPlayerDataPaths paths)`；目标目录用 `paths.Directory`，旧目录 `OldFolderName = "UmaPlayer"` 不变。类注释注明"仅 WPF 壳调用；WinUI 壳从空目录开始"。

- [x] **Step 5: DI 与壳的接线**

- `AddDPlayerCore(this IServiceCollection services, IConfiguration configuration, DPlayerDataPaths dataPaths)`：方法体开头 `services.AddSingleton(dataPaths);`，其余不动。
- `App.xaml.cs`：`services.AddDPlayerCore(configuration, new DPlayerDataPaths { FolderName = "D-player" });`；`LegacyDataMigration.MigrateIfNeeded()` → `MigrateIfNeeded(new DPlayerDataPaths { FolderName = "D-player" })`（保持"在 DI 构造持久化服务之前执行"的顺序）。

- [x] **Step 6: 修测试构造**

`Tests/Services/JsonLibraryCacheTests.cs`：把该文件里**所有** `new JsonLibraryCache(_tempDir)` 改成 `new JsonLibraryCache(new DPlayerDataPaths { Root = _tempDir })`（用编辑器的替换功能一次改完，不要手工数个数）；补 `using DPlayer.Configuration;`。

- [x] **Step 7: 全量测试**

```bash
dotnet build D-player.slnf -c Debug --nologo -v q
dotnet test  D-player.slnf -c Debug -v q
```

Expected：0 警告；**总计 175 / 失败 0**（172 + 新增 3）。
> **勘误**：真实值是 **177** = 174（Task 1 收尾，含补的两条 `SortBy`）+ 3（`DPlayerDataPathsTests`）。计划里"172 + 新增 3"这个算式没错，错在基数：见 Global Constraints 的计数勘误（P-2 + P-8）。
> 另记两条实施中的裁定：**(P-10)** Step 3 的 record 写法（`{ get; init; }`、无主构造函数）与 Step 1 的位置式 `new DPlayerDataPaths(@"C:\root", "D-player")`、以及 `Root` 的"非常量默认值"三者互相矛盾，没有任何单一类型形状能同时满足，实施保留 Step 3 逐字形状并补了显式的 `()` 与 `(string Root, string FolderName)` 两个构造函数，评审确认计划需要的两种构造方式都可用、record 相等性与 `with` 不受影响。**(P-11)** "壳实际传的 `FolderName` 没有任何断言钉住"这条评审 Minor 被折进 Task 4（接线第二个壳的那一刻才成为活风险），于是有了 `Tests/Configuration/` 里的壳路径钉桩——但它只钉 Core 侧组合逻辑，**钉不住壳自己传错名字**。

- [x] **Step 8: 目视确认 WPF 数据没搬家**

启动 WPF 版 → 歌单/设置仍在（读的仍是 `%LocalAppData%\D-player\`）→ 关闭应用，确认没有新建 `%LocalAppData%\D-player\D-player\` 之类的嵌套目录（`Directory` 组合错误会立刻表现为空歌单）。

- [x] **Step 9: 提交**

```bash
git add -A
git commit -m "refactor: make the user data folder injectable per UI shell

settings.json, queue.json and library-cache.json stop hard-coding the
D-player folder: DPlayerDataPaths now carries the root and the folder
name, and AddDPlayerCore takes it as a parameter. The WPF shell passes
'D-player' (unchanged on disk, verified on the real app), which lets the
WinUI shell use its own 'D-player-winui' folder so the two can be
compared without writing over each other. The UmaPlayer migration now
takes the same record and stays WPF-only."
```

---

### Task 3: WinUI 工程骨架（工具链验证）

**Files:**
- Create: `D-player.WinUI/D-player.WinUI.csproj`、`D-player.WinUI/App.xaml`、`App.xaml.cs`、`D-player.WinUI/MainWindow.xaml`、`MainWindow.xaml.cs`
- Modify: `D-player.sln`（`dotnet sln add`，**不进 `.slnf`**）

**Interfaces:**
- Consumes: 无（最小空壳，先不接 Core）
- Produces: 可构建、可运行的 `D-player.WinUI` 工程 + **实测记录下来的 WASDK 版本 × TFM × 构建参数组合**（Task 4 依赖）

- [x] **Step 1: 建工程与最小窗口**

`D-player.WinUI/D-player.WinUI.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <OutputType>WinExe</OutputType>
        <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
        <RootNamespace>DPlayer.WinUI</RootNamespace>
        <ApplicationManifest>app.manifest</ApplicationManifest>
        <Platforms>x64</Platforms>
        <RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
        <UseWinUI>true</UseWinUI>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <!-- 非打包（unpackaged）+ 免装运行时（self-contained） -->
        <WindowsPackageType>None</WindowsPackageType>
        <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.WindowsAppSDK" Version="*" />
    </ItemGroup>

</Project>
```

`D-player.WinUI/app.manifest`（DPI 感知，WinUI 模板同款最小版）：

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="DPlayer.WinUI.app"/>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

`D-player.WinUI/App.xaml`：

```xml
<Application
    x:Class="DPlayer.WinUI.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

`D-player.WinUI/App.xaml.cs`：

```csharp
using Microsoft.UI.Xaml;

namespace DPlayer.WinUI;

public partial class App : Application
{
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
```

`D-player.WinUI/MainWindow.xaml`：

```xml
<Window
    x:Class="DPlayer.WinUI.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid>
        <TextBlock Text="D-player WinUI" HorizontalAlignment="Center" VerticalAlignment="Center" />
    </Grid>
</Window>
```

`D-player.WinUI/MainWindow.xaml.cs`：

```csharp
using Microsoft.UI.Xaml;

namespace DPlayer.WinUI;

public sealed partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
}
```

- [x] **Step 2: restore + 构建（工具链验证，允许按阶梯重试）**

```bash
dotnet restore D-player.WinUI/D-player.WinUI.csproj
dotnet build   D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q
```

按顺序处置，**记录最终生效的组合到报告与提交信息**：

1. `--nologo` 在 `dotnet build` 上可用（`dotnet test` 上不可用，本工程不涉及）。
2. 若报 `NETSDK1057`/平台错误 → 改为 `dotnet build ... -p:Platform=x64`。
3. 若报 WASDK 与 TFM 不兼容（版本门槛）→ `dotnet add D-player.WinUI/D-player.WinUI.csproj package Microsoft.WindowsAppSDK`（取最新稳定版）后重试。
4. 若报 RID 相关错误 → 显式 `-r win-x64`。
5. 仍不通 → 停下，报告实际错误文本（**不降级 TFM、不伪造**），并把"骨架验证失败"作为本 Task 的结论交回。

Expected（通过时）：0 错误 0 警告。

- [x] **Step 3: 真机起窗口**

```bash
dotnet run --project D-player.WinUI/D-player.WinUI.csproj -c Debug
```

Expected：出现一个 WinUI 窗口，标题栏与内容区显示 `D-player WinUI`；关闭窗口后进程退出。**记录实际 WASDK 版本**（`dotnet list D-player.WinUI/D-player.WinUI.csproj package`）。
> **勘误（裁定 P-13：本步的预期不可能由本步自己的代码满足）**：Step 1 逐字给出的 `MainWindow.xaml` **没有** `Title` 属性，WinUI 3 于是显示框架默认标题——实测标题栏是 `WinUI Desktop`，只有内容区是 `D-player WinUI`。"标题栏与内容区显示 `D-player WinUI`"这条判据与同批 verbatim XAML 自相矛盾。实施者没有擅自加 `Title`（本步交付物是工具链可行性，且要求逐字使用）而是上报，控制方裁定：**不返工 Task 3**，由 Task 4 重写 `MainWindow` 并在 code-behind 里 `Title = "D-player"` 关掉用户可见的那一半。
> 顺带两条同批裁定：**(P-7)** Step 1 的 `Version="*"` 浮动版本让提交不可复现，工具链阶梯通过后必须钉成具体版本——实钉 **Microsoft.WindowsAppSDK 2.5.1** × `net10.0-windows10.0.19041.0` × **x64**（阶梯停在其第 2 级，靠 `-p:Platform=x64`，TFM 未降级），并把版本与构建参数写进提交信息。**(P-15)** 后来又授权把 `<Platform>x64</Platform>` 直接钉进 csproj（带注释），因为 `<Platforms>` 只声明支持面、不设默认值，裸 `dotnet build` / `dotnet run` 会因求值出 `Platform=AnyCPU` 被 WASDK 的 self-contained targets 拒掉——本计划的主命令是裸命令，纸割伤在大路上。
> **(P-6)** 本计划从没检查过整解 `.sln`。IDE 用户点的是"Build Solution"，所以额外跑了一次 `dotnet build D-player.sln -c Debug --nologo -v q` 作为**信息性**检查（不是门禁，门禁仍走 `.slnf`）：结果 0 警告 / 0 错误。

- [x] **Step 4: 挂进 sln（但不进 slnf）**

```bash
dotnet sln D-player.sln add D-player.WinUI/D-player.WinUI.csproj
dotnet build D-player.slnf -c Debug --nologo -v q
dotnet test  D-player.slnf -c Debug -v q
```

Expected：sln 里能看到 WinUI 工程；**门禁结果不变**（0 警告 / 175 通过）——证明隔离生效。
> **勘误**：门禁结果确实不变，但基数是 **177**（见 Global Constraints 的计数勘误）。另外本步实际还做了两件计划没写的事：给 `D-player.csproj` 追加**第三组** sibling 排除（`D-player.WinUI/**`，P-1 的延续，计划正文里根本没有这条排除项，是裁定 P-1 把它拆成"Task 1 补 `D-player.Core/**`、Task 3 再补 `D-player.WinUI/**`"两段分别落地）与一次整解 `.sln` 构建的信息性核验（P-6；不是门禁）。`.sln` 里 WinUI 的 `Debug|x86`/`Release|x86` 映射到 `x64` 是 `dotnet sln add` 对单平台工程的标准输出，`.sln` 不是门禁，记账备查、不返工。

- [x] **Step 5: 提交**

```bash
git add -A
git commit -m "feat(winui): scaffold an unpackaged WinUI 3 shell and prove the toolchain

An empty WinUI 3 window (Windows App SDK, unpackaged + self-contained,
x64) builds and launches on this machine: <填入实测的 WASDK 版本与
构建参数>. It joins D-player.sln for IDE use but stays out of
D-player.slnf, so the main gates keep running Core + WPF + Tests only -
verified by re-running them unchanged after adding the project."
```

---

### Task 4: 第一条纵向切片（新视觉，真机可播）

**Files:**
- Modify: `D-player.WinUI/D-player.WinUI.csproj`（引用 Core）
- Create: `D-player.WinUI/Services/WinUiFileDialogService.cs`（切片期最小实现）
- Modify: `D-player.WinUI/App.xaml.cs`（DI 引导 + 建 MainWindow）
- Rewrite: `D-player.WinUI/MainWindow.xaml`、`MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `AddDPlayerCore(IConfiguration, DPlayerDataPaths)`（Task 2）、`MainViewModel` / `PlaylistsViewModel` / `PlayerViewModel` / `PlaylistViewModel` 的公开成员（`Playlists`、`CurrentPlaylistId`、`ViewedPlaylist`、`Queue`、`CurrentIndex`、`PlayCurrentCommand`、`PlayPauseCommand`、`PlayState`、`CurrentTrack`、`Position`、`Duration`）
- Produces: 可对比的 WinUI 切片（Task 5 的对比对象）

- [x] **Step 1: 引用 Core 与 DI 引导**

`D-player.WinUI.csproj` 加：

```xml
  <ItemGroup>
    <ProjectReference Include="..\D-player.Core\D-player.Core.csproj" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.*" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.*" />
  </ItemGroup>
```

`D-player.WinUI/Services/WinUiFileDialogService.cs`（切片期最小实现——切片不暴露导入/导出入口，PlaylistViewModel 的工厂又必须能解析它）：

```csharp
using DPlayer.Services;

namespace DPlayer.WinUI.Services;

/// <summary>
/// 切片阶段的最小实现：本阶段不暴露文件选择入口（导入/导出留待第二阶段），
/// 因此固定返回空集合；接入真实 FileOpenPicker 属于第二阶段。
/// </summary>
public sealed class WinUiFileDialogService : IFileDialogService
{
    public IReadOnlyList<string> OpenFiles(string filter, bool multiselect = false)
        => Array.Empty<string>();
}
```
> **勘误（Task 4 Step 1 的片段无法编译）**：`IFileDialogService` 有**三个**成员（`OpenFiles` / `OpenFolder` / `SaveFile`），本片段只实现了 1 个，照抄就是 CS0535"未实现接口成员"。交付形态是三个都实现、全部返回"用户取消"（`OpenFiles` 空集合、`OpenFolder`/`SaveFile` 返回 `null`），见 `D-player.WinUI/Services/WinUiFileDialogService.cs`。
> 顺带把这条纪律登记进 COUPLING §5/§7：Core 的 `PlaylistViewModel` 工厂是**在被调用时**才 `GetRequiredService<IFileDialogService>()`，所以壳漏注册时 `GetRequiredService<MainViewModel>()` 仍然成功、**第一次构造歌单才炸**；新增的 `Tests/Extensions/AddDPlayerCoreTests.cs` 把"真的构造一个歌单 VM"包含进断言，但它只钉 **Core** 的图，钉不到壳自己那条注册——因为 WinUI 按设计不在门禁里（P-19 相关），真机起窗仍是唯一防线。

`App.xaml.cs` 改为（注意：**在 UI 线程构造容器**，`NAudioPlaybackService` 的上下文捕获依赖这一点）：

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using DPlayer.Configuration;
using DPlayer.Extensions;
using DPlayer.Services;
using DPlayer.ViewModels;
using DPlayer.WinUI.Services;

namespace DPlayer.WinUI;

public partial class App : Application
{
    private ServiceProvider? _services;
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var services = new ServiceCollection();
        services.AddDPlayerCore(configuration, new DPlayerDataPaths { FolderName = "D-player-winui" });
        services.AddSingleton<IFileDialogService, WinUiFileDialogService>();
        _services = services.BuildServiceProvider();

        // 需在 UI 线程构造状态与恢复；窗口显示后立刻执行
        var vm = _services.GetRequiredService<MainViewModel>();
        _window = new MainWindow(vm);
        _window.Activate();

        // 启动后就位（断点续播）——不自动出声
        _ = vm.InitializeAsync();
    }
}
```

`appsettings.json` 的拷贝：在 WinUI csproj 里加与 WPF 壳相同的条目：

```xml
  <ItemGroup>
    <None Include="..\appsettings.json" Link="appsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

- [x] **Step 2: 主窗口（Fluent 深色 + Mica + 自绘标题栏 + 三区布局）**

> **勘误（本节两处 verbatim 片段不能工作 + 一处 Mica 前提计划没写）**：
> 1. **曲目列表绑定**：`ItemsSource="{x:Bind ViewModel.ViewedPlaylist.Queue, Mode=OneWay}"` 让 **XamlCompiler 直接崩在 MarkupCompilePass1，报 WMC9999**（`x:Bind` 走可空中间段 `ViewedPlaylist?`），错误信息不含任何可读线索，只能逐块剥离定位。交付形态改成**代码后置赋值** `TrackList.ItemsSource = vm.Queue`，并把原因写在该 XAML 现场的注释里（见 §2 对照表"曲目列表"行）。
> 2. **时间文本**：`$"{_vm.Player.Position:mm\:ss} / …"`（本节末尾 `TimeText` 那行）在插值里是 **CS1009 转义序列非法**——插值格式说明符里的 `\` 不会被 C# 编译器当转义处理。可写形式是把格式串提成常量 `@"mm\:ss"` 或用 `ToString(@"mm\:ss")`。
> 3. **Mica 的真实前提**：本节只说"`Window.SystemBackdrop = new MicaBackdrop()`"，但材质可见还需要**根 `Grid` 的 `Background="Transparent"`**——若根背景是不透明页面刷，材质整片被盖住，肉眼与截图都像"没挂上"。Task 4 一度据此误判"Mica 未生效"，重测后推翻（裁定 P-18：材质是生效的；`DWMWA_SYSTEMBACKDROP_TYPE` 对组合器挂载的 backdrop **不是有效探针**，实测恒为 0，有效判据是"透明表面后的像素是否随窗外内容变化"）。这条约定现在是 COUPLING §5 的契约行 + `MainWindow.xaml` 顶部的三行像素对照注释。

`MainWindow.xaml`（`NavigationView` 承载歌单、中间曲目列表、底部播放器栏）：

```xml
<Window
    x:Class="DPlayer.WinUI.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:muxc="using:Microsoft.UI.Xaml.Controls"
    xmlns:models="using:DPlayer.Models">

    <Grid x:Name="RootGrid" Background="{ThemeResource ApplicationPageBackgroundThemeBrush}">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <!-- 自绘标题栏（第一行交给系统拖拽区） -->
        <Grid x:Name="AppTitleBar" Grid.Row="0" Height="32">
            <TextBlock x:Name="AppTitle" Text="D-player" Margin="16,0,0,0"
                       VerticalAlignment="Center" Style="{StaticResource CaptionTextBlockStyle}" />
        </Grid>

        <muxc:NavigationView Grid.Row="1" x:Name="Nav"
                             IsBackButtonVisible="Collapsed"
                             IsSettingsVisible="False"
                             PaneDisplayMode="Left"
                             SelectionChanged="Nav_SelectionChanged">
            <ListView x:Name="TrackList"
                      ItemsSource="{x:Bind ViewModel.ViewedPlaylist.Queue, Mode=OneWay}"
                      DoubleTapped="TrackList_DoubleTapped">
                <ListView.ItemTemplate>
                    <DataTemplate x:DataType="models:Track">
                        <Grid ColumnSpacing="12" Padding="4,6">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="{x:Bind Title}" TextTrimming="CharacterEllipsis" />
                            <TextBlock Grid.Column="1" Text="{x:Bind Artist}"
                                       Foreground="{ThemeResource TextFillColorSecondaryBrush}" />
                        </Grid>
                    </DataTemplate>
                </ListView.ItemTemplate>
            </ListView>
        </muxc:NavigationView>

        <!-- 播放器栏 -->
        <Grid Grid.Row="2" Padding="16,8" ColumnSpacing="12"
              Background="{ThemeResource LayerFillColorDefaultBrush}">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <Button x:Name="PlayPauseButton" Click="PlayPause_Click"
                    AutomationProperties.Name="播放/暂停">
                <FontIcon Glyph="{x:Bind PlayPauseGlyph, Mode=OneWay}" />
            </Button>
            <StackPanel Grid.Column="1" Orientation="Vertical">
                <TextBlock Text="{x:Bind NowPlayingText, Mode=OneWay}" TextTrimming="CharacterEllipsis" />
                <Slider x:Name="PositionSlider" Minimum="0" Maximum="1"
                        Value="{x:Bind PositionFraction, Mode=OneWay}"
                        ValueChanged="Position_Changed" />
            </StackPanel>
            <TextBlock Grid.Column="2" VerticalAlignment="Center"
                       Text="{x:Bind TimeText, Mode=OneWay}" />
        </Grid>
    </Grid>
</Window>
```

`MainWindow.xaml.cs` 要点（**这些是本切片的实现核心，逐条照做**）：

```csharp
using System.ComponentModel;
using DPlayer.Models;
using DPlayer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace DPlayer.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _suppressSeek;

    public MainViewModel ViewModel => _vm;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();

        Title = "D-player";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();

        // Window 本身没有 Loaded 事件（那是 FrameworkElement 的）——挂根 Grid
        RootGrid.Loaded += (_, _) => BuildPlaylistMenu();
        _vm.Player.PropertyChanged += Player_PropertyChanged;
    }

    private void BuildPlaylistMenu()
    {
        foreach (var pl in _vm.Playlists.Playlists)
            Nav.MenuItems.Add(new NavigationViewItem { Content = pl.Name, Tag = pl });

        var first = Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault();
        if (first is not null) Nav.SelectedItem = first;   // 触发 SelectionChanged → 绑定列表
    }

    private void Nav_SelectionChanged(object sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: PlaylistViewModel pl })
        {
            _vm.Playlists.ViewedPlaylist = pl;
            Bindings.Update();   // 重新求值 x:Bind（ViewedPlaylist.Queue）
        }
    }

    private async void TrackList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (TrackList.SelectedItem is not Track track) return;
        var pl = _vm.Playlists.ViewedPlaylist;
        if (pl is null) return;
        var index = pl.Queue.IndexOf(track);
        if (index < 0) return;
        await pl.PlayIndexAsync(index);
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Player.CurrentTrack is null)
            _vm.Playlists.ViewedPlaylist?.PlayCurrentCommand.Execute(null);
        else
            _vm.Player.PlayPauseCommand.Execute(null);
    }

    private void Position_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressSeek || _vm.Player.Duration <= TimeSpan.Zero) return;
        // PlayerViewModel 的 Seek 方法本体是 private（只通过生成的命令暴露），故走命令
        _vm.Player.SeekStartedCommand.Execute(null);
        _vm.Player.SeekCompletedCommand.Execute(e.NewValue);   // 归一化 [0,1] → 服务
    }

    private void Player_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _suppressSeek = true;
        Bindings.Update();
        _suppressSeek = false;
    }

    public string PlayPauseGlyph => _vm.Player.PlayState == PlayState.Playing ? "\uE769" : "\uE768";
    public string NowPlayingText => _vm.Player.CurrentTrack is { } t
        ? $"{t.Title} — {t.Artist}" : "未在播放";
    public string TimeText => $"{_vm.Player.Position:mm\:ss} / {_vm.Player.Duration:mm\:ss}";
    public double PositionFraction => _vm.Player.Duration.TotalSeconds <= 0 ? 0
        : _vm.Player.Position.TotalSeconds / _vm.Player.Duration.TotalSeconds;
}
```

**切片期已知差异（写进提交信息，第二阶段再对齐）**：WPF 版只在拖动结束时 Seek（`IsSeeking` 抑制中间的定位）；本切片每次 `ValueChanged` 都完成一次 Seek（拖拽中会连续定位）。功能可用，手感待第二阶段打磨。

- [x] **Step 3: 补一个 Core 侧公开入口（唯一允许的 Core 改动）**

> **勘误（裁定 P-16：本节被整体推翻，这个"唯一允许的 Core 改动"最后被删掉了）**：
> - **推翻的原因不是风格，是行为**：本节新增的 `PlayIndexAsync(int)` 直进 `PlayTrackAtAsync(index)`，而 WPF 的双击经 `PlaylistView.xaml.cs:171` → `PlaylistsViewModel.HandleDoubleClickPlay` → `PlayTrackAt`，**后者开头有 `_shuffleHistory.Clear()`（把这次双击视为新会话）**。于是计划"批准"的这条新 API 恰恰在**本阶段要对比的那个手势**上造出了两壳漂移：随机模式下 WinUI 双击会持续蚕食未播池，`RepeatMode.Off` 且池耗尽时 `CalculateNextIndex` 可返回 −1 而 WPF 继续播。而且这个入口**本来就存在**——属于重复 API。
> - **交付形态**：`PlayIndexAsync` 与其测试**已删除**，WinUI 的 `TrackList_DoubleTapped` 改调 `await PlaylistsViewModel.HandleDoubleClickPlay(target, index)`，与 WPF 同一个方法；原先手工设置的 `CurrentPlaylistId` 也一并删掉（该入口内部先切指针）。核对证据：`git diff --stat 69fedad..71613f2 -- D-player.Core Views` 为**空输出**——Core 与 WPF 壳源码相对 Task 4 之前逐字节未变，Task 4 的 Core 侧改动只剩测试。
> - **留下的纪律**（已登记 COUPLING §5/§7）：跨壳的同一手势必须走 Core 里**同一个**公开入口；确实缺入口时先让两个壳都走新入口再删旧的，**并行 API 本身就是漂移源**。
> - **本节测试片段的第二处缺陷**：`var (vm, _) = CreateVm();` 解构不了——本文件的 `CreateVm()` helper 返回的是 VM 本身、不是元组，照抄即 CS8132。交付形态是直接 `var vm = CreateVm();`。
> - **Expected 的 176 作废**：本步收尾与 Task 4 结束时都是 **180**（P-19 的分解见 Global Constraints）。越界那条事实随 `PlayIndexAsync` 删除而消失（−1），换成了落在共享入口上的 `HandleDoubleClickPlay_OutOfRangeIndex_TouchesNothing`（+1，且实测过判别力：把守卫绕开就跑红），另加 1 条 DI 图解析 = 180。

切片需要"按索引播放当前查看歌单的某一首"。`PlaylistViewModel` 现有 `PlayCurrent()`（播放 `CurrentIndex`）与私有 `PlayTrackAt(int)`；新增：

```csharp
    /// <summary>按索引播放本歌单曲目（供 UI 双击列表项等直接定位播放；等价于既有 PlayCurrent 的带参版本）。</summary>
    [RelayCommand]
    public async Task PlayIndexAsync(int index)
    {
        if (index < 0 || index >= Queue.Count) return;
        await PlayTrackAtAsync(index);
    }
```

（放在 `PlayCurrent` 旁边；`[RelayCommand]` 会生成 `PlayIndexCommand`，切片只用方法本体。）为它补一条单测（`Tests/ViewModels/PlaylistViewModelTests.cs` 追加）：

```csharp
    [Fact]
    public async Task PlayIndexAsync_OutOfRange_DoesNothing()
    {
        var (vm, _) = CreateVm();          // 复用本文件既有的构造 helper
        await vm.PlayIndexAsync(99);
        Assert.Equal(-1, vm.CurrentIndex);
    }
```

若该文件没有名为 `CreateVm` 的 helper，就用文件里既有的构造方式（读文件顶部 30 行按现有模式写），**不要新造框架**。

Expected：`dotnet test D-player.slnf -c Debug -v q` → **176 通过 / 0 失败**。

- [x] **Step 4: 构建并真机运行切片**

> **勘误（裁定 P-17：整个 Task 4 漏了关闭落盘，本计划的 Step 1-3 里没有任何一条提到它）**：本节 Expected 要求"双击出声 / 断点续播"成立，但计划给的 `App.xaml.cs` 与 `MainWindow.xaml.cs` 从来没有把**最终播放位置**写盘的那一次。WPF 壳靠 `Views/MainWindow.xaml.cs:82-112` 的 cancel-and-close：`Window_Closing` 里 `await MainViewModel.CleanupAsync()` 之后再关——那才是写最终断点并释放 WASAPI 设备的地方。缺了它，验收项 ⑤ 只在"恰好撞上 30 秒节流"或"先暂停再关"时才可能成立。交付形态：WinUI 走 **`AppWindow.Closing`** → `await MainViewModel.CleanupAsync()` → `Close()`。
> **同时明令禁止**：不要在壳的关闭路径上**同步** `Dispose(ServiceProvider)`。`MainViewModel` 只实现 `IAsyncDisposable`，同步 Dispose 抛 `InvalidOperationException`（"`MainViewModel` type only implements IAsyncDisposable. Use DisposeAsync…"）——WPF `App.OnExit` 那条是**已知既有缺陷**（数据已落盘，表现为退出码非 0），刻意没有把它复制进第二壳；两处该一起修，属 Phase 20 之后的独立决策。这条现在是 COUPLING §5 的"每壳各自负责关闭落盘"契约行 + §7 的 ❌。
> **另一处计划的时序缺陷**：`SyncPlaylistMenu` 只在 `Loaded` 建一次左栏，而 `Hydrate` 还没跑完，之后新增的歌单永远不会出现在栏里——交付形态改成 **`CollectionChanged` 驱动重建**，并把选中优先级定为 `ViewedPlaylist` → 重建前已选项 → 第一项（`NavigationView` 的选中不再当权威），配 `_syncingMenu` 重入守卫。这条**没有自动化保护**（WinUI 不进门禁），只能由用户用一份多歌单的 `queue.json` 走（对比材料 §2.1 的 ②）。

```bash
dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q
dotnet run   --project D-player.WinUI/D-player.WinUI.csproj -c Debug
```

Expected：WinUI 窗口以深色 + Mica 打开，左栏列出 `%LocalAppData%\D-player-winui\queue.json` 里的歌单（首启为空则只有一个 seed 歌单），双击曲目**出声**。

- [x] **Step 5: 把 6 项验收清单交给用户**

```
① 启动即出窗口（Mica/深色生效）
② 左侧歌单与曲目列表正确显示
③ 双击曲目出声、进度条推进
④ 暂停/继续可用
⑤ 关闭后重开按 ▶ 从断点续播（数据落在 %LocalAppData%\D-player-winui\）
⑥ 干净退出，无异常
```

无法自动化的项由用户确认；未确认的如实标 NOT VERIFIED。
> **实施后的真实分布（不是全绿）**：① 启动即出窗口、⑥ 干净退出 = **已自动化取证**（深色 Fluent chrome + Mica、`UIA_TITLE=D-player`、左栏列出 seed 歌单、空状态、播放器栏 `未在播放` / `00:00 / 00:00`、关窗退出码 0 且无残留进程）；② 多歌单恢复态、③ 双击出声与进度推进、④ 暂停/继续手感、⑤ 关闭后重开按 ▶ 断点续播 = **NOT VERIFIED，交给用户**。⑤ 特别提醒：**必须在"正在播放"时关窗**才走得到 P-17 新加的关闭落盘，在 30 秒节流点或暂停后关闭会绕过它。②需要一份 ≥2 歌单且持久化 current **不是第一个**的 `queue.json`。手工测试数据步骤（在资源管理器里把 `%LocalAppData%\D-player\queue.json` 复制到 `%LocalAppData%\D-player-winui\`）写进对比材料 §2.1，并显式标注"这是测试数据、不是产品功能"；实施方全程未读写 `%LOCALAPPDATA%`（策略禁止，也是用户数据）。

- [x] **Step 6: 提交**

```bash
git add -A
git commit -m "feat(winui): build the first vertical slice of the new UI

The WinUI shell boots the shared Core (its own D-player-winui data
folder), shows the playlists in a NavigationView, the selected playlist's
tracks in a Fluent ListView, and a bottom player bar - Mica backdrop and
a custom title bar replace the WPF chrome. Playback, pause/resume and
resume-on-startup all ride on the same Core services as the WPF build.

PlaylistViewModel gains PlayIndexAsync so a list double-click can start a
specific row (the WPF double-click path went through the view); one test
covers the out-of-range guard.

Slice acceptance (six items) was walked through on the real machine:
<填入用户确认结果>"
```

---

### Task 5: 对比材料、决策门与文档同步

**Files:**
- Create: `docs/PHASE20-COMPARISON.md`
- Modify: `README.md`、`docs/PROJECT.md`、`docs/COUPLING.md`、`tools/coupling-audit/Invoke-CouplingAudit.ps1`

**Interfaces:**
- Consumes: Task 1-4 的全部产物
- Produces: 决策门材料 + 与代码一致的文档

- [x] **Step 1: 写对比材料**

`docs/PHASE20-COMPARISON.md`：两张表——**功能等价核对表**（行：启动与窗口 / 歌单导航 / 曲目列表 / 播放与暂停 / 进度显示 / 断点续播 / 排序；列：WPF、WinUI、备注；明显缺失项（频谱/拖拽/EQ/设置/导入导出）单列一节标"本阶段不在 WinUI 侧"）与**六维评分表**（视觉观感 / 操作手感 / 性能 / 开发体验 / 维护与演进成本 / 生态与可扩展性；每维 1-5 分 + 备注）。表留空交用户填，并在文首写明"填完把结论告诉我，我记进文档与项目记忆"。

- [x] **Step 2: 文档同步**

> **勘误（本步的三处计数与一条脚本指令）**：
> - 两处"测试计数改 **176**"（下面 `README.md` 与 `docs/PROJECT.md` 两条）都作废，实际是 **180**（P-19）。README 那句"当前共 N 个单元测试"连同它的覆盖面描述一起重写：现在有 `Tests/Configuration/`（`DPlayerDataPathsTests`）与 `Tests/Extensions/AddDPlayerCoreTests.cs`（DI 图解析），而 `PlaylistImportReportFormatter` 在 Task 1 已从 `Views/Controls` 搬进 `D-player.Core/ViewModels`，**不再是"View 层纯字符串函数"那个例外**。
> - 本步只让脚本"扫 `D-player.Core\<层>` 与壳目录"，**漏了第二处**：第 121 行 `Join-Path $RepoRoot 'Extensions\ServiceCollectionExtensions.cs'` 指向的文件在 Task 1 就搬到了 `D-player.Core/Extensions/`。按本步的片段改完，M6 会读一个不存在的路径、**静默检查不到任何注册**（绿色但失明）。裁定 P-5 要求两处一起修：注册表路径先在 Core 根下解析、回落到仓库根，两处都没有时**显式抛**。（实测：不修时脚本连结果都出不来——M2 的 BFS 对未被扫描到的命名空间取 key，`ContainsKey(null)` 抛。）
> - `docs/COUPLING.md` 的 §5/§7 除了本步列的四条新契约/四个 ❌，还必须补**实施真正造出来的约定**：同一手势走同一个 Core 入口（不留并行 API，P-16）、Mica 依赖根 `Grid Background="Transparent"`（且 `DWMWA_SYSTEMBACKDROP_TYPE` 不是判据，P-18）、每壳各自负责关闭落盘且不得同步 Dispose 容器（P-17）、根级新工程必须进 `D-player.csproj` 的 glob 排除集（P-1）。
> - 本步还连带一件 spec 未列的事：`D-player.Core/ViewModels/PlaylistImportReport.cs:6` 的注释仍说报告文案由"**View 层**"格式化器组装（Task 1 已把它搬进 Core），Task 5 一并修这一行源码注释。

- `README.md`：项目结构树补 `D-player.Core/`、`D-player.WinUI/`、`D-player.slnf`；**构建/测试命令改 `dotnet build|test D-player.slnf -c Debug`**；补 WinUI 壳启动命令；阶段表加 Phase 20 行；测试计数改 **176**。
- `docs/PROJECT.md`：§8 构建/测试命令改 `.slnf` 并解释筛选器用途（WinUI 不进主门禁）；工程结构、依赖清单、数据目录（`D-player` / `D-player-winui`）、Phase 20 状态；测试计数 176；`SortedView` 相关描述改为"列表直接绑 `Queue`，排序由 `SortBy` 物理重排"。
- `docs/COUPLING.md`：§5 新增契约行（Core 必须 WPF-free；两壳共享一份 VM/Service；数据目录由壳注入；主门禁走 `D-player.slnf`）；§7 新增 ❌（Core 里引 WPF；给 WinUI 做 UmaPlayer 迁移；两壳共用数据目录；把 WinUI 加进 `D-player.slnf`）。
- `tools/coupling-audit/Invoke-CouplingAudit.ps1`：`$layerDirs` 现在要扫 `D-player.Core\<层>` 与壳目录——最小改法是把层根参数化：

```powershell
$layerDirs = @('Models','Services','ViewModels','Views','Configuration','Extensions','Converters')
$scanRoots = @((Join-Path $RepoRoot 'D-player.Core'), $RepoRoot)
...
foreach ($root in $scanRoots) {
    foreach ($d in $layerDirs) {
        $p = Join-Path $root $d
        if (Test-Path $p) { $files += @(Get-ChildItem -Path $p -Filter *.cs -Recurse -File) }
    }
}
```

改完运行 `powershell -File tools/coupling-audit/Invoke-CouplingAudit.ps1`，确认仍能输出层依赖图且无新增违规（脚本是只读审计）。

- [x] **Step 3: 全量门禁复核**

> **勘误 + 实测**：Expected 里的"**176 通过** / 0 失败"作废，三扇门的真实收尾（Task 5 复跑，逐字输出见 `task-5-report.md` §6）是：`dotnet build D-player.slnf -c Debug --nologo -v q` → **已成功生成 / 0 个警告 / 0 个错误**；`dotnet test D-player.slnf -c Debug -v q`（**不带 `--nologo`**）→ **总计 180 / 失败 0 / 成功 180 / 已跳过 0**；`dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q` → **0 / 0**（裸命令，靠 P-15 钉进 csproj 的 `<Platform>x64</Platform>`）。

```bash
dotnet build D-player.slnf -c Debug --nologo -v q
dotnet test  D-player.slnf -c Debug -v q
dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q
```

Expected：0 警告；**176 通过 / 0 失败**；WinUI 单独构建通过。

- [x] **Step 4: 提交并交给用户**

> **勘误（实施纪律）**：本步给的是一次 `git add -A` 的大提交。Task 5 实际按"小而诚实"拆开落地——文档同步 / 审计脚本 / 对比材料 / 计划标注 + 源码注释各一次，理由：草稿是上一名实施者留下的**未验证**工作，把它和验证结果混在一个提交里会让"哪些是我核过的"无法回看。提交信息同样只写实际确认过的部分（P-9 的纪律）。

```bash
git add -A
git commit -m "docs: record the two-shell layout, the new gates and the comparison sheet

README/PROJECT/COUPLING now describe D-player.Core, the WinUI shell, the
per-shell data folders and the switch of the main gates to D-player.slnf;
the coupling audit script learns to scan the Core project as well. The
comparison sheet (feature parity + six subjective dimensions) is added
for the user to fill in before the continue-or-stop decision."
```

- [ ] **Step 5: 决策门（用户填写后）**

> **状态（截至 Task 5 收口）**：**未发生**，因此本条刻意保持未勾。用户尚未填 `docs/PHASE20-COMPARISON.md` 的六维评分表、也尚未给出"续投 / 停止"的结论；该文件的 §2.1 四项（② 多歌单恢复态、③ 双击出声与进度推进、④ 暂停/继续手感、⑤ 播放中关窗后重开续播）同样待用户真机确认。本步的任何"已选定 WinUI / 已决定停止"表述都是错的。

用户填完 `docs/PHASE20-COMPARISON.md` 并给出结论后：把结论（续投/停止 + 理由摘要）写入该文件末尾与 `docs/PROJECT.md` 的阶段状态，提交一个 `docs:` 提交，并把结论同步进项目记忆（阶段 20 的范围记忆文件）。**若结论是"停止"：本计划的 Task 1-2（Core 抽取 + 数据目录参数化）建议保留**（它们对 WPF 版零行为影响、且让未来任何新壳都更省事），WinUI 工程的去留由用户另行决定。

---

## Self-Review 记录

**1. Spec 覆盖**

| spec 章节 | 实现于 |
|---|---|
| §4.1 工程与切分 | Task 1（Core 建工程、搬家、DI 拆分、Tests 改引用、sln/slnf） |
| §4.1.1 解决方案与门禁隔离 | Task 1 Step 8-9；Task 3 Step 4（验证 WinUI 不进主门禁） |
| §4.2 两处实质改动（`SortedView` / `Win32FileDialogService`） | Task 1 Step 2、5、6 |
| §4.3 数据目录（`DPlayerDataPaths`）与线程契约 | Task 2；Task 4 Step 1（UI 线程构造容器） |
| §4.4 新视觉方向（Mica / 自绘标题栏 / NavigationView / ListView / 播放器栏） | Task 4 Step 2 |
| §4.5 测试与验收（门禁 + 6 项真机清单） | Task 1 Step 9、Task 4 Step 4-5 |
| §4.6 对比材料与决策门（两表 + 结论记录） | Task 5 Step 1、5 |
| §4.7 步骤划分 20-1…20-4 | Task 1-2 = 20-1；Task 3 = 20-2；Task 4 = 20-3；Task 5 = 20-4 |
| §5 文档与契约同步 | Task 5 Step 2（含 coupling-audit 脚本——spec 未列，实现时发现的连带项） |
| §6 验收标准 1-5 | 各 Task 的 Expected 与 Step 5 用户确认 |
| §7 风险（WASDK 兼容 / 门禁 / 无 Adorner / 频谱 / Core 提取 / 两壳漂移） | Task 3 Step 2 阶梯、Task 1 Step 11 冒烟、Task 5 Step 2 契约登记；Adorner 与频谱属第二阶段（spec Non-Goals） |

**2. 占位符扫描**：无 TBD/TODO；两处 `<填入…>` 是**有意的实测填充位**（Task 3 Step 5 的 WASDK 版本组合、Task 4 Step 6 的用户确认结果），两处都写明"实测/确认后填入"，不得照抄。Task 4 Step 3 提到"若该文件没有 `CreateVm` helper 就用文件既有构造方式"——这是给实施者的判断余地，不是缺内容。

**3. 类型一致性**：`DPlayerDataPaths(string Root, string FolderName)` 在 Task 2 定义、Task 4 的 `new DPlayerDataPaths { FolderName = "D-player-winui" }` 使用；`AddDPlayerCore(IConfiguration, DPlayerDataPaths)` 在 Task 2 定型、Task 4 按此调用；`PlayIndexAsync(int)` 在 Task 4 Step 3 定义并使用（**勘误：这条"类型一致性"后来被裁定 P-16 推翻并删除——两壳改走既有公开入口 `PlaylistsViewModel.HandleDoubleClickPlay(target, index)`，Core 相对 Task 4 之前逐字节未变**）；`MainViewModel` 的 `Playlists`/`Player` 属性、`PlaylistsViewModel.ViewedPlaylist`、`PlayerViewModel.PlayPauseCommand`/`SeekCompleted`/`PlayState`/`Position`/`Duration`/`CurrentTrack` 均为**既有公开成员**（`SeekCompleted` 与 `PlayPauseCommand` 由 `[RelayCommand]` 生成），Task 4 只读不改。
