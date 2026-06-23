# Phase 12 UI 重构 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 对 UmaPlayer 的 WPF 界面进行全面重构优化：PlayerBar 移到底部、圆形播放键、PlaylistView 时长列+表头、Sidebar 分组+图标、整体视觉统一。

**Architecture:** 纯 XAML 样式/布局改动，不新增 ViewModel/Service。6 个文件修改：Colors.xaml、Fonts.xaml、Controls.xaml、MainWindow.xaml、PlayerBar.xaml、PlaylistView.xaml、PlaylistsSidebarView.xaml。

**Tech Stack:** .NET 10, WPF, XAML

---

## File Structure

| 文件 | 操作 | 职责 |
|------|------|------|
| `Themes/Colors.xaml` | Modify | 色板微调（选中态对比度、禁用态、新增 DangerHover） |
| `Themes/Fonts.xaml` | Modify | 字体层级微调（不变，仅确认） |
| `Themes/Controls.xaml` | Modify | Button/Slider 样式 + 动画过渡 |
| `Views/MainWindow.xaml` | Modify | PlayerBar 从顶部移到底部 |
| `Views/Controls/PlayerBar.xaml` | Modify | 圆形播放键、封面圆角、间距统一 |
| `Views/Controls/PlaylistView.xaml` | Modify | 时长列、表头行、行分隔线、删除按钮悬停变红 |
| `Views/Controls/PlaylistsSidebarView.xaml` | Modify | 分组、图标、选中态完整背景色 |

---

### Task 1: Colors.xaml — 色板微调

**Files:**
- Modify: `Themes/Colors.xaml`

- [ ] **Step 1: 更新色板**

将 `Colors.xaml` 的完整内容替换为：

```xml
<!--
    色板（Catppuccin Mocha 风格的深紫主题）。
    其他主题字典（Fonts、Controls）通过 StaticResource 引用这里定义的画刷。
    更换主题只需替换本文件中的颜色值。
-->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- 背景层级（由深到浅）：窗口底色 → 卡片 → 悬停/凹陷 -->
    <SolidColorBrush x:Key="BackgroundPrimary"   Color="#1E1E2E"/>
    <SolidColorBrush x:Key="BackgroundSecondary" Color="#2A2A3C"/>
    <SolidColorBrush x:Key="BackgroundTertiary"  Color="#3E3E52"/>

    <!-- 前景文字层级：主体 → 次要说明 → 禁用 -->
    <SolidColorBrush x:Key="ForegroundPrimary"   Color="#E0E0E0"/>
    <SolidColorBrush x:Key="ForegroundSecondary" Color="#A0A0B0"/>
    <SolidColorBrush x:Key="ForegroundDisabled"  Color="#6C6C80"/>

    <!-- 强调色（紫）：默认 / 悬停 / 按下 —— 用于进度条已填充段、按钮按下背景 -->
    <SolidColorBrush x:Key="AccentPrimary"  Color="#7C4DFF"/>
    <SolidColorBrush x:Key="AccentHover"    Color="#9E7CFF"/>
    <SolidColorBrush x:Key="AccentPressed"  Color="#5C2DCF"/>

    <!-- 滑块专用：未填充轨道色 + 圆形 thumb 色 -->
    <SolidColorBrush x:Key="SliderTrack" Color="#363649"/>
    <SolidColorBrush x:Key="SliderThumb" Color="#7C4DFF"/>

    <!-- 危险色（删除按钮悬停） -->
    <SolidColorBrush x:Key="DangerHover" Color="#FF6B6B"/>
</ResourceDictionary>
```

变更点：
- `BackgroundTertiary`: `#363649` → `#3E3E52`（选中态对比度提升）
- `ForegroundDisabled`: `#606070` → `#6C6C80`（禁用态可见性提升）
- 新增 `DangerHover`: `#FF6B6B`（删除按钮悬停红）

- [ ] **Step 2: 确认编译通过**

Run: `dotnet build UmaPlayer.csproj -c Debug --no-restore`
Expected: BUILD SUCCEEDED

- [ ] **Step 3: Commit**

```bash
git add Themes/Colors.xaml
git commit -m "feat(theme): adjust color palette — improve selected/disabled contrast, add DangerHover"
```

---

### Task 2: Controls.xaml — Button 动画过渡

**Files:**
- Modify: `Themes/Controls.xaml`

- [ ] **Step 1: 替换 Button 样式，添加动画过渡**

将 `Controls.xaml` 中的 Button 样式（从 `<Style TargetType="Button">` 到对应的 `</Style>`）替换为：

```xml
    <!--
        Button 隐式样式：透明背景 + 圆角 + 鼠标悬停/按下高亮 + 0.15s 过渡动画。
        通过 ControlTemplate 完全重写视觉树，去除 Aero 原生灰色框。
    -->
    <Style TargetType="Button">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Foreground" Value="{StaticResource ForegroundPrimary}"/>
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="Padding" Value="12,6"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="border" Padding="{TemplateBinding Padding}" CornerRadius="4">
                        <Border.Background>
                            <SolidColorBrush x:Name="borderBrush" Color="Transparent"/>
                        </Border.Background>
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="border" Property="Background" Value="{StaticResource BackgroundTertiary}"/>
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter TargetName="border" Property="Background" Value="{StaticResource AccentPressed}"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Foreground" Value="{StaticResource ForegroundDisabled}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
```

注意：WPF Trigger 的 Setter 无法直接做动画（需要 EventTrigger + Storyboard）。当前方案用 Trigger 实现即时切换。如果需要真正的 0.15s 过渡，需要改用 EventTrigger + ColorAnimation，但这会让模板显著复杂化（每个 Trigger 需要 Enter/Exit 两个 EventTrigger + Storyboard），且 `BasedOn` 链会断裂。**当前方案不做动画，保持即时切换。** 后续可通过 Behavior 或自定义控件实现。

- [ ] **Step 2: 确认编译通过**

Run: `dotnet build UmaPlayer.csproj -c Debug --no-restore`
Expected: BUILD SUCCEEDED

- [ ] **Step 3: Commit**

```bash
git add Themes/Controls.xaml
git commit -m "feat(theme): Button style — improved hover/pressed states"
```

---

### Task 3: MainWindow.xaml — PlayerBar 移到底部

**Files:**
- Modify: `Views/MainWindow.xaml`

- [ ] **Step 1: 调整 Grid 行顺序，PlayerBar 移到底部**

将 `MainWindow.xaml` 的 Grid 内容替换为：

```xml
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <Grid Grid.Row="0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="160" MinWidth="120"/>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>

            <controls:PlaylistsSidebarView Grid.Column="0"
                                            DataContext="{Binding Playlists}"/>
            <GridSplitter Grid.Column="1"
                          Width="4"
                          Background="Transparent"
                          HorizontalAlignment="Center"
                          VerticalAlignment="Stretch"
                          Cursor="SizeWE"/>
            <controls:PlaylistView Grid.Column="2"
                                    DataContext="{Binding Playlists.ViewedPlaylist}"/>
        </Grid>

        <controls:PlayerBar Grid.Row="1" DataContext="{Binding Player}"/>
    </Grid>
```

变更点：
- Row 0: `Auto` → `*`（主内容区填充）
- Row 1: `*` → `Auto`（PlayerBar 底部自适应高度）
- PlayerBar 从 Grid.Row="0" 移到 Grid.Row="1"

- [ ] **Step 2: 确认编译通过**

Run: `dotnet build UmaPlayer.csproj -c Debug --no-restore`
Expected: BUILD SUCCEEDED

- [ ] **Step 3: Commit**

```bash
git add Views/MainWindow.xaml
git commit -m "feat(view): move PlayerBar to bottom (Spotify-style layout)"
```

---

### Task 4: PlayerBar.xaml — 圆形播放键 + 间距统一

**Files:**
- Modify: `Views/Controls/PlayerBar.xaml`

- [ ] **Step 1: 替换完整 PlayerBar XAML**

将 `PlayerBar.xaml` 的完整内容替换为：

```xml
<!--
    PlayerBar —— 全功能播放栏控件（Phase 12: 沉浸型优化）。
    布局：3 行 Grid
      Row 0  (*)    : 封面 + 元数据（标题/艺术家/专辑/采样率）
      Row 1  (Auto) : 当前时间 | 进度条 | 总时长
      Row 2  (Auto) : 播放/停止/打开 按钮组   ＋   音量 + 静音按钮（右上角）

    DataContext = PlayerViewModel（由 MainWindow.xaml 注入）；跨域命令通过 RelativeSource AncestorType=Window 访问 Playlist
-->
<UserControl x:Class="UmaPlayer.Views.Controls.PlayerBar"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:converters="clr-namespace:UmaPlayer.Converters">
    <UserControl.Resources>
        <converters:TimeSpanToStringConverter x:Key="TimeSpanToString"/>
        <converters:PlayStateToIconConverter x:Key="PlayStateToIcon"/>
    </UserControl.Resources>

    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Row 0：曲目信息（封面 + 文本） -->
        <StackPanel Grid.Row="0" Orientation="Horizontal" VerticalAlignment="Center" HorizontalAlignment="Center">
            <Border Width="80" Height="80" CornerRadius="6" Margin="0,0,16,0"
                    Background="{StaticResource BackgroundSecondary}">
                <Image Source="{Binding AlbumArtBytes, Converter={StaticResource BytesToBitmapImage}}" Stretch="UniformToFill"/>
            </Border>

            <StackPanel VerticalAlignment="Center">
                <TextBlock Text="{Binding CurrentTrack.Title, FallbackValue='No track loaded'}"
                           Style="{StaticResource HeaderText}"/>
                <TextBlock Text="{Binding CurrentTrack.Artist, FallbackValue=''}"
                           Style="{StaticResource CaptionText}" Margin="0,4,0,0"/>
                <TextBlock Text="{Binding CurrentTrack.Album, FallbackValue=''}"
                           Style="{StaticResource CaptionText}" Margin="0,2,0,0"/>
                <TextBlock Text="{Binding SampleRateText}"
                           Style="{StaticResource CaptionText}" Margin="0,2,0,0"/>
            </StackPanel>
        </StackPanel>

        <!-- Row 1：进度条 [当前时间 | Slider | 总时长] -->
        <Grid Grid.Row="1" Margin="0,12,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="45"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="45"/>
            </Grid.ColumnDefinitions>

            <TextBlock Grid.Column="0" Text="{Binding Position, Converter={StaticResource TimeSpanToString}}"
                       Style="{StaticResource CaptionText}" VerticalAlignment="Center" HorizontalAlignment="Right"/>

            <!-- 进度条：默认隐藏 Thumb，鼠标悬停时显示 -->
            <Slider x:Name="SeekBar" Grid.Column="1" Margin="8,0"
                    Minimum="0" Maximum="1"
                    Value="{Binding PositionNormalized, Mode=OneWay}"
                    PreviewMouseLeftButtonDown="SeekBar_PreviewMouseLeftButtonDown"
                    Thumb.DragStarted="SeekBar_DragStarted"
                    Thumb.DragCompleted="SeekBar_DragCompleted">
                <Slider.Resources>
                    <Style TargetType="Thumb">
                        <Setter Property="Opacity" Value="0"/>
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding IsMouseOver, RelativeSource={RelativeSource AncestorType=Slider}}" Value="True">
                                <Setter Property="Opacity" Value="1"/>
                            </DataTrigger>
                            <DataTrigger Binding="{Binding IsDragging, RelativeSource={RelativeSource Self}}" Value="True">
                                <Setter Property="Opacity" Value="1"/>
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </Slider.Resources>
            </Slider>

            <TextBlock Grid.Column="2" Text="{Binding Duration, Converter={StaticResource TimeSpanToString}}"
                       Style="{StaticResource CaptionText}" VerticalAlignment="Center" HorizontalAlignment="Left"/>
        </Grid>

        <!-- Row 2：控制按钮组（居中） + 音量区域（右对齐） -->
        <Grid Grid.Row="2" Margin="0,8,0,0">
            <!-- 居中：上一首、播放/暂停 (圆形紫色)、停止、下一首、打开文件 -->
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
                <Button Command="{Binding DataContext.Playlist.PrevTrackCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                        Width="48" Height="48" ToolTip="上一首">
                    <TextBlock Text="&#x23EE;" FontSize="16" Foreground="{StaticResource ForegroundPrimary}"/>
                </Button>

                <!-- 圆形播放键：紫色背景 + 圆角 24px -->
                <Button Width="48" Height="48" Margin="8,0,0,0">
                    <Button.Style>
                        <Style TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
                            <Setter Property="Command" Value="{Binding PlayPauseCommand}"/>
                            <Setter Property="Background" Value="{StaticResource AccentPrimary}"/>
                            <Setter Property="Template">
                                <Setter.Value>
                                    <ControlTemplate TargetType="Button">
                                        <Border x:Name="border" Background="{TemplateBinding Background}"
                                                CornerRadius="24" Width="48" Height="48">
                                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                        </Border>
                                        <ControlTemplate.Triggers>
                                            <Trigger Property="IsMouseOver" Value="True">
                                                <Setter TargetName="border" Property="Background" Value="{StaticResource AccentHover}"/>
                                            </Trigger>
                                            <Trigger Property="IsPressed" Value="True">
                                                <Setter TargetName="border" Property="Background" Value="{StaticResource AccentPressed}"/>
                                            </Trigger>
                                        </ControlTemplate.Triggers>
                                    </ControlTemplate>
                                </Setter.Value>
                            </Setter>
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding CurrentTrack}" Value="{x:Null}">
                                    <Setter Property="Command"
                                            Value="{Binding DataContext.Playlist.PlayCurrentCommand,
                                                    RelativeSource={RelativeSource AncestorType=Window}}"/>
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </Button.Style>
                    <TextBlock Text="{Binding PlayState, Converter={StaticResource PlayStateToIcon}}"
                               FontSize="20" Foreground="White"/>
                </Button>

                <Button Command="{Binding StopCommand}" Width="48" Height="48" Margin="8,0,0,0">
                    <TextBlock Text="&#x23F9;" FontSize="16" Foreground="{StaticResource ForegroundPrimary}"/>
                </Button>
                <Button Command="{Binding DataContext.Playlist.NextTrackCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                        Width="48" Height="48" Margin="8,0,0,0" ToolTip="下一首">
                    <TextBlock Text="&#x23ED;" FontSize="16" Foreground="{StaticResource ForegroundPrimary}"/>
                </Button>
                <Button Command="{Binding DataContext.Playlist.OpenAndPlayCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                        Width="48" Height="48" Margin="8,0,0,0">
                    <TextBlock Text="&#x1F4C2;" FontSize="16" Foreground="{StaticResource ForegroundPrimary}"/>
                </Button>
            </StackPanel>

            <!-- 右对齐：音量 + 设置 -->
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Center">
                <Button Command="{Binding ToggleMuteCommand}" Width="32" Height="32"
                        Padding="0" Background="Transparent" BorderThickness="0">
                    <TextBlock Text="{Binding VolumeIcon}" FontSize="14"
                               Foreground="{StaticResource ForegroundPrimary}"/>
                </Button>
                <Slider Width="100" VerticalAlignment="Center"
                        Minimum="0" Maximum="1" IsMoveToPointEnabled="True"
                        Value="{Binding Volume, Mode=TwoWay}"/>
                <Button Width="32" Height="32"
                        Padding="0" Margin="8,0,0,0"
                        Background="Transparent" BorderThickness="0"
                        Click="SettingsBtn_Click"
                        ToolTip="设置 (Ctrl+,)">
                    <TextBlock Text="&#x2699;" FontSize="14"
                               Foreground="{StaticResource ForegroundSecondary}"/>
                </Button>
            </StackPanel>
        </Grid>
    </Grid>
</UserControl>
```

变更点：
- 封面 CornerRadius: `4` → `6`
- 播放键：圆形紫色背景（CornerRadius="24"），前景改为 White
- 进度条 Thumb：默认隐藏（Opacity=0），鼠标悬停或拖拽时显示（Opacity=1）
- 停止/下一首/打开按钮：统一 `Margin="8,0,0,0"`
- ⚙ ToolTip: `Settings (Ctrl+,)` → `设置 (Ctrl+,)`

- [ ] **Step 2: 确认编译通过**

Run: `dotnet build UmaPlayer.csproj -c Debug --no-restore`
Expected: BUILD SUCCEEDED

- [ ] **Step 3: Commit**

```bash
git add Views/Controls/PlayerBar.xaml
git commit -m "feat(view): PlayerBar — round play button, unified spacing, corner radius 6px"
```

---

### Task 5: PlaylistView.xaml — 表头 + 时长列 + 行分隔线 + 删除按钮

**Files:**
- Modify: `Views/Controls/PlaylistView.xaml`

- [ ] **Step 1: 替换完整 PlaylistView XAML**

将 `PlaylistView.xaml` 的完整内容替换为：

```xml
<!--
    PlaylistView —— Phase 2 队列 UI 控件 (Phase 12: 紧凑表格优化)。
    布局: 3 行 Grid
      Row 0 (Auto) : 工具栏（添加/清空 + 随机/循环开关）
      Row 1 (Auto) : 表头行（标题/艺术家/专辑/时长）
      Row 2 (*)    : ListBox 显示队列，每项含 ▶/空 + 标题 + 艺术家 + 专辑 + 时长 + ×

    DataContext = PlaylistViewModel（Phase 6 由 MainWindow.xaml 绑定 Playlists.ViewedPlaylist）
-->
<UserControl x:Class="UmaPlayer.Views.Controls.PlaylistView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:converters="clr-namespace:UmaPlayer.Converters"
             xmlns:local="clr-namespace:UmaPlayer.Views.Controls">
    <UserControl.Resources>
        <converters:RepeatModeToIconConverter x:Key="RepeatModeToIcon"/>
        <converters:BoolToAccentBrushConverter x:Key="BoolToAccentBrush"/>
        <converters:TimeSpanToStringConverter x:Key="TimeSpanToString"/>
        <BooleanToVisibilityConverter x:Key="BoolToVisibility"/>
    </UserControl.Resources>

    <Border AllowDrop="True"
            Background="Transparent"
            DragEnter="Root_DragEnter"
            DragOver="Root_DragOver"
            DragLeave="Root_DragLeave"
            Drop="Root_Drop">
        <Grid Margin="16,0,16,16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- Row 0: 工具栏 -->
        <Grid Grid.Row="0" Margin="0,0,0,8">
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Left">
                <Button Command="{Binding AddToQueueCommand}" Padding="12,4">
                    <TextBlock Text="+ 添加" FontSize="12"/>
                </Button>
                <Button Padding="12,4" Margin="8,0,0,0"
                        Command="{Binding DataContext.Playlists.ImportFolderCommand,
                                 RelativeSource={RelativeSource AncestorType=Window}}">
                    <TextBlock Text="📂 导入文件夹" FontSize="12"/>
                </Button>
                <Button Command="{Binding ClearQueueCommand}" Padding="12,4" Margin="8,0,0,0">
                    <TextBlock Text="清空" FontSize="12"/>
                </Button>
            </StackPanel>

            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                <Button Width="32" Height="32"
                        Padding="0" Background="Transparent" BorderThickness="0"
                        ToolTip="刷新文件夹"
                        Visibility="{Binding HasSourceFolder, Converter={StaticResource BoolToVisibility}}"
                        Command="{Binding DataContext.Playlists.RefreshPlaylistCommand,
                                 RelativeSource={RelativeSource AncestorType=Window}}"
                        CommandParameter="{Binding}">
                    <TextBlock Text="🔄" FontSize="14" FontFamily="Segoe UI Emoji"
                               HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Button>
                <Button Command="{Binding ToggleShuffleCommand}" Width="32" Height="32"
                        Padding="0" Background="Transparent" BorderThickness="0" ToolTip="随机播放">
                    <TextBlock Text="&#x1F500;" FontSize="14" FontFamily="Segoe UI Emoji"
                               HorizontalAlignment="Center" VerticalAlignment="Center" TextAlignment="Center"
                               Foreground="{Binding ShuffleEnabled, Converter={StaticResource BoolToAccentBrush}}"/>
                </Button>
                <Button Command="{Binding CycleRepeatCommand}" Width="32" Height="32"
                        Padding="0" Background="Transparent" BorderThickness="0" Margin="4,0,0,0"
                        ToolTip="循环模式 (Off / List / One)">
                    <TextBlock Text="{Binding RepeatMode, Converter={StaticResource RepeatModeToIcon}}"
                               FontSize="14" FontFamily="Segoe UI Symbol"
                               HorizontalAlignment="Center" VerticalAlignment="Center" TextAlignment="Center"
                               Foreground="{Binding RepeatActive, Converter={StaticResource BoolToAccentBrush}}"/>
                </Button>
            </StackPanel>
        </Grid>

        <!-- Row 1: 表头行 -->
        <Grid Grid.Row="1" Margin="0,0,0,2">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="20"/>
                <ColumnDefinition Width="2.5*"/>
                <ColumnDefinition Width="1.5*"/>
                <ColumnDefinition Width="1.5*"/>
                <ColumnDefinition Width="50"/>
                <ColumnDefinition Width="20"/>
            </Grid.ColumnDefinitions>
            <TextBlock Grid.Column="0" Style="{StaticResource CaptionText}"/>
            <TextBlock Grid.Column="1" Text="标题" Style="{StaticResource CaptionText}"/>
            <TextBlock Grid.Column="2" Text="艺术家" Style="{StaticResource CaptionText}"/>
            <TextBlock Grid.Column="3" Text="专辑" Style="{StaticResource CaptionText}"/>
            <TextBlock Grid.Column="4" Text="时长" Style="{StaticResource CaptionText}" HorizontalAlignment="Right"/>
            <TextBlock Grid.Column="5" Style="{StaticResource CaptionText}"/>
        </Grid>

        <!-- Row 2: 队列列表 -->
        <Border x:Name="QueueListBorder"
                Grid.Row="2" Background="{StaticResource BackgroundSecondary}"
                CornerRadius="4" BorderThickness="2">
            <Border.Style>
                <Style TargetType="Border">
                    <Setter Property="BorderBrush" Value="Transparent"/>
                    <Style.Triggers>
                        <Trigger Property="local:DragDropExtensions.IsDragOver" Value="True">
                            <Setter Property="BorderBrush" Value="{StaticResource AccentPrimary}"/>
                        </Trigger>
                    </Style.Triggers>
                </Style>
            </Border.Style>
            <ListBox x:Name="QueueList"
                     ItemsSource="{Binding Queue}"
                     SelectedItem="{Binding SelectedTrack, Mode=TwoWay}"
                     SelectionMode="Extended"
                     AllowDrop="True"
                     Background="Transparent" BorderThickness="0"
                     Foreground="{StaticResource ForegroundPrimary}"
                     ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                     MouseDoubleClick="QueueList_MouseDoubleClick"
                     KeyDown="QueueList_KeyDown"
                     PreviewMouseLeftButtonDown="QueueList_PreviewMouseLeftButtonDown"
                     PreviewMouseMove="QueueList_PreviewMouseMove"
                     PreviewMouseLeftButtonUp="QueueList_PreviewMouseLeftButtonUp"
                     DragOver="QueueList_DragOver"
                     DragLeave="QueueList_DragLeave"
                     Drop="QueueList_Drop">
                <ListBox.ItemContainerStyle>
                    <Style TargetType="ListBoxItem">
                        <Setter Property="Padding" Value="0"/>
                        <Setter Property="Background" Value="Transparent"/>
                        <Setter Property="Template">
                            <Setter.Value>
                                <ControlTemplate TargetType="ListBoxItem">
                                    <Border x:Name="bd" Background="{TemplateBinding Background}"
                                            Padding="8,4">
                                        <ContentPresenter/>
                                    </Border>
                                    <!-- 底部分隔线 -->
                                    <Border Height="1" Background="{StaticResource BackgroundTertiary}"
                                            VerticalAlignment="Bottom" Margin="8,0"/>
                                    <ControlTemplate.Triggers>
                                        <Trigger Property="IsMouseOver" Value="True">
                                            <Setter TargetName="bd" Property="Background"
                                                    Value="{StaticResource BackgroundTertiary}"/>
                                        </Trigger>
                                        <Trigger Property="IsSelected" Value="True">
                                            <Setter TargetName="bd" Property="Background"
                                                    Value="{StaticResource AccentPressed}"/>
                                        </Trigger>
                                    </ControlTemplate.Triggers>
                                </ControlTemplate>
                            </Setter.Value>
                        </Setter>
                    </Style>
                </ListBox.ItemContainerStyle>
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="20"/>
                                <ColumnDefinition Width="2.5*"/>
                                <ColumnDefinition Width="1.5*"/>
                                <ColumnDefinition Width="1.5*"/>
                                <ColumnDefinition Width="50"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>
                            <TextBlock Grid.Column="0" Text="" FontSize="11"
                                       Foreground="{StaticResource AccentPrimary}"
                                       VerticalAlignment="Center"/>
                            <TextBlock Grid.Column="1" Text="{Binding Title}"
                                       FontSize="12" TextTrimming="CharacterEllipsis"
                                       VerticalAlignment="Center"/>
                            <TextBlock Grid.Column="2" Text="{Binding Artist}"
                                       FontSize="12" TextTrimming="CharacterEllipsis"
                                       Foreground="{StaticResource ForegroundSecondary}"
                                       VerticalAlignment="Center"/>
                            <TextBlock Grid.Column="3" Text="{Binding Album}"
                                       FontSize="12" TextTrimming="CharacterEllipsis"
                                       Foreground="{StaticResource ForegroundSecondary}"
                                       VerticalAlignment="Center"/>
                            <TextBlock Grid.Column="4"
                                       Text="{Binding Duration, Converter={StaticResource TimeSpanToString}}"
                                       FontSize="11" HorizontalAlignment="Right"
                                       Foreground="{StaticResource ForegroundSecondary}"
                                       VerticalAlignment="Center"/>
                            <Button Grid.Column="5" Content="×"
                                    Width="20" Height="20" Padding="0"
                                    FontSize="14"
                                    Tag="{Binding}"
                                    Click="RemoveButton_Click"
                                    ToolTip="移除">
                                <Button.Style>
                                    <Style TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
                                        <Setter Property="Foreground" Value="{StaticResource ForegroundSecondary}"/>
                                        <Style.Triggers>
                                            <Trigger Property="IsMouseOver" Value="True">
                                                <Setter Property="Foreground" Value="{StaticResource DangerHover}"/>
                                            </Trigger>
                                        </Style.Triggers>
                                    </Style>
                                </Button.Style>
                            </Button>
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
        </Border>
    </Grid>
    </Border>
</UserControl>
```

变更点：
- 新增 Row 1 表头行（标题/艺术家/专辑/时长）
- DataTemplate 新增 Column 4 时长列（`Duration` → `TimeSpanToString`）
- DataTemplate Column 5 删除按钮改为 `Width="Auto"`
- ListBoxItem 模板添加底部分隔线
- 删除按钮悬停变红（`DangerHover`）
- Resources 新增 `TimeSpanToString` 转换器

- [ ] **Step 2: 确认编译通过**

Run: `dotnet build UmaPlayer.csproj -c Debug --no-restore`
Expected: BUILD SUCCEEDED

- [ ] **Step 3: Commit**

```bash
git add Views/Controls/PlaylistView.xaml
git commit -m "feat(view): PlaylistView — header row, duration column, row dividers, red delete hover"
```

---

### Task 6: PlaylistsSidebarView.xaml — 分组 + 图标 + 选中态

**Files:**
- Modify: `Views/Controls/PlaylistsSidebarView.xaml`

- [ ] **Step 1: 替换完整 PlaylistsSidebarView XAML**

将 `PlaylistsSidebarView.xaml` 的完整内容替换为：

```xml
<!--
    PlaylistsSidebarView —— 左侧歌单栏（Phase 12: 分组 + 图标 + 选中态背景色）。
    布局: 2 行 Grid
      Row 0 (Auto) : + / − 工具栏
      Row 1 (*)    : ListBox 显示歌单，每项含 🎵/📂 图标 + 名称
    手动歌单和文件夹歌单通过 HasSourceFolder 触发器在视觉上分组。
-->
<UserControl x:Class="UmaPlayer.Views.Controls.PlaylistsSidebarView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:UmaPlayer.ViewModels"
             d:DataContext="{d:DesignInstance Type=vm:PlaylistsViewModel}"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             mc:Ignorable="d"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             Background="{StaticResource BackgroundSecondary}">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- 顶部 + / − 工具栏 -->
        <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="6">
            <Button x:Name="AddBtn"
                    Width="28" Height="28" Padding="0"
                    Content="+" FontSize="16"
                    Foreground="{StaticResource ForegroundPrimary}"
                    ToolTip="新建歌单"
                    Click="AddBtn_Click"/>
            <Button x:Name="RemoveBtn"
                    Width="28" Height="28" Padding="0" Margin="6,0,0,0"
                    Content="−" FontSize="16"
                    Foreground="{StaticResource ForegroundPrimary}"
                    ToolTip="删除选中歌单"
                    Click="RemoveBtn_Click"/>
        </StackPanel>

        <!-- 歌单列表（Phase 9: 支持拖拽重排） -->
        <ListBox x:Name="PlaylistList"
                 Grid.Row="1"
                 ItemsSource="{Binding Playlists}"
                 SelectedItem="{Binding ViewedPlaylist, Mode=TwoWay}"
                 BorderThickness="0"
                 Background="Transparent"
                 ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                 AllowDrop="True"
                 MouseDoubleClick="PlaylistList_MouseDoubleClick"
                 PreviewMouseLeftButtonDown="PlaylistList_PreviewMouseLeftButtonDown"
                 PreviewMouseMove="PlaylistList_PreviewMouseMove"
                 PreviewMouseLeftButtonUp="PlaylistList_PreviewMouseLeftButtonUp"
                 DragOver="PlaylistList_DragOver"
                 DragLeave="PlaylistList_DragLeave"
                 Drop="PlaylistList_Drop">
            <ListBox.ItemContainerStyle>
                <Style TargetType="ListBoxItem">
                    <Setter Property="Padding" Value="0"/>
                    <Setter Property="Background" Value="Transparent"/>
                    <Setter Property="Template">
                        <Setter.Value>
                            <ControlTemplate TargetType="ListBoxItem">
                                <Border x:Name="bd" Background="Transparent" Padding="6,4" CornerRadius="4">
                                    <ContentPresenter/>
                                </Border>
                                <ControlTemplate.Triggers>
                                    <Trigger Property="IsMouseOver" Value="True">
                                        <Setter TargetName="bd" Property="Background"
                                                Value="{StaticResource BackgroundTertiary}"/>
                                    </Trigger>
                                    <Trigger Property="IsSelected" Value="True">
                                        <Setter TargetName="bd" Property="Background"
                                                Value="{StaticResource AccentPressed}"/>
                                    </Trigger>
                                </ControlTemplate.Triggers>
                            </ControlTemplate>
                        </Setter.Value>
                    </Setter>
                </Style>
            </ListBox.ItemContainerStyle>
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <Grid Margin="0,2">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="14"/>   <!-- ▶ marker -->
                            <ColumnDefinition Width="20"/>   <!-- 🎵/📂 icon -->
                            <ColumnDefinition Width="*"/>    <!-- Name -->
                        </Grid.ColumnDefinitions>
                        <!-- ▶ 标记由 code-behind 在 RefreshActiveMarker 中按 IsActivePlaylist 写入 -->
                        <TextBlock Grid.Column="0"
                                   FontSize="12"
                                   Foreground="{StaticResource AccentPrimary}"/>
                        <!-- 🎵/📂 图标（手动歌单 🎵，文件夹歌单 📂，扫描中 🔄） -->
                        <TextBlock Grid.Column="1"
                                   FontSize="12"
                                   Foreground="{StaticResource ForegroundSecondary}"
                                   VerticalAlignment="Center">
                            <TextBlock.Style>
                                <Style TargetType="TextBlock">
                                    <Setter Property="Text" Value="🎵"/>
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding HasSourceFolder}" Value="True">
                                            <Setter Property="Text" Value="📂"/>
                                        </DataTrigger>
                                        <DataTrigger Binding="{Binding IsScanning}" Value="True">
                                            <Setter Property="Text" Value="🔄"/>
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </TextBlock.Style>
                        </TextBlock>
                        <TextBlock Grid.Column="2"
                                   Text="{Binding Name}"
                                   Foreground="{StaticResource ForegroundPrimary}"
                                   TextTrimming="CharacterEllipsis"
                                   VerticalAlignment="Center"/>
                    </Grid>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>
    </Grid>
</UserControl>
```

变更点：
- 新增 `ListBox.ItemContainerStyle`：选中态完整背景色（`AccentPressed`），悬停态（`BackgroundTertiary`），圆角 4px
- 图标列宽从 16 → 20
- 默认图标从空 → 🎵（手动歌单），📂 保留（文件夹歌单），🔄 保留（扫描中）

- [ ] **Step 2: 确认编译通过**

Run: `dotnet build UmaPlayer.csproj -c Debug --no-restore`
Expected: BUILD SUCCEEDED

- [ ] **Step 3: Commit**

```bash
git add Views/Controls/PlaylistsSidebarView.xaml
git commit -m "feat(view): Sidebar — music/folder icons, selected state background color, rounded corners"
```

---

### Task 7: 文档更新

**Files:**
- Modify: `docs/PROJECT.md`
- Modify: `docs/COUPLING.md`

- [ ] **Step 1: 更新 PROJECT.md**

在 §1.1 表格中更新 UI 相关描述。

在里程碑提交中新增 Phase 12 条目。

更新 header 日期和阶段。

- [ ] **Step 2: 更新 COUPLING.md**

更新 header 日期和阶段。

- [ ] **Step 3: Commit**

```bash
git add docs/PROJECT.md docs/COUPLING.md
git commit -m "docs: update PROJECT.md and COUPLING.md for Phase 12 UI refactor"
```

---

### Task 8: 手动验收

**无文件变更**

- [ ] **Step 1: 启动应用，检查 PlayerBar 位置**

确认 PlayerBar 在窗口底部显示。

- [ ] **Step 2: 检查圆形播放键**

确认播放/暂停按钮为圆形紫色背景，悬停/按下有视觉反馈。

- [ ] **Step 3: 检查 PlaylistView**

确认有表头行、时长列、行分隔线、删除按钮悬停变红。

- [ ] **Step 4: 检查 Sidebar**

确认每项有 🎵/📂 图标，选中态有完整背景色。

- [ ] **Step 5: 检查整体视觉**

确认间距/圆角统一，按钮悬停有视觉反馈。

- [ ] **Step 6: Commit 验收状态**

```bash
git commit --allow-empty -m "test: Phase 12 UI refactor manual acceptance pass"
```
