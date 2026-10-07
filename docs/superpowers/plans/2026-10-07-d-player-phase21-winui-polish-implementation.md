# Phase 21：WinUI 壳视觉与交互打磨 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 `D-player.WinUI` 从"能用的切片"打磨成"像样的播放器"——三栏 IA、令牌化视觉、六态齐备、键盘与无障碍补齐、9 项动效落地；**不加任何功能**。

**Architecture:** 全部改动落在 WinUI 壳内。`MainWindow` 从 418 行的"五职责文件"拆成窗口壳 + 四个 UserControl（NavRail / TrackList / InfoPanel / PlayerBar），左栏改声明式；新增 `Theme/Tokens.xaml`（间距/圆角/字号）与 `Theme/Styles.xaml`（六态容器模板）；动效时长以 C# 常量表达（WinUI 的 XAML 没有可靠的 TimeSpan 资源）；封面从 Core 的 `Track.AlbumArtBytes` 在壳内解码为 `BitmapImage`。`D-player.Core` 与 WPF 壳**一个字节不改**。

**Tech Stack:** .NET 10（`net10.0-windows10.0.19041.0`）· WinUI 3 / Windows App SDK **2.5.1**（unpackaged + self-contained，x64）· CommunityToolkit.Mvvm（经 Core）· CommunityToolkit.WinUI（本阶段新引入，版本实测钉死）· xunit.v3（不新增测试）

**Spec:** [`docs/superpowers/specs/2026-10-07-d-player-phase21-winui-polish-design.md`](../specs/2026-10-07-d-player-phase21-winui-polish-design.md)

**验证基线：** 起点 `a49727a`（master，工作树干净）；`.slnf` 门禁 **180 测试绿 / 0 警告**。

## Global Constraints

- **硬边界：`D-player.Core` 与 WPF 壳零改动。** 每个 Task 结束都要跑：
  `git diff --stat a49727a..HEAD -- D-player.Core D-player.csproj Views Converters Themes Models Services ViewModels Configuration Extensions appsettings.json` → **空**。
- **门禁三件套**：`dotnet build D-player.slnf -c Debug --nologo -v q`（0 错误 0 警告）· `dotnet test D-player.slnf -c Debug -v q`（**180 通过 / 0 失败**，**`dotnet test` 一律不加 `--nologo`**——加了会静默跑 0 条并打印「成功: 0」）· `dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q`（0/0）。整体用 `powershell -File tools/verify-gates.ps1 -Full` 复核。
- **WinUI 不在任何门禁里**：XAML 编译、左栏逻辑、关闭 flush、模板部件挂接都没有自动回归——**每个 Task 必须真机起窗一次**（`dotnet run --project D-player.WinUI/D-player.WinUI.csproj -c Debug`）并把观察结果写进报告；无法自动化的项如实标 `NOT VERIFIED` 交用户，**绝不假装通过**。
- **不改行为**：Phase 20 已验收的行为（双击播放、拖动/单击定位、暂停继续、播放中关窗断点续播、退出码 0）是本阶段的**回归门槛**，任何一条退化都要停下修。
- **非目标（写死，不许顺手做）**：表头排序、列表 ▶ 当前曲标记、频谱、拖拽、文件/文件夹对话框、EQ、设置、导入导出、音量、上一首/下一首、随机/循环、窗口几何持久化、折叠状态持久化、崩溃/日志出口、MSIX。
- **设计令牌**：间距只用 `4/8/12/16/24`；圆角控件 `4` / 列表项与卡片 `8` / 面板 `12`；字号 `12/13.5/15/20`；动效时长只用 `100/150/200ms` 三档，任何动画 ≤250ms、无循环。
- **颜色一律引用 Fluent 主题资源**（`SubtleFillColorSecondaryBrush`、`TextFillColorSecondaryBrush` 等）；壳内不出现写死十六进制色值（`#RRGGBB`）。**注意**：`MainWindow.xaml` 顶部那段 Mica 取证注释里有 `#202020` 一类字样，属既有记录文本，不受此约束，但**新写的样式代码里不许有**。
- 行尾纪律：仓库 `core.autocrlf=true`；**Edit 工具会把 CRLF 静默转 LF** → 改完 `git ls-files --eol <file>` 必须是 `i/lf    w/crlf`，否则 `unix2dos <file>`；**禁用 `sed -i`**。
- PowerShell 一律纯 ASCII；`dotnet test` 不许带 `--nologo`。
- **不自动推送**：每次推送单独等用户发话。工作直接在 `master` 上做（用户既定习惯）。**不读写 `%LOCALAPPDATA%\D-player-winui\*`**（安全分类器拦截、且是用户数据）——需要落盘证据时写 `$TEMP`。
- 提交风格 `type(scope): subject` + 要点式 body；每个 Task 至少一次提交。
- 任何**观察类断言**（"部件找到了""落点对了""动效时长是 150ms"）都要给出取证方式（UIA 树 / `$TEMP` 探针文件 / 像素读数），没量到就写 NOT VERIFIED。

---

## 文件结构

| 路径 | 责任 | Task |
|---|---|---|
| `D-player.WinUI/Theme/Tokens.xaml`（新） | 间距 / 圆角 / 字号 三类 `x:Double` 令牌 | 1 |
| `D-player.WinUI/Theme/Motion.cs`（新） | 动效时长与缓动的 C# 常量 + `Duration` 帮助方法 | 1 |
| `D-player.WinUI/Theme/Styles.xaml`（新） | 六态容器模板：NavRail 项、TrackList 行、按钮、滑块、滚动条 | 1（骨架）/ 3（六态） |
| `D-player.WinUI/App.xaml`（改） | 合并上述资源字典 | 1 |
| `D-player.WinUI/Views/NavRail.xaml(.cs)`（新） | 左导航：声明式歌单列表 + 折叠 | 2 |
| `D-player.WinUI/Views/TrackList.xaml(.cs)`（新） | 中列表：静态表头 + 行 + 空态 + 滚动条 | 2 |
| `D-player.WinUI/Views/InfoPanel.xaml(.cs)`（新） | 右信息栏：封面/元数据 + 折叠 + 自动收起 | 2 |
| `D-player.WinUI/Views/PlayerBar.xaml(.cs)`（新） | 底栏：▶/⏸ + 进度 + 时间（含部件挂接与三路 Seek） | 2 |
| `D-player.WinUI/MainWindow.xaml(.cs)`（改） | 窗口壳：标题栏、Mica、关闭 flush、装配四控件、键盘与阈值 | 2-4 |
| `D-player.WinUI/D-player.WinUI.csproj`（改） | 加 `CommunityToolkit.WinUI`（版本实测后钉死） | 1 |
| `README.md` / `docs/PROJECT.md` / `docs/COUPLING.md`（改） | WinUI 视觉规范、折叠/阈值契约、清单交接 | 6 |

---

### Task 1: 底座（令牌层 + CommunityToolkit 探针 + 拆四个 UserControl 骨架）

**Files:**
- Create: `D-player.WinUI/Theme/Tokens.xaml`、`D-player.WinUI/Theme/Motion.cs`、`D-player.WinUI/Theme/Styles.xaml`
- Create: `D-player.WinUI/Views/NavRail.xaml(.cs)`、`Views/TrackList.xaml(.cs)`、`Views/InfoPanel.xaml(.cs)`、`Views/PlayerBar.xaml(.cs)`
- Modify: `D-player.WinUI/App.xaml`、`MainWindow.xaml`、`MainWindow.xaml.cs`、`D-player.WinUI.csproj`

**Interfaces:**
- Consumes: Core 既有公开面——`MainViewModel.Player`/`Playlists`、`PlaylistsViewModel.Playlists`(ObservableCollection<PlaylistViewModel>)/`ViewedPlaylist`、`PlaylistViewModel.Queue`/`CurrentIndex`、`PlayerViewModel.PlayState`/`CurrentTrack`/`Position`/`Duration`/`PositionNormalized`/`IsSeeking`/`PlayPauseCommand`/`SeekStartedCommand`/`SeekCompletedCommand`。
- Produces: 四个 UserControl 的公开构造与属性——`NavRail(PlaylistsViewModel)`、`TrackList(PlaylistsViewModel)`、`InfoPanel(PlayerViewModel)`、`PlayerBar(PlayerViewModel)`；`Theme/Motion.cs` 的 `MotionTokens.Fast/Normal/Slow`（`TimeSpan`）与 `MotionTokens.StandardEasing`；`App.xaml` 已合并 `Tokens.xaml`/`Styles.xaml`。

- [ ] **Step 1: 探 `CommunityToolkit.WinUI` 与 WASDK 2.5.1 的兼容性，并钉版本**

```bash
dotnet add D-player.WinUI/D-player.WinUI.csproj package CommunityToolkit.WinUI
dotnet restore D-player.WinUI/D-player.WinUI.csproj
dotnet list D-player.WinUI/D-player.WinUI.csproj package | grep -i communitytoolkit
```

把 csproj 里的浮动版本改成解析出的**确切版本**（与 WASDK 的 P-7 同纪律），并在 csproj 里加一行注释说明是本机实测钉的。

**探针（必须实测，不许假设 API 名字）**：**不要凭记忆写 toolkit 的类型名**。用"解析出的引用路径 + PowerShell 反射"两步拿到真实类型面，全程不写一行 toolkit 的 C# 代码：

```bash
# 1) 让 MSBuild 把已解析的引用路径打出来，定位 CommunityToolkit 的程序集文件
dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v d 2>&1 \
  | grep -i "communitytoolkit" | grep -i "\.dll" | head -20
```

```powershell
# 2) 对上一步找到的每个 dll 反射列类型（纯 ASCII 输出）
$dll = '<上一步得到的 dll 绝对路径>'
$a = [Reflection.Assembly]::LoadFrom($dll)
try { $t = $a.GetExportedTypes() } catch { $t = $a.GetTypes() }
$t | Where-Object { $_.FullName -match 'Animat|Fade|Offset|Scale|Implicit' } |
  ForEach-Object { $_.FullName } | Sort-Object | Out-File -Encoding ascii $env:TEMP\winui-anim-surface.txt
```

**把该文件前 40 行原样粘进报告**——Task 5 只能按这份实测清单写 toolkit 相关代码。**若反射也拿不到类型面**（缺依赖、`BadImageFormatException` 等）：**本阶段不使用 toolkit**，9 项动效全部走 Storyboard，并在报告结论段写明原因。**在未拿到实测类型面之前，任何 Task 都不许写 toolkit 类型的代码。**

- [ ] **Step 2: 写 `Theme/Tokens.xaml`**

```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- 间距（只用 4 的倍数，实际取值 4/8/12/16/24） -->
    <x:Double x:Key="SpacingXs">4</x:Double>
    <x:Double x:Key="SpacingSm">8</x:Double>
    <x:Double x:Key="SpacingMd">12</x:Double>
    <x:Double x:Key="SpacingLg">16</x:Double>
    <x:Double x:Key="SpacingXl">24</x:Double>

    <!-- 圆角：控件 4 / 列表项与卡片 8 / 面板 12 -->
    <CornerRadius x:Key="RadiusControl">4</CornerRadius>
    <CornerRadius x:Key="RadiusItem">8</CornerRadius>
    <CornerRadius x:Key="RadiusPanel">12</CornerRadius>

    <!-- 字号：Caption 12 / Body 13.5 / Subtitle 15 / Title 20 -->
    <x:Double x:Key="FontSizeCaption">12</x:Double>
    <x:Double x:Key="FontSizeBody">13.5</x:Double>
    <x:Double x:Key="FontSizeSubtitle">15</x:Double>
    <x:Double x:Key="FontSizeTitle">20</x:Double>

    <!-- 结构尺寸（三栏与底栏；改动集中在这里） -->
    <x:Double x:Key="NavRailWidth">200</x:Double>
    <x:Double x:Key="NavRailCollapsedWidth">48</x:Double>
    <x:Double x:Key="InfoPanelWidth">260</x:Double>
    <x:Double x:Key="PlayerBarHeight">64</x:Double>
    <x:Double x:Key="TitleBarHeight">32</x:Double>
    <x:Double x:Key="RowHeight">36</x:Double>
    <!-- 自动收起阈值（spec §10：起始值，集中在此便于调） -->
    <x:Double x:Key="InfoPanelAutoCollapseWidth">960</x:Double>
</ResourceDictionary>
```

> 动效时长**不放在这里**：WinUI 的 XAML 没有可靠的 `TimeSpan` 资源类型，Storyboard 的 `Duration` 从字符串资源转换会失败（Phase 21 实施时若发现可行可反悔，先按 C# 常量走）。

- [ ] **Step 3: 写 `Theme/Motion.cs`**

```csharp
namespace DPlayer.WinUI.Theme;

/// <summary>
/// 动效令牌：只有三档时长（spec §4），任何动画 ≤250ms 且一次性、无循环。
/// 用 C# 常量而非 XAML 资源——WinUI 的 XAML 没有可靠的 TimeSpan 资源类型，
/// Storyboard.Duration 从字符串资源转换会失败（实测路线见 Task 1 Step 1 报告）。
/// </summary>
internal static class MotionTokens
{
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan Normal = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(200);

    /// <summary>Fluent 标准缓动（与系统过渡同源）。</summary>
    public static readonly EasingFunctionBase StandardEasing = new CubicEase { EasingMode = EasingMode.EaseOut };

    public static Duration D(TimeSpan t) => new(t);
}
```

（`using Microsoft.UI.Xaml.Media.Animation;` 与 `Microsoft.UI.Xaml;` 按需补。）

- [ ] **Step 4: 写 `Theme/Styles.xaml` 骨架（本 Task 只放两块：行容器模板与滚动条）**

本 Task 的 Styles.xaml 先建立 **六态容器模板** 的公共架子（Task 3 会往里加细则）。核心是给"我们自己的列表项"一个可控模板——**不抄 Fluent 的整份模板**，而是写最小可控版，六态由我们自己给。

> **先量再写（重要）**：下面这段 XAML 里的 `CommonStates` / `SelectionStates` 两组**组名与状态名都是控件按名字驱动的**——名字写错不会报错，只会让状态**静默不生效**（正是本项目最恨的失败）。写之前先读 WASDK 自带的 `Themes/generic.xaml`（Phase 20 量 `HorizontalThumb` 用的就是它，路径可在 `dotnet build -v d` 的输出或 NuGet 包目录里找到）里 `ListViewItem` 的默认模板，**把它的组名与全部状态名逐字抄过来**，再按下述结构填 Setter；本文件里的 `SelectedPointerOver` / `SelectedPressed` 只是占位写法，**以 `generic.xaml` 为准**（若它是 `PointerOverSelected` / `PressedSelected`，就改成那两个名字）。报告里要贴出抄到的原始状态名列表。

```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!--
      列表项容器（NavRail 的 ListView 与 TrackList 的 ListView 共用）。
      ListViewItem 的代码会驱动 VSM 的 "CommonStates" 与 "SelectionStates" 两个组，
      组名不能改（改了控件找不到组 → 状态不生效）。左侧强调条在 Selected 态从 0 长到 3px。
    -->
    <Style x:Key="RailListViewItemStyle" TargetType="ListViewItem">
        <Setter Property="UseSystemFocusVisuals" Value="True" />
        <Setter Property="HorizontalContentAlignment" Value="Stretch" />
        <Setter Property="MinHeight" Value="{StaticResource RowHeight}" />
        <Setter Property="Padding" Value="0" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ListViewItem">
                    <Grid x:Name="ItemRoot" Background="{TemplateBinding Background}">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>
                        <Rectangle x:Name="SelectionBar" Width="3" Opacity="0"
                                   Fill="{ThemeResource AccentFillColorDefaultBrush}" />
                        <ContentPresenter Grid.Column="1"
                                          Content="{TemplateBinding Content}"
                                          ContentTemplate="{TemplateBinding ContentTemplate}"
                                          Padding="{TemplateBinding Padding}"
                                          VerticalAlignment="Center" />
                        <VisualStateManager.VisualStateGroups>
                            <VisualStateGroup x:Name="CommonStates">
                                <VisualState x:Name="Normal" />
                                <VisualState x:Name="PointerOver">
                                    <VisualState.Setters>
                                        <Setter Target="ItemRoot.Background" Value="{ThemeResource SubtleFillColorSecondaryBrush}" />
                                    </VisualState.Setters>
                                </VisualState>
                                <VisualState x:Name="Pressed">
                                    <VisualState.Setters>
                                        <Setter Target="ItemRoot.Background" Value="{ThemeResource SubtleFillColorTertiaryBrush}" />
                                    </VisualState.Setters>
                                </VisualState>
                                <VisualState x:Name="Disabled">
                                    <VisualState.Setters>
                                        <Setter Target="ItemRoot.Opacity" Value="0.4" />
                                    </VisualState.Setters>
                                </VisualState>
                            </VisualStateGroup>
                            <VisualStateGroup x:Name="SelectionStates">
                                <VisualState x:Name="Unselected" />
                                <VisualState x:Name="Selected">
                                    <VisualState.Setters>
                                        <Setter Target="ItemRoot.Background" Value="{ThemeResource SubtleFillColorSecondaryBrush}" />
                                        <Setter Target="SelectionBar.Opacity" Value="1" />
                                    </VisualState.Setters>
                                </VisualState>
                                <VisualState x:Name="SelectedPointerOver">
                                    <VisualState.Setters>
                                        <Setter Target="ItemRoot.Background" Value="{ThemeResource SubtleFillColorTertiaryBrush}" />
                                        <Setter Target="SelectionBar.Opacity" Value="1" />
                                    </VisualState.Setters>
                                </VisualState>
                                <VisualState x:Name="SelectedPressed">
                                    <VisualState.Setters>
                                        <Setter Target="SelectionBar.Opacity" Value="1" />
                                    </VisualState.Setters>
                                </VisualState>
                            </VisualStateGroup>
                        </VisualStateManager.VisualStateGroups>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
</ResourceDictionary>
```

先把**一个** 容器模板与项目其余结构搭好；列表行侧的表头/列宽、TrackList 专用样式在 Task 3 再补。

- [ ] **Step 5: 在 `App.xaml` 合并资源字典**

```xml
<ResourceDictionary.MergedDictionaries>
    <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
    <ResourceDictionary Source="Theme/Tokens.xaml" />
    <ResourceDictionary Source="Theme/Styles.xaml" />
</ResourceDictionary.MergedDictionaries>
```

- [ ] **Step 6: 建四个 UserControl（骨架，行为等价）**

每个控件只放"它那一块"的 XAML 与对应的代码后置；**把 `MainWindow` 里搬过来的逻辑放进去**（不重写逻辑，只搬家）：

- `Views/PlayerBar.xaml(.cs)`：搬 `PositionSlider` 及其三条输入路径（`HookSliderParts` / `Position_TrackPressed` / `TryGetTrackFraction` / `Position_Changed` / `Position_DragStarted` / `Position_DragCompleted`）与 `PlayPause_Click`、`PlayPauseGlyph`/`NowPlayingText`/`TimeText` 三个投影 + `INotifyPropertyChanged` 的两个静态数组。构造签名 `public PlayerBar(PlayerViewModel player)`；`x:Bind` 的源写 `Player`（本控件自己的属性）。**`Player.PositionNormalized` 仍然直绑**，禁止壳侧再抄公式。
- `Views/NavRail.xaml(.cs)`：本 Task 先搬"整栏重建"版（`SyncPlaylistMenu` 的手工版本）以保证行为等价，**Task 2 再改声明式**（一次只动一件事，便于评审定位）。
- `Views/TrackList.xaml(.cs)`：搬 `TrackList`/`EmptyHint`/`ResyncView` 的列表部分（`ItemsSource` 代码赋值、`DoubleTapped` 命中测试、`IndexOfByReference`、`FindAncestor`、`FindDescendant` 中列表需要的那份）。构造签名 `public TrackList(PlaylistsViewModel playlists)`，内部订阅 `ViewedPlaylist` 变化。
- `Views/InfoPanel.xaml(.cs)`：本 Task 先只放占位（`TextBlock`「信息面板」），Task 2 实现内容。
- `Theme/VisualTree.cs`（新，**唯一一份**）：把 `MainWindow.xaml.cs` 现有的 `FindAncestor<T>` 与 `FindDescendant<T>` 搬进来当 `internal static` 帮助方法。**三个控件都要用它们**（TrackList 找 `ListViewItem`、PlayerBar 找 `Thumb`、MainWindow 找 `Button`），所以不许各留一份、也不许在搬家时顺手改逻辑：

```csharp
namespace DPlayer.WinUI.Theme;

/// <summary>可视化树查找：全壳唯一实现（原来散在 MainWindow 里，拆分后三处都要用）。</summary>
internal static class VisualTree
{
    public static T? FindAncestor<T>(DependencyObject? obj) where T : DependencyObject
    {
        while (obj is not null)
        {
            if (obj is T match) return match;
            obj = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    public static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0, n = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i < n; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T hit) return hit;
            if (FindDescendant<T>(child) is { } deeper) return deeper;
        }
        return null;
    }
}
```

**`MainWindow.xaml` 结构改为**：

```xml
<Grid x:Name="RootGrid" Background="Transparent">
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="*" />
        <RowDefinition Height="Auto" />
    </Grid.RowDefinitions>
    <Grid x:Name="AppTitleBar" Grid.Row="0" Height="{StaticResource TitleBarHeight}">…</Grid>
    <Grid Grid.Row="1" ColumnSpacing="0">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <views:NavRail x:Name="Rail" Grid.Column="0" />
        <views:TrackList x:Name="Tracks" Grid.Column="1" />
        <views:InfoPanel x:Name="Info" Grid.Column="2" />
    </Grid>
    <views:PlayerBar x:Name="Bar" Grid.Row="2" />
</Grid>
```

`MainWindow.xaml.cs` 保留：`Title`/`ExtendsContentIntoTitleBar`/`SetTitleBar`/`SystemBackdrop = new MicaBackdrop()`（**连同 `using Microsoft.UI.Xaml.Media;` 与顶部注释**）、`AppWindow.Closing` 的 cancel-and-close、以及四个控件的创建与装配。删除已搬走的成员。`MainWindow` 不再实现 `INotifyPropertyChanged`（三个投影搬进 PlayerBar 了）。

- [ ] **Step 7: 构建 + 起窗 + 真机等价性核对**

```bash
dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q
dotnet run --project D-player.WinUI/D-player.WinUI.csproj -c Debug
```

核对（与 Phase 20 验收同口径）：窗口起得来、三区在、左栏列出歌单、双击出声、进度推进、**拖动与单击定位都对**（单击落点 25/50/75% → `Value` ≈ 0.25/0.50/0.75，用 `$TEMP` 探针或 UIA 读数取证）、关窗退出码 0。**任何一项对不上就停下修，不许"下个 Task 再补"。**

- [ ] **Step 8: 门禁 + 硬边界 + 提交**

```bash
dotnet build D-player.slnf -c Debug --nologo -v q
dotnet test  D-player.slnf -c Debug -v q
git diff --stat a49727a..HEAD -- D-player.Core D-player.csproj Views Converters Themes Models Services ViewModels Configuration Extensions appsettings.json
```

Expected：0/0；180 通过 0 失败；`git diff --stat` **空输出**。

```bash
git add -A && git commit -m "refactor(winui): split the shell into four controls and add the token layer

MainWindow carried five responsibilities at 418 lines; the nav rail, track
list, info panel and player bar move into their own UserControls so the
polish work has somewhere to live. Theme/Tokens.xaml holds the spacing,
radius, font-size and structural sizes, and Motion.cs holds the three
motion durations as C# constants because WinUI XAML has no dependable
TimeSpan resource for Storyboard durations. Behaviour is unchanged, which
the real-machine run confirms: same three regions, same double-click
playback, same drag and click-to-position readings.

CommunityToolkit.WinUI is added at <填入实测版本> after probing it against
Windows App SDK 2.5.1; the probe's type surface is in the task report."
```

---

### Task 2: 三栏与折叠（NavRail 声明式 + InfoPanel 实现 + 折叠）

**Files:**
- Modify: `D-player.WinUI/Views/NavRail.xaml(.cs)`、`Views/InfoPanel.xaml(.cs)`、`Views/TrackList.xaml(.cs)`、`MainWindow.xaml(.cs)`
- Create: `D-player.WinUI/Views/CoverArtLoader.cs`（`AlbumArtBytes` → `BitmapImage`，按目标尺寸解码）

**Interfaces:**
- Consumes: Task 1 的四个控件与 `{StaticResource NavRailWidth}` 等令牌。
- Produces: `NavRail.IsCollapsed`（`bool`，双向可读）与 `NavRail.SetCollapsed(bool collapsed, bool animate)`；`InfoPanel.IsCollapsed` / `InfoPanel.SetCollapsed(bool collapsed, bool animate)`（两侧同构，MainWindow 只调方法不直接改字段）；`CoverArtLoader.LoadAsync(byte[]? bytes, int decodeSize) → Task<ImageSource?>`；`MainWindow` 持有阈值与手动覆盖逻辑。

- [ ] **Step 1: NavRail 改声明式**

```xml
<ListView x:Name="PlaylistList"
          ItemsSource="{x:Bind Playlists.Playlists, Mode=OneWay}"
          SelectedItem="{x:Bind Playlists.ViewedPlaylist, Mode=TwoWay}"
          ItemContainerStyle="{StaticResource RailListViewItemStyle}"
          SelectionMode="Single" />
```

要点：**不再手工增删 `NavigationViewItem`、不再每次点击整栏重建**（Phase 20 遗留）。`SelectedItem` 双向绑 `ViewedPlaylist`——它仍是唯一真源（WPF 侧栏同纪律）。`x:Bind` 的源是本控件自己的 `Playlists` 属性；**不要写 `Playlists.ViewedPlaylist.Playlists` 这类跨层链**（XamlCompiler WMC9999）。

删除 `_syncingMenu` 与 `SyncPlaylistMenu`（连同注释）。键盘 ↑/↓/Home/End 由 `ListView` 自带，Enter 由 `ItemClick` 或 `KeyDown` 补。

- [ ] **Step 2: InfoPanel 实现（封面 + 元数据 + 空态）**

```xml
<StackPanel Spacing="{StaticResource SpacingMd}" Padding="{StaticResource SpacingLg}">
    <Grid Width="228" Height="228">
        <Border CornerRadius="{StaticResource RadiusItem}"
                Background="{ThemeResource SubtleFillColorSecondaryBrush}" />
        <FontIcon Glyph="&#xE8D6;" FontSize="48" Opacity="0.4"
                  HorizontalAlignment="Center" VerticalAlignment="Center" />
        <Image x:Name="Cover" Stretch="UniformToFill" />
    </Grid>
    <TextBlock x:Name="TitleText" FontSize="{StaticResource FontSizeSubtitle}" FontWeight="SemiBold"
               TextWrapping="Wrap" MaxLines="2" TextTrimming="CharacterEllipsis" />
    <TextBlock x:Name="ArtistText" FontSize="{StaticResource FontSizeBody}"
               Foreground="{ThemeResource TextFillColorSecondaryBrush}" TextTrimming="CharacterEllipsis" />
    <TextBlock x:Name="AlbumText" FontSize="{StaticResource FontSizeBody}"
               Foreground="{ThemeResource TextFillColorSecondaryBrush}" TextTrimming="CharacterEllipsis" />
    <TextBlock x:Name="DurationText" FontSize="{StaticResource FontSizeCaption}"
               Foreground="{ThemeResource TextFillColorSecondaryBrush}" />
</StackPanel>
```

`CoverArtLoader`（新文件）：

```csharp
namespace DPlayer.WinUI.Views;

/// <summary>
/// Core 的 Track.AlbumArtBytes 是原始字节；这里在壳内解码成 BitmapImage，
/// 并按目标尺寸限制解码分辨率（大图直接解码会拖慢切歌）。
/// </summary>
internal static class CoverArtLoader
{
    public static async Task<ImageSource?> LoadAsync(byte[]? bytes, int decodeSize)
    {
        if (bytes is null || bytes.Length == 0) return null;
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(bytes.AsBuffer());
        stream.Seek(0);
        var image = new BitmapImage { DecodePixelWidth = decodeSize, DecodePixelHeight = decodeSize };
        await image.SetSourceAsync(stream);
        return image;
    }
}
```

（`using System.Runtime.InteropServices.WindowsRuntime;` 取 `AsBuffer()`；`InMemoryRandomAccessStream` 来自 `Windows.Storage.Streams`。若 `AsBuffer` 在当前 SDK 上不可用，改用 `DataWriter`：`var w = new DataWriter(stream); w.WriteBytes(bytes); await w.StoreAsync();`——两种写法都在报告里记下你实际用的那种。）

切换订阅：`Player.CurrentTrack` 变化 → 无封面时 `Cover.Source = null` 且占位可见；有封面 → `await LoadAsync(bytes, 228)` 后赋值（`Image` 的 `Source` 赋值本身不做动画，交叉淡入在 Task 5）。

- [ ] **Step 3: 折叠（两栏）+ 阈值自动收起 + 迟滞**

- `NavRail`：`Rail.IsCollapsed` 从 `200` ↔ `48`，折叠时只留图标（歌单首字或符号），文字先淡出。
- `InfoPanel`：`IsCollapsed` 从 `260` ↔ `0`。
- `MainWindow`：`RootGrid.SizeChanged` 里按阈值处理——

```csharp
private bool _manualOverride;
private const double Hysteresis = 8;

private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
{
    var threshold = (double)Application.Current.Resources["InfoPanelAutoCollapseWidth"];
    if (_manualOverride)
    {
        if (e.NewSize.Width >= threshold + Hysteresis) _manualOverride = false;  // 越回阈值上方才恢复自动
        return;
    }
    var shouldCollapse = e.NewSize.Width < threshold;
    if (Info.IsCollapsed != shouldCollapse) Info.SetCollapsed(shouldCollapse, animate: true);
}
```

手动点折叠钮时置 `_manualOverride = true`。

- [ ] **Step 4: 真机核对（清单 A 组）**

`dotnet run` 后逐项走 spec §7 A 组：A1 三栏比例（左 200 / 右 260 / 底 64）、A2 拖窄到 <960px 右栏自动收起且不抖、A3 两栏折叠往返、A5 三栏都收起时列表占满且行仍 36px。取证：UIA 树里读各元素 `ActualWidth`（写进 `$TEMP` 或直接引用 UIA 输出）。**A4（标题栏拖拽/双击最大化）在 Task 4 做。**

- [ ] **Step 5: 门禁 + 硬边界 + 提交**

同 Task 1 Step 8 三条命令（含 `git diff --stat` 空）。提交信息写清"左栏改声明式、右栏落地、折叠与阈值"，并附实测的宽度读数。

---

### Task 3: 六态与排版（清单 B 组）

**Files:**
- Modify: `D-player.WinUI/Theme/Styles.xaml`、`Views/TrackList.xaml(.cs)`、`Views/NavRail.xaml`、`Views/PlayerBar.xaml(.cs)`、`Views/InfoPanel.xaml`

**Interfaces:**
- Consumes: Task 1 的 `RailListViewItemStyle`、Task 2 的三栏。
- Produces: `TrackListHeaderStyle`（静态表头行）、`PlayPauseButtonStyle`、`ProgressSliderStyle`、`RailScrollViewerStyle` 四个具名样式，供后续任务与评审引用。

- [ ] **Step 1: TrackList 结构与排版（36px 行、三列、静态表头）**

表头（**不可点**，docs 里写明这是有意的）：

```xml
<Grid x:Name="Header" Height="{StaticResource RowHeight}" Padding="{StaticResource SpacingMd},0"
      Background="{ThemeResource LayerFillColorDefaultBrush}">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="40" />
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="220" />
        <ColumnDefinition Width="72" />
    </Grid.ColumnDefinitions>
    <TextBlock Grid.Column="0" Text="#" FontSize="{StaticResource FontSizeCaption}"
               Foreground="{ThemeResource TextFillColorSecondaryBrush}" TextAlignment="Right" />
    <TextBlock Grid.Column="1" Text="标题" … />
    <TextBlock Grid.Column="2" Text="艺术家" … />
    <TextBlock Grid.Column="3" Text="时长" HorizontalAlignment="Right" … />
</Grid>
```

行模板列宽必须与表头一致（把列宽写进 `TrackList.xaml` 的一个 `Grid` 并在行模板里复用同一组数值；数值以注释标出"与表头同步"）。

行内 `#` 用 `{x:Bind TrackNumber}`（`null` 显示 `-`）、标题/艺术家截断、时长 `mm:ss` 且**等宽数字**（`FontFamily="Consolas"` 或 `Typography.NumeralStyle="Tabular"`，取系统可用者，写进报告）。

- [ ] **Step 2: 空态文案换产品话术**

```xml
<StackPanel x:Name="EmptyHint" … Spacing="{StaticResource SpacingSm}">
    <TextBlock Text="还没有曲目" HorizontalAlignment="Center"
               Style="{StaticResource SubtitleTextBlockStyle}" />
    <TextBlock Text="导入入口将在下一阶段加入" HorizontalAlignment="Center" MaxWidth="360"
               TextWrapping="Wrap" Foreground="{ThemeResource TextFillColorSecondaryBrush}" />
</StackPanel>
```

**删除**那句开发味文案（"把曲目写进 %LocalAppData%\D-player-winui\queue.json…"）。**不许**承诺本阶段没有的能力（拖拽/导入都不存在）。

- [ ] **Step 3: 六态矩阵落地（B1-B5）**

逐元素过一遍，缺哪态补哪态，全部引用主题资源：

| 元素 | 需要的处理 |
|---|---|
| TrackList 行 / NavRail 项 | `RailListViewItemStyle` 已给六态；确认 `UseSystemFocusVisuals=True` 生效（B4 的焦点环） |
| 播放钮（44px 圆钮） | 默认 `Button` 六态 + **本项目补**：`Disabled` 时 `Opacity 0.4`（B3：无曲目时禁用——`IsEnabled="{x:Bind Player.CurrentTrack, Converter=…}"` 或代码后置赋值），按下/悬停缩放见 Task 5⑥ |
| 进度滑块 | 默认六态 + `Disabled` 一致化；`IsEnabled` 绑 `Player.Duration > 0` |
| 折叠钮 | 新建 `IconButtonStyle`（透明底、悬停 `SubtleFillColorSecondaryBrush`、按下 tertiary） |
| 滚动条 | `RailScrollViewerStyle`：`ListView.Resources` 里覆盖 `ScrollBar` 的宽/缩略图样式（细窄、悬停加宽由系统默认行为提供，确认即可） |

- [ ] **Step 4: 颜色合规检查（B5）**

```bash
grep -rn "#[0-9A-Fa-f]\{6\}" D-player.WinUI/ --include=*.xaml --include=*.cs | grep -v "/obj/\|/bin/"
```

Expected：只命中 `MainWindow.xaml` 顶部 Mica 取证注释（既有记录文本）。**新写的样式里出现任何十六进制色值都要改回 `ThemeResource`。**

- [ ] **Step 5: 真机核对 B 组 + 提交**

走 spec §7 B1–B5；B4 的焦点环用 Tab 逐个看（UIA 树里读 `HasKeyboardFocus`）。提交信息列明"六态矩阵逐元素过了一遍、新样式名、空态文案替换、无写死色值"。

---

### Task 4: 键盘与无障碍（清单 C 组）

**Files:**
- Modify: `D-player.WinUI/Views/PlayerBar.xaml(.cs)`、`Views/TrackList.xaml(.cs)`、`Views/NavRail.xaml(.cs)`、`MainWindow.xaml(.cs)`
- Create: `D-player.WinUI/Views/TrackRowLabels.cs`

**Interfaces:**
- Consumes: Task 2/3 的结构与样式。
- Produces: `TrackRowLabels.Build(string? title, string? artist, TimeSpan duration) → string`（UIA 名称用）；`PlayerBar` 暴露 `SmallChange`/`LargeChange` 已设为 `0.005`/`0.05`。

- [ ] **Step 1: 进度条键盘步进修复（G7）**

```xml
<Slider x:Name="PositionSlider" Minimum="0" Maximum="1" StepFrequency="0.001"
        SmallChange="0.005" LargeChange="0.05" … />
```

真机：焦点在滑块上按 → 一次约 0.5%，PageUp 约 5%，**不再一步到头**。取证：`$TEMP` 探针或 UIA `RangeValue` 前后对比，两次读数写进报告。

- [ ] **Step 2: 空格 = 播放/暂停（C3）**

在 `MainWindow` 的 `RootGrid` 上挂 `KeyDown`（`AddHandler(..., handledEventsToo: true)`），并**跳过会消费空格的控件**：

```csharp
RootGrid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(RootGrid_KeyDown), handledEventsToo: true);

private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
{
    if (e.Key != VirtualKey.Space) return;
    // 焦点在按钮/滑块/文本输入上时不抢：那是它们的键
    var focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot) as DependencyObject;
    if (FindAncestor<Button>(focused) is not null) return;
    if (focused is Slider) return;
    Bar.TogglePlayPause();          // PlayerBar 上的公开方法：内部走既有的 PlayPause_Click 逻辑
    e.Handled = true;
}
```

（`VirtualKey` 来自 `Windows.System`；`FindAncestor` 用 Task 1 建好的 `Theme/VisualTree.cs` 那**唯一一份**，不要在 `MainWindow` 里再留副本。）

- [ ] **Step 3: Tab 顺序（C4）**

`InfoPanel` 设 `IsTabStop="False"`（只读栏不拦焦点）；`NavRail`/`TrackList`/`PlayerBar` 按视觉顺序（左→中→底）。UIA 树里逐个 `Tab` 验证顺序与焦点环位置。

- [ ] **Step 4: UIA 名称（C5）**

`TrackRowLabels.cs`：

```csharp
namespace DPlayer.WinUI.Views;

internal static class TrackRowLabels
{
    public static string Build(string? title, string? artist, TimeSpan duration)
    {
        var t = string.IsNullOrWhiteSpace(title) ? "未知曲目" : title;
        var a = string.IsNullOrWhiteSpace(artist) ? "未知艺术家" : artist;
        return $"{t}，{a}，{duration:mm\\:ss}";
    }
}
```

行模板根元素加 `AutomationProperties.Name="{x:Bind views:TrackRowLabels.Build(Title, Artist, Duration)}"`（`views` = `using:DPlayer.WinUI.Views`）。**若 XamlCompiler 拒绝静态函数绑定**（报 WMC 类错误）：退路是 `TrackList` 的 `ContainerContentChanging` 里给容器设名——

```csharp
private void List_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
{
    if (args.Item is Track t)
        AutomationProperties.SetName(args.ItemContainer, TrackRowLabels.Build(t.Title, t.Artist, t.Duration));
}
```

两条路选实测能过的那条，报告里写明选了哪条、报了什么错。

播放钮已有 `AutomationProperties.Name="播放/暂停"`（保留）；滑块设 `AutomationProperties.Name="播放进度"` 并给它 `AutomationProperties.HelpText` 说明方向键语义（值语义由 `RangeValue` 模式自带百分比）。

- [ ] **Step 5: 标题栏双击最大化（清单 A4）**

```csharp
AppTitleBar.DoubleTapped += (_, _) =>
{
    var presenter = AppWindow.Presenter as OverlappedPresenter;
    if (presenter is null) return;
    if (presenter.State == OverlappedPresenterState.Maximized) presenter.Restore();
    else presenter.Maximize();
};
```

- [ ] **Step 6: 真机核对 C 组 + A4 + 提交**

逐项走 C1–C5 与 A4，把每条的操作与观察写进报告（C5 用 UIA 树里读到的 `Name` 字段）。

---

### Task 5: 动效 9 项（清单 D 组）

**Files:**
- Modify: `Views/PlayerBar.xaml(.cs)`、`Views/TrackList.xaml(.cs)`、`Views/NavRail.xaml(.cs)`、`Views/InfoPanel.xaml(.cs)`、`MainWindow.xaml(.cs)`、`Theme/Styles.xaml`

**Interfaces:**
- Consumes: Task 1 的 `MotionTokens`（三档时长）与探针报告里的 CommunityToolkit API 面（若探针失败则只用 Storyboard）；Task 2 的折叠、Task 4 的键盘。
- Produces: 9 项动效，全部一次性、≤200ms、无循环。

- [ ] **Step 1: 实现 9 项（逐条，时长只取 `Fast/Normal/Slow`）**

| # | 实现方式（按探针结果二选一） |
|---|---|
| ① 列表行悬停/选中渐变 | **先看探针结果**：若类型面里有 implicit/animation API，就在 `RailListViewItemStyle` 的 VisualState 上挂它（Background 150ms）。否则**只做能诚实做到的**：VisualStateManager 的 Setter 换 Brush 是**瞬变**，而 `ObjectAnimationUsingKeyFrames` 对 Brush 也只能离散关键帧（不产生渐隐）——此时如实实现为"悬停切换底色（无渐变）"并在报告里写明"渐变需要 toolkit，探针结果不支持/未拿到类型面，本项降级"。**不许假称有淡入淡出。** |
| ② 导航项强调条 0→3px | `SelectionBar.Width` 的 `DoubleAnimation`（100ms，走 VSM Setter 不成立 → 用 `VisualState` 里的 `Storyboard`：`<Storyboard><DoubleAnimation Storyboard.TargetName="SelectionBar" Storyboard.TargetProperty="Width" …`），VSM 的 `VisualState` 内可直接嵌 `Storyboard` |
| ③ 按钮按下/悬停缩放 | `PlayerBar` 播放钮加 `ScaleTransform`（`RenderTransformOrigin=0.5,0.5`），`PointerEntered/Pressed/Released/Exited` 里用 `Storyboard` 动 `ScaleX/ScaleY`（100/150ms） |
| ④ 折叠宽度 200ms + 内容先淡出 100ms | 折叠钮点击 → 先 `Storyboard` 动内层 `Opacity` 到 0（100ms），完成后动栏 `Width`（200ms，`MotionTokens.Slow`），再逆向展开 |
| ⑤ 焦点环 100ms 淡入 | 系统焦点视觉（`UseSystemFocusVisuals`）无法自定时长；改为对 `ItemRoot` 加 `FocusVisual` 自绘？**不做自绘**：在报告里写明"焦点环沿用系统视觉，其淡入时长由系统控制，无法按 100ms 指定"，清单 C4/B4 仍验证"看得见焦点环" |
| ⑥ 切歌封面交叉淡入 + 文本上移 | `Cover` 上叠一张"旧图"层：`InfoPanel` 里维护 `CoverA/CoverB` 两张图轮换（新图淡入 150ms、旧图淡出 150ms，避免闪白）；`TitleText/ArtistText` 用 `TranslateTransform.Y` 4→0 + `Opacity` 0→1（100ms），在 `CurrentTrack` 变化时触发 |
| ⑦ 播放图标交叉淡入（不旋转） | `PlayPauseButton` 里放两个 `FontIcon`（▶/⏸）叠放，状态变化时交叉 `Opacity` 100ms |
| ⑧ 进度拇指缩放 | 复用 Task 2 的 Thumb 引用：`DragStarted` → 给 Thumb 挂 `ScaleTransform` 动画到 1.15（100ms）；`DragCompleted` → 回 1.0（100ms）。**部件找不到时静默跳过**（既有退化纪律） |
| ⑨ 空态与占位淡入 | 空态文案首次可见时 `Opacity 0→1` + `TranslateTransform.Y 8→0`（150ms）；`InfoPanel` 占位封面 ↔ 真封面交叉淡入（150ms，与⑥共用两张图机制） |

**硬性检查**：每个动画的 `Duration` 只能取 `MotionTokens.Fast/Normal/Slow`；不得有 `RepeatBehavior="Forever"`；不得有 >250ms。

- [ ] **Step 2: 动效不伤人检查（D4）**

连续快速点折叠钮 5 次：终态必须正确、动画不叠加抽搐（用"动画开始时先 `Stop` 同名 Storyboard"的手法）。把观察写进报告。

- [ ] **Step 3: 真机核对 D 组 + 提交**

逐项走 D1–D5 并把观察写进报告；无法量化的（观感）如实标"待用户确认"，不得写成通过。

---

### Task 6: 文档与收尾

**Files:**
- Modify: `README.md`、`docs/PROJECT.md`、`docs/COUPLING.md`

- [ ] **Step 1: `docs/COUPLING.md` 增补契约**

- §5 新增行：**WinUI 视觉层契约**（令牌三档时长、颜色只走主题资源、六态齐备）· **折叠与阈值契约**（左右栏折叠尺寸、`<960px` 自动收起 + 8px 迟滞、手动覆盖优先）· **无跨层 x:Bind 链**（`Playlists.ViewedPlaylist.X` 这类会让 XamlCompiler WMC9999）。
- §7 新增 ❌：壳内写死十六进制色值；引入第四个动效时长或 >250ms 动画；把折叠状态写进 `settings.json`（属功能面契约）；在 WinUI 侧新增功能项（本阶段非目标）。

- [ ] **Step 2: `README.md` / `docs/PROJECT.md` 同步**

- 项目结构树补 `Theme/` 与 `Views/`；测试计数**仍是 180**（本阶段不加测试）。
- 新增一节 **WinUI 视觉规范**：三栏尺寸、令牌表、六态定义、动效清单与三档时长、折叠/阈值契约。
- 阶段表补 Phase 21 行（状态：打磨已交付，待用户真机走清单）。

- [ ] **Step 3: 交付验收清单（20 项）**

把 spec §7 的 A/B/C/D/E 五组清单**原样**放进 `docs/PHASE21-ACCEPTANCE.md`（或 PROJECT 的对应小节），并交给用户；每项三列（操作 / 通过标准 / 失败长什么样）。E 组三项（播放回归 / 续播回归 / 门禁）必须标注为**硬门槛**。

- [ ] **Step 4: 门禁复核 + 提交**

跑齐：`.slnf` build/test、WinUI 单壳 build、`verify-gates.ps1 -Full`、`git diff --stat` 硬边界。提交 `docs:`。

---

## Self-Review 记录

**1. Spec 覆盖**

| spec 章节 | 实现于 |
|---|---|
| §3.1 文件结构（四 UserControl + Theme/） | Task 1 Step 2-6 |
| §3.2 左栏改声明式 | Task 2 Step 1 |
| §4 令牌（间距/排版/圆角/颜色/时长） | Task 1 Step 2-3；Task 3 Step 4 验颜色合规 |
| §5 各区细则（标题栏/NavRail/TrackList/InfoPanel/PlayerBar/六态） | Task 2（结构）+ Task 3（排版与六态）+ Task 4 A4（标题栏双击） |
| §6 动效 9 项 + 4 条不做 | Task 5 |
| §7 验收清单 A/B/C/D/E | Task 2-4（A/B/C/D 分组核对）、Task 6 Step 3（交付 E 组并交用户） |
| §8 风险（折叠抖动/工具包兼容/跨层 x:Bind/封面卡顿/动效伤人/空格冲突/回归） | Task 2 Step 3（迟滞）、Task 1 Step 1（工具包探针与退路）、Task 1 Step 6 与 Task 2 Step 1（禁跨层 x:Bind）、Task 2 Step 2（解码限制）、Task 5 Step 2（动画 Stop）、Task 4 Step 2（空格跳过）、Task 1 Step 7 与各 Task 门禁（回归） |
| §9 六步划分 21-1…21-6 | Task 1-6 一一对应 |
| §10 未决项（工具包版本 / 阈值 / 右栏默认态） | Task 1 Step 1（钉版本）、Task 1 Step 2（阈值入令牌）、Task 2 Step 3（默认展开，可一句话改） |

**2. 占位符扫描**：唯一的 `<填入实测版本>` 在 Task 1 的提交信息里，写明"实测后填入、不得照抄"；Task 5 的 ① 与 ⑤ 各有一条**明确的退路实现 + 必须在报告里写明选哪条**，属实测分支不是缺内容。

**3. 类型一致性**：`MotionTokens.Fast/Normal/Slow`（Task 1 定义 → Task 5 使用）；`RailListViewItemStyle`（Task 1 定义 → Task 2/3 引用）；`CoverArtLoader.LoadAsync(byte[]?, int)`（Task 2 定义并使用）；`TrackRowLabels.Build(string?, string?, TimeSpan)`（Task 4 定义并使用）；`NavRail.IsCollapsed`/`InfoPanel.IsCollapsed`/`InfoPanel.SetCollapsed(bool, bool)`（Task 2 定义 → MainWindow 使用）；`Bar.TogglePlayPause()`（Task 4 定义并使用）。所有 Core 成员均取自 Phase 20 已验证的公开面，无新增 Core API。
