# Phase 16 应用图标矢量化重构 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用统一描边矢量图标（`Themes/Icons.xaml` 几何资源）替换应用内全部 UTF-8/emoji 字形图标，使其可随主题着色、风格协调。

**Architecture:** 新建 `Themes/Icons.xaml`（每图标一条 24×24 `Geometry` + 共享 `IconPath` Path 样式），在 `App.xaml` 合并；转换器（PlayState/RepeatMode/IsMuted）改返回 `Geometry`；XAML 由 `TextBlock.Text`(emoji) 改 `Path.Data`；活跃态 `Path.Stroke` 复用 `BoolToAccentBrushConverter`；移除 VM 的 emoji 属性 `VolumeIcon`。纯表现层，不改功能行为。

**Tech Stack:** WPF / XAML ResourceDictionary + `Geometry`(path markup) / `Path` · CommunityToolkit.Mvvm · 几何数据源自 Feather/Lucide（MIT/ISC，需在 Icons.xaml 头注释署名）

**设计稿：** [`docs/superpowers/specs/2026-09-13-d-player-phase16-icon-refactor-design.md`](../specs/2026-09-13-d-player-phase16-icon-refactor-design.md)

**验证基线：** 每 Task 后 `dotnet build` 0 错误 + `dotnet test` 96 全绿（无测试引用被改转换器/VolumeIcon，涟漪≈0）；最终 ComputerUse GUI 截图确认无 emoji 残留、活跃态着色、▶ 标记正常。

---

## 文件结构

| 文件 | 责任 |
|------|------|
| `Themes/Icons.xaml`（新） | 全部图标 `Geometry` 资源（`Icon.*`）+ 共享 `IconPath` 样式 |
| `App.xaml`（改） | 合并 `Icons.xaml` |
| `Converters/PlayStateToIconConverter.cs`（改） | PlayState→`Geometry` |
| `Converters/RepeatModeToIconConverter.cs`（改） | RepeatMode→`Geometry` |
| `Converters/BoolToVolumeIconConverter.cs`（新） | IsMuted→`Geometry` |
| `ViewModels/PlayerViewModel.cs`（改） | 删除 `VolumeIcon` + 其 Notify 特性 |
| `Views/Controls/PlayerBar.xaml`（改） | 全部 emoji TextBlock → Path |
| `Views/Controls/PlaylistView.xaml(.cs)`（改） | marker/refresh/×/+添加/清空 → Path；code-behind Visibility + FindChildByName<Path> |
| `Views/Controls/PlaylistsSidebarView.xaml(.cs)`（改） | +/−/marker/folder/refresh → Path；code-behind 同上 |
| `docs/COUPLING.md`（改） | 更新「x:Name 定位 marker」契约（元素类型 TextBlock→Path） |

---

### Task 1: 图标资源 Themes/Icons.xaml + App.xaml 合并

**Files:**
- Create: `Themes/Icons.xaml`
- Modify: `App.xaml`

- [ ] **Step 1: 创建 `Themes/Icons.xaml`**（完整内容；几何为 Feather/Lucide 描边路径，24 viewbox）

```xml
<!--
    Themes/Icons.xaml —— 应用矢量图标集（Phase 16）。
    每图标一条 24x24 viewbox 描边型 Geometry（x:Key="Icon.<Name>"）。
    几何数据来源：Feather / Lucide 图标集（MIT / ISC license）—— 特此署名。
    使用：<Path Style="{StaticResource IconPath}" Data="{StaticResource Icon.Play}" Stroke="{StaticResource ForegroundPrimary}"/>
    活跃态：Stroke 绑 BoolToAccentBrushConverter。PlayMarker 为实心（Fill），其余描边（Stroke）。
-->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- 共享 Path 样式：尺寸/拉伸/描边粗细/圆头；Stroke 由使用处绑定以便主题化 -->
    <Style x:Key="IconPath" TargetType="Path">
        <Setter Property="Width" Value="16"/>
        <Setter Property="Height" Value="16"/>
        <Setter Property="Stretch" Value="Uniform"/>
        <Setter Property="Fill" Value="{x:Null}"/>
        <Setter Property="StrokeThickness" Value="1.75"/>
        <Setter Property="StrokeLineJoin" Value="Round"/>
        <Setter Property="StrokeStartLineCap" Value="Round"/>
        <Setter Property="StrokeEndLineCap" Value="Round"/>
    </Style>

    <!-- 传输 -->
    <Geometry x:Key="Icon.Play">M5,3 L19,12 L5,21 Z</Geometry>
    <Geometry x:Key="Icon.Pause">M6,4 H10 V20 H6 Z M14,4 H18 V20 H14 Z</Geometry>
    <Geometry x:Key="Icon.Prev">M19,20 L9,12 L19,4 Z M5,19 V5</Geometry>
    <Geometry x:Key="Icon.Next">M5,4 L15,12 L5,20 Z M19,5 V19</Geometry>

    <!-- 模式 -->
    <Geometry x:Key="Icon.Shuffle">M16,3 H21 V8 M4,20 L21,3 M21,16 V21 H16 M15,15 L21,21 M4,4 L9,9</Geometry>
    <Geometry x:Key="Icon.RepeatOff">M17,1 L21,5 L17,9 M3,11 V9 A4,4 0 0 1 7,5 H21 M7,23 L3,19 L7,15 M21,13 V15 A4,4 0 0 1 17,19 H3</Geometry>
    <Geometry x:Key="Icon.RepeatList">M17,1 L21,5 L17,9 M3,11 V9 A4,4 0 0 1 7,5 H21 M7,23 L3,19 L7,15 M21,13 V15 A4,4 0 0 1 17,19 H3</Geometry>
    <Geometry x:Key="Icon.RepeatOne">M17,1 L21,5 L17,9 M3,11 V9 A4,4 0 0 1 7,5 H21 M7,23 L3,19 L7,15 M21,13 V15 A4,4 0 0 1 17,19 H3 M11,10 L12.5,9 V15</Geometry>

    <!-- 音量 -->
    <Geometry x:Key="Icon.Volume">M11,5 L6,9 H2 V15 H6 L11,19 Z M19.07,4.93 A10,10 0 0 1 19.07,19.07 M15.54,8.46 A5,5 0 0 1 15.54,15.54</Geometry>
    <Geometry x:Key="Icon.VolumeMuted">M11,5 L6,9 H2 V15 H6 L11,19 Z M23,9 L17,15 M17,9 L23,15</Geometry>

    <!-- 功能 -->
    <Geometry x:Key="Icon.Equalizer">M4,21 V14 M4,10 V3 M12,21 V12 M12,8 V3 M20,21 V16 M20,12 V3 M1,14 H7 M9,8 H15 M17,16 H23</Geometry>
    <Geometry x:Key="Icon.Settings">M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z M12,15 A3,3 0 1 0 12,9 A3,3 0 1 0 12,15</Geometry>

    <!-- 通用操作 -->
    <Geometry x:Key="Icon.Plus">M12,5 V19 M5,12 H19</Geometry>
    <Geometry x:Key="Icon.Minus">M5,12 H19</Geometry>
    <Geometry x:Key="Icon.Close">M18,6 L6,18 M6,6 L18,18</Geometry>
    <Geometry x:Key="Icon.Refresh">M23,4 V10 H17 M1,20 V14 H7 M3.51,9 A9,9 0 0 1 18.36,5.64 L23,10 M1,14 L5.64,18.36 A9,9 0 0 0 20.49,15</Geometry>
    <Geometry x:Key="Icon.Folder">M22,19 A2,2 0 0 1 20,21 H4 A2,2 0 0 1 2,19 V5 A2,2 0 0 1 4,3 H9 L11,6 H20 A2,2 0 0 1 22,8 Z</Geometry>
    <Geometry x:Key="Icon.Clear">M3,6 H21 M19,6 V20 A2,2 0 0 1 17,22 H7 A2,2 0 0 1 5,20 V6 M8,6 V4 A2,2 0 0 1 10,2 H14 A2,2 0 0 1 16,4 V6 M10,11 V17 M14,11 V17</Geometry>

    <!-- ▶ 当前/活跃标记：实心小三角（Fill，非描边） -->
    <Geometry x:Key="Icon.PlayMarker">M8,5 L19,12 L8,19 Z</Geometry>
</ResourceDictionary>
```

- [ ] **Step 2: 在 `App.xaml` 合并 Icons.xaml**

在 `App.xaml` 的 `<Application.Resources>` / `MergedDictionaries` 中，与 Colors/Fonts/Controls 同级追加：
```xml
<ResourceDictionary Source="Themes/Icons.xaml"/>
```

- [ ] **Step 3: 构建验证**

Run: `dotnet build D-player.sln -c Debug --nologo -v q`
Expected: 0 错误（XAML 资源可解析；Geometry path markup 合法）。

- [ ] **Step 4: 提交**

```bash
git add Themes/Icons.xaml App.xaml
git commit -m "feat(theme): add vector icon set Icons.xaml (Phase 16)"
```

---

### Task 2: 转换器返回 Geometry + 移除 VM VolumeIcon

**Files:**
- Modify: `Converters/PlayStateToIconConverter.cs`
- Modify: `Converters/RepeatModeToIconConverter.cs`
- Create: `Converters/BoolToVolumeIconConverter.cs`
- Modify: `ViewModels/PlayerViewModel.cs`

- [ ] **Step 1: `PlayStateToIconConverter` 返回 Geometry**

把 `Convert` 的返回改为查 Icons 资源：
```csharp
public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
{
    var key = value is PlayState.Playing ? "Icon.Pause" : "Icon.Play";
    return Application.Current.FindResource(key);
}
```
并在文件顶加 `using System.Windows;`；把类注释与 `[ValueConversion(typeof(PlayState), typeof(string))]` 的目标类型改为 `typeof(Geometry)`（加 `using System.Windows.Media;`）。

- [ ] **Step 2: `RepeatModeToIconConverter` 返回 Geometry**

```csharp
public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
{
    var key = value switch
    {
        RepeatMode.List => "Icon.RepeatList",
        RepeatMode.One  => "Icon.RepeatOne",
        _               => "Icon.RepeatOff",
    };
    return Application.Current.FindResource(key);
}
```
同样加 `using System.Windows;` / `using System.Windows.Media;`，`[ValueConversion(typeof(RepeatMode), typeof(Geometry))]`。

- [ ] **Step 3: 新建 `Converters/BoolToVolumeIconConverter.cs`**

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DPlayer.Converters;

/// <summary>
/// 将 IsMuted 转换为音量图标 Geometry：true→Icon.VolumeMuted，false→Icon.Volume。
/// （替代原 PlayerViewModel.VolumeIcon emoji 字符串，保持 VM 无 WPF/emoji 类型。）
/// </summary>
[ValueConversion(typeof(bool), typeof(System.Windows.Media.Geometry))]
public sealed class BoolToVolumeIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is true ? "Icon.VolumeMuted" : "Icon.Volume";
        return Application.Current.FindResource(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 4: 移除 `PlayerViewModel.VolumeIcon`**

在 `ViewModels/PlayerViewModel.cs`：删除 `public string VolumeIcon => IsMuted ? "\U0001F507" : "\U0001F50A";` 一行，以及 `_volume` 与 `_isMuted` 上两处 `[NotifyPropertyChangedFor(nameof(VolumeIcon))]` 特性。

- [ ] **Step 5: 构建 + 测试验证**

Run: `dotnet build D-player.sln -c Debug --nologo -v q` → 0 错误。
Run: `dotnet test D-player.sln -c Debug --nologo -v q` → 96 通过 / 0 失败。
（注：此时 PlayerBar.xaml 仍绑定旧 VolumeIcon 会编译失败——故本 Task 与 Task 3 需连续完成；若构建因 XAML 绑定 VolumeIcon 报错，属预期，继续 Task 3 即修复。）

- [ ] **Step 6: 提交**（与 Task 3 合并提交亦可；若分开则本步先不提交，待 Task 3 一起）

---

### Task 3: PlayerBar.xaml 全部 emoji → Path

**Files:**
- Modify: `Views/Controls/PlayerBar.xaml`

- [ ] **Step 1: 在 PlayerBar.Resources 注册新转换器**

在 `<UserControl.Resources>` 内（BoolToAccentBrush 之后）追加：
```xml
<converters:BoolToVolumeIconConverter x:Key="BoolToVolumeIcon"/>
```

- [ ] **Step 2: 逐个替换 emoji TextBlock 为 Path**（before → after）

| before | after |
|--------|-------|
| `<TextBlock Text="&#x23EE;" FontSize="16" Foreground="{StaticResource ForegroundPrimary}"/>` | `<Path Style="{StaticResource IconPath}" Width="20" Height="20" Data="{StaticResource Icon.Prev}" Stroke="{StaticResource ForegroundPrimary}"/>` |
| `<TextBlock Text="{Binding PlayState, Converter={StaticResource PlayStateToIcon}}" FontSize="20" Foreground="White"/>` | `<Path Style="{StaticResource IconPath}" Width="20" Height="20" Data="{Binding PlayState, Converter={StaticResource PlayStateToIcon}}" Stroke="White"/>` |
| `<TextBlock Text="&#x23ED;" FontSize="16" Foreground="{StaticResource ForegroundPrimary}"/>` | `<Path Style="{StaticResource IconPath}" Width="20" Height="20" Data="{StaticResource Icon.Next}" Stroke="{StaticResource ForegroundPrimary}"/>` |
| shuffle 的 `<TextBlock Text="&#x1F500;" … Foreground="{Binding DataContext.Playlists.ShuffleEnabled, RelativeSource={RelativeSource AncestorType=Window}, Converter={StaticResource BoolToAccentBrush}}"/>` | `<Path Style="{StaticResource IconPath}" Width="18" Height="18" Data="{StaticResource Icon.Shuffle}" Stroke="{Binding DataContext.Playlists.ShuffleEnabled, RelativeSource={RelativeSource AncestorType=Window}, Converter={StaticResource BoolToAccentBrush}}"/>` |
| repeat 的 `<TextBlock Text="{Binding DataContext.Playlists.RepeatMode, …, Converter={StaticResource RepeatModeToIcon}}" … Foreground="{Binding DataContext.Playlists.RepeatActive, …, Converter={StaticResource BoolToAccentBrush}}"/>` | `<Path Style="{StaticResource IconPath}" Width="18" Height="18" Data="{Binding DataContext.Playlists.RepeatMode, RelativeSource={RelativeSource AncestorType=Window}, Converter={StaticResource RepeatModeToIcon}}" Stroke="{Binding DataContext.Playlists.RepeatActive, RelativeSource={RelativeSource AncestorType=Window}, Converter={StaticResource BoolToAccentBrush}}"/>` |
| `<TextBlock Text="{Binding VolumeIcon}" FontSize="14" Foreground="{StaticResource ForegroundPrimary}"/>` | `<Path Style="{StaticResource IconPath}" Width="16" Height="16" Data="{Binding IsMuted, Converter={StaticResource BoolToVolumeIcon}}" Stroke="{StaticResource ForegroundPrimary}"/>` |
| EQ 的 `<TextBlock Text="&#x1F39A;" … Foreground="{Binding EqualizerEnabled, Converter={StaticResource BoolToAccentBrush}}"/>` | `<Path Style="{StaticResource IconPath}" Width="16" Height="16" Data="{StaticResource Icon.Equalizer}" Stroke="{Binding EqualizerEnabled, Converter={StaticResource BoolToAccentBrush}}"/>` |
| `<TextBlock Text="&#x2699;" FontSize="14" Foreground="{StaticResource ForegroundSecondary}"/>` | `<Path Style="{StaticResource IconPath}" Width="16" Height="16" Data="{StaticResource Icon.Settings}" Stroke="{StaticResource ForegroundSecondary}"/>` |

（保留各 Button 的 Width/Height/Margin/ToolTip/Click 不变；仅换内部图标元素。删除被换掉 TextBlock 上的 `FontFamily="Segoe UI Emoji"`。）

- [ ] **Step 3: 构建 + 测试验证**

Run: `dotnet build D-player.sln -c Debug --nologo -v q` → 0 错误（此时 Task 2 的 VolumeIcon 移除不再报错，因 XAML 已改绑 IsMuted+BoolToVolumeIcon）。
Run: `dotnet test D-player.sln -c Debug --nologo -v q` → 96 通过 / 0 失败。

- [ ] **Step 4: 提交**（含 Task 2 改动一起）

```bash
git add Converters/ ViewModels/PlayerViewModel.cs Views/Controls/PlayerBar.xaml
git commit -m "refactor(view): vector icons for PlayerBar + converters return Geometry (Phase 16)"
```

---

### Task 4: PlaylistView 图标 → Path（marker/refresh/×/添加/清空）

**Files:**
- Modify: `Views/Controls/PlaylistView.xaml`
- Modify: `Views/Controls/PlaylistView.xaml.cs`

- [ ] **Step 1: marker TextBlock → Path**

把 `<TextBlock x:Name="PART_Marker" Grid.Column="0" Text="" FontSize="11" Foreground="{StaticResource AccentPrimary}" …/>` 替换为：
```xml
<Path x:Name="PART_Marker" Grid.Column="0"
      Data="{StaticResource Icon.PlayMarker}" Fill="{StaticResource AccentPrimary}"
      Width="9" Height="9" Stretch="Uniform" Visibility="Collapsed"
      VerticalAlignment="Center"/>
```

- [ ] **Step 2: code-behind marker 逻辑改 Visibility + 泛型改 Path**

在 `PlaylistView.xaml.cs`：
- `RefreshCurrentIndicator` 中 `FindChildByName<TextBlock>(container, "PART_Marker")` → `FindChildByName<Path>(container, "PART_Marker")`（变量类型改 `Path`）。
- `marker.Text = isCurrent ? "▶" : "";` → `marker.Visibility = isCurrent ? Visibility.Visible : Visibility.Collapsed;`

- [ ] **Step 3: 其余图标替换**

- 🔄 刷新：`<TextBlock Text="🔄" FontSize="14" …/>` → `<Path Style="{StaticResource IconPath}" Width="14" Height="14" Data="{StaticResource Icon.Refresh}" Stroke="{StaticResource ForegroundPrimary}"/>`
- × 删除：`<Button Grid.Column="6" Content="×" …/>` 的 Content 改为 `<Path Style="{StaticResource IconPath}" Width="12" Height="12" Data="{StaticResource Icon.Close}" Stroke="{StaticResource ForegroundSecondary}"/>`
- “+ 添加”：`<TextBlock Text="+ 添加" FontSize="12"/>` → `<StackPanel Orientation="Horizontal"><Path Style="{StaticResource IconPath}" Width="12" Height="12" Data="{StaticResource Icon.Plus}" Stroke="{StaticResource ForegroundPrimary}"/><TextBlock Text="添加" FontSize="12" Margin="4,0,0,0"/></StackPanel>`
- “清空”：`<TextBlock Text="清空" FontSize="12"/>` → `<StackPanel Orientation="Horizontal"><Path Style="{StaticResource IconPath}" Width="12" Height="12" Data="{StaticResource Icon.Clear}" Stroke="{StaticResource ForegroundPrimary}"/><TextBlock Text="清空" FontSize="12" Margin="4,0,0,0"/></StackPanel>`

- [ ] **Step 4: 构建 + 测试验证**

Run: `dotnet build …` → 0 错误；`dotnet test …` → 96 通过。

- [ ] **Step 5: 提交**

```bash
git add Views/Controls/PlaylistView.xaml Views/Controls/PlaylistView.xaml.cs
git commit -m "refactor(view): vector icons for PlaylistView incl. play marker (Phase 16)"
```

---

### Task 5: PlaylistsSidebarView 图标 → Path（+/−/marker/folder/refresh）

**Files:**
- Modify: `Views/Controls/PlaylistsSidebarView.xaml`
- Modify: `Views/Controls/PlaylistsSidebarView.xaml.cs`

- [ ] **Step 1: +/− 按钮 Content → Path**

- `Content="+"` → `Content` 改为 `<Path Style="{StaticResource IconPath}" Width="14" Height="14" Data="{StaticResource Icon.Plus}" Stroke="{StaticResource ForegroundPrimary}"/>`
- `Content="−"` → `<Path Style="{StaticResource IconPath}" Width="14" Height="14" Data="{StaticResource Icon.Minus}" Stroke="{StaticResource ForegroundPrimary}"/>`

- [ ] **Step 2: 活跃标记 marker TextBlock → Path（x:Name）**

在歌单 DataTemplate 中，把作为第 0 列的 marker `TextBlock` 替换为：
```xml
<Path x:Name="PART_SidebarMarker"
      Data="{StaticResource Icon.PlayMarker}" Fill="{StaticResource AccentPrimary}"
      Width="9" Height="9" Stretch="Uniform" Visibility="Collapsed"
      VerticalAlignment="Center"/>
```

- [ ] **Step 3: folder / scanning 图标 → Path**

在 DataTemplate 中定位 `Text="📂"` 与 `Text="🔄"` 的 TextBlock，分别替换为：
- `<Path Style="{StaticResource IconPath}" Width="14" Height="14" Data="{StaticResource Icon.Folder}" Stroke="{StaticResource ForegroundSecondary}"/>`
- `<Path Style="{StaticResource IconPath}" Width="12" Height="12" Data="{StaticResource Icon.Refresh}" Stroke="{StaticResource ForegroundSecondary}"/>`

- [ ] **Step 4: code-behind RefreshActiveMarker 改 Path + Visibility**

在 `PlaylistsSidebarView.xaml.cs`：`FindChildByOrder<TextBlock>(container, 0)` → 改用 `FindChildByName<Path>(container, "PART_SidebarMarker")`；`marker.Text = vm.IsActivePlaylist ? "▶" : string.Empty;` → `marker.Visibility = vm.IsActivePlaylist ? Visibility.Visible : Visibility.Collapsed;`。

- [ ] **Step 5: 构建 + 测试验证**

Run: `dotnet build …` → 0 错误；`dotnet test …` → 96 通过。

- [ ] **Step 6: 提交**

```bash
git add Views/Controls/PlaylistsSidebarView.xaml Views/Controls/PlaylistsSidebarView.xaml.cs
git commit -m "refactor(view): vector icons for sidebar incl. active marker (Phase 16)"
```

---

### Task 6: 更新 COUPLING.md 隐式契约（marker 元素类型）

**Files:**
- Modify: `docs/COUPLING.md`

- [ ] **Step 1: 更新 §5 两条 marker 契约**

把 §5 中「`PlaylistView.RefreshCurrentIndicator` 改用 `FindChildByName`（PART_Marker / PART_Title）」与「`PlaylistsSidebarView.RefreshActiveMarker` 用 `FindChildByOrder<TextBlock>(container, 0)`」两条契约更新为 Phase 16 后现状：
- marker 元素类型由 `TextBlock` 改为 `Path`（`PART_Marker` / `PART_SidebarMarker`），code-behind 用 `FindChildByName<Path>` 定位并切 `Visibility`（不再写 `Text`）；Sidebar 不再用 `FindChildByOrder`（改 x:Name）。
- 补一句：图标均为 `Themes/Icons.xaml` 矢量 `Geometry`，活跃态经 `BoolToAccentBrush` 着 `Path.Stroke`；VM 无 emoji/图标类型（`VolumeIcon` 已移除）。

- [ ] **Step 2: 提交**

```bash
git add docs/COUPLING.md
git commit -m "docs: update COUPLING.md marker/icon contracts for Phase 16"
```

---

### Task 7: GUI 验收（ComputerUse）+ 终验

**Files:** 无代码改动（验证）

- [ ] **Step 1: 构建 + 全量测试**

Run: `dotnet build D-player.sln -c Debug --nologo -v q` → 0 错误。
Run: `dotnet test D-player.sln -c Debug --nologo -v q` → 96 通过 / 0 失败。

- [ ] **Step 2: 启动应用 + ComputerUse 截图验收**

启动 `bin\Debug\net10.0-windows\D-player.exe`，用 ComputerUse 子代理截图并核对：
1. PlayerBar：⏮ ▶/⏸ ⏭ 🔀 🔁 🎚 ⚙ 均为**描边矢量图标**（非 emoji），尺寸协调；
2. 活跃态：启用 shuffle/repeat/EQ 后对应图标变 **accent 紫**；
3. 音量：静音/非静音切换图标变化（speaker / speaker-x）；
4. PlaylistView：当前曲 ▶ 标记为实心小三角（accent）；🔄/×/添加/清空 为矢量；
5. Sidebar：+/−、活跃 ▶ 标记、📂/🔄 为矢量；
6. **无 emoji 残留**（全窗口截图检查）。
返回截图路径；主代理独立读取≥ 2 张复核。

- [ ] **Step 3: 关闭应用；如有文档微调则提交**

```bash
git add -A
git commit -m "docs: Phase 16 icon refactor acceptance notes"   # 仅当有改动
```

---

## 附：本计划自检结果

**1. 规格覆盖**：spec §2 图标系统→Task 1；§4 转换器/VolumeIcon→Task 2；§4 PlayerBar→Task 3；§4 PlaylistView/marker→Task 4；§4 Sidebar→Task 5；§4/§7 COUPLING 契约→Task 6；§6 验收→Task 7。无遗漏。
**2. 占位符扫描**：Icons.xaml 为完整几何资源；各 Task 给出 before→after 具体 markup 与命令；无 TBD/TODO。
**3. 类型/名称一致性**：`Icon.*` key 在 Icons.xaml 与所有使用处一致；`BoolToVolumeIcon`/`PlayStateToIcon`/`RepeatModeToIcon` 转换器名与 XAML `x:Key` 一致；`PART_Marker`/`PART_SidebarMarker` 与 code-behind `FindChildByName<Path>` 一致；`IconPath` 样式名一致。

**预期产出**：Icons.xaml 1 个 + 转换器 3 改/1 新 + VM 1 改 + 3 个 View 改 + COUPLING 更新；功能行为零变化；测试保持 96 全绿。
