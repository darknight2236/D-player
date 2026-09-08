# Phase 14 均衡器（Equalizer）实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 D-player 加入 10 段图形均衡器（±12dB 峰值滤波 + preamp + 内置预设 + 启用开关），实时作用于播放链并持久化。

**Architecture:** 完全镜像 Phase 13 频谱可视化模式——新增 `EqualizerSampleProvider`（`ISampleProvider` 透明中间件，每声道独立 `BiQuadFilter` 峰值滤波链）插入 `ToSampleProvider()` 与 `SampleAggregator` 之间；`IPlaybackService` 加 `EqualizerConfig` 属性（setter 实时下发在链系数）；`EqualizerDialog` 复用 `SettingsDialog` 交互模式（Show 工厂 + OnLoaded 读盘 + 拖动实时预览 + Save 写盘并同步 VM）；`PlayerViewModel` 仅加 `EqualizerEnabled` observable + 启动 load/apply。

**Tech Stack:** C# / .NET 10 / WPF · NAudio 2.2.1（`NAudio.Dsp.BiQuadFilter.PeakingEQ/SetPeakingEq/Transform`）· CommunityToolkit.Mvvm · xUnit + NSubstitute

**设计稿：** [`docs/superpowers/specs/2026-09-08-d-player-phase14-equalizer-design.md`](../specs/2026-09-08-d-player-phase14-equalizer-design.md)

---

## 文件结构（先锁定分解决策）

**新增：**

| 文件 | 责任 |
|------|------|
| `Models/EqualizerPresets.cs` | 频段常量（中心频率/Q/±12dB）+ 9 个内置预设表 + 名称查找/匹配。纯数据，可脱离 UI 单测 |
| `Models/EqualizerConfig.cs` | 不可变 record：`Enabled`/`PreampDb`/`BandGainsDb[10]`/`Preset` + `Create` Clamp 工厂 + `PreampLinearGain` |
| `Services/EqualizerSampleProvider.cs` | `ISampleProvider` 中间件：每声道独立 `BiQuadFilter[10]` + preamp；`Update(config)` 就地重算系数；Nyquist 旁路；buffer 粒度 lock |
| `Views/Dialogs/EqualizerDialog.xaml` / `.xaml.cs` | 独立 EQ 对话框：竖直滑块模板 + 预设下拉 + 启用开关 + 实时预览 + 保存/取消 |
| `Tests/Models/EqualizerPresetsTests.cs` | 预设表完整性 / 查找往返 / 匹配 |
| `Tests/Models/EqualizerConfigTests.cs` | 默认值 / Clamp |
| `Tests/Services/EqualizerSampleProviderTests.cs` | 透传 / unity / WaveFormat / 低音增强实际提升 |
| `Tests/ViewModels/PlayerViewModelEqualizerTests.cs` | 加载 / 传播 / 持久化（镜像 `PlayerViewModelSpectrumTests`） |

**修改：**

| 文件 | 改动 |
|------|------|
| `Services/IPlaybackService.cs` | 加 `EqualizerConfig EqualizerConfig { get; set; }` |
| `Services/NAudioPlaybackService.cs` | 加 `_equalizer`/`_equalizerConfig` 字段；LoadAsync 建链插入；`EqualizerConfig` setter；DisposePlayback 清理 |
| `Configuration/AppSettings.cs` | 加 `EqualizerEnabled`/`EqualizerPreamp`/`EqualizerBands`/`EqualizerPreset` 四字段 |
| `ViewModels/PlayerViewModel.cs` | 加 `EqualizerEnabled` observable + `LoadEqualizerSettings` + `OnEqualizerEnabledChanged` |
| `Views/Controls/PlayerBar.xaml` / `.xaml.cs` | 加 🎚 EQ 按钮（激活态高亮）+ `EqualizerBtn_Click` |
| `docs/PROJECT.md` · `docs/COUPLING.md` | Phase 14 条目 + 新增隐式契约 |

**任务顺序（每步可独立编译 + 测试 + 提交）：** Models → DSP → Service 集成 → Config → VM → Dialog → PlayerBar → Docs → 验收。

---

## Task 1: Models — EqualizerPresets + EqualizerConfig

**Files:**
- Create: `Models/EqualizerPresets.cs`
- Create: `Models/EqualizerConfig.cs`
- Test: `Tests/Models/EqualizerPresetsTests.cs`
- Test: `Tests/Models/EqualizerConfigTests.cs`

- [ ] **Step 1: 写失败测试（预设表 + 配置 Clamp）**

创建 `Tests/Models/EqualizerPresetsTests.cs`：

```csharp
using DPlayer.Models;
using Xunit;

namespace DPlayer.Tests.Models;

public class EqualizerPresetsTests
{
    [Fact]
    public void CenterFrequencies_Has10IsoOctaveBands()
    {
        Assert.Equal(EqualizerPresets.BandCount, EqualizerPresets.CenterFrequencies.Count);
        Assert.Equal(31, EqualizerPresets.CenterFrequencies[0]);
        Assert.Equal(16000, EqualizerPresets.CenterFrequencies[9]);
    }

    [Fact]
    public void EveryPreset_Has10Bands_Within12dB()
    {
        foreach (var (name, gains) in EqualizerPresets.All)
        {
            Assert.Equal(EqualizerPresets.BandCount, gains.Length);
            Assert.All(gains, g => Assert.InRange(g, EqualizerPresets.MinGainDb, EqualizerPresets.MaxGainDb));
        }
    }

    [Fact]
    public void Flat_IsAllZero()
    {
        Assert.True(EqualizerPresets.TryGet(EqualizerPresets.Flat, out var gains));
        Assert.All(gains, g => Assert.Equal(0, g));
    }

    [Fact]
    public void Names_DoesNotContainCustom()
    {
        Assert.DoesNotContain(EqualizerPresets.Custom, EqualizerPresets.Names);
        Assert.Contains(EqualizerPresets.Flat, EqualizerPresets.Names);
    }

    [Fact]
    public void Match_ReturnsPresetName_OrCustom()
    {
        Assert.True(EqualizerPresets.TryGet("Rock", out var rock));
        Assert.Equal("Rock", EqualizerPresets.Match(rock));
        Assert.Equal(EqualizerPresets.Custom, EqualizerPresets.Match(new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }));
    }
}
```

创建 `Tests/Models/EqualizerConfigTests.cs`：

```csharp
using DPlayer.Models;
using Xunit;

namespace DPlayer.Tests.Models;

public class EqualizerConfigTests
{
    [Fact]
    public void Default_IsDisabledFlatTransparent()
    {
        var c = new EqualizerConfig();
        Assert.False(c.Enabled);
        Assert.Equal(0, c.PreampDb);
        Assert.Equal(EqualizerPresets.Flat, c.Preset);
        Assert.Equal(EqualizerPresets.BandCount, c.BandGainsDb.Count);
        Assert.All(c.BandGainsDb, g => Assert.Equal(0, g));
    }

    [Fact]
    public void Create_ClampsOutOfRangeGains()
    {
        var c = EqualizerConfig.Create(true, 99, new double[] { 50, -50 }, "Custom");
        Assert.Equal(12, c.PreampDb);           // Clamp 到 +12
        Assert.Equal(12, c.BandGainsDb[0]);      // Clamp 到 +12
        Assert.Equal(-12, c.BandGainsDb[1]);     // Clamp 到 -12
        Assert.Equal(0, c.BandGainsDb[2]);       // 不足补 0
    }

    [Fact]
    public void PreampLinearGain_ConvertsDbToLinear()
    {
        Assert.Equal(1f, new EqualizerConfig { PreampDb = 0 }.PreampLinearGain, 3);
        Assert.True(new EqualizerConfig { PreampDb = 6 }.PreampLinearGain > 1.9f);
        Assert.True(new EqualizerConfig { PreampDb = -6 }.PreampLinearGain < 0.6f);
    }
}
```

- [ ] **Step 2: 运行测试确认失败（编译错误）**

Run: `dotnet test D-player.sln --filter "FullyQualifiedName~Equalizer" --nologo -v q`
Expected: **编译失败** —— `CS0246: 找不到类型或命名空间名"EqualizerPresets"/"EqualizerConfig"`（尚未创建）。

- [ ] **Step 3: 实现 EqualizerPresets**

创建 `Models/EqualizerPresets.cs`：

```csharp
namespace DPlayer.Models;

/// <summary>
/// 均衡器频段常量与内置预设（Phase 14）。纯数据，无副作用，可脱离 UI 单测。
/// 预设曲线来源：经典播放器听感（可在验收期微调）。
/// </summary>
public static class EqualizerPresets
{
    /// <summary>频段数（10 段图形均衡器）。</summary>
    public const int BandCount = 10;

    /// <summary>单段/preamp 增益下限（dB）。</summary>
    public const double MinGainDb = -12;
    /// <summary>单段/preamp 增益上限（dB）。</summary>
    public const double MaxGainDb = 12;

    /// <summary>峰值滤波器 Q 因子（约一个倍频程带宽，图形 EQ 常用值）。</summary>
    public const float Q = 1.1f;

    /// <summary>"平直"预设名。</summary>
    public const string Flat = "Flat";
    /// <summary>用户手动偏离任何预设后的状态名（不作为可选择的下拉项）。</summary>
    public const string Custom = "Custom";

    /// <summary>ISO 倍频程中心频率（Hz），固定常量、不持久化；顺序对应 BandGainsDb。</summary>
    public static readonly IReadOnlyList<double> CenterFrequencies =
        [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    /// <summary>内置预设（有序，供 UI 下拉展示；不含 Custom）。数组视为只读，勿原地修改。</summary>
    public static readonly IReadOnlyList<(string Name, double[] Gains)> All =
    [
        (Flat,          [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
        ("Rock",        [5, 4, 3, 1, -1, -1, 0, 2, 3, 4]),
        ("Pop",         [-1, 1, 3, 4, 3, 0, -1, -1, 1, 2]),
        ("Jazz",        [3, 2, 1, 2, -2, -2, 0, 1, 2, 3]),
        ("Classical",   [4, 3, 2, 0, -1, -1, 0, 1, 2, 3]),
        ("Dance",       [6, 4, 1, 0, -2, -2, 0, 1, 3, 4]),
        ("Bass Boost",  [6, 5, 4, 2, 0, 0, 0, 0, 0, 0]),
        ("Treble Boost",[0, 0, 0, 0, 0, 0, 1, 3, 5, 6]),
        ("Vocal",       [-2, -1, 0, 2, 4, 4, 3, 1, 0, -1]),
    ];

    /// <summary>可选预设名（下拉项，不含 Custom）。</summary>
    public static IReadOnlyList<string> Names { get; } = All.Select(p => p.Name).ToList();

    /// <summary>按名称查找预设曲线（返回副本）；找不到返回 false 且 gains 为全 0。</summary>
    public static bool TryGet(string name, out double[] gains)
    {
        foreach (var (n, g) in All)
        {
            if (n == name) { gains = (double[])g.Clone(); return true; }
        }
        gains = new double[BandCount];
        return false;
    }

    /// <summary>给定一组增益，返回精确匹配的预设名；无匹配返回 Custom。</summary>
    public static string Match(IReadOnlyList<double> gains)
    {
        foreach (var (n, g) in All)
        {
            if (gains.Count == g.Length && gains.SequenceEqual(g))
                return n;
        }
        return Custom;
    }
}
```

- [ ] **Step 4: 实现 EqualizerConfig**

创建 `Models/EqualizerConfig.cs`：

```csharp
namespace DPlayer.Models;

/// <summary>
/// 均衡器运行时配置（Phase 14）。不可变 record，通过 `with` 更新。
/// 默认 Enabled=false（透明旁路），保证不影响存量用户既有听感。
/// </summary>
public sealed record EqualizerConfig
{
    /// <summary>启用 EQ；false 时 EqualizerSampleProvider 原样透传（零成本旁路）。</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>前置放大 dB，Clamp 到 [-12, 12]；多段提升时留余量防削波。</summary>
    public double PreampDb { get; init; } = 0;

    /// <summary>10 段增益 dB（顺序对应 EqualizerPresets.CenterFrequencies），各 Clamp 到 [-12, 12]。</summary>
    public IReadOnlyList<double> BandGainsDb { get; init; } = new double[EqualizerPresets.BandCount];

    /// <summary>当前预设名；手动偏离即 EqualizerPresets.Custom。</summary>
    public string Preset { get; init; } = EqualizerPresets.Flat;

    /// <summary>preamp 的线性增益系数（10^(dB/20)）。</summary>
    public float PreampLinearGain => (float)Math.Pow(10, PreampDb / 20.0);

    /// <summary>
    /// 工厂：把 preamp 与各段增益 Clamp 到 [-12, 12]，并规整段数（不足补 0，超出截断）。
    /// 用于把持久化/外部输入安全转为运行时配置。
    /// </summary>
    public static EqualizerConfig Create(
        bool enabled, double preampDb, IReadOnlyList<double>? bandGainsDb, string? preset)
    {
        var bands = new double[EqualizerPresets.BandCount];
        if (bandGainsDb != null)
        {
            for (int i = 0; i < bands.Length && i < bandGainsDb.Count; i++)
                bands[i] = Math.Clamp(bandGainsDb[i], EqualizerPresets.MinGainDb, EqualizerPresets.MaxGainDb);
        }
        return new EqualizerConfig
        {
            Enabled = enabled,
            PreampDb = Math.Clamp(preampDb, EqualizerPresets.MinGainDb, EqualizerPresets.MaxGainDb),
            BandGainsDb = bands,
            Preset = string.IsNullOrWhiteSpace(preset) ? EqualizerPresets.Flat : preset!
        };
    }
}
```

- [ ] **Step 5: 运行测试确认通过**

Run: `dotnet test D-player.sln --filter "FullyQualifiedName~Equalizer" --nologo -v q`
Expected: **PASS** —— 8 个测试（EqualizerPresetsTests 5 + EqualizerConfigTests 3）全绿。

- [ ] **Step 6: 提交**

```bash
git add Models/EqualizerPresets.cs Models/EqualizerConfig.cs Tests/Models/EqualizerPresetsTests.cs Tests/Models/EqualizerConfigTests.cs
git commit -m "feat(models): add EqualizerConfig + EqualizerPresets (Phase 14)"
```

---

## Task 2: Services — EqualizerSampleProvider（DSP 中间件）

**Files:**
- Create: `Services/EqualizerSampleProvider.cs`
- Test: `Tests/Services/EqualizerSampleProviderTests.cs`

> **NAudio API（已核对 NAudio.Core 2.2.1 XML 文档）：**
> - `static BiQuadFilter BiQuadFilter.PeakingEQ(float sampleRate, float centreFrequency, float q, float dbGain)`
> - `void BiQuadFilter.SetPeakingEq(float sampleRate, float centreFrequency, float q, float dbGain)`（就地重算，保留滤波状态）
> - `float BiQuadFilter.Transform(float sample)`（逐样本处理）

- [ ] **Step 1: 写失败测试**

创建 `Tests/Services/EqualizerSampleProviderTests.cs`：

```csharp
using NAudio.Wave;
using DPlayer.Models;
using DPlayer.Services;
using Xunit;

namespace DPlayer.Tests.Services;

public class EqualizerSampleProviderTests
{
    /// <summary>产生固定样本的假 ISampleProvider（默认单声道 44.1kHz IeeeFloat）。</summary>
    private sealed class FakeSampleProvider : ISampleProvider
    {
        private readonly float[] _data;
        private int _pos;
        public FakeSampleProvider(float[] data, int channels = 1, int sampleRate = 44100)
        {
            _data = data;
            WaveFormat = new WaveFormat(sampleRate, 32, channels, AudioEncoding.IeeeFloat);
        }
        public WaveFormat WaveFormat { get; }
        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, _data.Length - _pos);
            if (n <= 0) return 0;
            Array.Copy(_data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
    }

    private static float[] Sine(float freq, int n, int sr = 44100, float amp = 0.3f)
    {
        var d = new float[n];
        for (int i = 0; i < n; i++) d[i] = amp * MathF.Sin(2 * MathF.PI * freq * i / sr);
        return d;
    }

    [Fact]
    public void Disabled_PassesThroughExactly()
    {
        var data = new float[] { 0.1f, -0.2f, 0.3f, -0.4f, 0.5f, -0.6f };
        var src = new FakeSampleProvider((float[])data.Clone());
        var eq = new EqualizerSampleProvider(src, new EqualizerConfig { Enabled = false });

        var buf = new float[data.Length];
        int read = eq.Read(buf, 0, buf.Length);

        Assert.Equal(data.Length, read);
        Assert.Equal(data, buf); // 未处理 → 逐位相等
    }

    [Fact]
    public void WaveFormat_PassesThroughSource()
    {
        var src = new FakeSampleProvider(new float[4], channels: 2, sampleRate: 48000);
        var eq = new EqualizerSampleProvider(src, new EqualizerConfig());
        Assert.Equal(2, eq.WaveFormat.Channels);
        Assert.Equal(48000, eq.WaveFormat.SampleRate);
    }

    [Fact]
    public void FlatEnabled_IsUnity_ForDcSignal()
    {
        var data = new float[8];
        Array.Fill(data, 0.5f);
        var src = new FakeSampleProvider((float[])data.Clone());
        var eq = new EqualizerSampleProvider(src, new EqualizerConfig { Enabled = true }); // Flat, preamp 0

        var buf = new float[data.Length];
        eq.Read(buf, 0, buf.Length);

        // 0 dB 峰值滤波 = unity；DC 精确透传（4 位小数容差防浮点）
        Assert.All(buf, x => Assert.Equal(0.5f, x, 4));
    }

    [Fact]
    public void BassBoost_IncreasesLowFrequencyAmplitude()
    {
        const int sr = 44100, n = 16384, skip = 4096; // 跳过建立期
        var boostGains = EqualizerPresets.All.First(p => p.Name == "Bass Boost").Gains;

        var flatBuf = new float[n];
        new EqualizerSampleProvider(new FakeSampleProvider(Sine(31, n, sr)),
            new EqualizerConfig { Enabled = true }).Read(flatBuf, 0, n);

        var boostBuf = new float[n];
        new EqualizerSampleProvider(new FakeSampleProvider(Sine(31, n, sr)),
            EqualizerConfig.Create(true, 0, boostGains, "Bass Boost")).Read(boostBuf, 0, n);

        double Rms(float[] b) { double s = 0; for (int i = skip; i < n; i++) s += b[i] * b[i]; return Math.Sqrt(s / (n - skip)); }
        Assert.True(Rms(boostBuf) > Rms(flatBuf) * 1.2, $"低音增强应显著提升 31Hz 幅度 (flat={Rms(flatBuf):F4}, boost={Rms(boostBuf):F4})");
    }

    [Fact]
    public void Update_DoesNotThrow_AndKeepsPlaying()
    {
        var src = new FakeSampleProvider(Sine(1000, 256));
        var eq = new EqualizerSampleProvider(src, new EqualizerConfig { Enabled = true });
        eq.Update(EqualizerConfig.Create(true, -3, new double[] { 6, 0, 0, 0, 0, 0, 0, 0, 0, 6 }, "Custom"));
        var buf = new float[256];
        Assert.Equal(256, eq.Read(buf, 0, 256));
    }
}
```

- [ ] **Step 2: 运行测试确认失败（编译错误）**

Run: `dotnet test D-player.sln --filter "FullyQualifiedName~EqualizerSampleProvider" --nologo -v q`
Expected: **编译失败** —— `CS0246: 找不到类型或命名空间名"EqualizerSampleProvider"`。

- [ ] **Step 3: 实现 EqualizerSampleProvider**

创建 `Services/EqualizerSampleProvider.cs`：

```csharp
using NAudio.Dsp;
using NAudio.Wave;
using DPlayer.Models;

namespace DPlayer.Services;

/// <summary>
/// 10 段图形均衡器中间件（Phase 14）。实现 ISampleProvider，透明插入播放链
/// （位于 ToSampleProvider 与 SampleAggregator 之间 → 频谱反映 EQ 后信号）。
///
/// 每声道独立一组 BiQuadFilter（峰值 EQ），避免立体声共享滤波状态导致串扰。
///
/// 线程模型：Read 在 NAudio 音频线程；Update 在 UI 线程。二者用 buffer 粒度 lock 互斥，
/// 防止撕裂系数。Update 用 SetPeakingEq 就地重算（保留 x1/x2/y1/y2 状态，避免拖动爆音）。
/// </summary>
public sealed class EqualizerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly BiQuadFilter?[][] _filters; // [channel][band]，null = 该频段旁路
    private readonly object _lock = new();

    private float _preampGain = 1f;
    private bool _enabled;

    public EqualizerSampleProvider(ISampleProvider source, EqualizerConfig config)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _sampleRate = source.WaveFormat.SampleRate;
        _channels = Math.Max(1, source.WaveFormat.Channels);
        _filters = new BiQuadFilter?[_channels][];
        for (int c = 0; c < _channels; c++)
            _filters[c] = new BiQuadFilter?[EqualizerPresets.BandCount];
        Update(config);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (!_enabled || read <= 0) return read; // 透明旁路（零处理成本）

        lock (_lock)
        {
            for (int i = 0; i < read; i++)
            {
                int c = _channels > 1 ? i % _channels : 0; // 交织样本 → 声道索引
                float s = buffer[offset + i] * _preampGain;
                var bank = _filters[c];
                for (int b = 0; b < bank.Length; b++)
                {
                    var f = bank[b];
                    if (f != null) s = f.Transform(s);
                }
                buffer[offset + i] = s;
            }
        }
        return read;
    }

    /// <summary>
    /// 运行时更新配置：就地重算系数（保留滤波状态，避免爆音）。
    /// 中心频率 ≥ 奈奎斯特的频段旁路（PeakingEQ 在 ≥Nyquist 时不稳定）。
    /// </summary>
    public void Update(EqualizerConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        lock (_lock)
        {
            _enabled = config.Enabled;
            _preampGain = config.PreampLinearGain;

            double nyquist = _sampleRate / 2.0;
            for (int b = 0; b < EqualizerPresets.BandCount; b++)
            {
                double centre = EqualizerPresets.CenterFrequencies[b];
                float gainDb = (float)config.BandGainsDb[b];
                bool bypass = centre >= nyquist; // Nyquist 保护

                for (int c = 0; c < _channels; c++)
                {
                    if (bypass) { _filters[c][b] = null; continue; }
                    var existing = _filters[c][b];
                    if (existing == null)
                        _filters[c][b] = BiQuadFilter.PeakingEQ(_sampleRate, (float)centre, EqualizerPresets.Q, gainDb);
                    else
                        existing.SetPeakingEq(_sampleRate, (float)centre, EqualizerPresets.Q, gainDb);
                }
            }
        }
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test D-player.sln --filter "FullyQualifiedName~EqualizerSampleProvider" --nologo -v q`
Expected: **PASS** —— 5 个测试全绿（透传/WaveFormat/unity/低音增强/Update）。

- [ ] **Step 5: 提交**

```bash
git add Services/EqualizerSampleProvider.cs Tests/Services/EqualizerSampleProviderTests.cs
git commit -m "feat(services): add EqualizerSampleProvider for 10-band graphic EQ (Phase 14)"
```

---

## Task 3: Service 集成 — IPlaybackService + NAudioPlaybackService

**Files:**
- Modify: `Services/IPlaybackService.cs`
- Modify: `Services/NAudioPlaybackService.cs`

> NAudioPlaybackService 依赖真实音频设备，项目现有惯例**不对其单测**（Tests/Services 仅覆盖 Scanner/Cache）。本任务由**编译**验证；运行时行为在 Task 9 手动听感验收。

- [ ] **Step 1: IPlaybackService 加 EqualizerConfig 属性**

在 `Services/IPlaybackService.cs` 的 `SpectrumConfig` 属性之后（第 66 行后）追加：

```csharp
    /// <summary>
    /// 均衡器配置（运行时可更新；setter 实时下发到在链 EQ provider）。
    /// </summary>
    EqualizerConfig EqualizerConfig { get; set; }
```

（文件顶部已有 `using DPlayer.Models;`，无需新增 using。）

- [ ] **Step 2: NAudioPlaybackService 加字段**

在 `Services/NAudioPlaybackService.cs` 的 Phase 13 频谱字段之后（第 32 行 `private SpectrumConfig _spectrumConfig = new();` 后）追加：

```csharp
    // Phase 14: 均衡器
    private EqualizerSampleProvider? _equalizer;
    private EqualizerConfig _equalizerConfig = new();
```

- [ ] **Step 3: NAudioPlaybackService 加 EqualizerConfig 属性**

在 `SpectrumConfig` 属性块之后（第 77 行 `}` 后）追加：

```csharp
    // Phase 14: 均衡器配置（setter 语义对齐 SpectrumConfig：存字段 + 在链 provider 实时下发）
    public EqualizerConfig EqualizerConfig
    {
        get => _equalizerConfig;
        set
        {
            _equalizerConfig = value;
            _equalizer?.Update(value);
        }
    }
```

- [ ] **Step 4: LoadAsync 建链插入 EQ provider**

把 `LoadAsync` 中的建链段（第 100-104 行）：

```csharp
                // Phase 13: 插入 SampleAggregator
                var sampleProvider = _reader.ToSampleProvider();
                _sampleAggregator = new SampleAggregator(sampleProvider, _spectrumConfig);
                _sampleAggregator.SpectrumDataReady += OnSpectrumDataReady;
```

改为（EQ 插在 SampleAggregator **之前** → 频谱反映 EQ 后信号）：

```csharp
                // Phase 13/14: ToSample → EqualizerSampleProvider → SampleAggregator
                var sampleProvider = _reader.ToSampleProvider();
                _equalizer = new EqualizerSampleProvider(sampleProvider, _equalizerConfig);
                _sampleAggregator = new SampleAggregator(_equalizer, _spectrumConfig);
                _sampleAggregator.SpectrumDataReady += OnSpectrumDataReady;
```

- [ ] **Step 5: DisposePlayback 清理 _equalizer**

在 `DisposePlayback` 的 SampleAggregator 清理块之后（第 285 行 `}` 后）追加：

```csharp
        // Phase 14: 清理 EqualizerSampleProvider（无事件订阅，仅置空引用，随播放链释放）
        _equalizer = null;
```

- [ ] **Step 6: 编译验证**

Run: `dotnet build D-player.sln -c Debug --nologo -v q`
Expected: **0 个错误**（现有 77 测试仍编译通过——NSubstitute 自动实现新接口成员）。

- [ ] **Step 7: 提交**

```bash
git add Services/IPlaybackService.cs Services/NAudioPlaybackService.cs
git commit -m "feat(playback): wire EqualizerSampleProvider into playback chain (Phase 14)"
```

---

## Task 4: Configuration — AppSettings EQ 字段

**Files:**
- Modify: `Configuration/AppSettings.cs`

- [ ] **Step 1: 加 4 个持久化字段**

在 `Configuration/AppSettings.cs` 的 Phase 13 频谱字段之后（第 42 行 `SpectrumSmoothing` 后、`}` 前）追加：

```csharp
    // ====== Phase 14: 均衡器 ======

    /// <summary>启用均衡器（默认关，透明旁路）。</summary>
    public bool EqualizerEnabled { get; init; } = false;

    /// <summary>EQ 前置放大 dB（-12 ~ +12）。</summary>
    public double EqualizerPreamp { get; init; } = 0;

    /// <summary>EQ 10 段增益 dB（各 -12 ~ +12）；序列化为 JSON 数组。</summary>
    public double[] EqualizerBands { get; init; } = new double[10];

    /// <summary>EQ 当前预设名（手动偏离即 "Custom"）。</summary>
    public string EqualizerPreset { get; init; } = "Flat";
```

- [ ] **Step 2: 编译验证**

Run: `dotnet build D-player.sln -c Debug --nologo -v q`
Expected: **0 个错误**。旧 settings.json 缺这些字段 → 反序列化为 record 默认值（Enabled=false/全 0/Flat），无需迁移。

- [ ] **Step 3: 提交**

```bash
git add Configuration/AppSettings.cs
git commit -m "feat(config): add equalizer settings to AppSettings (Phase 14)"
```

---

## Task 5: ViewModel — PlayerViewModel 加 EqualizerEnabled

**Files:**
- Modify: `ViewModels/PlayerViewModel.cs`
- Test: `Tests/ViewModels/PlayerViewModelEqualizerTests.cs`

> **record-mock 契约（同 COUPLING.md §5 SpectrumConfig）：** `EqualizerConfig` 是 record（引用类型），NSubstitute 默认返回 null；测试构造函数须 `_player.EqualizerConfig.Returns(new EqualizerConfig())`，否则 `OnEqualizerEnabledChanged` 里 `_player.EqualizerConfig with {...}` 会 NRE。
> 现有 `PlayerViewModelTests`/`PlayerViewModelSpectrumTests` 用默认 settings（EqualizerEnabled=false），构造期 `OnEqualizerEnabledChanged` **不触发**（值未变），故无需改动。

- [ ] **Step 1: 写失败测试**

创建 `Tests/ViewModels/PlayerViewModelEqualizerTests.cs`：

```csharp
using Microsoft.Extensions.Options;
using NSubstitute;
using DPlayer.Configuration;
using DPlayer.Models;
using DPlayer.Services;
using DPlayer.ViewModels;
using Xunit;

namespace DPlayer.Tests.ViewModels;

public class PlayerViewModelEqualizerTests
{
    private readonly IPlaybackService _player = Substitute.For<IPlaybackService>();
    private readonly ISettingsPersistence _persistence = Substitute.For<ISettingsPersistence>();
    private readonly IOptions<AppSettings> _options = Options.Create(new AppSettings());

    public PlayerViewModelEqualizerTests()
    {
        _persistence.LoadAsync().Returns(Task.FromResult(new AppSettings()));
        // EqualizerConfig 为 record，NSubstitute 默认返回 null → 预设实例避免 with 表达式 NRE
        _player.EqualizerConfig.Returns(new EqualizerConfig());
    }

    private PlayerViewModel CreateVm(AppSettings? settings = null)
    {
        if (settings != null)
            _persistence.LoadAsync().Returns(Task.FromResult(settings));
        return new PlayerViewModel(_player, _persistence, _options);
    }

    [Fact]
    public void Ctor_LoadsEqualizerEnabled_FromSettings()
    {
        var vm = CreateVm(new AppSettings { EqualizerEnabled = true });
        Assert.True(vm.EqualizerEnabled);
    }

    [Fact]
    public void Ctor_AppliesPersistedConfig_ToPlaybackService()
    {
        var settings = new AppSettings
        {
            EqualizerEnabled = true,
            EqualizerPreamp = -3,
            EqualizerBands = new double[] { 5, 4, 3, 1, -1, -1, 0, 2, 3, 4 },
            EqualizerPreset = "Rock"
        };
        CreateVm(settings);
        _player.Received().EqualizerConfig = Arg.Is<EqualizerConfig>(c =>
            c.Enabled && c.PreampDb == -3 && c.Preset == "Rock" && c.BandGainsDb[0] == 5);
    }

    [Fact]
    public void EqualizerEnabled_Changed_PropagatesToService_AndPersists()
    {
        var vm = CreateVm();
        vm.EqualizerEnabled = true;
        _player.Received().EqualizerConfig = Arg.Is<EqualizerConfig>(c => c.Enabled);
        _persistence.Received().UpdateAsync(Arg.Is<Func<AppSettings, AppSettings>>(fn =>
            fn(new AppSettings()).EqualizerEnabled));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test D-player.sln --filter "FullyQualifiedName~PlayerViewModelEqualizer" --nologo -v q`
Expected: **编译失败** —— `PlayerViewModel` 无 `EqualizerEnabled` 成员（CS1061）。

- [ ] **Step 3: PlayerViewModel 加 observable 字段**

在 `ViewModels/PlayerViewModel.cs` 的 Phase 13 频谱属性块之后（第 77 行 `_spectrumSmoothing` 后）追加：

```csharp
    // ====== Phase 14: 均衡器 ======

    /// <summary>EQ 启用态；供 PlayerBar EQ 按钮激活态高亮（镜像 Shuffle/Repeat）。</summary>
    [ObservableProperty]
    private bool _equalizerEnabled;
```

- [ ] **Step 4: Initialize 里加载 EQ 设置**

在 `Initialize()` 中 `LoadSpectrumSettings(settings);`（第 134 行）之后、`_isInitializing = false;` 之前追加：

```csharp
        // Phase 14: 加载均衡器设置并应用到播放链
        LoadEqualizerSettings(settings);
```

- [ ] **Step 5: 加 Load/OnChanged 方法**

在 `LoadSpectrumSettings` 方法（第 295-301 行）之后追加：

```csharp
    /// <summary>启动加载 EQ 设置：设启用态 observable + 把完整配置应用到播放服务。</summary>
    private void LoadEqualizerSettings(AppSettings settings)
    {
        // 先设 observable（构造期 _isInitializing=true，OnEqualizerEnabledChanged 不写盘）
        EqualizerEnabled = settings.EqualizerEnabled;
        // 再把完整配置（含 preamp/10 段/预设）下发到 service，首次播放即生效
        _player.EqualizerConfig = EqualizerConfig.Create(
            settings.EqualizerEnabled, settings.EqualizerPreamp,
            settings.EqualizerBands, settings.EqualizerPreset);
    }

    /// <summary>EQ 启用态变更：传播到播放链 + 持久化（构造期跳过写盘）。</summary>
    partial void OnEqualizerEnabledChanged(bool value)
    {
        _player.EqualizerConfig = _player.EqualizerConfig with { Enabled = value };
        if (_isInitializing) return;
        _ = _persistence.UpdateAsync(s => s with { EqualizerEnabled = value });
    }
```

- [ ] **Step 6: 运行测试确认通过**

Run: `dotnet test D-player.sln --filter "FullyQualifiedName~PlayerViewModelEqualizer" --nologo -v q`
Expected: **PASS** —— 3 个测试全绿。

- [ ] **Step 7: 跑全量测试确认无回归**

Run: `dotnet test D-player.sln -c Debug --nologo -v q`
Expected: **通过 93，失败 0**（77 原有 + 8 Models + 5 Provider + 3 VM = 16 新增）。

- [ ] **Step 8: 提交**

```bash
git add ViewModels/PlayerViewModel.cs Tests/ViewModels/PlayerViewModelEqualizerTests.cs
git commit -m "feat(vm): add EqualizerEnabled to PlayerViewModel with startup apply (Phase 14)"
```

---

## Task 6: Views — EqualizerDialog（对话框）

**Files:**
- Create: `Views/Dialogs/EqualizerDialog.xaml`
- Create: `Views/Dialogs/EqualizerDialog.xaml.cs`

> 对话框为 View 层，项目惯例**不单测**（同 SettingsDialog）；由**编译 + 手动验收**（Task 9）验证。11 根竖直滑块由 code-behind 动态构建（DRY，避免 11 块重复 XAML）。

- [ ] **Step 1: 写 EqualizerDialog.xaml**

创建 `Views/Dialogs/EqualizerDialog.xaml`：

```xml
<!-- Views/Dialogs/EqualizerDialog.xaml （Phase 14 均衡器） -->
<Window x:Class="DPlayer.Views.Dialogs.EqualizerDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="均衡器"
        Width="480" Height="400"
        WindowStartupLocation="CenterOwner"
        WindowStyle="ToolWindow"
        ResizeMode="NoResize"
        ShowInTaskbar="False"
        Background="{StaticResource BackgroundPrimary}"
        Foreground="{StaticResource ForegroundPrimary}">
    <Window.Resources>
        <!--
          竖直 EQ 滑块样式：Controls.xaml 的隐式 Slider 模板是横向专用
          （Height=20 + 填充条 Height=4 VerticalAlignment=Center），竖直滑块必须用本模板
          （Width=24 + 填充条 Width=4 HorizontalAlignment=Center），否则渲染错位。
        -->
        <Style x:Key="EqBandSlider" TargetType="Slider">
            <Setter Property="Orientation" Value="Vertical"/>
            <Setter Property="Width" Value="24"/>
            <Setter Property="Minimum" Value="-12"/>
            <Setter Property="Maximum" Value="12"/>
            <Setter Property="TickFrequency" Value="1"/>
            <Setter Property="IsSnapToTickEnabled" Value="True"/>
            <Setter Property="IsMoveToPointEnabled" Value="True"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Slider">
                        <Grid>
                            <Track x:Name="PART_Track">
                                <Track.DecreaseRepeatButton>
                                    <RepeatButton Command="{x:Static Slider.DecreaseLarge}">
                                        <RepeatButton.Template>
                                            <ControlTemplate TargetType="RepeatButton">
                                                <Border Background="Transparent">
                                                    <Border Background="{StaticResource AccentPrimary}" Width="4"
                                                            HorizontalAlignment="Center" CornerRadius="2"/>
                                                </Border>
                                            </ControlTemplate>
                                        </RepeatButton.Template>
                                    </RepeatButton>
                                </Track.DecreaseRepeatButton>
                                <Track.IncreaseRepeatButton>
                                    <RepeatButton Command="{x:Static Slider.IncreaseLarge}">
                                        <RepeatButton.Template>
                                            <ControlTemplate TargetType="RepeatButton">
                                                <Border Background="Transparent">
                                                    <Border Background="{StaticResource SliderTrack}" Width="4"
                                                            HorizontalAlignment="Center" CornerRadius="2"/>
                                                </Border>
                                            </ControlTemplate>
                                        </RepeatButton.Template>
                                    </RepeatButton>
                                </Track.IncreaseRepeatButton>
                                <Track.Thumb>
                                    <Thumb Cursor="Hand" FocusVisualStyle="{x:Null}">
                                        <Thumb.Template>
                                            <ControlTemplate TargetType="Thumb">
                                                <Ellipse Width="14" Height="14" Fill="{StaticResource SliderThumb}"/>
                                            </ControlTemplate>
                                        </Thumb.Template>
                                    </Thumb>
                                </Track.Thumb>
                            </Track>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>

    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>  <!-- 启用 + 预设 -->
            <RowDefinition Height="*"/>     <!-- 频段滑块区 -->
            <RowDefinition Height="Auto"/>  <!-- 按钮 -->
        </Grid.RowDefinitions>

        <!-- Row 0：启用开关 + 预设下拉 -->
        <DockPanel Grid.Row="0" Margin="0,0,0,12" LastChildFill="False">
            <CheckBox x:Name="EnableCheckBox"
                      Content="启用均衡器"
                      Foreground="{StaticResource ForegroundPrimary}"
                      VerticalAlignment="Center"
                      DockPanel.Dock="Left"
                      Checked="Enable_Changed" Unchecked="Enable_Changed"/>
            <StackPanel Orientation="Horizontal" DockPanel.Dock="Right" VerticalAlignment="Center">
                <TextBlock Text="预设:" Style="{StaticResource BodyText}" VerticalAlignment="Center" Margin="0,0,8,0"/>
                <ComboBox x:Name="PresetCombo" Width="140" SelectionChanged="PresetCombo_SelectionChanged"/>
            </StackPanel>
        </DockPanel>

        <!-- Row 1：11 根竖直滑块（10 段 + preamp），code-behind 动态填充 -->
        <Border Grid.Row="1" Background="{StaticResource BackgroundSecondary}" CornerRadius="8" Padding="8">
            <UniformGrid x:Name="BandsPanel" Rows="1"/>
        </Border>

        <!-- Row 2：恢复 Flat + 取消/保存 -->
        <Grid Grid.Row="2" Margin="0,12,0,0">
            <Button Content="恢复 Flat" Width="90" Height="28" HorizontalAlignment="Left" Click="RestoreFlat_Click"/>
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                <Button Content="取消" Width="80" Height="28" Margin="0,0,8,0" IsCancel="True"/>
                <Button Content="保存" Width="80" Height="28" IsDefault="True" Click="Save_Click"/>
            </StackPanel>
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 2: 写 EqualizerDialog.xaml.cs**

创建 `Views/Dialogs/EqualizerDialog.xaml.cs`：

```csharp
using System.Windows;
using System.Windows.Controls;
using DPlayer.Configuration;
using DPlayer.Models;
using DPlayer.Services;
using DPlayer.ViewModels;

namespace DPlayer.Views.Dialogs;

/// <summary>
/// Phase 14 均衡器对话框。复用 SettingsDialog 模式：静态 Show 工厂 + modal ShowDialog()。
/// 拖动滑块实时下发 IPlaybackService.EqualizerConfig（即时听感）；保存写 settings.json 并同步 PlayerViewModel。
/// 11 根竖直滑块（10 段 + preamp）由 code-behind 动态构建到 BandsPanel（UniformGrid）。
/// </summary>
public partial class EqualizerDialog : Window
{
    private readonly ISettingsPersistence _persistence;
    private readonly IPlaybackService _playbackService;
    private readonly PlayerViewModel? _playerViewModel;

    private EqualizerConfig _initialConfig = new();  // 进入时快照，取消时回滚
    private bool _saved;                              // Save 成功标志
    private bool _suppress;                           // 程序化设置滑块/下拉时抑制 ValueChanged 回推

    private readonly Slider[] _bandSliders = new Slider[EqualizerPresets.BandCount];
    private readonly TextBlock[] _bandLabels = new TextBlock[EqualizerPresets.BandCount];
    private Slider _preampSlider = null!;
    private TextBlock _preampLabel = null!;

    public EqualizerDialog(ISettingsPersistence persistence, IPlaybackService playbackService, PlayerViewModel? playerViewModel = null)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _playbackService = playbackService ?? throw new ArgumentNullException(nameof(playbackService));
        _playerViewModel = playerViewModel;
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>模态显示均衡器对话框。返回 true = 用户保存。</summary>
    public static bool Show(Window? owner, ISettingsPersistence persistence, IPlaybackService playbackService, PlayerViewModel? playerViewModel = null)
    {
        var dlg = new EqualizerDialog(persistence, playbackService, playerViewModel) { Owner = owner };
        return dlg.ShowDialog() == true;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await _persistence.LoadAsync().ConfigureAwait(true);
            _initialConfig = EqualizerConfig.Create(
                settings.EqualizerEnabled, settings.EqualizerPreamp,
                settings.EqualizerBands, settings.EqualizerPreset);
        }
        catch
        {
            _initialConfig = new EqualizerConfig();
        }

        BuildBands();

        PresetCombo.Items.Clear();
        foreach (var n in EqualizerPresets.Names) PresetCombo.Items.Add(n);
        PresetCombo.Items.Add(EqualizerPresets.Custom);

        // 链上已是本配置（PlayerViewModel 启动时已应用），仅同步 UI，无需再下发
        ApplyConfigToUi(_initialConfig);
    }

    /// <summary>构建 11 列（10 段 + preamp），每列：dB 值标签 / 竖直滑块 / 频率标签。</summary>
    private void BuildBands()
    {
        BandsPanel.Children.Clear();
        var sliderStyle = (Style)FindResource("EqBandSlider");

        for (int b = 0; b < EqualizerPresets.BandCount; b++)
        {
            var (slider, valueLabel, _) = MakeSliderColumn(sliderStyle, FreqText(EqualizerPresets.CenterFrequencies[b]));
            _bandSliders[b] = slider;
            _bandLabels[b] = valueLabel;
            slider.ValueChanged += BandSlider_ValueChanged;
        }

        var (preSlider, preValue, _) = MakeSliderColumn(sliderStyle, "Pre");
        _preampSlider = preSlider;
        _preampLabel = preValue;
        preSlider.ValueChanged += BandSlider_ValueChanged;
    }

    private (Slider slider, TextBlock value, TextBlock freq) MakeSliderColumn(Style sliderStyle, string freqText)
    {
        var grid = new Grid { Margin = new Thickness(2, 0, 2, 0) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var value = new TextBlock
        {
            Text = "0",
            Style = (Style)FindResource("CaptionText"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(value, 0);

        var slider = new Slider
        {
            Style = sliderStyle,
            Value = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetRow(slider, 1);

        var freq = new TextBlock
        {
            Text = freqText,
            Style = (Style)FindResource("CaptionText"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };
        Grid.SetRow(freq, 2);

        grid.Children.Add(value);
        grid.Children.Add(slider);
        grid.Children.Add(freq);
        BandsPanel.Children.Add(grid);
        return (slider, value, freq);
    }

    private static string FreqText(double hz) => hz >= 1000 ? $"{hz / 1000:0.#}k" : $"{hz:0}";

    private double[] CurrentGains()
    {
        var g = new double[EqualizerPresets.BandCount];
        for (int b = 0; b < g.Length; b++) g[b] = _bandSliders[b].Value;
        return g;
    }

    private void RefreshLabels()
    {
        for (int b = 0; b < EqualizerPresets.BandCount; b++)
            _bandLabels[b].Text = $"{_bandSliders[b].Value:0}";
        _preampLabel.Text = $"{_preampSlider.Value:0}";
    }

    private void PushToService(string preset)
    {
        _playbackService.EqualizerConfig = EqualizerConfig.Create(
            EnableCheckBox.IsChecked == true, _preampSlider.Value, CurrentGains(), preset);
    }

    private void ApplyConfigToUi(EqualizerConfig config)
    {
        _suppress = true;
        EnableCheckBox.IsChecked = config.Enabled;
        for (int b = 0; b < EqualizerPresets.BandCount; b++)
            _bandSliders[b].Value = config.BandGainsDb[b];
        _preampSlider.Value = config.PreampDb;
        PresetCombo.SelectedItem = config.Preset;
        _suppress = false;
        RefreshLabels();
    }

    private void BandSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress) return;
        RefreshLabels();
        var preset = EqualizerPresets.Match(CurrentGains());
        _suppress = true;
        PresetCombo.SelectedItem = preset;
        _suppress = false;
        PushToService(preset);
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (PresetCombo.SelectedItem is not string name || name == EqualizerPresets.Custom) return;
        if (!EqualizerPresets.TryGet(name, out var gains)) return;
        _suppress = true;
        for (int b = 0; b < EqualizerPresets.BandCount; b++) _bandSliders[b].Value = gains[b];
        _suppress = false;
        RefreshLabels();
        PushToService(name);
    }

    private void Enable_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        PushToService(PresetCombo.SelectedItem as string ?? EqualizerPresets.Flat);
    }

    private void RestoreFlat_Click(object sender, RoutedEventArgs e)
    {
        if (!EqualizerPresets.TryGet(EqualizerPresets.Flat, out var gains)) return;
        _suppress = true;
        for (int b = 0; b < EqualizerPresets.BandCount; b++) _bandSliders[b].Value = gains[b];
        _preampSlider.Value = 0;
        PresetCombo.SelectedItem = EqualizerPresets.Flat;
        _suppress = false;
        RefreshLabels();
        PushToService(EqualizerPresets.Flat);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var preset = PresetCombo.SelectedItem as string ?? EqualizerPresets.Flat;
            var enabled = EnableCheckBox.IsChecked == true;
            var preamp = _preampSlider.Value;
            var gains = CurrentGains();

            await _persistence.UpdateAsync(s => s with
            {
                EqualizerEnabled = enabled,
                EqualizerPreamp = preamp,
                EqualizerBands = gains,
                EqualizerPreset = preset
            });

            // 确保链上是最终配置（拖动已实时下发，这里再确认一次）
            _playbackService.EqualizerConfig = EqualizerConfig.Create(enabled, preamp, gains, preset);

            // 同步 PlayerViewModel（按钮高亮即时更新）—— 故意不 ConfigureAwait(false)，留在 UI 线程
            if (_playerViewModel != null)
                _playerViewModel.EqualizerEnabled = enabled;

            _saved = true;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存 EQ 设置失败：{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (!_saved)
            _playbackService.EqualizerConfig = _initialConfig; // 取消/关闭 → 撤销实时预览
    }
}
```

- [ ] **Step 3: 编译验证**

Run: `dotnet build D-player.sln -c Debug --nologo -v q`
Expected: **0 个错误**。

- [ ] **Step 4: 提交**

```bash
git add Views/Dialogs/EqualizerDialog.xaml Views/Dialogs/EqualizerDialog.xaml.cs
git commit -m "feat(view): add EqualizerDialog with 10-band vertical sliders + presets (Phase 14)"
```

---

## Task 7: Views — PlayerBar EQ 按钮

**Files:**
- Modify: `Views/Controls/PlayerBar.xaml`
- Modify: `Views/Controls/PlayerBar.xaml.cs`

- [ ] **Step 1: XAML 加 EQ 按钮**

在 `Views/Controls/PlayerBar.xaml` 右侧 StackPanel 的 ⚙ 设置按钮**之前**（第 160 行 `<!-- ⚙ 设置按钮 -->` 前）插入：

```xml
                <!-- 🎚 均衡器按钮：Phase 14；启用态高亮（BoolToAccentBrush，同 Shuffle/Repeat） -->
                <Button Width="32" Height="32"
                        Padding="0" Margin="8,0,0,0"
                        Background="Transparent" BorderThickness="0"
                        Click="EqualizerBtn_Click"
                        ToolTip="均衡器">
                    <TextBlock Text="&#x1F39A;" FontSize="14"
                               FontFamily="Segoe UI Emoji"
                               Foreground="{Binding EqualizerEnabled, Converter={StaticResource BoolToAccentBrush}}"/>
                </Button>
```

（`BoolToAccentBrush` 已在 PlayerBar.Resources 第 18 行声明；`EqualizerEnabled` 在 DataContext=PlayerViewModel 上，直接绑定无需 RelativeSource。）

- [ ] **Step 2: code-behind 加点击 handler**

在 `Views/Controls/PlayerBar.xaml.cs` 的 `SettingsBtn_Click` 方法之后（第 74 行 `}` 后）追加：

```csharp
    /// <summary>点击 🎚 按钮打开均衡器对话框（Phase 14）。</summary>
    private void EqualizerBtn_Click(object sender, RoutedEventArgs e)
    {
        var persistence = App.GetService<ISettingsPersistence>();
        var playbackService = App.GetService<IPlaybackService>();
        var playerViewModel = DataContext as PlayerViewModel;
        EqualizerDialog.Show(Window.GetWindow(this), persistence, playbackService, playerViewModel);
    }
```

（文件已有 `using DPlayer.Services;` / `using DPlayer.ViewModels;` / `using DPlayer.Views.Dialogs;`，无需新增 using。）

- [ ] **Step 3: 编译验证**

Run: `dotnet build D-player.sln -c Debug --nologo -v q`
Expected: **0 个错误**。

- [ ] **Step 4: 提交**

```bash
git add Views/Controls/PlayerBar.xaml Views/Controls/PlayerBar.xaml.cs
git commit -m "feat(view): add EQ button to PlayerBar with active-state highlight (Phase 14)"
```

---

## Task 8: Docs — 更新 PROJECT.md + COUPLING.md

**Files:**
- Modify: `docs/PROJECT.md`
- Modify: `docs/COUPLING.md`

- [ ] **Step 1: PROJECT.md 特性表加一行**

在 §1.1 关键特性表末尾（音频可视化行之后）追加：

```markdown
| 均衡器 | 10 段图形 EQ（31Hz–16kHz ±12dB 峰值滤波 + preamp）；9 个内置预设（Flat/Rock/Pop/Jazz/Classical/Dance/Bass Boost/Treble Boost/Vocal）+ 手动 Custom；实时生效；启用开关；独立对话框；设置持久化 (Phase 14) |
```

- [ ] **Step 2: PROJECT.md 目录结构补新文件**

在 §3 目录树对应位置补：`Models/EqualizerConfig.cs`、`Models/EqualizerPresets.cs`（Models 段）；`Services/EqualizerSampleProvider.cs`（Services 段）；`Views/Dialogs/EqualizerDialog.xaml(.cs)`（Dialogs 段）；`Tests/Models/` 两个测试文件 + `Tests/Services/EqualizerSampleProviderTests.cs` + `Tests/ViewModels/PlayerViewModelEqualizerTests.cs`（Tests 段，测试总数 77 → 93）。

- [ ] **Step 3: PROJECT.md 加模块详解小节**

在 §5.2a（SampleAggregator）之后新增 `### 5.2b Services/EqualizerSampleProvider + Models/EqualizerConfig（Phase 14）`，说明：插入点（ToSample 与 SampleAggregator 之间）、每声道独立 `BiQuadFilter[10]` 峰值滤波、preamp、`Update` 就地 `SetPeakingEq` 重算（保留状态防爆音）、Nyquist 旁路、buffer 粒度 lock 线程模型、`Enabled=false` 透明旁路。并在 §5.5 Views 补 `EqualizerDialog` 说明（竖直滑块模板为何必须自定义、实时预览、取消回滚）。

- [ ] **Step 4: PROJECT.md 持久化 + 数据流 + 约束**

- §6.2 被持久化字段追加：`EqualizerEnabled/EqualizerPreamp/EqualizerBands/EqualizerPreset（Phase 14）`。
- §7 加一条数据流 `7.6 均衡器数据流（Phase 14）`：启动 load→apply、拖动实时 `EqualizerConfig` setter→`provider.Update`、Save 写盘+同步 VM、取消回滚。
- §9 已知约束加：EQ 系数实时更新的跨线程一致性（buffer 粒度 lock + 就地重算保留状态）。
- §10 历史加 Phase 14 里程碑提交列表。

- [ ] **Step 5: COUPLING.md 登记新增隐式契约**

在 §5 隐式契约表新增「Phase 14 新增」小节，逐条登记（来自设计稿 §15）：

```markdown
| **Phase 14 新增** | | |
| `EqualizerSampleProvider.Read`（音频线程）与 `Update`（UI 线程）共用 buffer 粒度 lock | `Services/EqualizerSampleProvider.cs` | 无锁会撕裂系数；`Update` 必须用 `SetPeakingEq` 就地重算（保留 x1/x2/y1/y2 状态），重建 BiQuadFilter 会清空延迟线导致爆音 |
| 立体声必须每声道独立 `BiQuadFilter[10]` | `EqualizerSampleProvider._filters[channel][band]` | 左右共享滤波器实例会串扰滤波状态 |
| EQ 插入点必须在 `SampleAggregator` 之前 | `NAudioPlaybackService.LoadAsync` | 移到其后会让频谱可视化与实际听感（EQ 后）脱钩 |
| `IPlaybackService.EqualizerConfig` setter 语义对齐 `SpectrumConfig` | `NAudioPlaybackService.EqualizerConfig` | 存字段 + 在链 provider `Update`；链未建时仅存字段待 LoadAsync |
| `EqualizerDialog.Save_Click` 不得 `ConfigureAwait(false)` | `Views/Dialogs/EqualizerDialog.xaml.cs:Save_Click` | 保存后需在 UI 线程写 `PlayerViewModel.EqualizerEnabled`（同 SettingsDialog Phase 13 契约） |
| `EqualizerConfig` 是 record，mock 需预设实例 | `PlayerViewModelEqualizerTests` 构造函数 | NSubstitute 对 record 属性默认返回 null；`OnEqualizerEnabledChanged` 读 `_player.EqualizerConfig with {...}` 会 NRE |
| PlayerBar EQ 按钮竖直滑块模板必须自定义 | `EqualizerDialog.xaml` `EqBandSlider` | Controls.xaml 隐式 Slider 模板横向专用（Height=20 + 填充条 VerticalAlignment=Center）；竖直滑块直接用会渲染错位 |
```

并在 §7「不要做的事」加：❌ 让 `EqualizerSampleProvider.Update` 重建 `BiQuadFilter`（而非 `SetPeakingEq` 就地改）—— 拖动时爆音。TL;DR 与 §1 结论同步为「Phase 14 完成」。

- [ ] **Step 6: 提交**

```bash
git add docs/PROJECT.md docs/COUPLING.md
git commit -m "docs: update PROJECT.md and COUPLING.md for Phase 14 equalizer"
```

---

## Task 9: 验收

**Files:** 无（仅验证）

- [ ] **Step 1: 全量构建**

Run: `dotnet build D-player.sln -c Debug --nologo -v q`
Expected: **0 个错误**（1 个既有测试 null 警告可忽略）。

- [ ] **Step 2: 全量测试**

Run: `dotnet test D-player.sln -c Debug --nologo -v q`
Expected: **通过 93，失败 0，跳过 0**。

- [ ] **Step 3: 运行应用手动验收**

Run: `dotnet run --project D-player.csproj`
手动核对清单：
1. 播放一首歌 → 点 PlayerBar 🎚 → 弹出均衡器对话框，11 根竖直滑块正常渲染（非错位）。
2. 勾「启用均衡器」+ 选「Bass Boost」→ **拖动时立即听到低音增强**（无需重载曲目）。
3. 手动拖某段滑块偏离预设 → 预设下拉自动显示「Custom」。
4. 频谱可视化随 EQ 变化（推低音时低频柱抬升）→ 验证插入点在 SampleAggregator 之前。
5. 点「保存」→ 🎚 按钮高亮（AccentHover）；关闭应用重启 → EQ 设置恢复、按钮仍高亮、播放即生效。
6. 再次打开对话框改动后点「取消」→ 听感回滚到上次保存的配置。
7. 取消勾选「启用」并保存 → 🎚 按钮恢复常态，音频透明（与未加 EQ 一致）。
8. 「恢复 Flat」按钮 → 所有滑块归 0、preamp 归 0、预设显示 Flat。

- [ ] **Step 4: 收尾提交（如手动验收期有微调）**

```bash
git add -A
git commit -m "test: Phase 14 equalizer manual acceptance pass"
```

---

## 附：本计划自检结果

**1. 规格覆盖**（对照设计稿各节）：§5 数据模型→Task 1；§6 DSP→Task 2；§4/§6.2-6.3 集成→Task 3；§5.3 持久化字段→Task 4；§7 VM→Task 5；§8.2 对话框→Task 6；§8.1 PlayerBar 按钮→Task 7；§15 隐式契约→Task 8；§9 边界（Nyquist/Clamp/透明旁路/取消回滚/保存失败弹窗）分布于 Task 1/2/5/6；§13 测试→Task 1/2/5。**无遗漏**。

**2. 占位符扫描**：无 TBD/TODO/“稍后实现”；每个改代码的步骤均含完整代码；每个验证步骤含确切命令与预期输出。

**3. 类型一致性**：`EqualizerConfig`（`Enabled`/`PreampDb`/`BandGainsDb`/`Preset`/`PreampLinearGain`/`Create`）、`EqualizerPresets`（`BandCount`/`MinGainDb`/`MaxGainDb`/`Q`/`Flat`/`Custom`/`CenterFrequencies`/`All`/`Names`/`TryGet`/`Match`）、`EqualizerSampleProvider(source, config)`/`Update(config)`、`IPlaybackService.EqualizerConfig`、`PlayerViewModel.EqualizerEnabled`、`EqualizerDialog.Show(owner, persistence, playbackService, playerViewModel?)` —— 跨任务引用名称均一致。NAudio API（`PeakingEQ`/`SetPeakingEq`/`Transform`）已核对 2.2.1 XML 文档。

**预期新增测试 16 个**（Models 8 + Provider 5 + VM 3），总测试 77 → 93。
