# Phase 20：WinUI 3 第二套 UI（切片 + 决策门）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 抽出 WPF-free 的 `D-player.Core`，用它驱动一个全新的 WinUI 3 壳，做出"主窗口 + 歌单导航 + 曲目列表 + 播放器栏 + 真机播放/断点续播"的第一条纵向切片，并把对比材料交给用户拍板去留。

**Architecture:** 共享层（Models/Services/ViewModels/Configuration）搬进类库 `D-player.Core`（`net10.0-windows`，不开 `UseWPF`）；WPF 壳与 WinUI 壳各自引用它、各自注册自己的 UI 服务。主门禁走解决方案筛选器 `D-player.slnf`（只含 Core+WPF+Tests），WinUI 工程不进主门禁、只单独构建。数据目录由壳注入（`D-player` vs `D-player-winui`）。

**Tech Stack:** .NET 10（`net10.0-windows` / `net10.0-windows10.0.19041.0`）· WPF（既有壳）· WinUI 3 / Windows App SDK（unpackaged + self-contained）· CommunityToolkit.Mvvm · NAudio 3.1.0 · xunit.v3 + MTP

**Spec:** [`docs/superpowers/specs/2026-10-06-d-player-phase20-winui-shell-design.md`](../specs/2026-10-06-d-player-phase20-winui-shell-design.md)

**验证基线：** 起点 `92b8412`（工作树干净）；当前 **172 测试全绿 / 构建 0 警告**。每个 Task 结束都以"0 警告 + 172（或更多）测试绿"收尾。

## Global Constraints

- **门禁命令**：`dotnet build D-player.slnf -c Debug --nologo -v q` → **0 错误 0 警告**；`dotnet test D-player.slnf -c Debug -v q` → **172 通过 / 0 失败**（Task 2 之后为 **174**）。**`dotnet test` 一律不加 `--nologo`**（加了会静默跑 0 条并打印「成功: 0」；`--nologo` 只能用在 `dotnet build` 上）。
- **Core 必须 WPF-free**：`D-player.Core/` 下不得出现 `System.Windows.*` / `PresentationFramework` / `ICollectionView` / `CollectionViewSource`。可 grep 验证：`grep -rn "System.Windows\|CollectionViewSource" D-player.Core/` 无命中。
- **WPF 版行为与视觉零变化**：Task 1-2 只做搬迁与依赖注入改造，不改任何业务逻辑、不改 XAML 外观、不动播放链与并发不变量（Phase 19 契约）。
- **WinUI 工程不得进入主门禁**：`D-player.slnf` 只含 `D-player.Core`、`D-player`、`Tests/D-player.Tests`；WinUI 只以 csproj 单独构建。
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

- [ ] **Step 1: 建 Core 工程文件**

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

- [ ] **Step 2: 用 `git mv` 搬迁（保留历史）**

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

- [ ] **Step 3: 改 WPF 壳的 csproj**

`D-player.csproj`：加 `<ProjectReference Include="D-player.Core\D-player.Core.csproj" />`；从 `PackageReference` 移除 `NAudio.Core`、`NAudio.Wasapi`、`CommunityToolkit.Mvvm`、`z440.atl.core`（已随 Core 传递）；保留 `Microsoft.Extensions.DependencyInjection` 与 `Microsoft.Extensions.Configuration.Json`（`App.xaml.cs` 直接使用）。保留原有 `Compile Remove="Tests/**"` 等排除项与 `appsettings.json` 拷贝项。

- [ ] **Step 4: 拆 DI 注册**

`D-player.Core/Extensions/ServiceCollectionExtensions.cs`：方法改名 `AddDPlayerCore`，**删除** `services.AddSingleton<IFileDialogService, Win32FileDialogService>();` 一行，其余（配置绑定、全部服务、三个 VM 与工厂）原样保留。文件头注释补一句"UI 相关服务（文件对话框等）由各壳自行注册"。

`App.xaml.cs`：`services.AddDPlayerServices(configuration)` → `services.AddDPlayerCore(configuration)` 后紧跟 `services.AddSingleton<IFileDialogService, Win32FileDialogService>();`。

- [ ] **Step 5: 清掉排序视图的 WPF 依赖**

`D-player.Core/ViewModels/PlaylistViewModel.cs`：删除 `using System.Windows.Data;`（第 5 行）、删除 `public ICollectionView SortedView { get; private set; }`（第 65 行）与 `SortedView = CollectionViewSource.GetDefaultView(Queue);`（第 134 行）及 `SortBy` 末尾的 `OnPropertyChanged(nameof(SortedView));`（第 189 行）。`SortBy` 的物理重排与 `CurrentIndex` 重映射**逐行保留**。

`Views/Controls/PlaylistView.xaml:123`：`ItemsSource="{Binding SortedView}"` → `ItemsSource="{Binding Queue}"`。

先全仓确认无其他消费点：`grep -rn "SortedView" --include=*.cs --include=*.xaml . | grep -v "/obj/\|/bin/"` → 只应命中上面这些行。

- [ ] **Step 6: 改命名空间与引用（formatter）**

- `D-player.Core/ViewModels/PlaylistImportReportFormatter.cs`：`namespace DPlayer.Views.Controls;` → `namespace DPlayer.ViewModels;`；文件内 `using DPlayer.ViewModels;` 删除。
- `Views/Controls/PlaylistImportUi.cs`：确保有 `using DPlayer.ViewModels;`（原本已有则可直接用）。
- `Tests/ViewModels/PlaylistImportReportFormatterTests.cs`：`using DPlayer.Views.Controls;` → `using DPlayer.ViewModels;`（命名空间声明 `DPlayer.Tests.Views` → `DPlayer.Tests.ViewModels` 一并改，保持一致）。

- [ ] **Step 7: 改测试工程的引用**

`Tests/D-player.Tests.csproj`：`<ProjectReference Include="..\D-player.csproj" />` → `<ProjectReference Include="..\D-player.Core\D-player.Core.csproj" />`。其余属性（`OutputType=Exe`、MTP 两属性、`UseWPF`、包引用）保持不变。

- [ ] **Step 8: 建解决方案筛选器并挂进 sln**

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

- [ ] **Step 9: 构建 + 全量测试**

```bash
dotnet build D-player.slnf -c Debug --nologo -v q
dotnet test  D-player.slnf -c Debug -v q
```

Expected：构建 **0 错误 0 警告**；测试 **总计 172 / 失败 0**。

- [ ] **Step 10: 验证 Core 确实 WPF-free**

```bash
grep -rn "System.Windows\|CollectionViewSource\|PresentationFramework" D-player.Core/ --include=*.cs | grep -v "/obj/\|/bin/"
```

Expected：**无输出**（`PlayerViewModel.cs` / `Models/Track.cs` 里提到 `BitmapImage` 的只是注释，若 grep 命中注释行，人工确认后放行并在报告里写明）。

- [ ] **Step 11: WPF 版真机冒烟（行为零变化的证据）**

```bash
dotnet run --project D-player.csproj -c Debug
```

逐项确认：① 窗口起来、深色主题与自绘标题栏正常 ② 歌单列表显示 ③ 双击播放出声 ④ 列表表头点击排序仍生效（`SortBy` 改动的唯一可感点）⑤ 关闭再开，断点续播仍在。无法自动化的项如实标 NOT VERIFIED 交用户。

- [ ] **Step 12: 提交**

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

- [ ] **Step 1: 写失败测试**

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

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test D-player.slnf -c Debug -v q --filter "FullyQualifiedName~DPlayerDataPathsTests"`
Expected: 编译失败（`DPlayerDataPaths` 不存在）。

- [ ] **Step 3: 实现 `DPlayerDataPaths`**

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

- [ ] **Step 4: 三个持久化点改为注入**

- `JsonSettingsPersistence`：删无参构造器，新增 `public JsonSettingsPersistence(DPlayerDataPaths paths)`，内部 `var dir = paths.Directory; Directory.CreateDirectory(dir); _path = Path.Combine(dir, "settings.json");`。类注释里 `%LocalAppData%\D-player\settings.json` 改为"由 `DPlayerDataPaths` 决定（WPF 壳为 `%LocalAppData%\D-player\`）"。
- `JsonPlaylistService`：同上，文件名 `queue.json`。
- `JsonLibraryCache`：删 `public JsonLibraryCache()` 与 `internal JsonLibraryCache(string? overrideDir)`，改为单一 `public JsonLibraryCache(DPlayerDataPaths paths)`（`library-cache.json`）。
- `LegacyDataMigration`：`MigrateIfNeeded()` → `MigrateIfNeeded(DPlayerDataPaths paths)`；目标目录用 `paths.Directory`，旧目录 `OldFolderName = "UmaPlayer"` 不变。类注释注明"仅 WPF 壳调用；WinUI 壳从空目录开始"。

- [ ] **Step 5: DI 与壳的接线**

- `AddDPlayerCore(this IServiceCollection services, IConfiguration configuration, DPlayerDataPaths dataPaths)`：方法体开头 `services.AddSingleton(dataPaths);`，其余不动。
- `App.xaml.cs`：`services.AddDPlayerCore(configuration, new DPlayerDataPaths { FolderName = "D-player" });`；`LegacyDataMigration.MigrateIfNeeded()` → `MigrateIfNeeded(new DPlayerDataPaths { FolderName = "D-player" })`（保持"在 DI 构造持久化服务之前执行"的顺序）。

- [ ] **Step 6: 修测试构造**

`Tests/Services/JsonLibraryCacheTests.cs`：把该文件里**所有** `new JsonLibraryCache(_tempDir)` 改成 `new JsonLibraryCache(new DPlayerDataPaths { Root = _tempDir })`（用编辑器的替换功能一次改完，不要手工数个数）；补 `using DPlayer.Configuration;`。

- [ ] **Step 7: 全量测试**

```bash
dotnet build D-player.slnf -c Debug --nologo -v q
dotnet test  D-player.slnf -c Debug -v q
```

Expected：0 警告；**总计 175 / 失败 0**（172 + 新增 3）。

- [ ] **Step 8: 目视确认 WPF 数据没搬家**

启动 WPF 版 → 歌单/设置仍在（读的仍是 `%LocalAppData%\D-player\`）→ 关闭应用，确认没有新建 `%LocalAppData%\D-player\D-player\` 之类的嵌套目录（`Directory` 组合错误会立刻表现为空歌单）。

- [ ] **Step 9: 提交**

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

- [ ] **Step 1: 建工程与最小窗口**

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

- [ ] **Step 2: restore + 构建（工具链验证，允许按阶梯重试）**

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

- [ ] **Step 3: 真机起窗口**

```bash
dotnet run --project D-player.WinUI/D-player.WinUI.csproj -c Debug
```

Expected：出现一个 WinUI 窗口，标题栏与内容区显示 `D-player WinUI`；关闭窗口后进程退出。**记录实际 WASDK 版本**（`dotnet list D-player.WinUI/D-player.WinUI.csproj package`）。

- [ ] **Step 4: 挂进 sln（但不进 slnf）**

```bash
dotnet sln D-player.sln add D-player.WinUI/D-player.WinUI.csproj
dotnet build D-player.slnf -c Debug --nologo -v q
dotnet test  D-player.slnf -c Debug -v q
```

Expected：sln 里能看到 WinUI 工程；**门禁结果不变**（0 警告 / 175 通过）——证明隔离生效。

- [ ] **Step 5: 提交**

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

- [ ] **Step 1: 引用 Core 与 DI 引导**

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

- [ ] **Step 2: 主窗口（Fluent 深色 + Mica + 自绘标题栏 + 三区布局）**

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

- [ ] **Step 3: 补一个 Core 侧公开入口（唯一允许的 Core 改动）**

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

- [ ] **Step 4: 构建并真机运行切片**

```bash
dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q
dotnet run   --project D-player.WinUI/D-player.WinUI.csproj -c Debug
```

Expected：WinUI 窗口以深色 + Mica 打开，左栏列出 `%LocalAppData%\D-player-winui\queue.json` 里的歌单（首启为空则只有一个 seed 歌单），双击曲目**出声**。

- [ ] **Step 5: 把 6 项验收清单交给用户**

```
① 启动即出窗口（Mica/深色生效）
② 左侧歌单与曲目列表正确显示
③ 双击曲目出声、进度条推进
④ 暂停/继续可用
⑤ 关闭后重开按 ▶ 从断点续播（数据落在 %LocalAppData%\D-player-winui\）
⑥ 干净退出，无异常
```

无法自动化的项由用户确认；未确认的如实标 NOT VERIFIED。

- [ ] **Step 6: 提交**

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

- [ ] **Step 1: 写对比材料**

`docs/PHASE20-COMPARISON.md`：两张表——**功能等价核对表**（行：启动与窗口 / 歌单导航 / 曲目列表 / 播放与暂停 / 进度显示 / 断点续播 / 排序；列：WPF、WinUI、备注；明显缺失项（频谱/拖拽/EQ/设置/导入导出）单列一节标"本阶段不在 WinUI 侧"）与**六维评分表**（视觉观感 / 操作手感 / 性能 / 开发体验 / 维护与演进成本 / 生态与可扩展性；每维 1-5 分 + 备注）。表留空交用户填，并在文首写明"填完把结论告诉我，我记进文档与项目记忆"。

- [ ] **Step 2: 文档同步**

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

- [ ] **Step 3: 全量门禁复核**

```bash
dotnet build D-player.slnf -c Debug --nologo -v q
dotnet test  D-player.slnf -c Debug -v q
dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q
```

Expected：0 警告；**176 通过 / 0 失败**；WinUI 单独构建通过。

- [ ] **Step 4: 提交并交给用户**

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

**3. 类型一致性**：`DPlayerDataPaths(string Root, string FolderName)` 在 Task 2 定义、Task 4 的 `new DPlayerDataPaths { FolderName = "D-player-winui" }` 使用；`AddDPlayerCore(IConfiguration, DPlayerDataPaths)` 在 Task 2 定型、Task 4 按此调用；`PlayIndexAsync(int)` 在 Task 4 Step 3 定义并使用；`MainViewModel` 的 `Playlists`/`Player` 属性、`PlaylistsViewModel.ViewedPlaylist`、`PlayerViewModel.PlayPauseCommand`/`SeekCompleted`/`PlayState`/`Position`/`Duration`/`CurrentTrack` 均为**既有公开成员**（`SeekCompleted` 与 `PlayPauseCommand` 由 `[RelayCommand]` 生成），Task 4 只读不改。
