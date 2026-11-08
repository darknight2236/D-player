# Phase 23 WinUI 频谱与 10 段 EQ 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 WinUI 壳交付 32 柱频谱（含设置）与 10 段 EQ（底栏入口 + 对话框），不改 Core、不改 WPF 行为。

**Architecture:** 频谱用 `Canvas + 32 Rectangle`，由 `CompositionTarget.Rendering` 插值更新，数据直接取 `PlayerViewModel`；EQ 复用 `IPlaybackService.EqualizerConfig` 的实时下发链，新增 ContentDialog 做预览/取消/保存。设置与 EQ 的持久化分别走 `PlayerViewModel` 既有可观察属性与 `ISettingsPersistence`。

**Tech Stack:** .NET 10、WinUI 3 / Windows App SDK 2.5.1（实际构建路径 Microsoft.WindowsAppSDK.WinUI 2.3.9）、D-player.Core、无新增 NuGet。

**Spec:** `docs/superpowers/specs/2026-10-27-d-player-phase23-spectrum-eq-design.md`

## Global Constraints

- 在 `master` 工作；不引新包、不改 Core 公开面、不改 WPF 壳行为与外观。
- 门禁：`.slnf` 构建 0/0、测试 186/0、WinUI 构建 0/0、`verify-gates.ps1 -Full` 3/3；`dotnet test` 不得加 `--nologo`。
- 文件保持 `i/lf w/crlf`；PowerShell 临时脚本放 `%TEMP%` 且纯 ASCII；不得直接读写 `%LOCALAPPDATA%\D-player*`。
- 真机验证只能经应用 UI / UIA；不伪造通过，用户专属听感项明确交给用户。
- 每个 Task 完成后单独提交；**推送必须由用户另行明确指示**。
- 不改 Phase 20/21 的历史 spec、plan、验收报告。

---

### Task 1: 频谱控件与设置项

**Files:**
- Create: `D-player.WinUI/Views/SpectrumView.xaml`
- Create: `D-player.WinUI/Views/SpectrumView.xaml.cs`
- Modify: `D-player.WinUI/Views/InfoPanel.xaml.cs`
- Modify: `D-player.WinUI/Views/Dialogs/SettingsDialog.xaml`
- Modify: `D-player.WinUI/Views/Dialogs/SettingsDialog.xaml.cs`

**Interfaces:**
- Consumes: `PlayerViewModel.SpectrumData`（`float[]`，32 项）、`SpectrumEnabled`（bool）、`SpectrumSensitivity`（double）、`SpectrumColorTheme`（int）、`SpectrumSmoothing`（double）、`SpectrumColorThemes`（`string[]`）。
- Produces: `SpectrumView(PlayerViewModel player)`；InfoPanel 通过 `ContentHost.Children.Add(...)` 使用，Task 2 不依赖本控件内部成员。

- [ ] **Step 1: 创建 SpectrumView 的 XAML 外壳**

`D-player.WinUI/Views/SpectrumView.xaml` 只保留一个 Canvas，柱子由代码创建：

```xml
<UserControl
    x:Class="DPlayer.WinUI.Views.SpectrumView"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Canvas x:Name="SpectrumCanvas" />
</UserControl>
```

- [ ] **Step 2: 实现 32 柱布局、主题与渲染循环**

`SpectrumView.xaml.cs` 使用以下固定常量与字段，值与 WPF `Views/Controls/SpectrumView.xaml.cs` 一致：

```csharp
private const int BarCount = 32;
private const double BarSpacing = 2.0;
private const double MinBarHeight = 2.0;
private const double MaxBarHeight = 118.0;
private const double AnimationSmoothFactor = 0.3;

private readonly PlayerViewModel _player;
private readonly Rectangle[] _bars = new Rectangle[BarCount];
private readonly LinearGradientBrush[] _gradients = new LinearGradientBrush[3];
private readonly SolidColorBrush[] _rainbowBrushes = new SolidColorBrush[BarCount];
private readonly double[] _targetHeights = new double[BarCount];
```

构造函数与生命周期：

```csharp
public SpectrumView(PlayerViewModel player)
{
    _player = player;
    InitializeComponent();
    InitializeGradients();
    Loaded += (_, _) =>
    {
        RecalculateBarLayout();
        ApplyTheme();
        UpdateTargets(_player.SpectrumData);
        UpdateVisibility();
        _player.PropertyChanged += Player_PropertyChanged;
        CompositionTarget.Rendering += OnRendering;
    };
    Unloaded += (_, _) =>
    {
        CompositionTarget.Rendering -= OnRendering;
        _player.PropertyChanged -= Player_PropertyChanged;
    };
    SizeChanged += (_, _) => RecalculateBarLayout();
}
```

`Player_PropertyChanged` 只处理三项：`SpectrumData` → `UpdateTargets`；`SpectrumColorTheme` → `ApplyTheme`；`SpectrumEnabled` → `UpdateVisibility`。`UpdateVisibility` 把本控件 `Visibility` 设为 `Visible/Collapsed`，右栏折叠仍由 InfoPanel 现有淡出/Clip 负责，不新增逻辑。

`RecalculateBarLayout` 在 `ActualWidth > 0` 时清空并重建柱子：`barWidth = (ActualWidth - 31 * 2) / 32`，每柱 `RadiusX=RadiusY=2`、`Height=2`、`Canvas.SetLeft(bar, i * (barWidth + 2))`、`Canvas.SetBottom(bar, 0)`。`UpdateTargets` 仅在数组长度 ≥32 时写入：

```csharp
for (int i = 0; i < BarCount; i++)
    _targetHeights[i] = MinBarHeight + data[i] * (MaxBarHeight - MinBarHeight);
```

`OnRendering` 的 WinUI 签名必须是 `(object? sender, object e)`（不是 WPF 的 `EventArgs`），只改 `Height`：

```csharp
private void OnRendering(object? sender, object e)
{
    for (int i = 0; i < BarCount; i++)
    {
        if (_bars[i] is not { } bar) continue;
        var target = Math.Clamp(_targetHeights[i], MinBarHeight, MaxBarHeight);
        bar.Height += (target - bar.Height) * AnimationSmoothFactor;
    }
}
```

画刷：

- 主题 0：`#7C4DFF → #9E7CFF`；主题 1：`#2196F3 → #64B5F6`；主题 2：`#4CAF50 → #81C784`。均为 `StartPoint=(0,1)`、`EndPoint=(0,0)` 的竖向渐变。
- 主题 3：第 i 柱 hue = `300f / 31f * i`（红到紫），逐柱一个 `SolidColorBrush`。HSV→RGB 直接移植 WPF `HsvToRgb`，返回类型改为 `Windows.UI.Color`（用 `ColorHelper.FromRgb`）。
- `ApplyTheme`：主题 3 逐柱赋 `_rainbowBrushes[i]`；其余主题统一赋 `_gradients[Math.Clamp(theme, 0, 2)]`。

- [ ] **Step 3: 把频谱注入 InfoPanel 底部**

只改 `InfoPanel.xaml.cs` 构造函数，保留 `SpectrumView(PlayerViewModel)` 构造，不改 XAML：

```csharp
InitializeComponent();

ContentHost.Children.Add(new SpectrumView(player)
{
    Height = 120,
    Margin = new Thickness(0, 8, 0, 0),
});
```

插入点在 `Panel.SizeChanged` 订阅之前；频谱成为 `ContentHost` 的最后一项，位于采样率文本之后。右栏收起时的淡出、Clip、48px 宽度逻辑不改。

- [ ] **Step 4: SettingsDialog 增加四个频谱设置区**

在 `SettingsDialog.xaml` 的音量区之后新增以下命名控件（仍放在现有 360px 宽 StackPanel 中）：

```xml
<StackPanel Spacing="{StaticResource SpacingSm}">
    <CheckBox x:Name="SpectrumEnabledCheckBox" Content="启用频谱"
              Checked="SpectrumEnabled_Changed" Unchecked="SpectrumEnabled_Changed" />
</StackPanel>
<StackPanel Spacing="{StaticResource SpacingSm}">
    <TextBlock Text="频谱灵敏度" Style="{StaticResource BodyStrongTextBlockStyle}" />
    <Grid ColumnSpacing="{StaticResource SpacingSm}">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <Slider x:Name="SensitivitySlider" Minimum="0.5" Maximum="2" StepFrequency="0.05"
                ValueChanged="SensitivitySlider_ValueChanged" AutomationProperties.Name="频谱灵敏度" />
        <TextBlock x:Name="SensitivityValue" Grid.Column="1" VerticalAlignment="Center" />
    </Grid>
</StackPanel>
<StackPanel Spacing="{StaticResource SpacingSm}">
    <TextBlock Text="频谱主题" Style="{StaticResource BodyStrongTextBlockStyle}" />
    <ComboBox x:Name="ThemeComboBox" SelectionChanged="ThemeComboBox_SelectionChanged"
              AutomationProperties.Name="频谱主题" />
</StackPanel>
<StackPanel Spacing="{StaticResource SpacingSm}">
    <TextBlock Text="频谱平滑度" Style="{StaticResource BodyStrongTextBlockStyle}" />
    <Grid ColumnSpacing="{StaticResource SpacingSm}">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <Slider x:Name="SmoothingSlider" Minimum="0" Maximum="0.95" StepFrequency="0.05"
                ValueChanged="SmoothingSlider_ValueChanged" AutomationProperties.Name="频谱平滑度" />
        <TextBlock x:Name="SmoothingValue" Grid.Column="1" VerticalAlignment="Center" />
    </Grid>
</StackPanel>
```

`SettingsDialog.xaml.cs` 的 `OnLoaded` 在 `_ready = true` 之前填充初值：

```csharp
SpectrumEnabledCheckBox.IsChecked = _player.SpectrumEnabled;
SensitivitySlider.Value = _player.SpectrumSensitivity;
ThemeComboBox.ItemsSource = _player.SpectrumColorThemes;
ThemeComboBox.SelectedIndex = _player.SpectrumColorTheme;
SmoothingSlider.Value = _player.SpectrumSmoothing;
UpdateSpectrumValueLabels();
_ready = true;
```

四个处理器在 `if (!_ready) return;` 后分别执行：

```csharp
_player.SpectrumEnabled = SpectrumEnabledCheckBox.IsChecked == true;
_player.SpectrumSensitivity = SensitivitySlider.Value;
_player.SpectrumColorTheme = ThemeComboBox.SelectedIndex;
_player.SpectrumSmoothing = SmoothingSlider.Value;
```

`UpdateSpectrumValueLabels` 用 `SensitivityValue.Text = $"{SensitivitySlider.Value:0.00}"`、`SmoothingValue.Text = $"{SmoothingSlider.Value:0.00}"`，两个滑块处理器先更新标签再写 VM。VM 的 `On…Changed` 负责持久化，本对话框不另写 `AppSettings`。

- [ ] **Step 5: 构建与门禁**

```bash
dotnet build D-player.slnf -c Debug --nologo -v q
dotnet test D-player.slnf -c Debug -v q
dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q
powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-gates.ps1 -Full
```

预期：构建均 0/0、测试 186/0、Full 3/3。任何 Core 意外改动立即停止并回到 spec，不得顺手扩大范围。

- [ ] **Step 6: 真机验收 A 组并留证**

启动 WinUI，播一首已有曲目，记录到 `%TEMP%\p23-acc\`：

- A1 播放时 32 柱随音乐变化，暂停后回落到接近 2px；截图 `spectrum-playing.png`。
- A2 设置里依次选紫/蓝/绿/彩虹，柱色即时切换；截图 `spectrum-rainbow.png`。
- A3 调灵敏度到 2.0 与 0.5，柱高差异可见；调平滑度到 0 与 0.9，跳动由急到缓。
- A4 取消“启用频谱”后频谱区消失，重勾恢复；重启应用后四项设置仍为离开前值。
- A5 右栏折叠/展开、窗口 <960 自动收起/恢复时，频谱不残留、不盖住中列表；小窗高时 InfoPanel 可滚动，采样率不被裁掉。

听感不是 A 组判据；以 UIA、截图和设置读回为证。任一项失败先修再进入下一步。

- [ ] **Step 7: 提交 Task 1**

```bash
git add D-player.WinUI/Views/SpectrumView.xaml D-player.WinUI/Views/SpectrumView.xaml.cs D-player.WinUI/Views/InfoPanel.xaml.cs D-player.WinUI/Views/Dialogs/SettingsDialog.xaml D-player.WinUI/Views/Dialogs/SettingsDialog.xaml.cs
git commit -m "feat(winui): add the 32-bar spectrum and its settings"
```

---

### Task 2: 底栏 EQ 按钮与均衡器对话框

**Files:**
- Modify: `D-player.WinUI/App.xaml.cs`
- Modify: `D-player.WinUI/MainWindow.xaml.cs`
- Modify: `D-player.WinUI/Views/PlayerBar.xaml`
- Modify: `D-player.WinUI/Views/PlayerBar.xaml.cs`
- Create: `D-player.WinUI/Views/Dialogs/EqualizerDialog.xaml`
- Create: `D-player.WinUI/Views/Dialogs/EqualizerDialog.xaml.cs`

**Interfaces:**
- Consumes: Task 1 的频谱不依赖本任务；本任务使用 `IPlaybackService.EqualizerConfig`、`ISettingsPersistence.UpdateAsync`、`PlayerViewModel.EqualizerEnabled`。
- Produces:
  - `MainWindow(MainViewModel vm, ISettingsPersistence settings, IPlaybackService playbackService)`
  - `PlayerBar(PlayerViewModel player, PlaylistsViewModel playlists, IPlaybackService playbackService, ISettingsPersistence settings)`
  - `EqualizerDialog(IPlaybackService playbackService, ISettingsPersistence persistence, PlayerViewModel player)`

- [ ] **Step 1: 把 IPlaybackService 接入构造链**

`App.OnLaunched` 改为：

```csharp
var vm = _services.GetRequiredService<MainViewModel>();
var settings = _services.GetRequiredService<ISettingsPersistence>();
var playback = _services.GetRequiredService<IPlaybackService>();
var window = new MainWindow(vm, settings, playback);
```

`MainWindow` 增加只读字段 `_playbackService`，构造第三参接收；创建底栏改为：

```csharp
_bar = new PlayerBar(_vm.Player, _vm.Playlists, _playbackService, _settings);
```

`PlayerBar` 构造第四参接收 `_settings`，第三参接收 `_playbackService`。`MainViewModel`、其余三个控件的构造不变。

- [ ] **Step 2: 底栏加入 EQ 按钮与启用高亮**

在 `PlayerBar.xaml` 的 `SettingsButton` 前加入：

```xml
<Button x:Name="EqButton" Width="32" Height="32" Style="{StaticResource IconButtonStyle}"
        Click="EqButton_Click" AutomationProperties.Name="均衡器" ToolTipService.ToolTip="均衡器">
    <PathIcon x:Name="EqIcon"
              Data="M4,21 V14 M4,10 V3 M12,21 V12 M12,8 V3 M20,21 V16 M20,12 V3 M1,14 H7 M9,8 H15 M17,16 H23"
              Foreground="{ThemeResource TextFillColorSecondaryBrush}" />
</Button>
```

`PlayerBar.xaml.cs` 的 `SyncTransportVisuals` 在签名串中追加 `Player.EqualizerEnabled`，确保启用态变化会刷新：

```csharp
$"{Player.EqualizerEnabled}|" +
```

随后设置：

```csharp
EqIcon.Foreground = Player.EqualizerEnabled ? _accentBrush : _secondaryBrush;
```

`EqButton_Click`：

```csharp
private async void EqButton_Click(object sender, RoutedEventArgs e)
{
    var dialog = new EqualizerDialog(_playbackService, _settings, Player) { XamlRoot = XamlRoot };
    if (dialog.XamlRoot is null) return;
    try { await dialog.ShowAsync(); }
    catch (Exception ex) { AppLog.Write("[WinUI 壳] 打开均衡器失败", ex); }
}
```

需要补 `using DPlayer.Services;`（`IPlaybackService`、`ISettingsPersistence`）。

- [ ] **Step 3: 创建 EqualizerDialog 的 ContentDialog 外壳**

`EqualizerDialog.xaml` 用 `Title="均衡器"`，不放 Primary/Secondary 按钮；内容中显式放“恢复 Flat / 取消 / 保存”三个 Button，便于由代码控制关闭与回滚：

```xml
<ContentDialog
    x:Class="DPlayer.WinUI.Views.Dialogs.EqualizerDialog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="均衡器">
    <StackPanel Spacing="{StaticResource SpacingMd}" MinWidth="560">
        <CheckBox x:Name="EnableCheckBox" Content="启用均衡器"
                  Checked="Enable_Changed" Unchecked="Enable_Changed" />
        <ComboBox x:Name="PresetCombo" SelectionChanged="PresetCombo_SelectionChanged"
                  AutomationProperties.Name="均衡器预设" />
        <Grid x:Name="BandsPanel" Height="220" />
        <StackPanel Orientation="Horizontal" Spacing="{StaticResource SpacingSm}" HorizontalAlignment="Right">
            <Button Content="恢复 Flat" Click="RestoreFlat_Click" />
            <Button Content="取消" Click="Cancel_Click" />
            <Button Content="保存" Click="Save_Click" />
        </StackPanel>
    </StackPanel>
</ContentDialog>
```

- [ ] **Step 4: 实现预览、预设、取消回滚与保存**

字段：

```csharp
private readonly IPlaybackService _playbackService;
private readonly ISettingsPersistence _persistence;
private readonly PlayerViewModel _player;
private EqualizerConfig _initialConfig = new();
private bool _saved;
private bool _suppress;
private readonly Slider[] _bandSliders = new Slider[EqualizerPresets.BandCount];
private readonly TextBlock[] _bandLabels = new TextBlock[EqualizerPresets.BandCount];
private Slider _preampSlider = null!;
private TextBlock _preampLabel = null!;
```

构造函数记录三个参数，`Loaded += OnLoaded; Closed += OnClosed;`。

`OnLoaded` 顺序固定：先 `_initialConfig = _playbackService.EqualizerConfig`；再尝试 `LoadAsync` 并用 `EqualizerConfig.Create(settings.EqualizerEnabled, settings.EqualizerPreamp, settings.EqualizerBands, settings.EqualizerPreset)` 覆盖快照（失败保留实时链快照）；随后 `BuildBands()`；在 `_suppress=true` 下加入 `EqualizerPresets.Names` 和 `Custom`；最后 `ApplyConfigToUi(_initialConfig)`。

`BuildBands` 为 10 段各建一列，再建 Pre 列。每列是 `Grid` 三行：dB 标签、竖直 `Slider`（`Minimum=-12, Maximum=12, Orientation=Vertical, Height=170`）、频率标签（`hz >= 1000 ? $"{hz/1000:0.#}k" : $"{hz:0}"`）。把滑块存入 `_bandSliders` / `_preampSlider`，标签存入 `_bandLabels` / `_preampLabel`，全部 `ValueChanged += BandSlider_ValueChanged`。

关键方法保持以下语义：

```csharp
private double[] CurrentGains()
{
    var gains = new double[EqualizerPresets.BandCount];
    for (int b = 0; b < gains.Length; b++) gains[b] = _bandSliders[b].Value;
    return gains;
}

private void PushToService(string preset)
{
    _playbackService.EqualizerConfig = EqualizerConfig.Create(
        EnableCheckBox.IsChecked == true, _preampSlider.Value, CurrentGains(), preset);
    _player.EqualizerEnabled = EnableCheckBox.IsChecked == true;
}
```

`ApplyConfigToUi` 在 `_suppress` 内设启用、10 段、Pre 和 `PresetCombo.SelectedItem`，最后刷新标签。`BandSlider_ValueChanged` 在非 suppress 时刷新标签、用 `EqualizerPresets.Match(CurrentGains())` 回认预设、设置下拉（suppress）并 `PushToService(preset)`。

`PresetCombo_SelectionChanged` 跳过 `Custom`，`TryGet` 成功后调用 `ApplyGains(gains, name)`；`RestoreFlat_Click` 调用 `ApplyGains(gains, EqualizerPresets.Flat, preamp: 0)`。`ApplyGains` 在 suppress 下写滑块与预设，最后 `PushToService(preset)`。

`Save_Click` 为 `async void`：

```csharp
var preset = PresetCombo.SelectedItem as string ?? EqualizerPresets.Flat;
var enabled = EnableCheckBox.IsChecked == true;
await _persistence.UpdateAsync(s => s with
{
    EqualizerEnabled = enabled,
    EqualizerPreamp = _preampSlider.Value,
    EqualizerBands = CurrentGains(),
    EqualizerPreset = preset,
});
_player.EqualizerEnabled = enabled;
_saved = true;
Hide();
```

保存异常用 `AppLog.Write("[WinUI 壳] 保存 EQ 设置失败", ex)`，且不要把 `_saved` 置 true。`Cancel_Click` 只 `Hide()`。`OnClosed` 在 `!_saved` 时执行 `_playbackService.EqualizerConfig = _initialConfig; _player.EqualizerEnabled = _initialConfig.Enabled;`，撤销预览；保存成功不再回滚。

- [ ] **Step 5: 构建与门禁**

```bash
dotnet build D-player.slnf -c Debug --nologo -v q
dotnet test D-player.slnf -c Debug -v q
dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q
powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-gates.ps1 -Full
```

预期：0/0、186/0、0/0、3/3。重点检查新构造签名是否所有调用点都已更新、`PathIcon.Data` 是否被 XAML 编译器接受。

- [ ] **Step 6: 真机验收 B 组并留证**

- B1 打开 EQ，启用态、Preamp、10 段与预设与当前配置一致；启用时底栏 EQ 钮 Accent 高亮，关闭后恢复次级色。
- B2 选 Rock 后各段写入预设曲线；手动改任一段，预设变 Custom；恢复 Flat 后 Pre 与 10 段全 0。
- B3 播放中拖动任一段或 Pre，**由用户确认听感变化**；代理只记录配置/滑块读数，不代替用户宣称“听到了”。
- B4 改动后点取消，配置与启用态回到打开前；再次打开值一致。改动后点保存并重启应用，值与启用态保留。
- B5 无当前曲目时打开/编辑/保存不报错，不自动起播；播放中打开/关闭不停止、不切歌。
- B6 设置对话框、窄窗悬停音量弹层、空格播放/暂停不回退。

- [ ] **Step 7: 提交 Task 2**

```bash
git add D-player.WinUI/App.xaml.cs D-player.WinUI/MainWindow.xaml.cs D-player.WinUI/Views/PlayerBar.xaml D-player.WinUI/Views/PlayerBar.xaml.cs D-player.WinUI/Views/Dialogs/EqualizerDialog.xaml D-player.WinUI/Views/Dialogs/EqualizerDialog.xaml.cs
git commit -m "feat(winui): add the 10-band equalizer dialog and player-bar entry"
```

---

### Task 3: 全量回归、验收记录与文档同步

**Files:**
- Create: `docs/PHASE23-ACCEPTANCE.md`
- Modify: `docs/PROJECT.md`
- Modify: `docs/COUPLING.md`
- Modify: `README.md`
- Modify: `docs/PHASE20-COMPARISON.md`

**Interfaces:**
- Consumes: Task 1/2 已提交的实现与 A/B 组证据。
- Produces: 可复核的 Phase 23 验收记录与最新能力/契约文档；不新增代码接口。

- [ ] **Step 1: 跑 C 组回归并写验收记录**

逐项验证并记录到 `docs/PHASE23-ACCEPTANCE.md`：

- C1 双击/首播、上一首/下一首、随机/循环三态、进度拖动与单击、时间提示、静音/音量与窄窗悬停弹层。
- C2 左右栏折叠、<960 自动收起、手动优先、表头/行对齐、行尾移除确认框。
- C3 空格播放/暂停（焦点在按钮、滑块、文本框、对话框时不抢）、设置原音量行与日志入口。
- C4 门禁输出：`.slnf` 0/0、186/0、WinUI 0/0、Full 3/3。

文档结构固定为：范围与基线 → A 频谱 → B EQ → C 回归 → 交给用户的听感项 → 副作用与证据路径。B3 的听感项必须单列“由用户确认”，不得由代理填 PASS。

- [ ] **Step 2: 同步能力与契约文档**

- `docs/PROJECT.md`：在 WinUI 能力段补频谱/EQ、构造链（`IPlaybackService` 传入 MainWindow/PlayerBar）、频谱渲染生命周期；阶段历史与里程碑补 Phase 23，测试数仍 186。
- `docs/COUPLING.md` 新增三条：
  1. `SpectrumView` 必须在 `Unloaded` 退订 `CompositionTarget.Rendering` 与 `Player.PropertyChanged`，只逐帧改高度，不逐帧建柱子/画刷。
  2. EQ 对话框以打开时实时 `IPlaybackService.EqualizerConfig` 为快照；未保存关闭必须恢复快照与 `Player.EqualizerEnabled`，保存才写 `AppSettings`。
  3. `IPlaybackService` 经 App → MainWindow → PlayerBar 构造传入，不得在壳里新建第二份播放服务。
- `README.md`：WinUI 能力清单移除频谱/EQ 缺项，阶段表新增 Phase 23。
- `docs/PHASE20-COMPARISON.md` §3：曲目信息面板改 ✅（频谱已补）；“频谱/EQ/拖拽/排序/文件夹绑定管理”行改为“拖拽 / 排序 / 文件夹绑定管理”，并删除“频谱/EQ 的接法”两条，保留 Phase 24 的拖拽/排序/文件夹说明。

- [ ] **Step 3: 最终门禁与提交**

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-gates.ps1 -Full
git add docs/PHASE23-ACCEPTANCE.md docs/PROJECT.md docs/COUPLING.md README.md docs/PHASE20-COMPARISON.md
git commit -m "docs(phase23): record spectrum and EQ acceptance and sync the contracts"
```

完成后只报告实现、证据和交给用户的听感项；是否推送、是否进入 Phase 24 均由用户决定。
