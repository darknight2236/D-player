# Phase 14：均衡器（Equalizer）设计

> 设计日期：2026/09/08 · 对应分支：`master`（起点 HEAD `4a83865`） · 状态：**待实现**
>
> 目标：为 D-player 加入经典 **10 段图形均衡器**（foobar2000 / Winamp 形态），复用 Phase 13 已验证的 `ISampleProvider` 中间件模式，几乎不触碰核心架构。

---

## 1. 概述

在播放链中插入一个可实时调节的 10 段图形均衡器（Graphic EQ），提供：

- **10 段峰值滤波**：ISO 倍频程中心频率 31 / 62 / 125 / 250 / 500 / 1k / 2k / 4k / 8k / 16k Hz，每段 ±12 dB。
- **前置放大（Preamp）**：−12 ~ +12 dB，用于多段提升时留出余量、避免削波。
- **内置预设**：Flat / Rock / Pop / Jazz / Classical / Dance / Bass Boost / Treble Boost / Vocal；手动移动任一滑块即切为 `Custom`。
- **启用开关**：默认关闭；关闭时完全透明旁路，不改变现有听感。
- **实时生效**：拖动滑块 / 切换预设时立即作用于正在播放的音频（无需重载曲目）。
- **持久化**：EQ 状态（启用 / preamp / 10 段增益 / 预设名）保存到 `%LocalAppData%\D-player\settings.json`，启动时自动应用。
- **独立对话框 UI**：从 PlayerBar 的 🎚 按钮打开，按钮在 EQ 启用时高亮。

## 2. 范围

### Goals（本期实现）
- 10 段图形 EQ + preamp + 启用开关，实时作用于播放链。
- 内置预设（不可由用户新增/保存），下拉切换。
- 独立 `EqualizerDialog`，复用 `SettingsDialog` 交互模式。
- EQ 状态持久化到 settings.json，启动应用。
- PlayerBar EQ 按钮 + 启用态高亮。
- 纯逻辑（预设表 / Clamp / DSP 系数 / VM 传播）单元测试覆盖。

### Non-Goals（本期不做，留作后续增量）
- 用户命名保存 / 删除自定义预设。
- 参数均衡器（每段频率 / Q 可调）。
- 每首歌 / 每歌单独立 EQ。
- 预设导入导出。
- 其它 DSP 效果（混响 / 压缩 / 立体声宽度等）。

## 3. 现状与契合点

本设计刻意**完全沿用 Phase 13 频谱可视化**已落地的模式，最小化新概念：

| 既有模式（Phase 13） | Phase 14 EQ 对应 |
|----------------------|------------------|
| `SampleAggregator : ISampleProvider` 透明中间件插入播放链 | `EqualizerSampleProvider : ISampleProvider` 同样插入播放链 |
| `IPlaybackService.SpectrumConfig { get; set; }`，setter 实时下发到在链中间件 | `IPlaybackService.EqualizerConfig { get; set; }`，同构 |
| `SpectrumConfig` record（Models/） | `EqualizerConfig` record（Models/） |
| `AppSettings` 扁平字段持久化频谱设置 | `AppSettings` 扁平字段持久化 EQ 设置 |
| `SettingsDialog`：静态 Show 工厂 + OnLoaded 读盘 + ValueChanged 实时预览 + Save 写盘并同步 VM | `EqualizerDialog`：同模式 |
| `PlayerViewModel` 持有频谱设置 observable + 启动 load + `OnXxxChanged` 持久化/传播 | `PlayerViewModel` 仅加 `EqualizerEnabled` + 启动 load/apply |

**关键事实（已核对代码）**：
- 音频链（[NAudioPlaybackService.LoadAsync](../../../Services/NAudioPlaybackService.cs) 第 98-110 行）：
  `MediaFoundationReader → ToSampleProvider → SampleAggregator → VolumeSampleProvider → WasapiOut(Shared, 100ms)`
- 运行时配置传播（`SpectrumConfig` setter，第 66-77 行）：setter 存字段 + 若在链中间件存在则下发。
- `SpectrumConfig` 默认 `Enabled=true`；EQ 相反，默认 `Enabled=false`（透明旁路，不影响存量用户）。

## 4. 架构总览

### 4.1 新音频链

```
MediaFoundationReader
   │  (WaveStream, PCM)
   ▼  ToSampleProvider()
ISampleProvider (原始样本)
   ▼
EqualizerSampleProvider   ◀── 新增：preamp + 10 段 BiQuad 峰值滤波
   │                          Update(config) 实时重算系数
   ▼
SampleAggregator          (Phase 13 FFT 频谱分析 —— 现在分析 EQ 之后的信号)
   ▼
VolumeSampleProvider      (线性音量)
   ▼
WasapiOut(Shared, 100ms)
```

**插入点决策**：EQ 置于 `SampleAggregator` **之前**，使频谱可视化反映 EQ 处理后的信号（推高低音时频谱低频柱随之抬升），视觉与听感一致。

### 4.2 组件与文件清单

**新增：**

| 文件 | 职责 |
|------|------|
| `Models/EqualizerConfig.cs` | 不可变 record：`Enabled` / `PreampDb` / `BandGainsDb` / `Preset` + Clamp 工厂 |
| `Models/EqualizerPresets.cs` | 静态预设表 + 名称↔曲线查找 + 中心频率常量 |
| `Services/EqualizerSampleProvider.cs` | `ISampleProvider` 中间件：preamp + 10 段 BiQuad；`Update(config)` 实时重算 |
| `Views/Dialogs/EqualizerDialog.xaml` / `.xaml.cs` | 独立 EQ 对话框（复用 SettingsDialog 模式） |
| `Tests/Models/EqualizerConfigTests.cs` | Clamp / 默认值 |
| `Tests/Models/EqualizerPresetsTests.cs` | 预设表完整性 / 查找往返 |
| `Tests/Services/EqualizerSampleProviderTests.cs` | 透传 / unity / Update |
| `Tests/ViewModels/PlayerViewModelEqualizerTests.cs` | 加载 / 传播（镜像 `PlayerViewModelSpectrumTests`） |

**修改：**

| 文件 | 改动 |
|------|------|
| `Services/IPlaybackService.cs` | 加 `EqualizerConfig EqualizerConfig { get; set; }` |
| `Services/NAudioPlaybackService.cs` | LoadAsync 建链插入 EQ provider；`EqualizerConfig` setter 实时下发；DisposePlayback 清理字段 |
| `Configuration/AppSettings.cs` | 加 4 个 EQ 持久化字段 |
| `ViewModels/PlayerViewModel.cs` | 加 `EqualizerEnabled` observable + 启动 `LoadEqualizerSettings` / apply + `OnEqualizerEnabledChanged` |
| `Views/Controls/PlayerBar.xaml` / `.xaml.cs` | 加 🎚 EQ 按钮 + 打开对话框 + 激活态高亮绑定 |

## 5. 数据模型

### 5.1 `Models/EqualizerConfig.cs`

```csharp
public sealed record EqualizerConfig
{
    /// <summary>启用 EQ；false 时 EqualizerSampleProvider 原样透传（零成本旁路）。</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>前置放大 dB，范围 -12 ~ +12；用于多段提升时留余量防削波。</summary>
    public double PreampDb { get; init; } = 0;

    /// <summary>10 段增益 dB，各 -12 ~ +12；顺序对应 CenterFrequencies。</summary>
    public IReadOnlyList<double> BandGainsDb { get; init; }
        = new double[EqualizerPresets.BandCount]; // 全 0

    /// <summary>当前预设名；手动改动任一段即 "Custom"。</summary>
    public string Preset { get; init; } = EqualizerPresets.Flat;

    /// <summary>工厂：把越界的 preamp / 各段增益 Clamp 到 [-12, 12]。</summary>
    public static EqualizerConfig Create(bool enabled, double preampDb,
        IReadOnlyList<double> bandGainsDb, string preset) { /* Clamp */ }
}
```

- 采用 `IReadOnlyList<double>`（长度固定 10）而非 10 个具名字段，保持 record 紧凑；持久化时序列化为 JSON 数组。
- `Create(...)` 统一做 Clamp（preamp 与各段增益均截到 ±12），杜绝越界值进入 DSP。

### 5.2 `Models/EqualizerPresets.cs`

```csharp
public static class EqualizerPresets
{
    public const int BandCount = 10;
    public const double MinGainDb = -12, MaxGainDb = 12;

    /// <summary>ISO 倍频程中心频率（Hz），固定常量，不持久化。</summary>
    public static readonly IReadOnlyList<double> CenterFrequencies =
        [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    public const string Flat = "Flat";
    public const string Custom = "Custom";

    /// <summary>预设名 → 10 段增益曲线（dB）。有序，供 UI 下拉展示。</summary>
    public static readonly IReadOnlyList<(string Name, double[] Gains)> All = [ ... ];

    public static bool TryGet(string name, out double[] gains);
    public static IReadOnlyList<string> Names { get; }   // 下拉项（含 Flat..Vocal，不含 Custom）
}
```

**初始预设曲线（dB，顺序 31/62/125/250/500/1k/2k/4k/8k/16k）**（可在实现期微调听感）：

| 预设 | 31 | 62 | 125 | 250 | 500 | 1k | 2k | 4k | 8k | 16k |
|------|----|----|-----|-----|-----|----|----|----|----|-----|
| Flat | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Rock | 5 | 4 | 3 | 1 | -1 | -1 | 0 | 2 | 3 | 4 |
| Pop | -1 | 1 | 3 | 4 | 3 | 0 | -1 | -1 | 1 | 2 |
| Jazz | 3 | 2 | 1 | 2 | -2 | -2 | 0 | 1 | 2 | 3 |
| Classical | 4 | 3 | 2 | 0 | -1 | -1 | 0 | 1 | 2 | 3 |
| Dance | 6 | 4 | 1 | 0 | -2 | -2 | 0 | 1 | 3 | 4 |
| Bass Boost | 6 | 5 | 4 | 2 | 0 | 0 | 0 | 0 | 0 | 0 |
| Treble Boost | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 3 | 5 | 6 |
| Vocal | -2 | -1 | 0 | 2 | 4 | 4 | 3 | 1 | 0 | -1 |

- Q 因子（带宽）≈ 1.1（约一个倍频程），是图形 EQ 的常用取值。
- `Custom` 不作为可选择的下拉项，仅作为「用户手动偏离任何预设」后的状态标签。

### 5.3 `Configuration/AppSettings.cs` 新增字段

```csharp
// ====== Phase 14: 均衡器 ======
public bool   EqualizerEnabled { get; init; } = false;
public double EqualizerPreamp  { get; init; } = 0;      // -12 ~ +12 dB
public double[] EqualizerBands { get; init; } = new double[10]; // 全 0
public string EqualizerPreset  { get; init; } = "Flat";
```

- 沿用 Phase 13 频谱字段的扁平风格。
- `EqualizerBands` 用数组：`with { EqualizerBands = 新数组 }` 更新（不原地修改），`System.Text.Json` 序列化为 JSON 数组，跨版本稳定。

## 6. DSP 服务设计

### 6.1 `Services/EqualizerSampleProvider.cs`

```csharp
public sealed class EqualizerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sampleRate;
    private readonly BiQuadFilter[][] _filters;  // [channel][band]，每声道独立 10 段（见 §12）
    private readonly object _lock = new();
    private float _preampGain = 1f;
    private bool _enabled;

    public EqualizerSampleProvider(ISampleProvider source, EqualizerConfig config)
    {
        _source = source;
        _sampleRate = source.WaveFormat.SampleRate;
        int ch = source.WaveFormat.Channels;
        _filters = new BiQuadFilter[ch][];
        for (int c = 0; c < ch; c++)
            _filters[c] = new BiQuadFilter[EqualizerPresets.BandCount];
        Apply(config);   // 初始化所有声道 × 频段的系数
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (!_enabled) return read;             // 透明旁路

        int channels = WaveFormat.Channels;
        lock (_lock)                            // buffer 粒度锁，防撕裂系数
        {
            for (int i = 0; i < read; i++)
            {
                int c = i % channels;           // 交织样本 → 声道索引
                float s = buffer[offset + i] * _preampGain;
                var bank = _filters[c];         // 每声道独立滤波状态
                for (int f = 0; f < bank.Length; f++)
                    s = bank[f].Transform(s);
                buffer[offset + i] = s;
            }
        }
        return read;
    }

    /// <summary>运行时更新：就地重算系数（保留滤波器状态，避免拖动爆音）。</summary>
    public void Update(EqualizerConfig config) { /* lock 内 SetPeakingEq + preamp + enabled */ }
}
```

- **滤波实现**：`NAudio.Dsp.BiQuadFilter.PeakingEQ(sampleRate, centreFrequency, q, dbGain)` 构造；`Update` 用实例方法 `SetPeakingEq(...)` 就地重算系数（保留 x1/x2/y1/y2 状态，避免每次拖动重建滤波器造成的爆音）；逐样本 `Transform(float)` 处理。
- **多声道处理**：BiQuadFilter 携带滤波状态，立体声需**每声道一套滤波器**（否则左右声道共享状态会互相干扰）。实现时 `_filters` 应为 `BiQuadFilter[channels][bands]` 或按声道索引的二维结构；`Update` 对所有声道的滤波器组统一重算。规格中以「每声道独立 10 段」为准（§12 展开）。
- **Nyquist 保护**：中心频率 ≥ `sampleRate / 2` 的频段旁路（增益设 0 / 不建滤波器），避免不稳定滤波。44.1kHz 下 Nyquist=22.05kHz，全部 10 段有效；低采样率素材自动跳过超高频段。
- **preamp**：线性增益 `10^(preampDb/20)`，作为输入级乘子（先 preamp 再滤波）。
- **透传旁路**：`Enabled=false` 时 `Read` 直接返回源读取数，不做任何处理（零成本，与未加 EQ 时完全一致）。

### 6.2 `Services/IPlaybackService.cs`

新增（镜像 `SpectrumConfig`）：

```csharp
/// <summary>均衡器配置（运行时可更新；setter 实时下发到在链 EQ provider）。</summary>
EqualizerConfig EqualizerConfig { get; set; }
```

### 6.3 `Services/NAudioPlaybackService.cs`

- 新增字段：`private EqualizerSampleProvider? _equalizer;` + `private EqualizerConfig _equalizerConfig = new();`
- `LoadAsync` 建链（第 100-108 行区域）改为：
  ```csharp
  var sampleProvider = _reader.ToSampleProvider();
  _equalizer = new EqualizerSampleProvider(sampleProvider, _equalizerConfig); // 新增
  _sampleAggregator = new SampleAggregator(_equalizer, _spectrumConfig);      // 源改为 _equalizer
  _sampleAggregator.SpectrumDataReady += OnSpectrumDataReady;
  _volumeProvider = new VolumeSampleProvider(_sampleAggregator) { Volume = _volume };
  ```
- `EqualizerConfig` setter（对齐 `SpectrumConfig` setter 第 66-77 行）：
  ```csharp
  public EqualizerConfig EqualizerConfig
  {
      get => _equalizerConfig;
      set { _equalizerConfig = value; _equalizer?.Update(value); }
  }
  ```
- `DisposePlayback`（第 278-300 行）：清理 `_equalizer = null;`（EQ provider 无事件订阅，仅置空引用；随播放链一起释放）。

## 7. ViewModel 设计（`PlayerViewModel` 最小改动）

仅新增：

```csharp
[ObservableProperty]
private bool _equalizerEnabled;   // 供 PlayerBar EQ 按钮激活态高亮（镜像 Shuffle/Repeat）
```

- `Initialize()` 内调用 `LoadEqualizerSettings(settings)`（在 `_isInitializing = false` 之前）：
  ```csharp
  private void LoadEqualizerSettings(AppSettings s)
  {
      EqualizerEnabled = s.EqualizerEnabled;
      _player.EqualizerConfig = EqualizerConfig.Create(
          s.EqualizerEnabled, s.EqualizerPreamp, s.EqualizerBands, s.EqualizerPreset);
  }
  ```
  → 启动即把持久化 EQ 应用到 service，首次播放自动生效（无需先打开对话框）。
- `partial void OnEqualizerEnabledChanged(bool value)`：
  ```csharp
  _player.EqualizerConfig = _player.EqualizerConfig with { Enabled = value };
  if (!_isInitializing)
      _ = _persistence.UpdateAsync(s => s with { EqualizerEnabled = value });
  ```
- `CleanupAsync` 无需新增（EQ 全量由对话框 Save 时写盘；启用态已在 `OnEqualizerEnabledChanged` 持久化）。

> 说明：EQ 的完整编辑态（10 段增益 / preamp / 预设）**不进 PlayerViewModel**，由 `EqualizerDialog` 直接读写 `IPlaybackService` + `ISettingsPersistence`（与 SettingsDialog 直连模式一致）。PlayerViewModel 只保留 `EqualizerEnabled` 这一个用于主窗口按钮高亮的 observable。

## 8. UI 设计

### 8.1 PlayerBar EQ 按钮

- 在右侧按钮组（🔊 音量 / ⚙ 设置 附近）加一个 🎚 EQ 按钮。
- 激活态高亮：`EqualizerEnabled=true` 时前景用 `AccentHover`（与 🔀 Shuffle / 🔁 Repeat 激活态一致，见 Phase 12 continued）。绑定 `PlayerViewModel.EqualizerEnabled`（经 `RelativeSource AncestorType=Window` 跨级绑定，与其它 PlayerBar 按钮同法）。
- 点击：code-behind handler（同 ⚙ 设置按钮），`App.GetService` 解析 `ISettingsPersistence` / `IPlaybackService` / `PlayerViewModel`，调 `EqualizerDialog.Show(...)`。
- **inline Style 必须 `BasedOn="{StaticResource {x:Type Button}}"`**（COUPLING.md §5 隐式契约），否则按钮回退 OS 原生白底。

### 8.2 `EqualizerDialog`（约 460×360，模态 ToolWindow）

```
┌─ 均衡器 ───────────────────────────────────────────┐
│ [✓] 启用均衡器        预设: [ Rock            ▼ ]  │
│                                                    │
│  dB  31 62 125 250 500 1k 2k 4k 8k 16k   Pre      │
│ +12 ┤                                            │
│   0 ┤ ▐█ ▐█ ▐█ ▐█ ▐█ ▐█ ▐█ ▐█ ▐█ ▐█  │ ▐█ │      │
│ -12 ┤ (11 根竖直滑块：10 段 + preamp)            │
│      每根顶部标频率，底部标当前 dB 值             │
│                                                    │
│           [ 恢复 Flat ]     [ 取消 ]  [ 保存 ]     │
└────────────────────────────────────────────────────┘
```

- **竖直滑块**：`Slider Orientation="Vertical"`，范围 −12 ~ +12，`IsMoveToPointEnabled=True`（沿用 Phase 13 全局 Slider 样式）。11 根：10 段 + 1 根 preamp。
- **静态 Show 工厂**：`EqualizerDialog.Show(Window? owner, ISettingsPersistence, IPlaybackService, PlayerViewModel?)`，返回 `bool`（true=保存）。
- **OnLoaded（async）**：读 settings → 初始化各滑块 Value / 预设下拉 SelectedItem / 启用复选框；try/catch 静默回落到默认（对齐 SettingsDialog.OnLoaded）。
- **实时预览**：任一滑块 `ValueChanged` / 预设下拉切换 / 启用复选框 → 组装新 `EqualizerConfig` → `_playbackService.EqualizerConfig = config`（即时听感）。手动移动滑块时，若结果偏离所有预设则下拉显示 `Custom`。
- **保存**：`await _persistence.UpdateAsync(s => s with { EqualizerEnabled=…, EqualizerPreamp=…, EqualizerBands=…, EqualizerPreset=… })` + 同步 `_playerViewModel.EqualizerEnabled`（按钮高亮即时更新）+ `DialogResult=true`；失败弹 MessageBox（对齐 SettingsDialog.Save_Click）。**不得 `ConfigureAwait(false)`** —— 保存后需在 UI 线程直接写 `PlayerViewModel` 属性（COUPLING.md §5 Phase 13 契约同理）。
- **取消 / 关闭**：把 `_playbackService.EqualizerConfig` 恢复到进入对话框时读取的「上次已保存」config，撤销实时预览的临时改动。
- 「恢复 Flat」按钮：一键把所有滑块归 0、preamp 归 0、预设下拉设 Flat（并实时下发）。

## 9. 数据流

```
启动应用
  PlayerViewModel.Initialize → LoadEqualizerSettings(settings)
    → _player.EqualizerConfig = EqualizerConfig.Create(...)   （service 存字段，链未建）
    → EqualizerEnabled = settings.EqualizerEnabled            （按钮高亮）

播放一首歌
  PlaylistViewModel.PlayTrackAtAsync → IPlaybackService.LoadAsync
    → new EqualizerSampleProvider(sampleProvider, _equalizerConfig)  （按实测采样率建系数）
    → SampleAggregator(_equalizer, ...) → Volume → WasapiOut

用户打开 EQ 对话框并拖动滑块 / 选预设
  EqualizerDialog.ValueChanged → 组装 config → _playbackService.EqualizerConfig = config
    → setter: _equalizerConfig = config; _equalizer?.Update(config)
    → EqualizerSampleProvider.Update → lock 内 SetPeakingEq 重算系数 + preamp + enabled
    → 下一帧 Read 立即用新系数 → 实时听到变化（频谱也随之变化）

用户点保存
  Dialog.Save_Click → UpdateAsync 写 settings.json（4 字段）+ 同步 PlayerViewModel.EqualizerEnabled
  → 按钮高亮更新；下次启动自动恢复

用户点取消 / 关闭窗口
  → _playbackService.EqualizerConfig = 进入时快照（撤销预览）
```

## 10. 持久化

- 文件：`%LocalAppData%\D-player\settings.json`（复用 `JsonSettingsPersistence`，无新文件）。
- 字段：`EqualizerEnabled` / `EqualizerPreamp` / `EqualizerBands`(数组) / `EqualizerPreset`。
- 读：`PlayerViewModel.Initialize` 启动同步读盘（小文件，毫秒级，沿用现有模式）。
- 写：① 启用态切换（`OnEqualizerEnabledChanged`）；② 对话框 Save（全量 4 字段）。均走 `UpdateAsync(Func<>)` 原子读-改-写。
- 旧 settings.json 无这些字段 → 反序列化为 record 默认值（Enabled=false，全 0，Flat）→ EQ 透明，无需迁移。

## 11. 边界情况与错误处理

| 情况 | 处理 |
|------|------|
| `Enabled=false` | `Read` 直接透传，零处理成本，与未加 EQ 完全一致 |
| 越界增益 / preamp（如手写 settings.json 填 99） | `EqualizerConfig.Create` Clamp 到 ±12 |
| 中心频率 ≥ Nyquist（低采样率素材） | 该频段旁路，避免不稳定滤波 |
| 多段同时提升导致削波 | 提供 preamp，用户可下调；不做自动限幅（YAGNI） |
| settings.json 损坏 / 缺失 | `LoadAsync` 静默回落默认（既有契约）；对话框 OnLoaded try/catch |
| 保存写盘失败 | 弹 MessageBox（对齐 SettingsDialog） |
| 切歌 / Unload | DisposePlayback 置空 `_equalizer`；下次 LoadAsync 用当前 `_equalizerConfig` 重建 |
| 对话框打开时无曲目播放 | 实时下发到 `_equalizerConfig` 字段，下次 LoadAsync 生效；无异常 |

## 12. 线程模型

- `EqualizerSampleProvider.Read` 在 **NAudio 音频渲染线程**执行；`Update(config)` 由 **UI 线程**（对话框拖动）调用。
- **系数一致性**：`Update` 与 `Read` 用同一 `lock`（buffer 粒度，非逐样本）。`Read` 处理一个缓冲区内所有样本时持锁；`Update` 重算系数时持锁。锁竞争窗口极小（音频缓冲区读取为微秒级），UI 拖动不会阻塞音频。
- **滤波器状态保留**：`Update` 用 `SetPeakingEq` 就地改系数而非重建 `BiQuadFilter`，保留 x1/x2/y1/y2 状态，避免拖动时输出跳变（爆音）。
- **每声道独立滤波器**：BiQuad 携带状态，立体声必须每声道一套 `BiQuadFilter[10]`（`BiQuadFilter[channels, bands]`），否则左右声道共享延迟线会串扰。`Read` 按 `i % channels` 选对应声道滤波器组；`Update` 对所有声道组统一重算。
- 与 Phase 13 的分工一致：EQ 只处理音频样本，不触发任何跨线程 UI 事件（无需 `_syncContext.Post`）。

## 13. 测试计划（预计 8-11 个）

| 测试文件 | 覆盖 |
|----------|------|
| `Tests/Models/EqualizerConfigTests.cs` | 默认值（Enabled=false / 全 0 / Flat）；`Create` 对越界 preamp / 段增益 Clamp 到 ±12 |
| `Tests/Models/EqualizerPresetsTests.cs` | 每个预设恰好 10 段；全部在 ±12 内；Flat 全 0；`CenterFrequencies` 长度=10；名称查找往返；`Names` 不含 Custom |
| `Tests/Services/EqualizerSampleProviderTests.cs` | ① `Enabled=false` 精确透传（输出==输入）；② Flat+Enabled 时输出≈输入（unity 增益，0 dB 峰值滤波近似不改变）；③ `Update` 改增益后输出变化；④ WaveFormat 透传源 |
| `Tests/ViewModels/PlayerViewModelEqualizerTests.cs` | 镜像 `PlayerViewModelSpectrumTests`：启动从 settings 加载 `EqualizerEnabled` 并传播 `_player.EqualizerConfig`；`OnEqualizerEnabledChanged` 写回 service + 持久化 |

- Mock：`IPlaybackService`（NSubstitute）需预设 `EqualizerConfig.Returns(new EqualizerConfig())`（record 引用类型，避免 `with` 时 NRE，同 Phase 13 `SpectrumConfig` mock 契约）。
- `EqualizerSampleProvider` 测试用一个产生已知样本的假 `ISampleProvider`（如正弦 / 常数），无需真实音频文件。

## 14. 实现任务分解（供 writing-plans 细化）

建议顺序（每步可独立编译 + 测试）：

1. **Models**：`EqualizerConfig` + `EqualizerPresets`（纯数据，先建 + 单测）。
2. **Service DSP**：`EqualizerSampleProvider`（BiQuad 链 + Update + Nyquist + 每声道）+ 单测。
3. **IPlaybackService + NAudioPlaybackService**：加 `EqualizerConfig` 属性 + 建链插入 + DisposePlayback 清理。
4. **Configuration**：`AppSettings` 加 4 字段。
5. **ViewModel**：`PlayerViewModel` 加 `EqualizerEnabled` + 启动 load/apply + 传播 + 单测。
6. **View 对话框**：`EqualizerDialog.xaml(.cs)`（布局 + 实时预览 + 预设 + 保存/取消）。
7. **View PlayerBar**：EQ 按钮 + 打开对话框 + 激活态高亮。
8. **文档**：更新 PROJECT.md（§1.1 特性表 / §4 架构 / §5 模块 / §10 历史）+ COUPLING.md（§5 新增隐式契约）。
9. **验收**：`dotnet build` + `dotnet test` 全绿；手动听感验收（拖动实时生效、预设切换、重启恢复、关闭透明）。

## 15. 新增隐式契约（实现后登记到 COUPLING.md §5）

- `EqualizerSampleProvider.Read`（音频线程）与 `Update`（UI 线程）必须共用 buffer 粒度 `lock`；`Update` 用 `SetPeakingEq` 就地改系数、保留滤波器状态，禁止重建 `BiQuadFilter`（重建会清空延迟线导致爆音）。
- 立体声必须每声道独立 `BiQuadFilter[10]`，禁止左右共享滤波器实例（状态串扰）。
- EQ 插入点必须在 `SampleAggregator` **之前**（频谱须反映 EQ 后信号）；移到其后会让可视化与实际听感脱钩。
- `IPlaybackService.EqualizerConfig` setter 语义与 `SpectrumConfig` 一致：存字段 + 若在链 provider 存在则 `Update`，链未建时仅存字段待 LoadAsync 使用。
- `EqualizerDialog.Save_Click` 不得 `ConfigureAwait(false)`（保存后需在 UI 线程写 `PlayerViewModel.EqualizerEnabled`）。
- PlayerBar EQ 按钮 inline Style 必须 `BasedOn="{StaticResource {x:Type Button}}"`。
- `EqualizerConfig.Enabled` 默认 **false**（透明旁路），保证不影响存量用户既有听感。

## 16. 参考

- 现有音频链与运行时配置传播：[`Services/NAudioPlaybackService.cs`](../../../Services/NAudioPlaybackService.cs)
- 中间件模式先例（Phase 13）：[`Services/SampleAggregator.cs`](../../../Services/SampleAggregator.cs)
- 对话框模式先例：[`Views/Dialogs/SettingsDialog.xaml.cs`](../../../Views/Dialogs/SettingsDialog.xaml.cs)
- 设置持久化模式：[`ViewModels/PlayerViewModel.cs`](../../../ViewModels/PlayerViewModel.cs) · [`Configuration/AppSettings.cs`](../../../Configuration/AppSettings.cs)
- Phase 13 设计稿：[`2026-06-24-uma-player-phase13-audio-visualization-design.md`](./2026-06-24-uma-player-phase13-audio-visualization-design.md)
- 架构纪律与隐式契约登记册：[`docs/COUPLING.md`](../../COUPLING.md)
