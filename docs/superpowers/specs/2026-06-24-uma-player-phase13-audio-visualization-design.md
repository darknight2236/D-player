# Phase 13: 音频可视化设计

> 文档日期：2026/06/24 · 目标分支：`master` · 前置条件：Phase 12 completed

---

## 1. 概述

### 1.1 目标

为 UmaPlayer 添加音频频谱可视化功能，在 TrackInfoView 区域显示实时频谱条形图动画。

### 1.2 核心需求

| 需求 | 描述 |
|------|------|
| 频谱显示 | 32 条垂直频谱柱，对数分组映射 |
| 显示位置 | 集成到 TrackInfoView，封面下方 |
| 视觉样式 | 垂直条形图，渐变色填充，圆角顶部 |
| 配置选项 | 启用/禁用、灵敏度、颜色主题、平滑度 |
| 数据源 | NAudio SampleAggregator + FFT |

### 1.3 技术约束

- 复用现有 `IPlaybackService` 架构，不引入新的顶层服务
- 使用 WPF 渲染（Canvas + Rectangle），不引入 GPU 加速依赖
- 频谱设置持久化到 `settings.json`，与现有 `AppSettings` 合并

---

## 2. 架构设计

### 2.1 数据流

```
┌──────────────────────┐        ┌──────────────────────────────────┐
│ NAudioPlaybackService │        │ PlayerViewModel                  │
│                       │ event  │                                   │
│ MediaFoundationReader │───────▶│ SpectrumData: float[]            │
│       ↓               │        │ SpectrumEnabled: bool            │
│ SampleAggregator      │        │ SpectrumSensitivity: double      │
│  (FFT 1024 samples)  │        │ SpectrumColorTheme: int          │
│       ↓               │        │ SpectrumSmoothing: double        │
│ VolumeSampleProvider  │        │                                   │
│       ↓               │        │ [RelayCommand] ToggleSpectrum     │
│ WasapiOut             │        │                                   │
└──────────────────────┘        └───────────────┬──────────────────┘
                                                │
                                                ▼
                                 ┌──────────────────────────────────┐
                                 │ SpectrumView (UserControl)       │
                                 │                                   │
                                 │ Canvas + DrawingVisual            │
                                 │ 32 条垂直频谱柱                   │
                                 │ 渐变色填充 (AccentPrimary)        │
                                 │ 圆角顶部                          │
                                 └──────────────────────────────────┘
```

### 2.2 播放链修改

**现有播放链：**
```
MediaFoundationReader → VolumeSampleProvider → WasapiOut
```

**Phase 13 播放链：**
```
MediaFoundationReader → SampleAggregator → VolumeSampleProvider → WasapiOut
                      ↑
                      截取 PCM 数据，不修改音频流
```

---

## 3. 数据采集层

### 3.1 SpectrumConfig

```csharp
public record SpectrumConfig
{
    public bool Enabled { get; init; } = true;
    public int BarCount { get; init; } = 32;
    public double Sensitivity { get; init; } = 1.0;      // 0.5 ~ 2.0
    public double Smoothing { get; init; } = 0.8;         // 0.0 ~ 0.95
    public int FftSize { get; init; } = 1024;
}
```

### 3.2 IPlaybackService 扩展

```csharp
public interface IPlaybackService
{
    // ... 现有成员 ...
    
    /// <summary>
    /// 频谱数据可用事件（每帧 FFT 计算后触发）
    /// </summary>
    event Action<float[]>? SpectrumDataAvailable;
    
    /// <summary>
    /// 频谱配置（运行时可更新）
    /// </summary>
    SpectrumConfig SpectrumConfig { get; set; }
}
```

### 3.3 SampleAggregator 实现

`SampleAggregator` 实现 `ISampleProvider` 接口，插入播放链中截取 PCM 数据：

1. 从源读取 PCM 数据
2. 填充 FFT 缓冲区（1024 采样点）
3. 执行 FFT 计算
4. 提取幅度数据，触发 `SpectrumDataReady` 事件
5. 原始 PCM 数据原样传递（不修改音频）

### 3.4 频率分组策略（32 条频谱柱）

| 频率范围 (Hz) | 频谱柱 | 说明 |
|--------------|--------|------|
| 20 ~ 60 | #1 | 超低频 (Sub-bass) |
| 60 ~ 250 | #2~#4 | 低频 (Bass) - 3 条 |
| 250 ~ 500 | #5~#7 | 中低频 (Low-mid) - 3 条 |
| 500 ~ 2000 | #8~#13 | 中频 (Mid) - 6 条 |
| 2000 ~ 6000 | #14~#21 | 中高频 (Upper-mid) - 8 条 |
| 6000 ~ 20000 | #22~#32 | 高频 (Treble) - 11 条 |

采用对数分组，低频区域更密集（人耳对低频更敏感）。

---

## 4. ViewModel 层

### 4.1 PlayerViewModel 新增成员

**属性：**

```csharp
[ObservableProperty]
private float[] _spectrumData = new float[32];

[ObservableProperty]
private bool _spectrumEnabled = true;

[ObservableProperty]
private double _spectrumSensitivity = 1.0;  // 0.5 ~ 2.0

[ObservableProperty]
private int _spectrumColorTheme = 0;  // 0=Purple, 1=Blue, 2=Green, 3=Rainbow

[ObservableProperty]
private double _spectrumSmoothing = 0.8;  // 0.0 ~ 0.95

private float[] _smoothedSpectrum = new float[32];

public string[] SpectrumColorThemes { get; } = ["紫色", "蓝色", "绿色", "彩虹"];
```

**命令：**

```csharp
[RelayCommand]
private void ToggleSpectrum();
```

### 4.2 数据处理流程

1. 订阅 `IPlaybackService.SpectrumDataAvailable` 事件
2. 应用灵敏度增益：`rawData[i] * Sensitivity`
3. 对数分组映射：512 bins → 32 bars
4. 指数移动平均平滑：`smoothed = smoothed * Smoothing + current * (1 - Smoothing)`
5. 更新 `SpectrumData` 属性触发 UI 绑定

### 4.3 属性变更持久化

```csharp
partial void OnSpectrumSensitivityChanged(double value)
    => SaveSpectrumSettings();

partial void OnSpectrumColorThemeChanged(int value)
    => SaveSpectrumSettings();

partial void OnSpectrumSmoothingChanged(double value)
    => SaveSpectrumSettings();

private void SaveSpectrumSettings()
{
    _ = _persistence.UpdateAsync(s => s with
    {
        SpectrumEnabled = SpectrumEnabled,
        SpectrumSensitivity = SpectrumSensitivity,
        SpectrumColorTheme = SpectrumColorTheme,
        SpectrumSmoothing = SpectrumSmoothing
    });
}
```

---

## 5. View 层

### 5.1 SpectrumView 自定义控件

**结构：**
- `Canvas` 容器，`ClipToBounds=True`，高度 120px
- 32 个 `Rectangle` 元素，动态创建
- `CompositionTarget.Rendering` 驱动 60fps 渲染循环

**柱体参数：**
- 间距：2px
- 最小高度：2px
- 最大高度：118px
- 圆角：2px

**颜色主题：**
- 紫色（默认）：`#7C4DFF` → `#9E7CFF` 渐变
- 蓝色：`#2196F3` → `#64B5F6` 渐变
- 绿色：`#4CAF50` → `#81C784` 渐变
- 彩虹：多色 `LinearGradientBrush`

### 5.2 TrackInfoView 集成

```xml
<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>  <!-- 封面 -->
        <RowDefinition Height="Auto"/>  <!-- 频谱 (新增) -->
        <RowDefinition Height="*"/>     <!-- 文本信息 -->
    </Grid.RowDefinitions>
    
    <!-- 封面 (现有) -->
    <Border Grid.Row="0" ...>
        <Viewbox>
            <Image Source="{Binding AlbumArtImage}"/>
        </Viewbox>
    </Border>
    
    <!-- 频谱可视化 (新增) -->
    <local:SpectrumView Grid.Row="1"
                        Margin="0,8,0,0"
                        Height="120"
                        SpectrumData="{Binding SpectrumData}"
                        ColorTheme="{Binding SpectrumColorTheme}"
                        Visibility="{Binding SpectrumEnabled, 
                            Converter={StaticResource BoolToVisibilityConverter}}"/>
    
    <!-- 文本信息 (现有，移至 Row 2) -->
    <StackPanel Grid.Row="2" ...>
        <!-- 标题/艺术家/专辑/采样率 -->
    </StackPanel>
</Grid>
```

---

## 6. 配置持久化

### 6.1 AppSettings 新增字段

```csharp
public record AppSettings
{
    // ... 现有字段 ...
    
    /// <summary>启用频谱可视化</summary>
    public bool SpectrumEnabled { get; init; } = true;
    
    /// <summary>频谱灵敏度 (0.5 ~ 2.0)</summary>
    public double SpectrumSensitivity { get; init; } = 1.0;
    
    /// <summary>频谱颜色主题 (0=紫, 1=蓝, 2=绿, 3=彩虹)</summary>
    public int SpectrumColorTheme { get; init; } = 0;
    
    /// <summary>频谱动画平滑度 (0.0 ~ 0.95)</summary>
    public double SpectrumSmoothing { get; init; } = 0.8;
}
```

### 6.2 SettingsDialog UI 扩展

在 SettingsDialog 中新增 "Audio Visualization" 区域：

- ☑ 启用频谱可视化
- 灵敏度滑块：0.5 ~ 2.0，默认 1.0
- 颜色主题下拉框：紫色/蓝色/绿色/彩虹
- 平滑度滑块：0.0 ~ 0.95，默认 0.8

---

## 7. 实现阶段

| 阶段 | 内容 | 预估工作量 |
|------|------|-----------|
| Task 1 | Models: 新增 `SpectrumConfig` record | 小 |
| Task 2 | Services: `SampleAggregator` 实现 + `NAudioPlaybackService` 集成 | 中 |
| Task 3 | Services: `IPlaybackService` 接口扩展 | 小 |
| Task 4 | ViewModels: `PlayerViewModel` 频谱属性/命令/数据处理 | 中 |
| Task 5 | Views: `SpectrumView` 自定义控件实现 | 中 |
| Task 6 | Views: `TrackInfoView` 集成 `SpectrumView` | 小 |
| Task 7 | Configuration: `AppSettings` 新增字段 | 小 |
| Task 8 | Views: `SettingsDialog` UI 扩展 | 小 |
| Task 9 | DI 注册 + 初始化流程修改 | 小 |
| Task 10 | 测试 + 验收 | 中 |

---

## 8. 测试策略

### 8.1 单元测试（ViewModel 层）

- `SpectrumData_WhenSpectrumEnabled_UpdatesProperty`
- `SpectrumData_WhenSpectrumDisabled_DoesNotUpdate`
- `ToggleSpectrum_TogglesEnabled`
- `SpectrumSettings_Changed_PersistsToSettings`

### 8.2 验收标准

| 标准 | 验证方式 |
|------|---------|
| 频谱随音频实时变化 | 手动播放测试 |
| 32 条频谱柱对数分布 | 观察低频/高频响应差异 |
| 平滑度设置生效 | 调节滑块观察动画变化 |
| 灵敏度设置生效 | 调节滑块观察频谱幅度 |
| 颜色主题切换生效 | 下拉框切换 4 种主题 |
| 启用/禁用开关生效 | 勾选/取消勾选 |
| 暂停时频谱静止 | 暂停播放观察 |
| 设置持久化 | 重启应用验证 |
| CPU 占用 < 5% | 任务管理器监控 |
| 无音频播放时不显示 | DataTrigger 隐藏 |

---

## 9. 风险与缓解

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| FFT 计算开销过高 | CPU 占用超标 | 使用 1024 点 FFT（而非 4096），降低精度换性能 |
| WPF 渲染性能瓶颈 | 动画卡顿 | 使用 CompositionTarget.Rendering 而非 DispatcherTimer |
| 频谱数据抖动 | 视觉体验差 | 指数移动平均平滑 + UI 层二次平滑 |
| 播放链修改引入回归 | 现有功能异常 | SampleAggregator 纯透传，不修改 PCM 数据 |
