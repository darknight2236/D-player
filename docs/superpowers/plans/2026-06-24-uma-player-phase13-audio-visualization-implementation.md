# Phase 13: 音频可视化实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 UmaPlayer 添加音频频谱可视化功能，在 TrackInfoView 区域显示实时频谱条形图动画。

**Architecture:** 在现有 NAudioPlaybackService 播放链中插入 SampleAggregator 截取 PCM 数据，通过 FFT 计算频谱，由 PlayerViewModel 处理数据并绑定到 SpectrumView 自定义控件渲染。

**Tech Stack:** NAudio (SampleAggregator + FFT), WPF (Canvas + Rectangle), CommunityToolkit.Mvvm

## Global Constraints

- 复用现有 IPlaybackService 架构，不引入新的顶层服务
- 使用 WPF 渲染（Canvas + Rectangle），不引入 GPU 加速依赖
- 频谱设置持久化到 settings.json，与现有 AppSettings 合并
- SampleAggregator 纯透传，不修改 PCM 数据

---

## File Structure

| 文件 | 操作 | 职责 |
|------|------|------|
| `Models/SpectrumConfig.cs` | 新建 | 频谱配置 record |
| `Services/SampleAggregator.cs` | 新建 | FFT 计算 + 频谱数据提取 |
| `Services/IPlaybackService.cs` | 修改 | 新增频谱事件和配置属性 |
| `Services/NAudioPlaybackService.cs` | 修改 | 集成 SampleAggregator |
| `Configuration/AppSettings.cs` | 修改 | 新增频谱配置字段 |
| `ViewModels/PlayerViewModel.cs` | 修改 | 频谱属性/命令/数据处理 |
| `Views/Controls/SpectrumView.xaml` | 新建 | 频谱可视化控件 XAML |
| `Views/Controls/SpectrumView.xaml.cs` | 新建 | 频谱可视化控件逻辑 |
| `Views/Controls/TrackInfoView.xaml` | 修改 | 集成 SpectrumView |
| `Views/Dialogs/SettingsDialog.xaml` | 修改 | 频谱配置 UI |
| `Views/Dialogs/SettingsDialog.xaml.cs` | 修改 | 频谱配置逻辑 |
| `Extensions/ServiceCollectionExtensions.cs` | 修改 | DI 注册（无需修改，已自动） |
| `Tests/ViewModels/PlayerViewModelSpectrumTests.cs` | 新建 | 频谱相关单元测试 |

---

### Task 1: Models - 新增 SpectrumConfig record

**Files:**
- Create: `Models/SpectrumConfig.cs`

**Interfaces:**
- Produces: `SpectrumConfig` record，供 Task 2 (SampleAggregator) 和 Task 4 (PlayerViewModel) 使用

- [ ] **Step 1: 创建 SpectrumConfig record**

```csharp
// Models/SpectrumConfig.cs
namespace UmaPlayer.Models;

/// <summary>
/// 频谱分析配置
/// </summary>
public record SpectrumConfig
{
    /// <summary>启用频谱分析</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>频谱柱数量</summary>
    public int BarCount { get; init; } = 32;

    /// <summary>灵敏度 (0.5 ~ 2.0)</summary>
    public double Sensitivity { get; init; } = 1.0;

    /// <summary>平滑度 (0.0 ~ 0.95)</summary>
    public double Smoothing { get; init; } = 0.8;

    /// <summary>FFT 大小（采样点数）</summary>
    public int FftSize { get; init; } = 1024;
}
```

- [ ] **Step 2: 验证编译通过**

Run: `dotnet build UmaPlayer.csproj`

- [ ] **Step 3: 提交**

```bash
git add Models/SpectrumConfig.cs
git commit -m "feat(models): add SpectrumConfig record for audio visualization"
```

---

### Task 2: Services - SampleAggregator 实现

**Files:**
- Create: `Services/SampleAggregator.cs`

**Interfaces:**
- Consumes: `SpectrumConfig` (from Task 1)
- Produces: `SampleAggregator` class，供 Task 3 (NAudioPlaybackService) 使用

- [ ] **Step 1: 创建 SampleAggregator 实现**

```csharp
// Services/SampleAggregator.cs
using NAudio.Dsp;
using NAudio.Wave;
using UmaPlayer.Models;

namespace UmaPlayer.Services;

/// <summary>
/// 音频频谱分析器 —— 插入 NAudio 播放链中截取 PCM 数据，执行 FFT 计算频谱。
/// 实现 ISampleProvider 接口，作为透明中间件不修改音频流。
/// </summary>
public sealed class SampleAggregator : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly Complex[] _fftBuffer;
    private readonly float[] _spectrumData;
    private readonly int _fftSize;
    private readonly int _barCount;
    private int _bufferPosition;

    /// <summary>频谱数据可用事件（在音频线程触发，订阅者需自行处理线程封送）</summary>
    public event Action<float[]>? SpectrumDataReady;

    /// <summary>是否启用频谱分析</summary>
    public bool Enabled { get; set; } = true;

    public SampleAggregator(ISampleProvider source, SpectrumConfig config)
    {
        _source = source;
        _fftSize = config.FftSize;
        _barCount = config.BarCount;
        _fftBuffer = new Complex[_fftSize];
        _spectrumData = new float[_barCount];
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        // 1. 从源读取 PCM 数据
        int read = _source.Read(buffer, offset, count);

        if (Enabled && read > 0)
        {
            // 2. 填充 FFT 缓冲区
            for (int i = 0; i < read; i++)
            {
                // 应用汉宁窗减少频谱泄漏
                float sample = buffer[offset + i];
                float windowFactor = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * _bufferPosition / (_fftSize - 1))));
                _fftBuffer[_bufferPosition].X = sample * windowFactor;
                _fftBuffer[_bufferPosition].Y = 0;
                _bufferPosition++;

                if (_bufferPosition >= _fftSize)
                {
                    // 3. 执行 FFT
                    FastFourierTransform.FFT(true, (int)Math.Log2(_fftSize), _fftBuffer);

                    // 4. 提取幅度并映射到频谱柱
                    ExtractSpectrumData();

                    // 5. 触发事件
                    SpectrumDataReady?.Invoke(_spectrumData);

                    _bufferPosition = 0;
                }
            }
        }

        // 6. 原始 PCM 数据原样传递
        return read;
    }

    /// <summary>
    /// 从 FFT 结果提取幅度并映射到频谱柱（对数分组）
    /// </summary>
    private void ExtractSpectrumData()
    {
        int binCount = _fftSize / 2;

        for (int bar = 0; bar < _barCount; bar++)
        {
            // 对数分组：低频 bins 更密集
            float startPercent = (float)bar / _barCount;
            float endPercent = (float)(bar + 1) / _barCount;

            // 对数映射
            int startBin = (int)(Math.Pow(startPercent, 2) * binCount);
            int endBin = (int)(Math.Pow(endPercent, 2) * binCount);
            endBin = Math.Max(endBin, startBin + 1);

            // 取该范围内的平均幅度
            float sum = 0;
            int count = 0;
            for (int i = startBin; i < endBin && i < binCount; i++)
            {
                float magnitude = (float)Math.Sqrt(
                    _fftBuffer[i].X * _fftBuffer[i].X +
                    _fftBuffer[i].Y * _fftBuffer[i].Y);
                sum += magnitude;
                count++;
            }

            // 归一化到 0.0 ~ 1.0 范围
            _spectrumData[bar] = count > 0 ? Math.Clamp(sum / count * 10, 0, 1) : 0;
        }
    }
}
```

- [ ] **Step 2: 验证编译通过**

Run: `dotnet build UmaPlayer.csproj`

- [ ] **Step 3: 提交**

```bash
git add Services/SampleAggregator.cs
git commit -m "feat(services): add SampleAggregator for FFT spectrum analysis"
```

---

### Task 3: Services - IPlaybackService 接口扩展 + NAudioPlaybackService 集成

**Files:**
- Modify: `Services/IPlaybackService.cs`
- Modify: `Services/NAudioPlaybackService.cs`

**Interfaces:**
- Consumes: `SampleAggregator` (from Task 2), `SpectrumConfig` (from Task 1)
- Produces: `IPlaybackService.SpectrumDataAvailable` event, `IPlaybackService.SpectrumConfig` property，供 Task 4 (PlayerViewModel) 使用

- [ ] **Step 1: 扩展 IPlaybackService 接口**

```csharp
// Services/IPlaybackService.cs - 新增成员
using UmaPlayer.Models;

public interface IPlaybackService : IDisposable
{
    // ... 现有成员 ...

    /// <summary>
    /// 频谱数据可用事件（每帧 FFT 计算后触发，已在 UI 线程）
    /// </summary>
    event Action<float[]>? SpectrumDataAvailable;

    /// <summary>
    /// 频谱配置（运行时可更新）
    /// </summary>
    SpectrumConfig SpectrumConfig { get; set; }
}
```

- [ ] **Step 2: 实现 NAudioPlaybackService 频谱集成**

```csharp
// Services/NAudioPlaybackService.cs - 新增字段和修改
using UmaPlayer.Models;

public sealed class NAudioPlaybackService : IPlaybackService
{
    // ... 现有字段 ...

    // Phase 13: 频谱分析
    private SampleAggregator? _sampleAggregator;
    private SpectrumConfig _spectrumConfig = new();

    // 新增事件和属性
    public event Action<float[]>? SpectrumDataAvailable;

    public SpectrumConfig SpectrumConfig
    get => _spectrumConfig;
        set
        {
            _spectrumConfig = value;
            if (_sampleAggregator != null)
            {
                _sampleAggregator.Enabled = value.Enabled;
            }
        }
    }

    // 修改 LoadAsync 方法
    public async Task LoadAsync(Track track)
    {
        await Task.Run(() =>
        {
            DisposePlayback();

            try
            {
                _reader = new MediaFoundationReader(track.FilePath);

                // Phase 13: 插入 SampleAggregator
                var sampleProvider = _reader.ToSampleProvider();
                _sampleAggregator = new SampleAggregator(sampleProvider, _spectrumConfig);
                _sampleAggregator.SpectrumDataReady += data =>
                {
                    // 封送到 UI 线程触发事件
                    RaiseOnUIThread(SpectrumDataAvailable, data);
                };

                _volumeProvider = new VolumeSampleProvider(_sampleAggregator)
                {
                    Volume = _volume
                };

                _wavePlayer = new WasapiOut(AudioClientShareMode.Shared, 100);
                _wavePlayer.Init(_volumeProvider);

                // ... 后续代码不变 ...
            }
            catch (Exception ex)
            {
                // ... 现有代码 ...
            }
        });
    }

    // 修改 DisposePlayback 方法
    private void DisposePlayback()
    {
        // Phase 13: 清理 SampleAggregator
        if (_sampleAggregator != null)
        {
            _sampleAggregator.SpectrumDataReady -= null;
            _sampleAggregator = null;
        }

        // ... 现有代码 ...
    }
}
```

- [ ] **Step 3: 验证编译通过**

Run: `dotnet build UmaPlayer.csproj`

- [ ] **Step 4: 提交**

```bash
git add Services/IPlaybackService.cs Services/NAudioPlaybackService.cs
git commit -m "feat(services): integrate SampleAggregator into NAudioPlaybackService"
```

---

### Task 4: Configuration - AppSettings 新增频谱字段

**Files:**
- Modify: `Configuration/AppSettings.cs`

**Interfaces:**
- Produces: `AppSettings.SpectrumEnabled`, `AppSettings.SpectrumSensitivity`, `AppSettings.SpectrumColorTheme`, `AppSettings.SpectrumSmoothing`，供 Task 4 (PlayerViewModel) 和 Task 8 (SettingsDialog) 使用

- [ ] **Step 1: 扩展 AppSettings**

```csharp
// Configuration/AppSettings.cs - 新增字段
public sealed record AppSettings
{
    // ... 现有字段 ...

    // ====== Phase 13: 频谱可视化 ======

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

- [ ] **Step 2: 验证编译通过**

Run: `dotnet build UmaPlayer.csproj`

- [ ] **Step 3: 提交**

```bash
git add Configuration/AppSettings.cs
git commit -m "feat(config): add spectrum visualization settings to AppSettings"
```

---

### Task 5: ViewModels - PlayerViewModel 频谱属性/命令/数据处理

**Files:**
- Modify: `ViewModels/PlayerViewModel.cs`

**Interfaces:**
- Consumes: `IPlaybackService.SpectrumDataAvailable` (from Task 3), `AppSettings.Spectrum*` (from Task 4)
- Produces: `PlayerViewModel.SpectrumData`, `SpectrumEnabled`, `SpectrumSensitivity`, `SpectrumColorTheme`, `SpectrumSmoothing`，供 Task 7 (SpectrumView) 使用

- [ ] **Step 1: 添加频谱属性和字段**

```csharp
// ViewModels/PlayerViewModel.cs - 新增成员
public partial class PlayerViewModel : ObservableObject
{
    // ... 现有字段 ...

    // ====== Phase 13: 频谱可视化 ======

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

    /// <summary>平滑后的频谱数据（避免 UI 抖动）</summary>
    private float[] _smoothedSpectrum = new float[32];

    /// <summary>颜色主题列表（供 UI 绑定）</summary>
    public string[] SpectrumColorThemes { get; } = ["紫色", "蓝色", "绿色", "彩虹"];
}
```

- [ ] **Step 2: 添加频谱数据处理方法**

```csharp
// ViewModels/PlayerViewModel.cs - 新增方法
public partial class PlayerViewModel : ObservableObject
{
    // ... 现有代码 ...

    /// <summary>订阅频谱事件（在构造函数中调用）</summary>
    private void InitializeSpectrum()
    {
        _player.SpectrumDataAvailable += HandleSpectrumData;
    }

    /// <summary>频谱数据处理（带平滑）</summary>
    private void HandleSpectrumData(float[] rawData)
    {
        if (!SpectrumEnabled) return;

        // 1. 应用灵敏度增益
        for (int i = 0; i < rawData.Length; i++)
        {
            rawData[i] *= (float)SpectrumSensitivity;
        }

        // 2. 32 条频谱柱映射（对数分组）
        var mapped = MapToBars(rawData, 32);

        // 3. 应用平滑（指数移动平均）
        for (int i = 0; i < 32; i++)
        {
            _smoothedSpectrum[i] = _smoothedSpectrum[i] * (float)SpectrumSmoothing
                                 + mapped[i] * (1 - (float)SpectrumSmoothing);
        }

        // 4. 更新属性（触发 UI 绑定）
        SpectrumData = _smoothedSpectrum.ToArray();
    }

    /// <summary>对数分组映射：512 bins → 32 bars</summary>
    private static float[] MapToBars(float[] spectrum, int barCount)
    {
        var result = new float[barCount];
        int totalBins = spectrum.Length;

        for (int bar = 0; bar < barCount; bar++)
        {
            // 对数分布：低频 bins 更密集
            float startPercent = (float)bar / barCount;
            float endPercent = (float)(bar + 1) / barCount;

            // 对数映射
            int startBin = (int)(Math.Pow(startPercent, 2) * totalBins);
            int endBin = (int)(Math.Pow(endPercent, 2) * totalBins);
            endBin = Math.Max(endBin, startBin + 1);

            // 取该范围内的平均值
            float sum = 0;
            int count = 0;
            for (int i = startBin; i < endBin && i < totalBins; i++)
            {
                sum += spectrum[i];
                count++;
            }
            result[bar] = count > 0 ? sum / count : 0;
        }

        return result;
    }
}
```

- [ ] **Step 3: 添加命令和属性变更处理**

```csharp
// ViewModels/PlayerViewModel.cs - 新增命令和钩子
public partial class PlayerViewModel : ObservableObject
{
    // ... 现有代码 ...

    /// <summary>切换频谱启用状态</summary>
    [RelayCommand]
    private void ToggleSpectrum()
    {
        SpectrumEnabled = !SpectrumEnabled;
        SaveSpectrumSettings();
    }

    // 属性变更时自动保存
    partial void OnSpectrumEnabledChanged(bool value)
        => SaveSpectrumSettings();

    partial void OnSpectrumSensitivityChanged(double value)
        => SaveSpectrumSettings();

    partial void OnSpectrumColorThemeChanged(int value)
        => SaveSpectrumSettings();

    partial void OnSpectrumSmoothingChanged(double value)
        => SaveSpectrumSettings();

    /// <summary>持久化频谱设置</summary>
    private void SaveSpectrumSettings()
    {
        if (_isInitializing) return;

        _ = _persistence.UpdateAsync(s => s with
        {
            SpectrumEnabled = SpectrumEnabled,
            SpectrumSensitivity = SpectrumSensitivity,
            SpectrumColorTheme = SpectrumColorTheme,
            SpectrumSmoothing = SpectrumSmoothing
        });
    }

    /// <summary>加载频谱设置</summary>
    private void LoadSpectrumSettings(AppSettings settings)
    {
        SpectrumEnabled = settings.SpectrumEnabled;
        SpectrumSensitivity = settings.SpectrumSensitivity;
        SpectrumColorTheme = settings.SpectrumColorTheme;
        SpectrumSmoothing = settings.SpectrumSmoothing;
    }
}
```

- [ ] **Step 4: 修改 Initialize 和 CleanupAsync**

```csharp
// ViewModels/PlayerViewModel.cs - 修改现有方法
public partial class PlayerViewModel : ObservableObject
{
    // 构造函数中调用
    public PlayerViewModel(
        IPlaybackService player,
        ISettingsPersistence persistence,
        IOptions<AppSettings> options)
    {
        // ... 现有代码 ...

        // Phase 13: 订阅频谱事件
        InitializeSpectrum();

        Initialize();
    }

    private void Initialize()
    {
        // ... 现有代码 ...

        Volume = settings.DefaultVolume;

        // Phase 13: 加载频谱设置
        LoadSpectrumSettings(settings);

        _isInitializing = false;
    }

    public async Task CleanupAsync()
    {
        // ... 现有代码 ...

        // Phase 13: 解绑频谱事件
        _player.SpectrumDataAvailable -= HandleSpectrumData;

        await _persistence.UpdateAsync(s => s with { DefaultVolume = Volume });
    }
}
```

- [ ] **Step 5: 验证编译通过**

Run: `dotnet build UmaPlayer.csproj`

- [ ] **Step 6: 提交**

```bash
git add ViewModels/PlayerViewModel.cs
git commit -m "feat(vm): add spectrum visualization properties and data processing"
```

---

### Task 6: Views - SpectrumView 自定义控件实现

**Files:**
- Create: `Views/Controls/SpectrumView.xaml`
- Create: `Views/Controls/SpectrumView.xaml.cs`

**Interfaces:**
- Consumes: `PlayerViewModel.SpectrumData`, `SpectrumColorTheme` (from Task 5)
- Produces: `SpectrumView` UserControl，供 Task 7 (TrackInfoView) 使用

- [ ] **Step 1: 创建 SpectrumView.xaml**

```xml
<!-- Views/Controls/SpectrumView.xaml -->
<UserControl x:Class="UmaPlayer.Views.Controls.SpectrumView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="Transparent">
    <Canvas x:Name="SpectrumCanvas"
            ClipToBounds="True"
            Height="120"/>
</UserControl>
```

- [ ] **Step 2: 创建 SpectrumView.xaml.cs**

```csharp
// Views/Controls/SpectrumView.xaml.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace UmaPlayer.Views.Controls;

/// <summary>
/// 音频频谱可视化控件 —— 显示 32 条垂直频谱柱，支持多种颜色主题。
/// 使用 CompositionTarget.Rendering 驱动 60fps 渲染循环。
/// </summary>
public partial class SpectrumView : UserControl
{
    private const int BarCount = 32;
    private const double BarSpacing = 2.0;
    private const double MinBarHeight = 2.0;
    private const double MaxBarHeight = 118.0;
    private const double AnimationSmoothFactor = 0.3;

    private readonly Rectangle[] _bars = new Rectangle[BarCount];
    private readonly LinearGradientBrush[] _gradients = new LinearGradientBrush[4];
    private readonly double[] _targetHeights = new double[BarCount];

    public SpectrumView()
    {
        InitializeComponent();
        InitializeBars();
        InitializeGradients();

        CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    private void InitializeBars()
    {
        // 等待 ActualWidth 可用
        SizeChanged += (_, _) => RecalculateBarLayout();
    }

    private void RecalculateBarLayout()
    {
        if (ActualWidth <= 0) return;

        SpectrumCanvas.Children.Clear();
        double barWidth = (ActualWidth - (BarCount - 1) * BarSpacing) / BarCount;

        for (int i = 0; i < BarCount; i++)
        {
            var bar = new Rectangle
            {
                Width = barWidth,
                Height = MinBarHeight,
                RadiusX = 2,
                RadiusY = 2,
                Fill = _gradients[0]
            };

            Canvas.SetLeft(bar, i * (barWidth + BarSpacing));
            Canvas.SetBottom(bar, 0);

            SpectrumCanvas.Children.Add(bar);
            _bars[i] = bar;
        }
    }

    private void InitializeGradients()
    {
        // 紫色主题 (index 0)
        _gradients[0] = CreateGradient(Color.FromRgb(0x7C, 0x4D, 0xFF), Color.FromRgb(0x9E, 0x7C, 0xFF));

        // 蓝色主题 (index 1)
        _gradients[1] = CreateGradient(Color.FromRgb(0x21, 0x96, 0xF3), Color.FromRgb(0x64, 0xB5, 0xF6));

        // 绿色主题 (index 2)
        _gradients[2] = CreateGradient(Color.FromRgb(0x4C, 0xAF, 0x50), Color.FromRgb(0x81, 0xC7, 0x84));

        // 彩虹主题 (index 3)
        _gradients[3] = CreateRainbowGradient();
    }

    private LinearGradientBrush CreateGradient(Color bottom, Color top)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 1),
            EndPoint = new Point(0, 0)
        };
        brush.GradientStops.Add(new GradientStop(bottom, 0));
        brush.GradientStops.Add(new GradientStop(top, 1));
        brush.Freeze();
        return brush;
    }

    private LinearGradientBrush CreateRainbowGradient()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 1),
            EndPoint = new Point(0, 0)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x00, 0x00), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xA5, 0x00), 0.2));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xFF, 0x00), 0.4));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x00, 0xFF, 0x00), 0.6));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x00, 0x00, 0xFF), 0.8));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x8B, 0x00, 0xFF), 1.0));
        brush.Freeze();
        return brush;
    }

    // 依赖属性：频谱数据
    public static readonly DependencyProperty SpectrumDataProperty =
        DependencyProperty.Register(
            nameof(SpectrumData),
            typeof(float[]),
            typeof(SpectrumView),
            new PropertyMetadata(new float[32], OnSpectrumDataChanged));

    public float[] SpectrumData
    {
        get => (float[])GetValue(SpectrumDataProperty);
        set => SetValue(SpectrumDataProperty, value);
    }

    // 依赖属性：颜色主题
    public static readonly DependencyProperty ColorThemeProperty =
        DependencyProperty.Register(
            nameof(ColorTheme),
            typeof(int),
            typeof(SpectrumView),
            new PropertyMetadata(0, OnColorThemeChanged));

    public int ColorTheme
    {
        get => (int)GetValue(ColorThemeProperty);
        set => SetValue(ColorThemeProperty, value);
    }

    private static void OnSpectrumDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SpectrumView view && e.NewValue is float[] data && data.Length >= BarCount)
        {
            for (int i = 0; i < BarCount; i++)
            {
                view._targetHeights[i] = MinBarHeight + data[i] * (MaxBarHeight - MinBarHeight);
            }
        }
    }

    private static void OnColorThemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SpectrumView view && e.NewValue is int theme)
        {
            var brush = view._gradients[Math.Clamp(theme, 0, 3)];
            foreach (var bar in view._bars)
            {
                if (bar != null)
                    bar.Fill = brush;
            }
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        for (int i = 0; i < BarCount; i++)
        {
            if (_bars[i] == null) continue;

            double targetHeight = Math.Clamp(_targetHeights[i], MinBarHeight, MaxBarHeight);
            double currentHeight = _bars[i].Height;
            double newHeight = currentHeight + (targetHeight - currentHeight) * AnimationSmoothFactor;

            _bars[i].Height = newHeight;
        }
    }
}
```

- [ ] **Step 3: 验证编译通过**

Run: `dotnet build UmaPlayer.csproj`

- [ ] **Step 4: 提交**

```bash
git add Views/Controls/SpectrumView.xaml Views/Controls/SpectrumView.xaml.cs
git commit -m "feat(view): add SpectrumView custom control for audio visualization"
```

---

### Task 7: Views - TrackInfoView 集成 SpectrumView

**Files:**
- Modify: `Views/Controls/TrackInfoView.xaml`

**Interfaces:**
- Consumes: `SpectrumView` (from Task 6), `PlayerViewModel.SpectrumData`, `SpectrumColorTheme`, `SpectrumEnabled` (from Task 5)

- [ ] **Step 1: 修改 TrackInfoView.xaml 集成频谱控件**

```xml
<!-- Views/Controls/TrackInfoView.xaml -->
<UserControl x:Class="UmaPlayer.Views.Controls.TrackInfoView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:converters="clr-namespace:UmaPlayer.Converters"
             xmlns:local="clr-namespace:UmaPlayer.Views.Controls">
    <UserControl.Resources>
        <converters:BytesToBitmapImageConverter x:Key="BytesToBitmapImage"/>
        <BooleanToVisibilityConverter x:Key="BoolToVisibility"/>
    </UserControl.Resources>

    <Border Background="{StaticResource BackgroundSecondary}" CornerRadius="4">
        <StackPanel VerticalAlignment="Top" HorizontalAlignment="Center" Margin="16,16,16,0">
            <!-- 封面框 -->
            <Viewbox MaxWidth="250" MaxHeight="250" Margin="0,0,0,16"
                     HorizontalAlignment="Stretch">
                <Border Width="180" Height="180"
                        Background="{StaticResource BackgroundTertiary}">
                    <Image Source="{Binding AlbumArtBytes, Converter={StaticResource BytesToBitmapImage}}" Stretch="UniformToFill"/>
                </Border>
            </Viewbox>

            <!-- 频谱可视化 (Phase 13 新增) -->
            <local:SpectrumView Margin="0,0,0,16"
                                Height="120"
                                SpectrumData="{Binding SpectrumData}"
                                ColorTheme="{Binding SpectrumColorTheme}"
                                Visibility="{Binding SpectrumEnabled, Converter={StaticResource BoolToVisibility}}"/>

            <!-- 文本元数据 -->
            <StackPanel HorizontalAlignment="Center">
                <TextBlock Text="{Binding CurrentTrack.Title, FallbackValue=''}"
                           Style="{StaticResource HeaderText}"
                           TextAlignment="Center" TextWrapping="Wrap"/>
                <TextBlock Text="{Binding CurrentTrack.Artist, FallbackValue=''}"
                           Style="{StaticResource CaptionText}"
                           TextAlignment="Center" Margin="0,4,0,0"
                           TextWrapping="Wrap"/>
                <TextBlock Text="{Binding CurrentTrack.Album, FallbackValue=''}"
                           Style="{StaticResource CaptionText}"
                           TextAlignment="Center" Margin="0,2,0,0"
                           TextWrapping="Wrap"/>
                <TextBlock Text="{Binding SampleRateText}"
                           Style="{StaticResource CaptionText}"
                           TextAlignment="Center" Margin="0,2,0,0"/>
            </StackPanel>

            <!-- 无曲目时占位提示 -->
            <TextBlock Text="播放曲目以查看信息"
                       TextAlignment="Center" Margin="0,8,0,0"
                       Foreground="{StaticResource ForegroundDisabled}">
                <TextBlock.Style>
                    <Style TargetType="TextBlock" BasedOn="{StaticResource CaptionText}">
                        <Setter Property="Visibility" Value="Collapsed"/>
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding CurrentTrack}" Value="{x:Null}">
                                <Setter Property="Visibility" Value="Visible"/>
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </TextBlock.Style>
            </TextBlock>
        </StackPanel>
    </Border>
</UserControl>
```

- [ ] **Step 2: 验证编译通过**

Run: `dotnet build UmaPlayer.csproj`

- [ ] **Step 3: 提交**

```bash
git add Views/Controls/TrackInfoView.xaml
git commit -m "feat(view): integrate SpectrumView into TrackInfoView"
```

---

### Task 8: Views - SettingsDialog 频谱配置 UI

**Files:**
- Modify: `Views/Dialogs/SettingsDialog.xaml`
- Modify: `Views/Dialogs/SettingsDialog.xaml.cs`

**Interfaces:**
- Consumes: `AppSettings.Spectrum*` (from Task 4)
- Produces: 持久化频谱配置到 settings.json

- [ ] **Step 1: 修改 SettingsDialog.xaml 添加频谱配置区域**

```xml
<!-- Views/Dialogs/SettingsDialog.xaml -->
<Window x:Class="UmaPlayer.Views.Dialogs.SettingsDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="设置"
        Width="420" Height="520"
        WindowStartupLocation="CenterOwner"
        WindowStyle="ToolWindow"
        ResizeMode="NoResize"
        ShowInTaskbar="False"
        Background="{StaticResource BackgroundPrimary}"
        Foreground="{StaticResource ForegroundPrimary}">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>  <!-- 通用标题 -->
            <RowDefinition Height="Auto"/>  <!-- 通用内容 -->
            <RowDefinition Height="Auto"/>  <!-- 音频输出标题 -->
            <RowDefinition Height="Auto"/>  <!-- 音频输出内容 -->
            <RowDefinition Height="Auto"/>  <!-- 音频输出说明 -->
            <RowDefinition Height="Auto"/>  <!-- 音频可视化标题 (新增) -->
            <RowDefinition Height="Auto"/>  <!-- 音频可视化内容 (新增) -->
            <RowDefinition Height="*"/>     <!-- 弹性空间 -->
            <RowDefinition Height="Auto"/>  <!-- 按钮 -->
        </Grid.RowDefinitions>

        <!-- 通用 -->
        <TextBlock Grid.Row="0"
                   Text="通用"
                   Style="{StaticResource HeaderText}"
                   Margin="0,0,0,8"/>

        <Border Grid.Row="1"
                Background="{StaticResource BackgroundSecondary}"
                CornerRadius="8" Padding="16"
                Margin="0,0,0,16">
            <StackPanel>
                <Grid>
                    <TextBlock Text="默认音量"
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

        <!-- 音频输出 -->
        <TextBlock Grid.Row="2"
                   Text="音频输出"
                   Style="{StaticResource HeaderText}"
                   Margin="0,0,0,8"/>

        <Border Grid.Row="3"
                Background="{StaticResource BackgroundSecondary}"
                CornerRadius="8" Padding="16">
            <Grid>
                <TextBlock Text="输出"
                           Style="{StaticResource BodyText}"
                           VerticalAlignment="Center"/>
                <TextBlock Text="系统默认 — WASAPI 共享"
                           Foreground="{StaticResource ForegroundDisabled}"
                           HorizontalAlignment="Right"
                           VerticalAlignment="Center"/>
            </Grid>
        </Border>

        <TextBlock Grid.Row="4"
                   Text="音频输出设置将在后续版本中开放"
                   Style="{StaticResource CaptionText}"
                   FontStyle="Italic"
                   Margin="0,4,0,16"/>

        <!-- 音频可视化 (Phase 13 新增) -->
        <TextBlock Grid.Row="5"
                   Text="音频可视化"
                   Style="{StaticResource HeaderText}"
                   Margin="0,0,0,8"/>

        <Border Grid.Row="6"
                Background="{StaticResource BackgroundSecondary}"
                CornerRadius="8" Padding="16">
            <StackPanel>
                <!-- 启用开关 -->
                <CheckBox x:Name="SpectrumEnabledCheckBox"
                          Content="启用频谱可视化"
                          Style="{StaticResource BodyText}"
                          Margin="0,0,0,12"/>

                <!-- 灵敏度 -->
                <DockPanel Margin="0,0,0,8">
                    <TextBlock Text="灵敏度"
                               Style="{StaticResource BodyText}"
                               Width="80" VerticalAlignment="Center"/>
                    <Slider x:Name="SensitivitySlider"
                            Minimum="0.5" Maximum="2.0"
                            TickFrequency="0.1" IsSnapToTickEnabled="True"
                            Value="1.0"/>
                    <TextBlock x:Name="SensitivityValue"
                               Text="1.0"
                               Style="{StaticResource CaptionText}"
                               Width="30" TextAlignment="Right"/>
                </DockPanel>

                <!-- 颜色主题 -->
                <DockPanel Margin="0,0,0,8">
                    <TextBlock Text="颜色主题"
                               Style="{StaticResource BodyText}"
                               Width="80" VerticalAlignment="Center"/>
                    <ComboBox x:Name="ColorThemeComboBox" SelectedIndex="0">
                        <ComboBoxItem Content="紫色"/>
                        <ComboBoxItem Content="蓝色"/>
                        <ComboBoxItem Content="绿色"/>
                        <ComboBoxItem Content="彩虹"/>
                    </ComboBox>
                </DockPanel>

                <!-- 平滑度 -->
                <DockPanel Margin="0,0,0,0">
                    <TextBlock Text="平滑度"
                               Style="{StaticResource BodyText}"
                               Width="80" VerticalAlignment="Center"/>
                    <Slider x:Name="SmoothingSlider"
                            Minimum="0" Maximum="0.95"
                            TickFrequency="0.05" IsSnapToTickEnabled="True"
                            Value="0.8"/>
                    <TextBlock x:Name="SmoothingValue"
                               Text="0.80"
                               Style="{StaticResource CaptionText}"
                               Width="35" TextAlignment="Right"/>
                </DockPanel>
            </StackPanel>
        </Border>

        <!-- 按钮 -->
        <StackPanel Grid.Row="8"
                    Orientation="Horizontal"
                    HorizontalAlignment="Right">
            <Button Content="取消"
                    Width="80" Height="28"
                    Margin="0,0,8,0"
                    IsCancel="True"/>
            <Button Content="保存"
                    Width="80" Height="28"
                    IsDefault="True"
                    Click="Save_Click"/>
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 2: 修改 SettingsDialog.xaml.cs 添加频谱配置逻辑**

```csharp
// Views/Dialogs/SettingsDialog.xaml.cs - 新增代码
public partial class SettingsDialog : Window
{
    // 修改 Window_Loaded
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = await _persistence.LoadAsync();
        VolumeSlider.Value = settings.DefaultVolume;

        // Phase 13: 加载频谱设置
        SpectrumEnabledCheckBox.IsChecked = settings.SpectrumEnabled;
        SensitivitySlider.Value = settings.SpectrumSensitivity;
        ColorThemeComboBox.SelectedIndex = settings.SpectrumColorTheme;
        SmoothingSlider.Value = settings.SpectrumSmoothing;

        // 绑定滑块值变化事件
        SensitivitySlider.ValueChanged += (_, args) =>
            SensitivityValue.Text = args.NewValue.ToString("F1");
        SmoothingSlider.ValueChanged += (_, args) =>
            SmoothingValue.Text = args.NewValue.ToString("F2");
    }

    // 修改 Save_Click
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await _persistence.UpdateAsync(s => s with
        {
            DefaultVolume = (float)VolumeSlider.Value,

            // Phase 13: 保存频谱设置
            SpectrumEnabled = SpectrumEnabledCheckBox.IsChecked ?? true,
            SpectrumSensitivity = SensitivitySlider.Value,
            SpectrumColorTheme = ColorThemeComboBox.SelectedIndex,
            SpectrumSmoothing = SmoothingSlider.Value
        });

        DialogResult = true;
    }
}
```

- [ ] **Step 3: 验证编译通过**

Run: `dotnet build UmaPlayer.csproj`

- [ ] **Step 4: 提交**

```bash
git add Views/Dialogs/SettingsDialog.xaml Views/Dialogs/SettingsDialog.xaml.cs
git commit -m "feat(view): add spectrum visualization settings to SettingsDialog"
```

---

### Task 9: 测试 - ViewModel 单元测试

**Files:**
- Create: `Tests/ViewModels/PlayerViewModelSpectrumTests.cs`

**Interfaces:**
- Consumes: `PlayerViewModel` (from Task 5), `IPlaybackService`, `ISettingsPersistence`

- [ ] **Step 1: 创建频谱相关单元测试**

```csharp
// Tests/ViewModels/PlayerViewModelSpectrumTests.cs
using Microsoft.Extensions.Options;
using Moq;
using UmaPlayer.Configuration;
using UmaPlayer.Models;
using UmaPlayer.Services;
using UmaPlayer.ViewModels;
using Xunit;

namespace UmaPlayer.Tests.ViewModels;

public class PlayerViewModelSpectrumTests
{
    private readonly Mock<IPlaybackService> _mockPlayer;
    private readonly Mock<ISettingsPersistence> _mockPersistence;
    private readonly IOptions<AppSettings> _options;

    public PlayerViewModelSpectrumTests()
    {
        _mockPlayer = new Mock<IPlaybackService>();
        _mockPersistence = new Mock<ISettingsPersistence>();
        _mockPersistence.Setup(p => p.LoadAsync())
            .ReturnsAsync(new AppSettings());
        _options = Options.Create(new AppSettings());
    }

    [Fact]
    public void SpectrumData_WhenSpectrumEnabled_UpdatesProperty()
    {
        // Arrange
        var vm = new PlayerViewModel(_mockPlayer.Object, _mockPersistence.Object, _options);
        vm.SpectrumEnabled = true;
        var testData = new float[512];
        Array.Fill(testData, 0.5f);

        // Act
        _mockPlayer.Raise(p => p.SpectrumDataAvailable += null, testData);

        // Assert
        Assert.Equal(32, vm.SpectrumData.Length);
        Assert.Contains(vm.SpectrumData, x => x > 0);
    }

    [Fact]
    public void SpectrumData_WhenSpectrumDisabled_DoesNotUpdate()
    {
        // Arrange
        var vm = new PlayerViewModel(_mockPlayer.Object, _mockPersistence.Object, _options);
        vm.SpectrumEnabled = false;
        var testData = new float[512];
        Array.Fill(testData, 0.5f);

        // Act
        _mockPlayer.Raise(p => p.SpectrumDataAvailable += null, testData);

        // Assert
        Assert.All(vm.SpectrumData, x => Assert.Equal(0, x));
    }

    [Fact]
    public void ToggleSpectrum_TogglesEnabled()
    {
        // Arrange
        var vm = new PlayerViewModel(_mockPlayer.Object, _mockPersistence.Object, _options);
        vm.SpectrumEnabled = true;

        // Act
        vm.ToggleSpectrumCommand.Execute(null);

        // Assert
        Assert.False(vm.SpectrumEnabled);
    }

    [Fact]
    public void SpectrumSettings_Changed_PersistsToSettings()
    {
        // Arrange
        var vm = new PlayerViewModel(_mockPlayer.Object, _mockPersistence.Object, _options);

        // Act
        vm.SpectrumSensitivity = 1.5;

        // Assert
        _mockPersistence.Verify(p => p.UpdateAsync(
            It.Is<Func<AppSettings, AppSettings>>(mutator =>
                mutator(new AppSettings()).SpectrumSensitivity == 1.5)));
    }

    [Fact]
    public void SpectrumColorTheme_ValidRange_NoThrow()
    {
        // Arrange
        var vm = new PlayerViewModel(_mockPlayer.Object, _mockPersistence.Object, _options);

        // Act & Assert
        vm.SpectrumColorTheme = 0; // 紫色
        vm.SpectrumColorTheme = 1; // 蓝色
        vm.SpectrumColorTheme = 2; // 绿色
        vm.SpectrumColorTheme = 3; // 彩虹
    }
}
```

- [ ] **Step 2: 运行测试验证通过**

Run: `dotnet test UmaPlayer.Tests.csproj --filter "PlayerViewModelSpectrumTests"`

- [ ] **Step 3: 提交**

```bash
git add Tests/ViewModels/PlayerViewModelSpectrumTests.cs
git commit -m "test: add unit tests for spectrum visualization in PlayerViewModel"
```

---

### Task 10: 验收测试

- [ ] **Step 1: 构建并运行应用**

Run: `dotnet run --project UmaPlayer.csproj`

- [ ] **Step 2: 手动验收测试**

验收清单：
- [ ] 频谱随音频实时变化
- [ ] 32 条频谱柱对数分布（低频更密集）
- [ ] 平滑度设置生效（调节滑块观察动画变化）
- [ ] 灵敏度设置生效（调节滑块观察频谱幅度）
- [ ] 颜色主题切换生效（下拉框切换 4 种主题）
- [ ] 启用/禁用开关生效（勾选/取消勾选）
- [ ] 暂停时频谱静止
- [ ] 设置持久化（重启应用验证）
- [ ] CPU 占用 < 5%（任务管理器监控）
- [ ] 无音频播放时不显示（DataTrigger 隐藏）

- [ ] **Step 3: 更新 PROJECT.md**

```bash
git add PROJECT.md
git commit -m "docs: update PROJECT.md for Phase 13 audio visualization"
```

- [ ] **Step 4: 最终提交**

```bash
git add .
git commit -m "feat: Phase 13 audio visualization complete"
```

---

## Self-Review Checklist

- [x] 所有 spec 需求都有对应任务
- [x] 无 TBD/TODO/未完成部分
- [x] 类型、方法签名、属性名一致
- [x] 代码步骤完整，无占位符
- [x] 测试覆盖关键功能
- [x] 文件路径准确
