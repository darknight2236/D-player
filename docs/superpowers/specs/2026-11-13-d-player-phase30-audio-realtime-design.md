# Phase 30 设计：音频实时路径性能重写（基线、确定性安全网与热路径重构）

- 日期：2026-11-13
- 分支：`phase30-audio-realtime`（起点 `master` / `b92d3d6`）
- 前置：Phase 28（四工程 net11.0）、Phase 29（非音频路径现代化，验收基线 342 = 337 默认 + 5 音频）
- 用户口径（2026-11-13 拍板）：
  1. **全激进重写**：允许 FFT 离音频线程、EQ 系数无锁发布、频谱零分配发布、播放链闸门重构。
  2. 性能基线用**自定义 AudioBench**采集，不引入 BenchmarkDotNet。
  3. 性能验收目标**先测后定**：先产出可复现基线，再与用户确认降幅/预算，写进本文 §10 后才进入重写批次。

## 1. 背景

Phase 28 把四个工程抬到 net11.0，Phase 29 完成非音频路径现代化，两者都刻意没有触碰音频实时路径。Phase 30 承接三件事：

1. 用**实测基线**回答"音频线程到底花多少时间"，而不是凭直觉优化。
2. 把闸门侧"1–5 ms 错位窗口后不抛异常"的**随机证据**升级为**确定性证据**。
3. 在基线之上重写频谱、EQ、播放链与位置轮询，使音频线程不再执行 FFT、不再取 EQ 锁、稳态零分配。

## 2. 目标与非目标

### 2.1 目标

- **G1 基线**：`tools/AudioBench` 产出可复现的 p50/p95/p99、CPU/音频秒、分配字节数，落 `docs/PHASE30-AUDIO-BASELINE.md` / `.json`。
- **G2 确定性安全网**：用内部工厂 + 假 `IAudioSource`/`IWavePlayer` + 门闩替代 sleep 窗口，覆盖播放链生命周期、停止语义、自然播完、旧链迟到回调。
- **G3 频谱缺陷**：先以红测试固定"重叠帧必须使用真实时域样本"，再修复 `SampleAggregator` 的缓冲复用错误。
- **G4 音频线程结构目标**：音频回调内不执行 FFT、不取 EQ 锁、稳态零托管分配；FFT 在独立后台线程按 50% hop 计算。
- **G5 EQ 无锁下发**：`Update` 只发布不可变配置引用，音频线程在 `Read` 开头取走并就地更新系数；保留"配置在缓冲边界生效"的现有语义（**2026-11-13 校正：原稿同句写的"滤波状态不重置"不成立** —— NAudio 3.3.0 `SetPeakingEq` 会清零 `x1/x2/y1/y2`，见 §4.2/§7.3/§11 校正）。
- **G6 播放链**：抽出 `PlaybackChain`，重活（打开 reader、初始化输出）在 `_chainGate` 外完成；旧链 stop/dispose 在锁外完成；`OnPlaybackStopped` 继续不取链闸门。
- **G7 轮询**：`PeriodicTimer` + 播放会话代际替代 `Task.Delay(33)` 外套 `DateTime` 节流，杜绝重复轮询循环。
- **G8 验收**：`-Full -Audio`、5 条真实音频、ComputerUse GUI 走查、独立复核、数据 SHA-256 还原、改写前后 AudioBench 对照。

### 2.2 非目标

- 不改 WinUI 壳与 WPF 视图；WinUI 仅由门禁 3 编译覆盖（延续冻结决策）。
- 不改 `IPlaybackService` 的成员/事件形状，不改 `NAudioPlaybackService` 的公开构造函数。
- 不改音频格式支持、输出后端/延迟/共享模式、EQ 频段/预设/Q 值。
- 不做自定义 DSP 公式替换：继续使用 NAudio `BiQuadFilter` 的公式与状态形状，保证听感等价。
- 不做 `SpectrumConfig.FftSize`/`BarCount` 的运行时热更新（现状也不支持；本阶段把契约写明）。
- 不做 AOT/trimming、不引入新的音频依赖、不改 JSON/设置 schema。
- 不把 AudioBench 纳入日常门禁；它是人工基线工具。
- SIMD/Vector 化 EQ 仅作为基线后的备用手段，不在首批提交内；只有标量重排未达目标且等价性测试通过才启用。

## 3. 当前事实与证据

### 3.1 播放链现状

- 链路：`MediaFoundationReader → EqualizerSampleProvider → SampleAggregator → VolumeSampleProvider → WasapiPlayer`，100 ms 共享模式 + 事件同步。
- `_chainGate` 串行化整条链的重建/拆除/传输命令；`LoadAsync` 在 `Task.Run` 内**持锁完成** reader 打开与输出 `Init`。
- `OnPlaybackStopped` 不取锁，靠防御式读取与 `_stopRequested` 区分自然播完/用户停止。
- `PollPositionAsync` 用 `Task.Delay(33)`，外层再套 `DateTime` 节流；`Play()` 每次都会启动一个新循环。

### 3.2 频谱 50% 重叠缺陷（已取证）

[SampleAggregator.cs:60-81](../../../D-player.Core/Services/SampleAggregator.cs) 在 FFT 后执行：

```csharp
FastFourierTransform.FFT(true, (int)Math.Log2(_fftSize), _fftBuffer);
ExtractSpectrumData();
SpectrumDataReady?.Invoke(_spectrumData.ToArray());

int halfSize = _fftSize / 2;
Array.Copy(_fftBuffer, halfSize, _fftBuffer, 0, halfSize);
_bufferPosition = halfSize;
```

两条事实叠加导致重叠失效：

1. `FastFourierTransform.FFT` **就地**把时域缓冲改成频域。临时探针（2026-11-13，Release，NAudio 3.3.0）对单位脉冲执行 `FFT(true, 13, buffer)` 后，`[0]/[1]/[4096]` 均为 `1.221E-004`（即 1/8192 归一化），字段为 `float X, float Y`；输出是频域而不是原始样本。
2. 加窗发生在 FFT 之前，同样就地改写了 `_fftBuffer`，即使不发生 FFT，原始后半段也已被窗函数乘过。

因此 **第二帧起，前半段输入是上一帧的频域残留**，频谱显示建立在被污染的样本上；首帧正确，后续每帧只有后半段是真实音频。该缺陷不影响传给扬声器的 PCM（Read 原样透传），只影响频谱正确性。

### 3.3 成本探针（指示性，非验收基线）

同一临时探针（Release，本机）读数：

| 项 | 读数 |
|---|---|
| FFT 8192 点 | 0.160–0.176 ms/帧 |
| 每帧重算 Hann 窗（8192 次 `Math.Cos`） | 0.052–0.055 ms/帧 |
| 预计算窗表后应用 | 0.002 ms/帧 |
| 10 段立体声 BiQuad（48 kHz，1 音频秒） | 9.83–10.16 ms/音频秒 |

按 FftSize 8192、50% hop（44.1 kHz 约 93 ms 一帧）估算，当前音频线程约：

- EQ ≈ 10 ms/音频秒
- FFT ≈ 1.8 ms/音频秒
- 窗重算 ≈ 0.6 ms/音频秒
- 合计 ≈ 12.4 ms/音频秒（约单核 1.24%）

结论：**当前没有"救火级"性能问题**，Phase 30 的回报主要是结构正确性（频谱重叠修复、零分配、无锁、离线程）、可重复基线与确定性回归；降幅目标必须等 AudioBench 正式读数后再定，不预写百分比。

### 3.4 并发证据强度

- `NAudioPlaybackServiceConcurrencyTests` 的 `LoadAsync_TwoOverlappingCalls_DoNotThrow` / `Unload_DuringLoadAsync_DoesNotThrow` 以 1–5 ms 错位窗口断言"不抛异常"，窗口未命中只是没触发竞态，属随机证据。
- `StopSemanticsTests` 的 `Stop_WhenPlayheadIsAtTrackEnd_DoesNotRaiseTrackEnded` 是确定性证据；`Unload_...` 钉住的是"解订阅早于停止"的顺序。
- 本阶段保留这 5 条真实设备测试，但新增的默认门禁测试必须用门闩精确编排，不再用 sleep 当竞态证据。

## 4. 设计原则与不可动契约

### 4.1 原则

- **先红后绿**：频谱重叠缺陷先写失败测试；EQ/链重构先写等价性或并发测试。
- **行为等价优先**：频谱只改缓冲管理与线程归属，不改分桶/归一化语义；EQ 继续用 NAudio `BiQuadFilter`，不改公式与状态类型；播放链只改组装与生命周期，不改传输语义。
- **音频线程只做最少的事**：混音、写环形缓冲、发信号、PCM 原样透传；不做 FFT、不分配、不取锁、不等待。
- **安全快照优先于零分配**：频谱事件每帧仍发一份 32 float 快照（128 B），维持"订阅者不能篡改共享缓冲"的契约；"零分配"指音频线程，不把 FFT 线程/UI 线程的 128 B 快照算进去。
- **"全激进"的边界**：结构激进（FFT 离线程、EQ 无锁下发、链重构），但不拿公开安全契约换字面零分配；若要把 FFT 线程也做成零分配发布，需要把公开事件改成复用缓冲/`ReadOnlyMemory` 契约，本阶段不做。
- **可回滚**：每个批次独立提交；频谱离线程、EQ 无锁、链重构分别可单独回退。

### 4.2 不可动契约

- `_chainGate` 仍串行化链安装/拆除/传输命令；持锁期间不得 `await`。
- `OnPlaybackStopped` 不取 `_chainGate`；自然播完与用户停止由每链 `StopRequested` 显式区分；200 ms 容差保留。
- EQ 插入点仍在 `SampleAggregator` 之前；每声道独立滤波器；`SetPeakingEq` 就地重算、不重建滤波器对象；Nyquist 旁路保留。
  - **2026-11-13 校正（原文为"不清状态"，已证伪）**：NAudio 3.3.0 的 `BiQuadFilter.SetPeakingEq` 会把延迟线 `x1/x2/y1/y2` **清零**（独立探针：喂直流使状态为 0.5，调用 `SetPeakingEq` 后读回 0）。Phase 30 保留的语义是"配置在缓冲边界生效"，**不包含**跨缓冲的滤波状态保持；这是 Phase 14 起就存在的既有行为，不是本阶段引入的回归。状态保持/交叉淡化是后续候选，不在本阶段范围（非目标：不改自定义 DSP 公式）。
- 频谱事件仍经 `SynchronizationContext.Post` 封送到 UI 线程后才能到 VM。
- `IPlaybackService` 公开成员/事件不变；`NAudioPlaybackService` 公开无参构造不变。
- 引用身份回找继续用 `ReferenceEqualityComparer`（本阶段不触碰播放队列）。
- 零 `#pragma warning disable` / `NoWarn`；不迎合 IDE 诊断改产品代码。

## 5. 交付物 0：AudioBench 与基线

### 5.1 工程

- 新增 `tools/AudioBench/AudioBench.csproj`：
  - `TargetFramework=net11.0-windows`，`OutputType=Exe`，`UseWPF=false`；
  - `ProjectReference` 指向 `D-player.Core`；
  - 不加入 `D-player.slnf`，不参与门禁；如需 IDE 可见可加入 `D-player.sln`，但门禁命令仍只走 slnf。
- 只使用合成源（`ISampleProvider`），不打开真实设备、不依赖 COM/WASAPI，保证可重复。
- Release 运行；固定 `--warmup 2 --seconds 10 --runs 5`；缓冲区默认 480 帧（48 kHz 10 ms），另测 960 帧。

### 5.2 场景

| 场景 | 说明 |
|---|---|
| `eq-off` | 旁路路径，基线对照 |
| `eq-flat` | 10 段 0 dB，全部滤波器参与 |
| `eq-bass-boost` | Bass Boost 预设，含 preamp |
| `aggregator` | 当前 `SampleAggregator` 整链（含 FFT/窗/分桶/事件） |
| `full-chain` | 合成源 → EQ → Aggregator → Volume，读 `Read` 分布 |
| `alloc` | 音频线程分配字节/秒；FFT 线程用进程总分配增量核对 |

before/after 对比口径：**音频线程 `Read` 成本是主对照**；改写后另列 FFT 线程每帧成本，不能把"原 Read 内含 FFT"和"新 Read + FFT 线程"混成同一个总数。

### 5.3 指标与产物

- 每次 `Read` 用 `Stopwatch.GetTimestamp` 记录，输出 p50/p95/p99/max（微秒）。
- CPU/音频秒 = 总耗时（ms）/模拟音频秒数。
- 分配：音频线程 `GC.GetAllocatedBytesForCurrentThread()`；FFT 线程/进程 `GC.GetTotalAllocatedBytes(precise: true)` 增量。
- JSON 字段：commit SHA、OS/CPU/运行时/GC 模式、场景参数、读数；Markdown 由 JSON 生成。
- 基线：`docs/PHASE30-AUDIO-BASELINE.md` + `.json`；改写后：同名 `...-AFTER.md` + `.json`，验收文档并排列出。
- 测量循环内不 LINQ、不字符串拼接；百分位在测量结束后排序计算。

### 5.4 流程与目标确认

1. 在阶段30分支的批次 0 提交上跑基线（该提交只新增 AudioBench/测试缝，音频 provider 代码与 `master` / `b92d3d6` 一致），提交两份产物。
2. 把音频线程 CPU/音频秒、p99、分配读数回报用户，确认降幅/预算，写入本文 §10.2。
3. 每个重写批次后用同一命令复跑；最终验收用 before/after 对照，不以单次读数下结论。

## 6. 交付物 1：确定性安全网

### 6.1 内部工厂与适配器

- 新增 internal `IAudioSource`：

```csharp
internal interface IAudioSource : IDisposable
{
    WaveFormat WaveFormat { get; }
    TimeSpan TotalTime { get; }
    TimeSpan CurrentTime { get; set; }
    ISampleProvider ToSampleProvider();
}
```

- 新增 internal `MediaFoundationAudioSource`：包装 `MediaFoundationReader`，行为与现读取一致。
- 新增 internal `IAudioSourceFactory { IAudioSource Create(string path); }` 与默认实现。
- 消费已有 `IAudioOutputFactory` / `StubAudioOutputFactory`：默认无参构造使用它创建 WASAPI Shared/Event/100 ms 输出；该 Stub 从"预注册未消费"变为生产默认，名称保留（历史名，不在本阶段改名以免牵动 DI 注册）。
- 新增 internal 构造函数：

```csharp
internal NAudioPlaybackService(
    IAudioSourceFactory sourceFactory,
    IAudioOutputFactory outputFactory,
    SynchronizationContext? syncContext = null)
```

公开无参构造委托到它；DI 解析路径不变。

### 6.2 假实现

- `FakeAudioSource`：可控 `WaveFormat`/`TotalTime`/`CurrentTime`，返回可控 `FakeSampleProvider`。
- `FakeWavePlayer`：实现 `IWavePlayer`；`Init` 可阻塞在门闩上；`Stop` 可同步/异步触发 `PlaybackStopped`；记录 `Init/Play/Pause/Stop/Dispose` 顺序。
- 所有编排用 `ManualResetEventSlim`/`Barrier`；测试中的超时只用于"检测死锁/挂起"，不作为竞态窗口。

### 6.3 测试矩阵（默认门禁）

| 测试 | 钉住的行为 |
|---|---|
| `Load_BlocksInInit_UnloadCancelsInstall` | 装载期 Unload 立即返回；新链不安装并被释放 |
| `TwoOverlappingLoads_Serialized_LastChainWins` | 装载闸门；首个链被完整拆除 |
| `Stop_RaisesPlaybackStoppedSynchronously_DoesNotDeadlock` | `OnPlaybackStopped` 不取链闸门 |
| `Stop_WhenPlayheadAtEnd_DoesNotRaiseTrackEnded` | 停止意图标记（假源可精确摆位） |
| `NaturalEnd_RaisesTrackEndedOnce` | 自然播完判定 |
| `ReplacedChain_LatePlaybackStopped_IsIgnored` | 代际/当前链判定 |
| `ChainDispose_UnsubscribesBeforeStop` | 解订阅早于停止/销毁 |
| `Events_ArePostedToCapturedSynchronizationContext` | UI 线程封送契约 |
| `Unload_DuringLoad_NewChainNotInstalled` | 装载期 Unload 不被新链覆盖 |
| `Dispose_DuringLoad_DisposesNewChain` | 装载期 Dispose 不泄漏新链 |

- 现有 5 条 `[Trait("Category","AudioDevice")]` 真实设备测试保留；它们仍是集成证据，但不再承担主要并发论证。
- 新增测试不得使用 `Task.Delay` 做同步；代码审查时 grep `Task.Delay` 作为检查项。

## 7. 热路径重写

### 7.1 SampleAggregator

**结构**

- 音频线程（producer）：
  1. `_source.Read(buffer)`；
  2. 若 `Enabled`，按 1/2/多声道分支混音为单声道，写入 SPSC 环形缓冲；
  3. 单调写索引 `long _writeIndex`；每跨过一个 hop（`FftSize/2`）`AutoResetEvent.Set()` 一次；
  4. **不做 FFT、不分配、不取锁、不等待**。
- FFT 线程（consumer，`IsBackground=true`，`ThreadPriority.BelowNormal`）：
  1. 等信号（100 ms 超时用于检查停止标志）；
  2. 取环形缓冲中"最后 `FftSize` 个样本"作为一帧，hop 前进 `FftSize/2`；
  3. 复制到独立 FFT 工作缓冲，乘预计算 Hann 表；
  4. `FastFourierTransform.FFT`；
  5. 用预计算 bin 区间映射到 `BarCount` 根柱；
  6. 生成一份 `float[BarCount]` 快照并触发 `SpectrumDataReady`。
- 环形缓冲容量 `4 * FftSize`（8192 → 32768 float，128 KB）；消费者落后超过 `capacity - FftSize` 时直接跳到最新窗口，**丢帧不排队**，内存有界。
- 停止：`Dispose` 置停止标志、发信号、join（2 s 上限）；先解订阅事件再 dispose，避免销毁后回调。
- 事件回调异常不得杀死 FFT 线程：单帧回调包在 try/catch 内，异常被吞掉并继续下一帧（本阶段不新增日志设施）。
- 启用切换：`Enabled=false` 时 producer 不写入；重新启用时 consumer 把 `lastFrameEnd` 对齐到当前 `writeIndex`，不补算历史帧。
- 配置校验：`FftSize` 必须是 2 的幂且在 `[256, 65536]`，`BarCount > 0`；不合法在构造时抛 `ArgumentOutOfRangeException`。
- `FftSize`/`BarCount` 固定为建链参数；运行时只支持 `Enabled`（与现状一致，见 §8）。

**正确性**

- 重叠帧使用**真实时域样本**：帧 = 环形缓冲中截至 `writeIndex` 的最后 `FftSize` 个样本；hop = `FftSize/2`。
- 加窗只作用于 FFT 工作缓冲，不污染原始样本。
- 预计算窗表与 bin 区间使用与现实现相同的公式与顺序，首帧输出与旧实现保持一致（测试用固定信号 + 容差 1e-6）。
- 事件快照每帧新数组（128 B），保留订阅者不可篡改下一帧的契约。

**新增测试**

- `Overlap_UsesTimeDomainSamples_NotPreviousFftOutput`：先红；用两帧 ramp/正弦，断言第二帧分桶与测试侧参考 DFT 一致。
- `KnownSine_PeaksInExpectedBar`：1 kHz @ 44.1 kHz / 8192 点，断言峰值柱。
- `Silence_NoNaN_AllZero`。
- `Disabled_PassesThroughExactly_NoEvent`。
- `SubscriberMutation_DoesNotAffectNextFrame`。
- `Dispose_StopsThread_NoEventsAfterDispose`。
- `EnabledToggle_SkipsStaleFrames`。

### 7.2 PlayerViewModel 频谱处理

- `HandleSpectrumData` 不再对 `rawData` 做第二次 `ToArray()`：快照由 `SampleAggregator` 新建，VM 可直接原地乘灵敏度。
- 平滑写入 `_smoothedSpectrum`；发布给 WPF 绑定时保留一次 `ToArray()`（32 float，128 B/帧），保证绑定持有的是稳定数组。
- 结果：每帧从 3 次分配（聚合器 1 + VM 2）降为 2 次（聚合器 1 + 发布 1），音频线程 0 次。
- 若 `rawData.Length` 与 `_smoothedSpectrum.Length` 不一致，忽略该帧（防御 BarCount 变化）。

### 7.3 EqualizerSampleProvider

**无锁配置下发**

- 删除音频 `Read` 中的 `lock (_lock)`。
- UI 线程 `Update(config)`：仅 `Volatile.Write(ref _pendingConfig, config)`（不可变 record，最后一次写入胜出）。
- 音频线程每次 `Read` 开头：`var cfg = Interlocked.Exchange(ref _pendingConfig, null); if (cfg != null) ApplyConfig(cfg);` 然后处理样本。
- `ApplyConfig` 在音频线程执行：更新 `_enabled`/`_preampGain`，对每个声道/频段沿用现有逻辑——已存在则 `SetPeakingEq` 就地重算，不存在则新建，Nyquist 旁路置空。构造时直接应用初始配置，不走 pending。
- 这保留了旧实现靠整缓冲锁达到的"只在缓冲边界生效"语义；UI 不再能阻塞音频线程。
- **2026-11-13 校正**：`SetPeakingEq` 在 NAudio 3.3.0 会清零 `x1/x2/y1/y2`，因此"滤波状态不重置"这句**不成立**（Phase 14 起的既有行为）。本阶段的等价性测试是对**参考滤波器组**逐样本比对（每帧重算系数即复位状态），不是"状态连续"断言；要把 EQ 拖动做成无爆音，需要状态保持/交叉淡化，属后续候选。

**循环重排**

- 按声道处理，消除逐样本 `% _channels`；
- 每次 `ApplyConfig` 后重建每声道"有效滤波器数组"（去掉 null，保持频段升序），热循环不做 null 判断；
- `_preampGain == 1f` 时跳过乘法；
- 保留多声道通用路径，但 1/2 声道走专用分支。

**测试**

- 现有 7 条 EQ 测试全部保留，输出必须与旧实现等价。
- 新增 `Update_AppliedAtBufferBoundary_MatchesReferenceFilterBank`（**2026-11-13 校正：原稿名为 `Update_AppliedAtBufferBoundary_PreservesState`，"状态连续"断言建立在 `SetPeakingEq` 保持状态这一错误前提上**）：在缓冲中间发布配置，断言只在下一次 `Read` 生效，且输出与"每帧用当前配置重算系数的参考滤波器组"逐样本一致（含非有限值拒绝比较）。
- 新增 `Update_ConcurrentWithRead_NoExceptions_FiniteOutput`：双线程门闩编排，断言无异常、无 NaN/Inf。
- 新增 `PendingConfig_WhileDisabled_EnablesOnNextRead`。

### 7.4 PlaybackChain 与 NAudioPlaybackService

- 新增 private sealed `PlaybackChain`：`Source / Equalizer / Aggregator / Volume / Output / Track / Generation / volatile StopRequested`。
- `_current` 为当前链引用；传输命令在 `_chainGate` 内读取当前链，回调侧用 `Volatile.Read(ref _current)` 判断代际，不取链闸门。
- `LoadAsync` 流程：
  1. 入口检查 `_disposed`（已释放则抛 `ObjectDisposedException`）；`await _loadGate.WaitAsync()`（异步装载闸门，串行化装载）；取得闸门后再检查一次 `_disposed`，已释放则释放闸门并抛 `ObjectDisposedException`；
  2. 在 `_chainGate` 内摘下当前链（`_current=null`，置 `StopRequested`，`SetState(Stopped)`），记录 `_loadCancelVersion`，并捕获 Volume/EQ/SpectrumConfig 配置快照，释放锁；
  3. `await Task.Run(() => { old?.Dispose(); return BuildChain(track, configSnapshot); })`——旧链拆除与新链构建都在锁外、线程池上；`BuildChain` 内部分构建失败时必须释放已创建的 source/output/aggregator；
  4. 在 `_chainGate` 内检查 `_disposed` 与 `_loadCancelVersion`：若已 Dispose 或期间发生 Unload/Dispose，则不安装，锁外 dispose 新链并返回；否则安装新链、释放锁，通过 UI 线程依次广播 `Position=0`、`Duration`、`TrackChanged`；
  5. 构建失败：广播 `PlaybackError` 并抛出；`_current` 保持 null，状态 Stopped（与现状一致）。
- 装载期间传输命令看到 `_current=null`，按无链语义 no-op；现有调用方都是 `await LoadAsync(...)` 后再 `Play()`，不受影响。这是明示的行为变化（旧实现会让 UI 线程阻塞到装载结束）。
- `Play`/`Pause`/`Stop`/`Seek`：在 `_chainGate` 内操作当前链；`Stop` 先置 `StopRequested=true` 再调用 `Output.Stop()`、`Source.CurrentTime=0`；`Play` 清零 `StopRequested` 并递增 `_playSession`。
- `OnPlaybackStopped`：
  - 订阅时捕获 `chain`；
  - 先判断 `ReferenceEquals(Volatile.Read(ref _current), chain)`；旧链回调直接忽略；
  - 异常 → 当前链才广播 `PlaybackError` 并置 Stopped；
  - `StopRequested` → 置 Stopped，返回；
  - 否则按 200 ms 容差判断自然播完 → 广播 `TrackEnded`，再置 Stopped；
  - 读取 `chain.Source.TotalTime/CurrentTime` 仍用 try/catch 防御并发释放。
- `PlaybackChain.Dispose` 顺序：解订阅 `Aggregator.SpectrumDataReady` → 解订阅 `Output.PlaybackStopped` → `Output.Stop/Dispose`（可能阻塞）→ `Aggregator.Dispose()`（join FFT 线程）→ `Source.Dispose()`；`Equalizer`/`Volume` 无独立资源。任一步异常用 try/finally 保证后续释放。
- `Dispose()`/`Unload()`：在 `_chainGate` 内摘下链并递增 `_loadCancelVersion`，锁外 dispose；Unload 额外广播 `TrackChanged(null)`/`Duration=0`/`Position=0`/Stopped。
- `Position`/`Duration`：`Volatile.Read(ref _current)`，null 返回零；`Position` 读 `Source.CurrentTime` 用 try/catch，已释放返回零。
- `Volume`/`SpectrumConfig`/`EqualizerConfig` setter：在 `_chainGate` 内保存值并下发到当前链（EQ 走无锁发布，频谱只下发 `Enabled`）；建链读取这些字段也在 `_chainGate` 内，避免构建期读到撕裂组合。

**新增/调整测试**

- §6.3 全部确定性测试。
- `Load_WhileOldChainPlaying_OldIsDetachedBeforeBuild`：假输出记录 Stop 与假源工厂门闩顺序。
- `Load_DuringLoad_PlayIsNoOp`：明示装载期传输语义。
- 现有真实设备 5 条保留，必要时只适配新的私有链结构，不改断言语义。

### 7.5 位置轮询

- 删除 `_lastPositionEvent` 与 `DateTime` 节流。
- `Play()` 在 `_chainGate` 内递增 `long _playSession`，然后 fire-and-forget `PollPositionAsync(session)`。
- 循环：

```csharp
using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(33));
while (await timer.WaitForNextTickAsync().ConfigureAwait(false))
{
    var chain = Volatile.Read(ref _current);
    if (session != Volatile.Read(ref _playSession) || chain is null || Volatile.Read(ref _state) != PlayState.Playing)
        break;
    // 读 Source.CurrentTime，Clamp 到 Track.Duration，Post PositionChanged
}
```

- 暂停/停止/切歌/Dispose 后，循环最多在一个 tick（33 ms）内退出；不再有重复循环叠加，也不再需要双重节流。

## 8. 行为与契约变化

| 项 | 变化 | 说明 |
|---|---|---|
| 装载期传输命令 | 旧：UI 线程阻塞到装载结束；新：看到无链，no-op | 现有调用方都 `await LoadAsync` 后再 Play；GUI 走查覆盖 |
| EQ 配置生效时机 | 仍为缓冲边界，不再取锁 | 旧实现由整缓冲锁保证；新实现由缓冲开头取配置保证，语义等价 |
| 频谱事件内容 | 仍是 `float[]` 快照，长度 `BarCount` | 不改成 `ReadOnlyMemory`；每帧新数组，契约不变 |
| `SpectrumConfig.FftSize/BarCount` | 运行时修改被忽略 | 与现状一致；构造时校验，文档写明 |
| `SampleAggregator` | 增加 `IDisposable` 与后台线程 | 公开类型新增接口；调用方为 `PlaybackChain.Dispose` |
| `NAudioPlaybackService` | 公开无参构造不变；新增 internal 构造 | 生产/DI 路径不变 |
| 测试计数 | 新增默认测试约 18–22 条 | 最终以实跑为准，不预写总数 |

## 9. 批次与提交边界

| 批次 | 内容 | 门禁 | 回滚 |
|---|---|---|---|
| 0 | AudioBench + 基线产物 + 内部工厂/假实现/确定性测试 | 门禁 1/2；AudioBench 基线 | 独立工具与测试，可整体保留 |
| 1a | 频谱重叠缺陷红测试 + 修复 + 窗/bin 预计算（仍在音频线程） | 门禁 1/2 + 新 DSP 测试 | 单独回退，保留红测试 |
| 1b | FFT 离线程 + 环形缓冲 + `IDisposable` | 门禁 1/2 + 新 DSP/线程测试 | 回退 1b 保留 1a |
| 2 | EQ 无锁配置下发 + 循环重排 | 门禁 1/2 + EQ 等价/并发测试 | 回退到 `lock` 版本；循环重排可单独回退 |
| 3 | `PlaybackChain` + 装载/回调/轮询重构 | 门禁 1/2 + §6.3 全部确定性测试 | 回退整批，恢复共享字段版本 |
| 4 | 验收文档 + PROJECT/COUPLING 同步 + before/after 报告 | `-Full -Audio` | 仅文档 |

- 每批独立提交，提交信息用英文 `refactor(audio): ...` / `perf(audio): ...` / `fix(audio): ...`。
- 高风险项（1b、3）在后；每批失败只回退本批，不牵连前批。

## 10. 验收

### 10.1 流程

1. 批次 0 完成，基线产物提交；把关键读数回报用户。
2. 用户确认 §10.2 的目标值后，才进入批次 1a。
3. 每批跑门禁 1/2；批次 4 跑 `verify-gates.ps1 -Full -Audio`。
4. 最终 GUI 走查 + 数据还原 + 独立复核 + before/after AudioBench 对照。

### 10.2 性能目标（2026-11-13 用户确认，激进档）

基线（`docs/PHASE30-AUDIO-BASELINE.md`，`06cd9db`，48 kHz/2ch/480 帧，5×10 音频秒）：

- `full-chain` CPU 7.885 ms/音频秒、p99 0.337 ms、音频线程分配 17,824 B/轮；
- `aggregator` CPU 3.111 ms/音频秒（FFT/窗/分桶在音频线程）；
- `eq-bass-boost` 4.740 ms/音频秒；`full-chain − aggregator = 4.774 ms` 与之一致；
- `eq-flat` 6.251 ms/音频秒受冷启动噪声影响偏高，不作为 EQ 成本主证据。

确认目标：

- 音频线程 `Read` 的 CPU/音频秒 **≤ 3.94 ms**（较 `full-chain` 7.885 ms 降幅 ≥ 50%）；
- 音频线程 `Read` p99 **≤ 0.15 ms**；
- 音频线程稳态分配 **0 B/轮**（频谱快照分配移到 FFT 线程，不计入音频线程）；
- FFT 线程每帧成本 **< 2 ms**，帧率 **≥ 10 Hz**（44.1 kHz / 8192 / 50% hop 的理论值约 10.8 Hz）；
- 若 EQ 标量重排后仍差一点达到 CPU 目标，启用 SIMD/Vector 备用手段，并先通过等价性测试；不得为达标降低功能或跳过门禁。

验收报告逐项对照；未达目标时先报告读数与原因，不擅自降低目标。

### 10.3 门禁与独立核对

- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-gates.ps1 -Full -Audio`，末行 `all requested steps passed.`。
- 默认测试（`Category!=AudioDevice`）+ 5 条真实音频；计数以实跑为准。
- 确定性测试在默认门禁内，且不依赖 sleep 窗口。
- AudioBench before/after：同一命令、同一机器、Release；报告 p50/p95/p99、CPU/音频秒、分配。
- Core WPF-free grep 零命中；`#pragma warning disable`/`NoWarn` 零命中；耦合审计 `0 cycles / 0 violations`；输出目录为 `net11.0-windows`；发布命令仍可产出。
- Rider 红线重新观察（不为 IDE 诊断改产品代码）。

### 10.4 GUI 走查（ComputerUse + 主代理复核）

- 合成库 + 中文路径 + GBK 歌单，沿用 Phase 29 的备份/SHA-256 还原规范。
- 关键路径：播放/暂停、进度单击与拖动、上一首/下一首、自然播完自动推进、频谱可见且随播放变化、EQ 启用/调段/保存/取消（**人工听感检查；不要期待"拖动过程无爆音"**）、切歌后频谱继续、关闭落盘/重启续播、关闭中刷新。
- **2026-11-13 校正**：原稿把"拖动过程无爆音"写成预期，但 `SetPeakingEq` 会清零滤波状态（见 §4.2/§7.3 校正），EQ 拖动在换系数时可能出现复位型爆音。Phase 30 不修这条（属后续候选），GUI 只记录人工听感（有/无、可复现与否），**不作为本阶段验收的通过条件**；也不能因为听到爆音就判定 Phase 30 回归。
- 频谱正确性以 DSP 测试为主；GUI 只验"可见、不崩、随音频变化"，不做像素级频率断言。

### 10.5 验收文档

- `docs/PHASE30-ACCEPTANCE.md`：门禁读数、测试计数、AudioBench 对照、GUI 结果、数据 SHA、已知边界、结论。
- 同步 `docs/PROJECT.md` / `docs/COUPLING.md` 的当前阶段与音频路径事实；历史验收不回改。

## 11. 风险、回滚与已知边界

| 风险 | 影响 | 缓解 |
|---|---|---|
| FFT 线程 join 卡住 | 关闭/切歌挂起 | 输出先停再 dispose 聚合器；后台线程 + 2 s join 上限；确定性 Dispose 测试 |
| 环形缓冲内存序/覆盖 | 频谱错帧或越界 | SPSC + Volatile 索引 + 容量 4×FftSize + 落后丢帧；压力测试 |
| EQ 配置发布可见性 | 配置不生效/撕裂 | 不可变 config + Volatile/Interlocked；滤波器状态仅音频线程写；并发测试 |
| 链代际/回调竞态 | 旧链误发 TrackEnded/错误 | 订阅捕获链 + 当前链判定 + 解订阅早于销毁；确定性测试 |
| 装载期传输 no-op | 用户操作丢失 | 现有调用方 await；GUI 覆盖双击/切歌；文档明示 |
| 频谱听感/视觉变化 | 用户感知差异 | 分桶/归一化保持；首帧等价测试；GUI 走查 |
| 基线噪声 | 目标失真 | Release、固定参数、warmup、多轮取中位；不作为 CI 门槛 |
| WinUI 仅编译 | 运行时未覆盖 | 壳冻结；明示边界 |
| AudioBench 误入产品 | 依赖/体积 | 独立 tools 工程、不进 slnf、不引 NuGet |

- **回滚**：按批次独立提交；1b/3 失败即回退本批；不改数据/schema，无迁移。
- **已知边界**：
  - 真实设备下无 underrun/回调抖动计数器；性能以合成链 + GUI 走查为准。
  - EQ 听感只做人工走查，不做客观音质断言。
  - 频谱视觉正确性由 DSP 测试钉住；GUI 不做像素级断言。
  - WinUI 运行时行为不覆盖。
  - `StubAudioOutputFactory` 名称保留历史含义；改名不在本阶段。
  - **EQ 滤波状态不跨配置更新保持（2026-11-13 校正）**：NAudio 3.3.0 的 `SetPeakingEq` 清零 `x1/x2/y1/y2`（独立探针 0.5 → 0），Phase 14 起的"不清状态"注释与设计描述均为误记。Phase 30 保留"缓冲边界应用"与"不重建滤波器对象"，但**不保留**跨缓冲滤波状态；拖动 EQ 时的复位型爆音属既有行为，状态保持/交叉淡化列为后续候选，不在 Phase 30 范围（本阶段非目标：不改自定义 DSP 公式）。对应测试是 `Update_AppliedAtBufferBoundary_MatchesReferenceFilterBank`（对参考滤波器组逐样本比对），不是"状态连续"断言。
  - 音频线程零分配是"零**产品**分配"：AudioBench 的 `thread alloc` 地板恒为 40 B/run（`eq-off` 旁路、全部场景与基线都一致），不计入验收口径；`process alloc` 另含 FFT 分析线程的每帧 32-float 快照。

## 12. 待确认项

- 无。§10.2 的数值目标已于 2026-11-13 由用户确认（激进档）。

## 附录 A：频谱缺陷探针（2026-11-13，一次性临时工程，未入仓）

- 目的：确认 NAudio 3.3.0 `FastFourierTransform.FFT` 是否就地改写、输出量级，以及 FFT/窗/EQ 的成本量级。
- 方法：`net11.0` 控制台 + `NAudio.Core 3.3.0`，`Release`，单位脉冲/随机缓冲/固定正弦。
- 结果：
  - `Complex` 字段为 `Single X, Single Y`；
  - 单位脉冲 FFT 后 `[0]=[1]=[4096]=1.221E-004`（1/8192 归一化），证明就地输出频域；
  - FFT 8192 ≈ 0.160–0.176 ms/帧；
  - Hann 窗重算 8192 ≈ 0.052–0.055 ms/帧；预计算后应用 ≈ 0.002 ms/帧；
  - 10 段立体声 BiQuad 48 kHz ≈ 9.83–10.16 ms/音频秒。
- 正式基线以 `AudioBench` 为准；本附录只用于设计决策与缺陷定性。

## 附录 B：参考

- Phase 28 设计 §9.3（移交 Phase 30 的安全网缺口与不可动纪律）：`docs/superpowers/specs/2026-11-12-d-player-phase28-net11-upgrade-design.md`
- Phase 29 验收 §7（音频实时路径零改动、WinUI 仅编译）：`docs/PHASE29-ACCEPTANCE.md`
- 音频路径风险登记：`docs/PROJECT.md` §9（播放链串行化、停止意图、频谱跨线程、EQ 线程安全）
- 耦合纪律：`docs/COUPLING.md` §5/§7
- 构建/测试口径：`docs/README.md`、`tools/verify-gates.ps1`
- GUI 证据规范：项目记忆 `d-player-gui-computeruse`（备份 + SHA-256 还原）
