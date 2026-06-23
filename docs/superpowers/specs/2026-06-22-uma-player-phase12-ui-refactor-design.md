# Phase 12 设计稿：UI 界面重构

> 日期：2026/06/22 · 分支：master · 基于 Phase 11 完成

---

## 1. 目标

对 UmaPlayer 的 WPF 界面进行全面重构优化，提升视觉体验和交互质量：

- PlayerBar 从顶部移到底部（类 Spotify 布局）
- PlayerBar 视觉优化（沉浸型风格）
- PlaylistView 曲目列表增强（紧凑表格 + 时长列 + 表头）
- Sidebar 歌单列表增强（分组 + 图标 + 选中态）
- 整体视觉统一（动画过渡、间距/圆角、色板、字体层级）

---

## 2. 范围

### In Scope

| 项 | 说明 |
|----|------|
| 布局变更 | PlayerBar 从 Grid.Row="0" 移到 Grid.Row="1"（底部） |
| PlayerBar | 圆形播放键、进度条 Thumb 悬停显示、封面圆角 6px、间距统一 |
| PlaylistView | 时长列、表头行、行分隔线、选中态对比度提升、删除按钮悬停变红 |
| Sidebar | 手动/文件夹分组、每项 🎵/📂 图标、选中态完整背景色 |
| Colors.xaml | 微调色板（选中态对比度、禁用态） |
| Fonts.xaml | 字体层级微调 |
| Controls.xaml | Button/Slider 样式 + 动画过渡 |

### Out of Scope

- 功能变更（不新增功能，仅视觉/交互优化）
- 主题切换功能（仅优化当前深色主题）
- 响应式布局（固定窗口尺寸范围 600×500 ~ 任意）
- 新增 ViewModel / Service

---

## 3. 布局变更

### 3.1 MainWindow.xaml

PlayerBar 从顶部移到底部：

```xml
<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="*"/>      <!-- 主内容区 -->
        <RowDefinition Height="Auto"/>   <!-- PlayerBar 底部 -->
    </Grid.RowDefinitions>

    <Grid Grid.Row="0">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="160" MinWidth="120"/>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <controls:PlaylistsSidebarView Grid.Column="0" DataContext="{Binding Playlists}"/>
        <GridSplitter Grid.Column="1" .../>
        <controls:PlaylistView Grid.Column="2" DataContext="{Binding Playlists.ViewedPlaylist}"/>
    </Grid>

    <controls:PlayerBar Grid.Row="1" DataContext="{Binding Player}"/>
</Grid>
```

---

## 4. PlayerBar 优化

### 4.1 圆形播放键

播放/暂停按钮改为圆形紫色背景：

```xml
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
                    <Setter Property="Command" Value="{Binding DataContext.Playlist.PlayCurrentCommand,
                            RelativeSource={RelativeSource AncestorType=Window}}"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Button.Style>
    <TextBlock Text="{Binding PlayState, Converter={StaticResource PlayStateToIcon}}"
               FontSize="20" Foreground="White"/>
</Button>
```

### 4.2 进度条 Thumb 悬停显示

默认隐藏 Thumb，鼠标悬停在进度条上时显示：

```xml
<Style TargetType="Slider" BasedOn="{StaticResource {x:Type Slider}}">
    <Style.Triggers>
        <Trigger Property="IsMouseOver" Value="True">
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Slider">
                        <Grid>
                            <Track x:Name="PART_Track">
                                <!-- ... 同 Controls.xaml 但 Thumb 更大（18px）且有阴影 -->
                            </Track>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Trigger>
    </Style.Triggers>
</Style>
```

### 4.3 间距统一

- 封面：80×80，CornerRadius="6"
- 控制按钮组：统一 Margin="8,0"
- 音量区域：与控制按钮组右对齐

---

## 5. PlaylistView 优化

### 5.1 表头行

在 ListBox 上方添加固定表头：

```xml
<Grid Grid.Row="0" Margin="0,0,0,2">
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
```

### 5.2 时长列

在 DataTemplate 中新增时长列（需要 Track.Duration 属性）：

```xml
<TextBlock Grid.Column="4"
           Text="{Binding Duration, Converter={StaticResource TimeSpanToString}}"
           FontSize="11" HorizontalAlignment="Right"
           Foreground="{StaticResource ForegroundSecondary}"
           VerticalAlignment="Center"/>
```

### 5.3 行分隔线

ListBoxItem 模板中添加底部分隔线：

```xml
<ControlTemplate TargetType="ListBoxItem">
    <Border x:Name="bd" Background="{TemplateBinding Background}" Padding="8,4">
        <ContentPresenter/>
    </Border>
    <!-- 底部分隔线 -->
    <Border Height="1" Background="{StaticResource BackgroundTertiary}"
            VerticalAlignment="Bottom" Margin="8,0"/>
    <ControlTemplate.Triggers>...</ControlTemplate.Triggers>
</ControlTemplate>
```

### 5.4 删除按钮悬停变红

```xml
<Button.Style>
    <Style TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
        <Setter Property="Foreground" Value="{StaticResource ForegroundSecondary}"/>
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Foreground" Value="#FF6B6B"/>
            </Trigger>
        </Style.Triggers>
    </Style>
</Button.Style>
```

---

## 6. Sidebar 优化

### 6.1 分组显示

手动歌单和文件夹歌单分组。需要在 PlaylistsViewModel 中提供分组数据，或在 XAML 中用 CollectionViewSource 分组。

简化方案：在 DataTemplate 中通过 `HasSourceFolder` 触发器显示分组标题（仅视觉分组，不改 VM）。

### 6.2 每项图标

- 手动歌单：🎵
- 文件夹歌单：📂
- 扫描中：🔄（已有）

### 6.3 选中态完整背景色

当前 ListBoxItem 选中态仅文字颜色变化，改为完整背景色：

```xml
<Style TargetType="ListBoxItem">
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="ListBoxItem">
                <Border x:Name="bd" Background="Transparent" Padding="6,4" CornerRadius="4">
                    <ContentPresenter/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="bd" Property="Background" Value="{StaticResource BackgroundTertiary}"/>
                    </Trigger>
                    <Trigger Property="IsSelected" Value="True">
                        <Setter TargetName="bd" Property="Background" Value="{StaticResource AccentPressed}"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

---

## 7. 整体视觉优化

### 7.1 色板微调（Colors.xaml）

```xml
<!-- 选中态对比度提升 -->
<SolidColorBrush x:Key="BackgroundTertiary" Color="#3E3E52"/>  <!-- 原 #363649，稍亮 -->

<!-- 禁用态可见性 -->
<SolidColorBrush x:Key="ForegroundDisabled" Color="#6C6C80"/>  <!-- 原 #606070，稍亮 -->

<!-- 新增：删除按钮悬停红 -->
<SolidColorBrush x:Key="DangerHover" Color="#FF6B6B"/>
```

### 7.2 字体层级（Fonts.xaml）

```xml
<!-- 标题：16px SemiBold（不变） -->
<Style x:Key="HeaderText" TargetType="TextBlock">
    <Setter Property="FontSize" Value="16"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
</Style>

<!-- 正文：13px Regular（不变） -->
<Style x:Key="BodyText" TargetType="TextBlock">
    <Setter Property="FontSize" Value="13"/>
</Style>

<!-- 说明：11px Regular（不变） -->
<Style x:Key="CaptionText" TargetType="TextBlock">
    <Setter Property="FontSize" Value="11"/>
</Style>
```

### 7.3 动画过渡（Controls.xaml）

Button 样式中添加 Background 过渡动画：

```xml
<ControlTemplate.Triggers>
    <Trigger Property="IsMouseOver" Value="True">
        <Setter TargetName="border" Property="Background" Value="{StaticResource BackgroundTertiary}"/>
    </Trigger>
    <Trigger Property="IsPressed" Value="True">
        <Setter TargetName="border" Property="Background" Value="{StaticResource AccentPressed}"/>
    </Trigger>
</ControlTemplate.Triggers>
```

使用 VisualStateManager 或 EventTrigger 实现 0.15s 过渡：

```xml
<Border x:Name="border" ...>
    <Border.Background>
        <SolidColorBrush x:Name="borderBrush" Color="Transparent"/>
    </Border.Background>
</Border>
<EventTrigger RoutedEvent="MouseEnter">
    <BeginStoryboard>
        <Storyboard>
            <ColorAnimation Storyboard.TargetName="borderBrush"
                            Storyboard.TargetProperty="Color"
                            To="{StaticResource BackgroundTertiary.Color}"
                            Duration="0:0:0.15"/>
        </Storyboard>
    </BeginStoryboard>
</EventTrigger>
```

### 7.4 间距统一

4px 网格系统：
- 小间距：4px
- 标准间距：8px
- 中间距：12px
- 大间距：16px
- 超大间距：24px

圆角统一：
- 小圆角：4px（按钮、小卡片）
- 中圆角：6px（封面、曲目卡片）
- 大圆角：8px（面板、对话框）

---

## 8. 风险与缓解

| 风险 | 影响 | 缓解 |
|------|------|------|
| 动画影响性能 | 低端设备卡顿 | 仅对 Background 做 ColorAnimation，不动画 Layout |
| 时长列需要 Track.Duration | Track record 可能没有 Duration 字段 | 检查现有字段，必要时添加 |
| 分组显示需要 VM 支持 | 简化方案用 DataTemplate 触发器 | 仅视觉分组，不改 VM 结构 |
| PlayerBar 移到底部后跨级绑定 | RelativeSource AncestorType=Window 仍可用 | 无影响，绑定路径不变 |

---

## 9. 测试计划

### 手动验收

1. ✅ PlayerBar 在底部显示，功能正常
2. ✅ 圆形播放键视觉突出，悬停/按下有反馈
3. ✅ 进度条悬停时 Thumb 显示
4. ✅ PlaylistView 有表头行、时长列、行分隔线
5. ✅ 删除按钮悬停变红
6. ✅ Sidebar 分组显示，每项有图标
7. ✅ Sidebar 选中态完整背景色
8. ✅ 按钮悬停有 0.15s 过渡动画
9. ✅ 间距/圆角统一
