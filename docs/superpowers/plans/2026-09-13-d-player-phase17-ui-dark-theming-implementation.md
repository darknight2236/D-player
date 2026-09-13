# Phase 17 UI 深度深色定制 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把仍为默认浅色的 UI 元素全部纳入深色主题：自定义无边框标题栏（WindowStyle=None + WindowChrome + 自绘 TitleBar）、ComboBox、CheckBox、ScrollBar、ToolTip、ContextMenu/MenuItem。

**Architecture:** 新建可复用 `Views/Controls/TitleBar.xaml` UserControl（Title/ShowMaximize 依赖属性 + 最小化/最大化还原/关闭按钮，经 `SystemCommands` 动作 + `WindowChrome.IsHitTestVisibleInChrome`），各 Window 设 `WindowStyle=None` + `WindowChrome`(CaptionHeight=32) 并在顶部放 TitleBar；控件深色化通过向 `Themes/Controls.xaml` 追加隐式 Style/ControlTemplate 全局生效。纯表现层，不改功能行为。

**Tech Stack:** WPF XAML（`System.Windows.Shell.WindowChrome`、`SystemCommands`）· 复用 `Themes/Colors.xaml` 画刷与 `Themes/Icons.xaml` 矢量图标

**设计稿：** [`docs/superpowers/specs/2026-09-13-d-player-phase17-ui-dark-theming-design.md`](../specs/2026-09-13-d-player-phase17-ui-dark-theming-design.md)

**验证基线：** 每 Task 后 `dotnet build` 0 错误 + `dotnet test` 96 全绿；最终 ComputerUse 截图核对标题栏/下拉/复选框/滚动条/ToolTip/最大化边距。

---

## 文件结构

| 文件 | 责任 |
|------|------|
| `Themes/Icons.xaml`（改） | 新增 `Icon.Maximize` / `Icon.Restore` |
| `Views/Controls/TitleBar.xaml(.cs)`（新） | 可复用自绘标题栏（Title/ShowMaximize DP + min/max-restore/close + 最大化边距修正） |
| `Views/MainWindow.xaml`（改） | WindowStyle=None + WindowChrome + 顶部 TitleBar 行 |
| `Views/Dialogs/SettingsDialog.xaml`（改） | 同上，ShowMaximize=false |
| `Views/Dialogs/EqualizerDialog.xaml`（改） | 同上，ShowMaximize=false |
| `Views/Dialogs/PromptDialog.xaml`（改） | 同上，ShowMaximize=false |
| `Themes/Controls.xaml`（改） | 追加 ComboBox/CheckBox/ScrollBar/ToolTip/ContextMenu/MenuItem 深色隐式样式 |

---

### Task 1: Icons.xaml 新增 Maximize/Restore 图标

**Files:**
- Modify: `Themes/Icons.xaml`

- [ ] **Step 1: 在 `Icon.Clear` 之后追加两条 Geometry**

```xml
    <Geometry x:Key="Icon.Maximize">M6,6 H18 V18 H6 Z</Geometry>
    <Geometry x:Key="Icon.Restore">M7,7 H14 V14 H7 Z M10,7 V4 H17 V11 H14</Geometry>
```

- [ ] **Step 2: 构建验证**

Run: `dotnet build D-player.sln -c Debug --nologo -v q`
Expected: 0 错误（Geometry path markup 可解析）。

- [ ] **Step 3: 提交**

```bash
git add Themes/Icons.xaml
git commit -m "feat(theme): add maximize/restore icons (Phase 17)"
```

---

### Task 2: 可复用 TitleBar UserControl

**Files:**
- Create: `Views/Controls/TitleBar.xaml`
- Create: `Views/Controls/TitleBar.xaml.cs`

- [ ] **Step 1: 创建 `Views/Controls/TitleBar.xaml`**

```xml
<!--
    Views/Controls/TitleBar.xaml —— 自绘无边框标题栏（Phase 17）。
    配合 Window 上的 WindowStyle=None + WindowChrome(CaptionHeight=32) 使用。
    按钮标 shell:WindowChrome.IsHitTestVisibleInChrome=True 以接收点击（否则被 caption 拖动吞掉）。
-->
<UserControl x:Class="DPlayer.Views.Controls.TitleBar"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:DPlayer.Views.Controls"
             xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"
             Height="32" Background="{StaticResource BackgroundPrimary}">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>
        <TextBlock Grid.Column="0"
                   Text="{Binding Title, RelativeSource={RelativeSource AncestorType=local:TitleBar}}"
                   Foreground="{StaticResource ForegroundSecondary}" FontSize="12"
                   VerticalAlignment="Center" Margin="12,0,0,0"/>
        <StackPanel Grid.Column="1" Orientation="Horizontal">
            <Button x:Name="MinButton" Width="40" Height="32" Click="Minimize_Click"
                    shell:WindowChrome.IsHitTestVisibleInChrome="True" ToolTip="最小化">
                <Path Style="{StaticResource IconPath}" Width="12" Height="12"
                      Data="{StaticResource Icon.Minus}" Stroke="{StaticResource ForegroundSecondary}"/>
            </Button>
            <Button x:Name="MaxRestoreButton" Width="40" Height="32" Click="MaxRestore_Click"
                    shell:WindowChrome.IsHitTestVisibleInChrome="True" ToolTip="最大化/还原">
                <Grid>
                    <Path x:Name="MaxIcon" Style="{StaticResource IconPath}" Width="12" Height="12"
                          Data="{StaticResource Icon.Maximize}" Stroke="{StaticResource ForegroundSecondary}"/>
                    <Path x:Name="RestoreIcon" Style="{StaticResource IconPath}" Width="12" Height="12"
                          Data="{StaticResource Icon.Restore}" Stroke="{StaticResource ForegroundSecondary}"
                          Visibility="Collapsed"/>
                </Grid>
            </Button>
            <Button x:Name="CloseButton" Width="40" Height="32" Click="Close_Click"
                    shell:WindowChrome.IsHitTestVisibleInChrome="True" ToolTip="关闭">
                <Path Style="{StaticResource IconPath}" Width="12" Height="12"
                      Data="{StaticResource Icon.Close}" Stroke="{StaticResource ForegroundSecondary}"/>
            </Button>
        </StackPanel>
    </Grid>
</UserControl>
```

- [ ] **Step 2: 创建 `Views/Controls/TitleBar.xaml.cs`**

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;

namespace DPlayer.Views.Controls;

/// <summary>
/// 自绘无边框标题栏（Phase 17）。依赖属性 Title / ShowMaximize。
/// 动作经 SystemCommands 作用于父 Window；最大化时给窗口根容器加
/// WindowResizeBorderThickness 边距防内容贴屏边；Maximized 时切换 Maximize/Restore 图标。
/// </summary>
public partial class TitleBar : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(TitleBar), new PropertyMetadata(string.Empty));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty ShowMaximizeProperty =
        DependencyProperty.Register(nameof(ShowMaximize), typeof(bool), typeof(TitleBar),
            new PropertyMetadata(true, OnShowMaximizeChanged));

    public bool ShowMaximize
    {
        get => (bool)GetValue(ShowMaximizeProperty);
        set => SetValue(ShowMaximizeProperty, value);
    }

    private Window? _window;

    public TitleBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window is null) return;
        _window.StateChanged += OnWindowStateChanged;
        ApplyShowMaximize();
        ApplyWindowState();
    }

    private static void OnShowMaximizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TitleBar)d).ApplyShowMaximize();

    private void ApplyShowMaximize()
    {
        var vis = ShowMaximize ? Visibility.Visible : Visibility.Collapsed;
        MinButton.Visibility = vis;
        MaxRestoreButton.Visibility = vis;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e) => ApplyWindowState();

    private void ApplyWindowState()
    {
        if (_window is null) return;
        var maximized = _window.WindowState == WindowState.Maximized;
        MaxIcon.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreIcon.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;

        // 最大化边距修正：无边框窗最大化时内容会顶到屏边/任务栏下，加 resize 边框厚度补偿
        if (_window.Content is FrameworkElement root)
        {
            var b = SystemParameters.WindowResizeBorderThickness;
            root.Margin = maximized ? new Thickness(b.Left, b.Top, b.Right, b.Bottom) : new Thickness(0);
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (_window is not null) SystemCommands.MinimizeWindow(_window);
    }

    private void MaxRestore_Click(object sender, RoutedEventArgs e)
    {
        if (_window is null) return;
        if (_window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(_window);
        else SystemCommands.MaximizeWindow(_window);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_window is not null) SystemCommands.CloseWindow(_window);
    }
}
```

- [ ] **Step 3: 构建验证**

Run: `dotnet build D-player.sln -c Debug --nologo -v q`
Expected: 0 错误。

- [ ] **Step 4: 提交**

```bash
git add Views/Controls/TitleBar.xaml Views/Controls/TitleBar.xaml.cs
git commit -m "feat(view): add reusable custom TitleBar control (Phase 17)"
```

---

### Task 3: MainWindow 自定义 chrome

**Files:**
- Modify: `Views/MainWindow.xaml`

- [ ] **Step 1: Window 标签加 shell xmlns + WindowStyle=None**

before:
```xml
        xmlns:controls="clr-namespace:DPlayer.Views.Controls"
        Title="D-player"
```
after:
```xml
        xmlns:controls="clr-namespace:DPlayer.Views.Controls"
        xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"
        WindowStyle="None"
        Title="D-player"
```

- [ ] **Step 2: 加 WindowChrome（在 Window.Resources 之前）**

before:
```xml
        KeyDown="MainWindow_KeyDown">
    <Window.Resources>
```
after:
```xml
        KeyDown="MainWindow_KeyDown">
    <shell:WindowChrome.WindowChrome>
        <shell:WindowChrome CaptionHeight="32" UseAeroCaptionButtons="False" GlassFrameThickness="0"/>
    </shell:WindowChrome.WindowChrome>
    <Window.Resources>
```

- [ ] **Step 3: 根 Grid 加 TitleBar 行**

before:
```xml
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <Grid x:Name="ContentGrid" Grid.Row="0" SizeChanged="ContentGrid_SizeChanged">
```
after:
```xml
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <controls:TitleBar Grid.Row="0" Title="D-player" ShowMaximize="True"/>

        <Grid x:Name="ContentGrid" Grid.Row="1" SizeChanged="ContentGrid_SizeChanged">
```

- [ ] **Step 4: PlayerBar 行号 1→2**

定位 `<controls:PlayerBar` 元素，把其 `Grid.Row="1"` 改为 `Grid.Row="2"`。

- [ ] **Step 5: 构建验证**

Run: `dotnet build D-player.sln -c Debug --nologo -v q` → 0 错误。

- [ ] **Step 6: 提交**

```bash
git add Views/MainWindow.xaml
git commit -m "feat(view): MainWindow custom borderless chrome with TitleBar (Phase 17)"
```

---

### Task 4: 三个对话框自定义 chrome（仅关闭按钮）

**Files:**
- Modify: `Views/Dialogs/SettingsDialog.xaml`
- Modify: `Views/Dialogs/EqualizerDialog.xaml`
- Modify: `Views/Dialogs/PromptDialog.xaml`

对每个对话框重复以下 4 步（Title 分别为：设置 / 均衡器 / 输入）：

- [ ] **Step 1: Window 标签：加 shell+controls xmlns；`WindowStyle="ToolWindow"` → `WindowStyle="None"`**（保留 ResizeMode/ShowInTaskbar/WindowStartupLocation）。

- [ ] **Step 2: 加 WindowChrome（Window  opening 标签后、Window.Resources 或根 Grid 之前）**
```xml
    <shell:WindowChrome.WindowChrome>
        <shell:WindowChrome CaptionHeight="32" UseAeroCaptionButtons="False" GlassFrameThickness="0"/>
    </shell:WindowChrome.WindowChrome>
```

- [ ] **Step 3: 包裹根 Grid：外层 Grid(Row0=TitleBar, Row1=内层)**

before（根 Grid 开标签，保留其原 Margin 值 M）：
```xml
    <Grid Margin="M">
```
after：
```xml
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>
        <controls:TitleBar Grid.Row="0" Title="<标题>" ShowMaximize="False"/>
        <Grid Grid.Row="1" Margin="M">
```
并在 `</Window>` 之前补一个 `</Grid>`（关闭新外层）。

- [ ] **Step 4: 构建验证 + 提交（三个对话框一起）**

Run: `dotnet build D-player.sln -c Debug --nologo -v q` → 0 错误。
```bash
git add Views/Dialogs/SettingsDialog.xaml Views/Dialogs/EqualizerDialog.xaml Views/Dialogs/PromptDialog.xaml
git commit -m "feat(view): dialogs custom borderless chrome with TitleBar (Phase 17)"
```

---

### Task 5: Controls.xaml 追加控件深色隐式样式

**Files:**
- Modify: `Themes/Controls.xaml`（在 `</ResourceDictionary>` 之前追加）

- [ ] **Step 1: 追加 ComboBox + ComboBoxItem 深色模板**

```xml
    <!-- ComboBox 深色：toggle 边框 + 箭头 + 深色 popup -->
    <ControlTemplate x:Key="ComboBoxTemplate" TargetType="ComboBox">
        <Grid>
            <Border x:Name="border" Background="{StaticResource BackgroundSecondary}"
                    BorderBrush="{StaticResource BackgroundTertiary}" BorderThickness="1" CornerRadius="4">
                <Grid>
                    <ContentPresenter Margin="8,4,24,4" VerticalAlignment="Center"
                                      Content="{TemplateBinding SelectionBoxItem}"
                                      ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"/>
                    <Path Data="M0,0 L4,4 L8,0" Stroke="{StaticResource ForegroundSecondary}" StrokeThickness="1.5"
                          Width="8" Height="4" Stretch="Uniform" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,8,0"/>
                </Grid>
            </Border>
            <Popup x:Name="PART_Popup" Placement="Bottom" AllowsTransparency="True" PopupAnimation="Slide"
                   IsOpen="{TemplateBinding IsDropDownOpen}">
                <Border Background="{StaticResource BackgroundSecondary}" BorderBrush="{StaticResource BackgroundTertiary}"
                        BorderThickness="1" CornerRadius="4" MinWidth="{TemplateBinding ActualWidth}"
                        MaxHeight="{TemplateBinding MaxDropDownHeight}">
                    <ScrollViewer><ItemsPresenter/></ScrollViewer>
                </Border>
            </Popup>
        </Grid>
        <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="border" Property="BorderBrush" Value="{StaticResource AccentPrimary}"/>
            </Trigger>
            <Trigger Property="IsDropDownOpen" Value="True">
                <Setter TargetName="border" Property="BorderBrush" Value="{StaticResource AccentPrimary}"/>
            </Trigger>
        </ControlTemplate.Triggers>
    </ControlTemplate>
    <Style TargetType="ComboBox">
        <Setter Property="Foreground" Value="{StaticResource ForegroundPrimary}"/>
        <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
        <Setter Property="Template" Value="{StaticResource ComboBoxTemplate}"/>
    </Style>
    <Style TargetType="ComboBoxItem">
        <Setter Property="Foreground" Value="{StaticResource ForegroundPrimary}"/>
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Padding" Value="8,4"/>
        <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ComboBoxItem">
                    <Border x:Name="bd" Background="Transparent" Padding="{TemplateBinding Padding}">
                        <ContentPresenter/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="bd" Property="Background" Value="{StaticResource BackgroundTertiary}"/>
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="bd" Property="Background" Value="{StaticResource AccentPrimary}"/>
                            <Setter Property="Foreground" Value="{StaticResource BackgroundPrimary}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
```

- [ ] **Step 2: 追加 CheckBox 深色模板**

```xml
    <Style TargetType="CheckBox">
        <Setter Property="Foreground" Value="{StaticResource ForegroundPrimary}"/>
        <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="CheckBox">
                    <StackPanel Orientation="Horizontal">
                        <Border x:Name="box" Width="16" Height="16" CornerRadius="3"
                                Background="{StaticResource BackgroundSecondary}"
                                BorderBrush="{StaticResource BackgroundTertiary}" BorderThickness="1">
                            <Path x:Name="check" Data="M3,8 L6.5,11.5 L13,4.5" Stroke="{StaticResource AccentPrimary}"
                                  StrokeThickness="2" Stretch="Uniform" Width="10" Height="10"
                                  HorizontalAlignment="Center" VerticalAlignment="Center" Visibility="Collapsed"/>
                        </Border>
                        <ContentPresenter Margin="6,0,0,0" VerticalAlignment="Center"/>
                    </StackPanel>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="check" Property="Visibility" Value="Visible"/>
                            <Setter TargetName="box" Property="BorderBrush" Value="{StaticResource AccentPrimary}"/>
                        </Trigger>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="box" Property="BorderBrush" Value="{StaticResource AccentHover}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
```

- [ ] **Step 3: 追加 ScrollBar 深色模板（竖+横，隐藏箭头按钮）**

```xml
    <Style TargetType="ScrollBar">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Width" Value="10"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ScrollBar">
                    <Grid Background="Transparent">
                        <Track x:Name="PART_Track" IsDirectionReversed="True">
                            <Track.Thumb>
                                <Thumb>
                                    <Thumb.Template>
                                        <ControlTemplate TargetType="Thumb">
                                            <Border x:Name="thumb" Background="{StaticResource BackgroundTertiary}" CornerRadius="5" Margin="2"/>
                                            <ControlTemplate.Triggers>
                                                <Trigger Property="IsMouseOver" Value="True">
                                                    <Setter TargetName="thumb" Property="Background" Value="{StaticResource ForegroundDisabled}"/>
                                                </Trigger>
                                            </ControlTemplate.Triggers>
                                        </ControlTemplate>
                                    </Thumb.Template>
                                </Thumb>
                            </Track.Thumb>
                        </Track>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
        <Style.Triggers>
            <Trigger Property="Orientation" Value="Horizontal">
                <Setter Property="Width" Value="Auto"/>
                <Setter Property="Height" Value="10"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="ScrollBar">
                            <Grid Background="Transparent">
                                <Track x:Name="PART_Track" IsDirectionReversed="False">
                                    <Track.Thumb>
                                        <Thumb>
                                            <Thumb.Template>
                                                <ControlTemplate TargetType="Thumb">
                                                    <Border x:Name="thumb" Background="{StaticResource BackgroundTertiary}" CornerRadius="5" Margin="2"/>
                                                    <ControlTemplate.Triggers>
                                                        <Trigger Property="IsMouseOver" Value="True">
                                                            <Setter TargetName="thumb" Property="Background" Value="{StaticResource ForegroundDisabled}"/>
                                                        </Trigger>
                                                    </ControlTemplate.Triggers>
                                                </ControlTemplate>
                                            </Thumb.Template>
                                        </Thumb>
                                    </Track.Thumb>
                                </Track>
                            </Grid>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Trigger>
        </Style.Triggers>
    </Style>
```

- [ ] **Step 4: 追加 ToolTip / ContextMenu / MenuItem / Separator 深色样式**

```xml
    <Style TargetType="ToolTip">
        <Setter Property="Background" Value="{StaticResource BackgroundSecondary}"/>
        <Setter Property="Foreground" Value="{StaticResource ForegroundPrimary}"/>
        <Setter Property="BorderBrush" Value="{StaticResource BackgroundTertiary}"/>
        <Setter Property="Padding" Value="8,4"/>
    </Style>
    <Style TargetType="ContextMenu">
        <Setter Property="Background" Value="{StaticResource BackgroundSecondary}"/>
        <Setter Property="Foreground" Value="{StaticResource ForegroundPrimary}"/>
        <Setter Property="BorderBrush" Value="{StaticResource BackgroundTertiary}"/>
    </Style>
    <Style TargetType="MenuItem">
        <Setter Property="Foreground" Value="{StaticResource ForegroundPrimary}"/>
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Padding" Value="8,4"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="MenuItem">
                    <Border x:Name="bd" Background="Transparent" Padding="{TemplateBinding Padding}">
                        <ContentPresenter ContentSource="Header"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsHighlighted" Value="True">
                            <Setter TargetName="bd" Property="Background" Value="{StaticResource BackgroundTertiary}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style TargetType="Separator">
        <Setter Property="Background" Value="{StaticResource BackgroundTertiary}"/>
        <Setter Property="Height" Value="1"/>
        <Setter Property="Margin" Value="4,2"/>
    </Style>
```

- [ ] **Step 5: 构建 + 测试验证**

Run: `dotnet build D-player.sln -c Debug --nologo -v q` → 0 错误。
Run: `dotnet test D-player.sln -c Debug --nologo -v q` → 96 通过 / 0 失败。

- [ ] **Step 6: 提交**

```bash
git add Themes/Controls.xaml
git commit -m "feat(theme): dark implicit styles for ComboBox/CheckBox/ScrollBar/ToolTip/ContextMenu (Phase 17)"
```

---

### Task 6: GUI 验收（ComputerUse）+ 终验

**Files:** 无代码改动（验证）

- [ ] **Step 1: 构建 + 全量测试**

Run: `dotnet build …` → 0 错误；`dotnet test …` → 96 通过。

- [ ] **Step 2: 启动应用 + ComputerUse 截图核对**

启动 `bin\Debug\net10.0-windows\D-player.exe`，核对：
1. 主窗/设置/均衡器/输入窗标题栏为深色自绘（含矢量最小化/最大化/关闭按钮）；
2. 拖动标题栏可移动；最大化/还原正常且边距正确（内容不贴屏边）；
3. ComboBox 展开 popup 深色、选项悬停/选中态正确；
4. CheckBox 勾选显示 accent 勾；
5. ScrollBar 深色（仅 track+thumb）；ToolTip/ContextMenu 深色；
6. 无残留浅色原生元素。
返回截图路径；主代理独立读取≥ 2 张复核。验收完恢复状态并关闭应用。

- [ ] **Step 3: （仅当有文档微调）提交**

---

## 附：本计划自检结果

**1. 规格覆盖**：spec §2 标题栏→Task 2/3/4；§3 ComboBox/CheckBox/ScrollBar/ToolTip/ContextMenu→Task 5；§1 新图标→Task 1；§5 验收→Task 6。无遗漏。
**2. 占位符扫描**：TitleBar/各模板为完整 XAML/C#；窗口修改给出 before/after；无 TBD/TODO。
**3. 类型/名称一致性**：`TitleBar` 的 DP（Title/ShowMaximize）、按钮名（MinButton/MaxRestoreButton/CloseButton/MaxIcon/RestoreIcon）、`Icon.Maximize/Restore`、`shell:` xmlns 在各 Task 一致；WindowChrome 属性（CaptionHeight=32/UseAeroCaptionButtons=False/GlassFrameThickness=0）一致。

**预期产出**：Icons.xaml +2 几何；TitleBar 新控件；4 窗口 chrome；Controls.xaml 追加深色样式；功能行为零变化；测试保持 96 全绿。
