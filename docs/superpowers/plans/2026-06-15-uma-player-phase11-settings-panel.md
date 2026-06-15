# Phase 11: Settings Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 UmaPlayer 添加设置面板 — 模态对话框，默认音量滑块可编辑，音频输出灰色占位，PlayerBar ⚙ 按钮 + Ctrl+, 快捷键入口。

**Architecture:** 新建 `SettingsDialog`（Window，同 PromptDialog 模式），直接读写 `ISettingsPersistence`，不加 SettingsViewModel（YAGNI）。修改 `PlayerBar` 加 ⚙ 按钮，修改 `MainWindow` 加 Ctrl+, 快捷键。

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, xUnit, NSubstitute

---

### Task 1: SettingsDialog — XAML 布局 + Code-behind

**Files:**
- Create: `Views/Dialogs/SettingsDialog.xaml`
- Create: `Views/Dialogs/SettingsDialog.xaml.cs`

- [ ] **Step 1: Create SettingsDialog.xaml**

```xml
<!-- Views/Dialogs/SettingsDialog.xaml -->
<Window x:Class="UmaPlayer.Views.Dialogs.SettingsDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Settings"
        Width="420" Height="280"
        WindowStartupLocation="CenterOwner"
        WindowStyle="ToolWindow"
        ResizeMode="NoResize"
        ShowInTaskbar="False"
        Background="{StaticResource BackgroundPrimary}"
        Foreground="{StaticResource ForegroundPrimary}">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>  <!-- General 标题 -->
            <RowDefinition Height="Auto"/>  <!-- General 卡片 -->
            <RowDefinition Height="Auto"/>  <!-- Audio Output 标题 -->
            <RowDefinition Height="Auto"/>  <!-- Audio Output 卡片 -->
            <RowDefinition Height="Auto"/>  <!-- 占位说明 -->
            <RowDefinition Height="*"/>     <!-- 间距 -->
            <RowDefinition Height="Auto"/>  <!-- 按钮 -->
        </Grid.RowDefinitions>

        <!-- General 标题 -->
        <TextBlock Grid.Row="0"
                   Text="General"
                   Style="{StaticResource HeaderText}"
                   Margin="0,0,0,8"/>

        <!-- General 卡片：音量滑块 -->
        <Border Grid.Row="1"
                Background="{StaticResource BackgroundSecondary}"
                CornerRadius="8" Padding="16"
                Margin="0,0,0,16">
            <StackPanel>
                <Grid>
                    <TextBlock Text="Default volume"
                               Style="{StaticResource BodyText}"
                               VerticalAlignment="Center"/>
                    <TextBlock x:Name="VolumePercent"
                               Text="80%"
                               Style="{StaticResource CaptionText}"
                               HorizontalAlignment="Right"
                               VerticalAlignment="Center"/>
                </Grid>
                <Slider x:Name="VolumeSlider"
                        Minimum="0" Maximum="1"
                        IsMoveToPointEnabled="True"
                        Margin="0,8,0,0"
                        ValueChanged="VolumeSlider_ValueChanged"/>
            </StackPanel>
        </Border>

        <!-- Audio Output 标题 -->
        <TextBlock Grid.Row="2"
                   Text="Audio Output"
                   Style="{StaticResource HeaderText}"
                   Margin="0,0,0,8"/>

        <!-- Audio Output 卡片：灰色占位 -->
        <Border Grid.Row="3"
                Background="{StaticResource BackgroundSecondary}"
                CornerRadius="8" Padding="16">
            <Grid>
                <TextBlock Text="Output"
                           Style="{StaticResource BodyText}"
                           VerticalAlignment="Center"/>
                <TextBlock Text="System default — WASAPI Shared"
                           Foreground="{StaticResource ForegroundDisabled}"
                           HorizontalAlignment="Right"
                           VerticalAlignment="Center"/>
            </Grid>
        </Border>

        <!-- 占位说明 -->
        <TextBlock Grid.Row="4"
                   Text="Audio output settings will be available in Phase 12"
                   Style="{StaticResource CaptionText}"
                   FontStyle="Italic"
                   Margin="0,4,0,0"/>

        <!-- 按钮栏 -->
        <StackPanel Grid.Row="6"
                    Orientation="Horizontal"
                    HorizontalAlignment="Right">
            <Button Content="Cancel"
                    Width="80" Height="28"
                    Margin="0,0,8,0"
                    IsCancel="True"/>
            <Button Content="Save"
                    Width="80" Height="28"
                    IsDefault="True"
                    Click="Save_Click"/>
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 2: Create SettingsDialog.xaml.cs**

```csharp
// Views/Dialogs/SettingsDialog.xaml.cs
using System.Windows;
using UmaPlayer.Services;

namespace UmaPlayer.Views.Dialogs;

/// <summary>
/// Phase 11 设置对话框。模态显示，默认音量滑块可编辑，音频输出灰色占位。
/// 与 PromptDialog 模式一致：静态 Show() 工厂 + modal ShowDialog()。
/// </summary>
public partial class SettingsDialog : Window
{
    private readonly ISettingsPersistence _persistence;

    public SettingsDialog(ISettingsPersistence persistence)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// 模态显示设置对话框。owner 用于居中。
    /// 返回 true = 用户保存，false = 取消/关闭。
    /// </summary>
    public static bool Show(Window? owner, ISettingsPersistence persistence)
    {
        var dlg = new SettingsDialog(persistence) { Owner = owner };
        return dlg.ShowDialog() == true;
    }

    /// <summary>
    /// 加载时从 persistence 读取当前音量设置。
    /// 同步阻塞读盘：与 PlayerViewModel.Initialize() 和 MainWindow 构造函数同模式。
    /// 文件极小（几百字节），阻塞 < 1ms。
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var settings = _persistence.LoadAsync().GetAwaiter().GetResult();
        VolumeSlider.Value = settings.DefaultVolume;
        VolumePercent.Text = $"{settings.DefaultVolume:P0}";
    }

    /// <summary>拖动滑块时实时更新百分比显示。</summary>
    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // null 检查防止 InitializeComponent 期间的初始触发
        if (VolumePercent != null)
            VolumePercent.Text = $"{e.NewValue:P0}";
    }

    /// <summary>
    /// 保存：读 Slider.Value → UpdateAsync → DialogResult = true → Close。
    /// with 表达式保持其他字段不变（OutputMode, PreferredDeviceId, WindowLeft/Top/Width/Height）。
    /// </summary>
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var volume = (float)VolumeSlider.Value;
        await _persistence.UpdateAsync(s => s with { DefaultVolume = volume }).ConfigureAwait(true);
        DialogResult = true;
        Close();
    }
}
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build UmaPlayer.csproj -c Debug`
Expected: Build succeeded, 0 errors

- [ ] **Step 4: Commit**

```bash
git add Views/Dialogs/SettingsDialog.xaml Views/Dialogs/SettingsDialog.xaml.cs
git commit -m "feat(view): add SettingsDialog with volume slider (Phase 11)"
```

---

### Task 2: PlayerBar — ⚙ 按钮入口

**Files:**
- Modify: `Views/Controls/PlayerBar.xaml:124-134`
- Modify: `Views/Controls/PlayerBar.xaml.cs`

- [ ] **Step 1: Add gear button to PlayerBar.xaml**

在 `Views/Controls/PlayerBar.xaml` 的 Row 2 右侧 `StackPanel`（line 124）中，在 `</StackPanel>` 结束标签之前（line 134 之前）添加 ⚙ 按钮：

```xml
                <!-- ⚙ 设置按钮：Phase 11 -->
                <Button Width="32" Height="32"
                        Padding="0" Margin="8,0,0,0"
                        Background="Transparent" BorderThickness="0"
                        Click="SettingsBtn_Click"
                        ToolTip="Settings (Ctrl+,)">
                    <TextBlock Text="&#x2699;" FontSize="14"
                               Foreground="{StaticResource ForegroundSecondary}"/>
                </Button>
```

最终右侧 StackPanel 结构：
```xml
            <!-- 右对齐：🔊/🔇 静音切换 + 音量滑块 + ⚙ 设置 -->
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Center">
                <Button Command="{Binding ToggleMuteCommand}" Width="32" Height="32"
                        Padding="0"
                        Background="Transparent" BorderThickness="0">
                    <TextBlock Text="{Binding VolumeIcon}" FontSize="14"
                               Foreground="{StaticResource ForegroundPrimary}"/>
                </Button>
                <Slider Width="100" VerticalAlignment="Center"
                        Minimum="0" Maximum="1" IsMoveToPointEnabled="True"
                        Value="{Binding Volume, Mode=TwoWay}"/>
                <!-- ⚙ 设置按钮：Phase 11 -->
                <Button Width="32" Height="32"
                        Padding="0" Margin="8,0,0,0"
                        Background="Transparent" BorderThickness="0"
                        Click="SettingsBtn_Click"
                        ToolTip="Settings (Ctrl+,)">
                    <TextBlock Text="&#x2699;" FontSize="14"
                               Foreground="{StaticResource ForegroundSecondary}"/>
                </Button>
            </StackPanel>
```

- [ ] **Step 2: Add SettingsBtn_Click handler to PlayerBar.xaml.cs**

在 `Views/Controls/PlayerBar.xaml.cs` 的 `using` 区域添加：
```csharp
using UmaPlayer.Services;
using UmaPlayer.Views.Dialogs;
```

在类末尾（`SeekBar_DragCompleted` 方法之后）添加：
```csharp
    /// <summary>点击 ⚙ 按钮打开设置对话框。</summary>
    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        var persistence = App.GetService<ISettingsPersistence>();
        SettingsDialog.Show(Window.GetWindow(this), persistence);
    }
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build UmaPlayer.csproj -c Debug`
Expected: Build succeeded, 0 errors

- [ ] **Step 4: Commit**

```bash
git add Views/Controls/PlayerBar.xaml Views/Controls/PlayerBar.xaml.cs
git commit -m "feat(view): PlayerBar adds gear button for settings (Phase 11)"
```

---

### Task 3: MainWindow — Ctrl+, 快捷键

**Files:**
- Modify: `Views/MainWindow.xaml`
- Modify: `Views/MainWindow.xaml.cs`

- [ ] **Step 1: Add KeyDown event to MainWindow.xaml**

在 `Views/MainWindow.xaml` 的 `<Window>` 元素上，在 `Closing="Window_Closing"` 之后添加：

```xml
        KeyDown="MainWindow_KeyDown"
```

完整 Window 标签变为：
```xml
<Window x:Class="UmaPlayer.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="clr-namespace:UmaPlayer.Views.Controls"
        Title="UmaPlayer"
        Width="800" Height="650"
        MinWidth="600" MinHeight="500"
        Background="{StaticResource BackgroundPrimary}"
        Foreground="{StaticResource ForegroundPrimary}"
        WindowStartupLocation="Manual"
        Closing="Window_Closing"
        KeyDown="MainWindow_KeyDown">
```

- [ ] **Step 2: Add MainWindow_KeyDown handler**

在 `Views/MainWindow.xaml.cs` 的 `using` 区域添加（如果尚不存在）：
```csharp
using System.Windows.Input;
using UmaPlayer.Services;
using UmaPlayer.Views.Dialogs;
```

在类末尾（`Window_Closing` 方法之后）添加：
```csharp
    /// <summary>Ctrl+, 打开设置对话框。</summary>
    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.OemComma && Keyboard.Modifiers == ModifierKeys.Control)
        {
            var persistence = App.GetService<ISettingsPersistence>();
            SettingsDialog.Show(this, persistence);
            e.Handled = true;
        }
    }
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build UmaPlayer.csproj -c Debug`
Expected: Build succeeded, 0 errors

- [ ] **Step 4: Commit**

```bash
git add Views/MainWindow.xaml Views/MainWindow.xaml.cs
git commit -m "feat(view): MainWindow adds Ctrl+, shortcut for settings (Phase 11)"
```

---

### Task 4: Documentation — PROJECT.md + COUPLING.md

**Files:**
- Modify: `docs/PROJECT.md`
- Modify: `docs/COUPLING.md`

- [ ] **Step 1: Update PROJECT.md**

在 §1 项目简介末尾添加：
```
Phase 11 添加设置对话框（默认音量滑块 + 音频输出灰色占位 + PlayerBar ⚙ 按钮 + Ctrl+, 快捷键）。
```

在 §1.1 关键特性表格中添加行：
```
| 设置 | 模态对话框：默认音量滑块；音频输出占位（Phase 12）；Ctrl+, 快捷键 (Phase 11) |
```

在 §3 目录结构的 `Views/Dialogs/` 下添加：
```
│       ├── PromptDialog.xaml(.cs)    # 共享单输入对话框（新建/重命名歌单）(Phase 6)
│       └── SettingsDialog.xaml(.cs)  # 设置对话框（音量 + 音频输出占位）(Phase 11)
```

在 §4.3 关键设计决策中添加 #20：
```
20. **Phase 11 SettingsDialog 不加 SettingsViewModel（YAGNI）**：仅 DefaultVolume 可编辑，OutputMode/PreferredDeviceId 无消费者。对话框直接读写 `ISettingsPersistence`。Phase 12 音频设置有复杂交互（如切换输出模式需重载设备列表）时再引入 SettingsViewModel。
```

在 §5.5 Views 的 Dialogs 子节添加：
```
- **`SettingsDialog`** *(Window, Phase 11)*：设置对话框。模态 ToolWindow（420×280），General 区域音量滑块（0..1, IsMoveToPointEnabled）+ Audio Output 灰色占位。静态 `Show(Window?, ISettingsPersistence)` 工厂方法。Loaded 同步读盘加载当前音量；Save_Click 通过 `UpdateAsync(s => s with { DefaultVolume = v })` 原子写盘。
```

在 §10 历史中添加 Phase 11 条目。

- [ ] **Step 2: Update COUPLING.md**

在 §2 依赖表中添加行：
```
| `SettingsDialog` | `ISettingsPersistence` | `Window`, `App.GetService<>()` |
```

在 §5 隐式契约表中添加：
```
| SettingsDialog.Show 在 Loaded 中同步读盘 (.GetAwaiter().GetResult()) | SettingsDialog.xaml.cs:OnLoaded | 与 PlayerViewModel.Initialize() 和 MainWindow ctor 同模式；文件极小（几百字节） |
```

在 §6 启动检查清单中添加：
```
- [x] 设置对话框（Phase 11）—— 模态 Window，PlayerBar ⚙ 按钮 + Ctrl+, 快捷键；不加 SettingsViewModel（YAGNI）
```

在 §7 不要做的事中添加：
```
- ❌ **为 Phase 11 设置对话框加 SettingsViewModel** —— 仅 DefaultVolume 可编辑，对话框直接读写 ISettingsPersistence；Phase 12 音频设置有复杂交互时再加
```

- [ ] **Step 3: Commit**

```bash
git add docs/PROJECT.md docs/COUPLING.md
git commit -m "docs: update PROJECT.md and COUPLING.md for Phase 11"
```

---

### Task 5: Build + Run + Manual Acceptance

- [ ] **Step 1: Full build**

Run: `dotnet build UmaPlayer.sln -c Debug`
Expected: Build succeeded, 0 errors, 0 warnings

- [ ] **Step 2: Run existing tests**

Run: `dotnet test Tests/UmaPlayer.Tests.csproj -v minimal`
Expected: All 76 tests pass (no regressions)

- [ ] **Step 3: Run the app**

Run: `dotnet run --project UmaPlayer.csproj`
Expected: App launches, PlayerBar shows ⚙ button after volume slider

- [ ] **Step 4: Manual acceptance**

1. 点击 ⚙ 按钮 → 设置对话框打开，居中显示在主窗口上
2. 对话框显示当前默认音量（与 PlayerBar 滑块一致）
3. 拖动滑块 → 百分比实时更新
4. 点击 Save → PlayerBar 音量滑块同步更新
5. 关闭应用 → 重启 → 点击 ⚙ → 音量保持上次保存的值
6. 按 Ctrl+, → 对话框打开
7. 按 Escape → 对话框关闭（不保存）
8. Audio Output 区域显示灰色 "System default — WASAPI Shared"
9. 工具栏/侧边栏/播放栏所有按钮图标完整显示（无裁切）

- [ ] **Step 5: Final commit (if any fixes needed)**

```bash
git add -A
git commit -m "fix: address Phase 11 manual acceptance findings"
```
