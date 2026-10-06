# Phase 19 依赖迁移（NAudio 3 新 API + xunit v3）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把代码结构迁移到已升级依赖的新 API 上——NAudio 的播放输出从 legacy 的 `WasapiOut` 换成 `WasapiPlayerBuilder`/`WasapiPlayer` 并把 meta 包收窄为 `NAudio.Core` + `NAudio.Wasapi`；测试栈从 xunit v2 迁到 xunit.v3 并同时打通 MTP 与 VSTest 两条运行路径。**不改任何产品行为**。 —— 落地校正（Task 3 实测）："MTP 与 VSTest 两条运行路径"不成立，交付态只有一个 runner（MTP），到达它的命令有两条（`dotnet run --project Tests/D-player.Tests.csproj -c Debug` 与 `dotnet test D-player.sln -c Debug`，后者经仓根 `global.json` 路由）；详见本文件 Global Constraints 与 Task 3 Step 5 的注记、设计稿文末勘误第 3 条。

**Architecture:** 迁移是"替换实现类 + 重写注释理由"，不是重构：`NAudioPlaybackService` 的播放链结构、`_chainGate` 串行化、`_stopRequested` 停止意图、`OnPlaybackStopped` 的锁纪律**全部保留**，只把两处论证从"NAudio 2.x 内部细节"改写为"我们自己的不变量"。测试侧零代码改动（无 `Xunit.Abstractions`/`IAsyncLifetime` 用法；此句应读作"零**语义**改动"——v3 分析器的 8 处 `xUnit1051` 是靠改调用点满足 0 警告门禁的，见 Task 3 Step 3 的注记），只是工程形态（实验工程变可执行程序）与 runner 配置变化。

**Tech Stack:** .NET 10（`net10.0-windows`）· NAudio 3.1.0（Core + Wasapi）· xunit.v3 4.0.1 · xunit.runner.visualstudio 4.0.0 · Microsoft.NET.Test.Sdk 18.10.1 · NSubstitute 6.2.0 · Microsoft.Testing.Platform + VSTest 双路径 —— 落地校正（Task 3 实测）："双路径"不成立，runner 只有一个（Microsoft.Testing.Platform），到达它的命令有两条（`dotnet run --project Tests/D-player.Tests.csproj -c Debug` 与 `dotnet test D-player.sln -c Debug`，后者经仓根 `global.json` 路由）；VSTest 在 xunit.v3 4.0.1 + MTP 2.4.0 + .NET 10 SDK 上不可达，`xunit.runner.visualstudio` / `Microsoft.NET.Test.Sdk` 属兼容性保留 —— 详见 Task 3 Step 5 的注记

**Spec:** [`docs/superpowers/specs/2026-10-06-d-player-phase19-dependency-migration-design.md`](../specs/2026-10-06-d-player-phase19-dependency-migration-design.md)

**验证基线：** 起点 `7d42c08`（工作树干净）；每 Task 后 `dotnet build D-player.sln -c Debug --nologo -v q` **0 错误 0 警告** + 全量 **161** 条测试绿（当前基线）。

## Global Constraints

- 版本钉死：`NAudio.Core` / `NAudio.Wasapi` = `3.1.0`；`xunit.v3` = `4.0.1`；`xunit.runner.visualstudio` = `4.0.0`（保留）；`Microsoft.NET.Test.Sdk` = `18.10.1`（保留）；`NSubstitute` = `6.2.0`；`coverlet.collector` = `10.1.0`。
- **不改产品行为**：不引入 `WasapiPlayer` 的新能力（零拷贝、MMCSS 线程优先级、`IAudioClient3` 低延迟、`WithCategory`/`WithRawMode`/`WithMmcsThreadPriority`）；不动播放链其余结构（`EqualizerSampleProvider`/`SampleAggregator`/`VolumeSampleProvider`）。
- **保留 `IAudioOutputFactory` + `StubAudioOutputFactory` 原样**（含其 `WasapiOut` 用法与那处 pragma）——`COUPLING.md §7` 明确要求保留该"未来多后端"接缝。**迁移完成后，仓库里除该文件外不得再出现 `#pragma warning disable CS0618`。** —— 落地校正（收尾）：该约束只对主体阶段有效；收尾阶段经用户拍板把该工厂也迁到 `WasapiPlayer`（零契约改动），故仓库 pragma 数为 **0** 而非 1。下方 Expected 里的"只剩 1 处"按此理解。
- 三条纪律原样保留：**持 `_chainGate` 期间不得 await**；**`OnPlaybackStopped`（播放线程回调）不得取 `_chainGate`**；**任何主动停止必须在调用 NAudio 停止之前置 `_stopRequested = true`**。
- `dotnet test D-player.sln -c Debug` **必须继续可用**（双 runner 的硬要求）。落地校正：命令确实继续可用，但背后只有 **一个** MTP runner（"双 runner"已证伪，见 Task 3 Step 5 注记）。另记一条命令写法纪律：本文件 Task 1/2/3/5 里的 `dotnet test … --nologo` **不可照抄**——迁移后 MTP 不识别该参数，加上后一条测试都不跑而摘要打印 `成功: 0`（Task 1/2 执行时仍是 xunit v2 + VSTest，该写法当时可用且确实跑出 161 绿；Task 5 验收务必按 Task 5 Step 1 的警告用不带旗标的形式）。
- 行尾纪律：仓库 `core.autocrlf=true`；**Edit 工具会把 CRLF 静默转 LF** → 改完用 `git ls-files --eol <file>` 核对，`w/lf` 就 `unix2dos`；**禁用 `sed -i`**。
- 提交风格：`type(scope): subject` + 要点式 body；每个 Task 至少一次提交。
- 联网：`xunit.v3` 不在本机 NuGet 缓存（Task 3 需要一次 restore；nuget.org 可用，github.com 可能抖动——重试即可）。
- 注释纪律：只在"为什么"不显然处写注释；重写的论证注释必须**不依赖 NAudio 2.x 的实现细节**。

---

## 文件结构

| 文件 | 责任 | Task |
|------|------|------|
| `D-player.csproj`（改） | 包引用：meta `NAudio` → `NAudio.Core` + `NAudio.Wasapi` | 1 |
| `Services/NAudioPlaybackService.cs`（改） | 输出类换 `WasapiPlayer`；字段类型收窄；Init 桥接；两处论证注释重写 | 2 |
| `Tests/D-player.Tests.csproj`（改） | `xunit` → `xunit.v3`；`OutputType=Exe`；MTP 双属性 | 3 |
| `Tests/Services/NAudioPlaybackServiceConcurrencyTests.cs`（可能改） | 并行度兜底：`[Collection("AudioDevice")]`（仅当实测抖动） | 3 |
| `Tests/Services/NAudioPlaybackServiceStopSemanticsTests.cs`（可能改） | 同上 | 3 |
| `Tests/AssemblyInfo.cs`（可能新建） | 兜底：`[assembly: CollectionBehavior(DisableTestParallelization = true)]`（仅当集合级仍抖动） | 3 |
| `README.md` / `docs/PROJECT.md` / `docs/COUPLING.md`（改） | 测试命令补 MTP、计数修正 155→161、§9 陷阱条目改写、§5 两条论证改写 + 测试栈条目、§7 补 ❌ | 4 |
| 本计划文件（改） | 勾选 | 4 |

---

### Task 1: NAudio 包引用收窄（meta → Core + Wasapi）

**Files:**
- Modify: `D-player.csproj`（PackageReference 组，第 29-36 行附近）

**Interfaces:**
- Consumes: 现有代码对 NAudio 的类型使用（`WaveFormat`/`ISampleProvider`/`IWaveProvider`/`VolumeSampleProvider`/`BiQuadFilter`/`FastFourierTransform`/`WaveExtensionMethods` 来自 Core；`WasapiOut`/`MediaFoundationReader`/`AudioClientShareMode` 来自 Wasapi）
- Produces: 收窄后的依赖图（供 Task 5 的 `dotnet list package --include-transitive` 验收）

- [x] **Step 1: 替换包引用**

把 `D-player.csproj` 的这一行：

```xml
        <PackageReference Include="NAudio" Version="3.1.0" />
```

改为两行：

```xml
        <PackageReference Include="NAudio.Core" Version="3.1.0" />
        <PackageReference Include="NAudio.Wasapi" Version="3.1.0" />
```

（其余 `PackageReference` 不动。）

- [x] **Step 2: restore + 构建**

Run: `dotnet restore D-player.sln --nologo -v q && dotnet build D-player.sln -c Debug --nologo -v q`
Expected: 0 错误 0 警告。

若出现 `CS0246`（找不到类型/命名空间）：该类型确实属于别的子包（最可能是 `NAudio.Dmo`——`WasapiOut` 的 exclusive 路径内部用到 `ResamplerDmoStream`）。此时把**最小**的那个子包加回（例如 `<PackageReference Include="NAudio.Dmo" Version="3.1.0" />`），并在 Task 4 的依赖清单里如实写明例外。

- [x] **Step 3: 全量测试**

Run: `dotnet test D-player.sln -c Debug --nologo -v q`
Expected: **161 通过 / 0 失败**。

- [x] **Step 4: 核验依赖图**

Run: `dotnet list D-player.csproj package --include-transitive`
Expected: 出现 `NAudio.Core` / `NAudio.Wasapi`（若 Step 2 走了兜底则还有 `NAudio.Dmo`）；**不应出现** `NAudio.WinForms` / `NAudio.Midi` / `NAudio.Asio` / `NAudio.WinMM`。

- [x] **Step 5: 提交**

```bash
git add D-player.csproj
git commit -m "chore(deps): narrow NAudio to the two sub-packages we actually use

The meta package NAudio 3.1.0 pulls in Core, Wasapi, Dmo, WinMM, WinForms,
Midi and Asio; the code only uses Core (WaveFormat, ISampleProvider,
IWaveProvider, DSP helpers) and Wasapi (WasapiOut, MediaFoundationReader,
AudioClientShareMode), and NAudio.Wasapi itself depends only on Core. For a
WPF app this also keeps the WinForms assembly out of the graph.

Build 0 errors / 0 warnings, 161/161 tests green."
```

---

### Task 2: `WasapiOut` → `WasapiPlayer`（含两处论证重写）

**Files:**
- Modify: `Services/NAudioPlaybackService.cs`

**Interfaces:**
- Consumes: `NAudio.Wave.WasapiPlayerBuilder`（`.WithSharedMode()` / `.WithEventSync()` / `.WithLatency(int)` / `.Build()`）与 `NAudio.Wave.WasapiPlayer`（`Init(IWaveProvider)` / `Play()` / `Pause()` / `Stop()` / `Dispose()` / `PlaybackState` / `PlaybackStopped`）；`WaveExtensionMethods.ToWaveProvider(this ISampleProvider)` —— 落地校正：`ToWaveProvider()` 最终**没用上**，实际调用绑的是 `WaveExtensionMethods.Init(IWavePlayer, ISampleProvider)`（见 Step 2 注记）
- Produces: `private WasapiPlayer? _wavePlayer;` 字段（后续所有成员沿用；`_chainGate` / `_stopRequested` 语义不变）——Task 4 的文档要引用本文件的新注释文本 —— 落地校正是 `private IWavePlayer? _wavePlayer;`（`Services/NAudioPlaybackService.cs:23`），见 Step 1 注记

- [x] **Step 1: 字段类型收窄** —— 未照此执行，其前提已被反射证伪：`typeof(WasapiPlayer).GetInterfaces()` = `IWavePlayer, IDisposable, IWavePosition, IWaveLatency, IAsyncDisposable`（`IWavePlayer` 自己声明 `Init`），既已实现该接口就无需收窄。落地字段仍是 `private IWavePlayer? _wavePlayer;`（`Services/NAudioPlaybackService.cs:23`，纠正见 `50d9ba3`）

把字段声明：

```csharp
    private IWavePlayer? _wavePlayer;
```

改为：

```csharp
    private WasapiPlayer? _wavePlayer;
```

（`using NAudio.Wave;` 已在文件顶部，无需新增 using。若编译器报告 `IWavePlayer` 不再被引用等，按提示清理，但**先别删任何注释**。）上面两块代码（`IWavePlayer?` → `WasapiPlayer?`）是草稿原文、未落地：字段至今是 `IWavePlayer?`（`Services/NAudioPlaybackService.cs:23`）。

- [x] **Step 2: 替换构造与 Init** —— 一半照此落地、一半被证伪：`WasapiPlayer` 无公开构造函数（`typeof(WasapiPlayer).GetConstructors()` 为空），所以 builder 链（`WithSharedMode()/WithEventSync()/WithLatency(100)/Build()`）确属必需、已落地；但"`Init` 只收 `IWaveProvider` 故需 `ToWaveProvider()` 桥接"不成立——`IWavePlayer` 声明 `Init(IWaveProvider)`，而 `WaveExtensionMethods` 另有 `Init(IWavePlayer, ISampleProvider)` 扩展，落地代码是 `_wavePlayer.Init(_volumeProvider);`（`Services/NAudioPlaybackService.cs:162`），下方 after 块里"Init 只收 IWaveProvider，故用 ToWaveProvider() 把采样链桥接"那句已就地换成"builder 是唯一入口 + 仍按 `IWavePlayer` 使用"（`Services/NAudioPlaybackService.cs:154-156`）

把 `LoadAsync` 内的这一段：

```csharp
                    // WasapiOut 在 NAudio 3 被标记过时（建议 WasapiPlayerBuilder → WasapiPlayer）。
                    // 迁移会改变播放输出的语义（同步模式、teardown 行为），需要独立验证，故此处有意保留。
#pragma warning disable CS0618
                    _wavePlayer = new WasapiOut(AudioClientShareMode.Shared, 100);
#pragma warning restore CS0618
                    _wavePlayer.Init(_volumeProvider);
```

替换为：

```csharp
                    // NAudio 3 的输出入口：builder → WasapiPlayer（WasapiOut 已降级为 legacy placeholder）。
                    // WithEventSync 对齐原 WasapiOut(Shared, 100) 里 useEventSync=true 的语义；Init 只收
                    // IWaveProvider，故用 ToWaveProvider() 把采样链桥接成 WaveProvider。
                    _wavePlayer = new WasapiPlayerBuilder()
                        .WithSharedMode()
                        .WithEventSync()
                        .WithLatency(100)
                        .Build();
                    _wavePlayer.Init(_volumeProvider.ToWaveProvider());
```

**实施时确认（若此步编译失败）：**
- `ToWaveProvider()` 不可用 → 先跑 `grep -c "ToWaveProvider" ~/.nuget/packages/naudio.core/3.1.0/lib/net9.0/NAudio.Core.xml` 确认扩展存在；仍失败则改用显式包装 `_wavePlayer.Init(new SampleToWaveProvider(_volumeProvider))`，并在提交信息里记录。
- `WithEventSync()` 不可用（API 改了名）→ 用 `WithPollingSync()`，并在提交信息与 Task 4 的 COUPLING 条目里记录语义差异。

- [x] **Step 3: 构建并确认 pragma 减少**

Run: `dotnet build D-player.sln -c Debug --nologo -v q`
Expected: 0 错误 0 警告。

Run: `grep -rn "pragma warning disable CS0618" --include=*.cs . | grep -v "/obj/\|/bin/"`
Expected: **只剩 1 处**（`Services/StubAudioOutputFactory.cs`，Task 1-3 范围内刻意保留的那个）。── 收尾后为 **0 处**（该文件随后也迁到 `WasapiPlayer`，见 Global Constraints 注记）。

- [x] **Step 4: 重写两处论证注释（本 Task 的核心）**

**(a) `_chainGate` 的 XML 注释**——把现在的：

```csharp
    /// <summary>
    /// 播放链生命周期闸门。LoadAsync 在线程池上重建整条链，而 Unload/Dispose/传输命令
    /// 可能来自 UI 线程；没有串行化时，后一次重建的 DisposePlayback 会释放前一次
    /// 正在 Init 的 WasapiOut 实例（audioClient 被置空 / COM 包装分离），异常从
    /// NAudio 内部抛出并冒到 async void 事件处理器 —— 表现为"播完一首歌后进程崩溃"。
    ///
    /// 纪律：持锁期间不得 await；不得阻塞等待 UI 线程（事件一律 Post 异步派发）。
    /// NAudio 播放线程上的 OnPlaybackStopped **不能**取此锁：持锁方可能正阻塞在
    /// _wavePlayer.Stop() 的 Join(playThread) 上，取锁会立即死锁。
    /// </summary>
    private readonly object _chainGate = new();
```

替换为（去掉对 NAudio 2.x 内部细节的依赖）：

```csharp
    /// <summary>
    /// 播放链生命周期闸门：把"重建/拆除整条播放链"这件事串行化。
    /// LoadAsync 在线程池上重建链，而 Unload/Dispose/传输命令可能来自 UI 线程；
    /// 没有串行化时，后一次重建会释放前一次正在使用或正在构建的链（reader / provider /
    /// wavePlayer 三个共享字段被交错读写），异常从链内部抛出并冒到 async void 事件处理器
    /// —— 表现为"播完一首歌后进程崩溃"。这与 NAudio 内部是否防护无关：NAudio 3 的
    /// guarded dispose 只保证它自己的对象不被双重释放，管不到我们的共享字段。
    ///
    /// 纪律：持锁期间不得 await；不得阻塞等待 UI 线程（事件一律 Post 异步派发）；
    /// 播放线程回调 OnPlaybackStopped **不能**取此锁——持锁方可能正阻塞在输出类的
    /// Stop() 上等待播放线程退出，回调里取锁即死锁。
    /// </summary>
    private readonly object _chainGate = new();
```

**(b) `_stopRequested` 的 XML 注释**——把现在的：

```csharp
    /// <summary>
    /// 「本次停止是我们主动发起的」标记：Stop() / DisposePlayback() 在调用 NAudio 停止前
    /// 置位，Play() 开始新的播放会话时清零。
    ///
    /// 为什么需要它：NAudio 的播放线程退出时会**同步**回调 PlaybackStopped（WasapiOut 在线程池
    /// 线程上构造，SynchronizationContext 为 null），而 Stop() 内部 Join 该线程 —— 于是
    /// "用户按停止"与"曲目自然播完"以同一个回调到达。仅凭"播放头距 TotalTime 在容差内"
    /// 无法区分：用户在曲尾 200ms 内按停止会被误判为播完，向 VM 发出 TrackEnded 并自动推进下一首。
    /// </summary>
    private volatile bool _stopRequested;
```

替换为：

```csharp
    /// <summary>
    /// 「本次停止是我们主动发起的」标记：Stop() / DisposePlayback() 在调用输出类的停止方法前
    /// 置位，Play() 开始新的播放会话时清零。
    ///
    /// 为什么需要它："用户按停止"与"曲目自然播完"都只表现为一次 PlaybackStopped——输出类不会
    /// 告诉我们是谁发起的，而仅凭"播放头距 TotalTime 在容差内"无法区分（曲尾 200ms 内按停止
    /// 会被误判为播完，向 VM 发出 TrackEnded 并自动推进下一首）。这个不确定性不随实现变化：
    /// 无论回调是同步还是经 SynchronizationContext 异步派发、无论位置是否已被归零，两条路径
    /// 的信号都相同，所以意图必须由发起方显式标记。
    /// </summary>
    private volatile bool _stopRequested;
```

**(c) `Stop()` 里的置位说明**——把这一行注释：

```csharp
            _stopRequested = true; // 必须在 _wavePlayer.Stop() 之前置位：Join 期间播放线程会同步回调
```

改为：

```csharp
            _stopRequested = true; // 必须在调用输出类的停止之前置位：回调可能在其内部同步发生
```

**(d) `OnPlaybackStopped` 里那段说明**——把：

```csharp
        // 我们主动发起的停止（Stop/DisposePlayback）也会走到这里，且可能在播放头接近曲尾时发生。
        // 停止意图由发起方显式标记 —— 少了这一步，用户在曲尾 200ms 内按停止会被误判为"自然播完"，
        // 从而在停止之后又自动推进下一首。
```

改为：

```csharp
        // 我们主动发起的停止（Stop/DisposePlayback）也会走到这里，且可能在播放头接近曲尾时发生；
        // 停止意图由发起方显式标记（见 _stopRequested）——少了这一步，用户在曲尾 200ms 内按停止
        // 会被误判为"自然播完"，停止之后又自动推进下一首。
```

**(e) `DisposePlayback()` 里的 `_stopRequested = true;` 与 `OnPlaybackStopped` 的自然播完判定块（含 try/catch 与"播放线程不取锁"的说明）保持原样。** 只确认它们措辞里不再出现 `WasapiOut` 字样；若出现（例如引用了 `WasapiOut.Stop()` 的 Join），改成中性表述"输出类的 Stop()"。

- [x] **Step 5: 音频集成测试（回归证据）**

Run: `dotnet test D-player.sln -c Debug --nologo --filter "FullyQualifiedName~NAudioPlaybackService"`
Expected: **5 通过 / 0 失败** —— 3 条并发/播完（`NAudioPlaybackServiceConcurrencyTests`）+ 2 条停止语义（`NAudioPlaybackServiceStopSemanticsTests`）。这 5 条是两处论证的直接证据：若新类破坏了"停止不冒充播完"或"生命周期串行化"，它们会红。

- [x] **Step 6: 全量测试 + 行尾核对**

Run: `dotnet test D-player.sln -c Debug --nologo -v q`
Expected: **161 通过 / 0 失败**。

Run: `git ls-files --eol Services/NAudioPlaybackService.cs`
Expected: `i/lf    w/crlf`。若为 `w/lf`：`unix2dos Services/NAudioPlaybackService.cs` 后重新 `git add`。

- [x] **Step 7: 提交** —— 下方提交正文（"…through ToWaveProvider() because it only accepts IWaveProvider. WasapiPlayer does not implement IWavePlayer, so the field type narrows with it."）是 `ce67d9e` 的原文，该两句已被 `50d9ba3` 依反射证据推翻并回退（接口清单里就有 `IWavePlayer`；`ToWaveProvider()` 已撤）。仍成立的是后半段前提：`WasapiPlayer` 无公开构造函数，builder 仍是唯一入口——但字段类型从未需要改变

```bash
git add Services/NAudioPlaybackService.cs
git commit -m "refactor(playback): move the output to WasapiPlayer and re-argue both invariants

WasapiOut is a legacy placeholder in NAudio 3; the output now comes from
WasapiPlayerBuilder (WithSharedMode + WithEventSync + WithLatency(100), the
same semantics as the old WasapiOut(Shared, 100)) and Init takes the chain
through ToWaveProvider() because it only accepts IWaveProvider. WasapiPlayer
does not implement IWavePlayer, so the field type narrows with it.

Both concurrency invariants stay, with their justifications rewritten so they
no longer lean on NAudio 2.x internals:

- The chain gate is about serializing *our* lifecycle - two overlapping
  LoadAsync calls (or an Unload landing mid-load) rebuild shared chain fields
  under each other. NAudio 3's guarded dispose protects NAudio's own objects,
  not our fields, so the gate is not redundant.
- The stop-intent flag is about the signal itself: a user stop and a natural
  end both surface as one PlaybackStopped, and position-vs-duration cannot
  tell them apart regardless of how the callback is dispatched.

Evidence: the five audio integration tests (concurrency, stop semantics,
play-to-natural-end) are green on the new class; build 0 errors / 0 warnings
and only the deliberately-kept pragma in StubAudioOutputFactory remains."
```

---

### Task 3: xunit v2 → xunit.v3（工程形态 + 双 runner + 并行度实测）—— "双 runner"已证伪：落地是一个 runner（MTP）、两条命令，见 Step 5 注记

**Files:**
- Modify: `Tests/D-player.Tests.csproj`
- Modify（仅当实测抖动）: `Tests/Services/NAudioPlaybackServiceConcurrencyTests.cs`、`Tests/Services/NAudioPlaybackServiceStopSemanticsTests.cs`
- Create（仅当集合级仍抖动）: `Tests/AssemblyInfo.cs`

**Interfaces:**
- Consumes: Task 1-2 完成后的 161 条绿基线
- Produces: `dotnet run --project Tests/D-player.Tests.csproj -c Debug`（MTP）与 `dotnet test D-player.sln -c Debug`（VSTest 路径，经 `TestingPlatformDotnetTestSupport` 也走 MTP）两条命令均可跑；并行度的最终选择（Task 4 要写进 PROJECT）—— 落地校正：两条命令均可跑 ✓，但括号里那句机制为假（路由是仓根 `global.json`，不是该属性，见 Step 5 注记）；并行度的最终选择 = level 0（沿用 v3 默认并行），已写进 `docs/PROJECT.md` §9 与 `docs/COUPLING.md` §5

- [x] **Step 1: 替换测试工程配置**

把 `Tests/D-player.Tests.csproj` 的 `<PropertyGroup>` 改为（新增三行）：

```xml
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <AssemblyName>D-player.Tests</AssemblyName>
    <RootNamespace>DPlayer.Tests</RootNamespace>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <LangVersion>latest</LangVersion>
    <OutputType>Exe</OutputType>
    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
    <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
  </PropertyGroup>
```

把 `xunit` 引用：

```xml
    <PackageReference Include="xunit" Version="2.9.3" />
```

改为：

```xml
    <PackageReference Include="xunit.v3" Version="4.0.1" />
```

（`xunit.runner.visualstudio`、`Microsoft.NET.Test.Sdk`、`NSubstitute`、`coverlet.collector` 全部原样保留。）

- [x] **Step 2: restore（需要联网）**

Run: `dotnet restore D-player.sln --nologo -v q`
Expected: 无错误。`xunit.v3 4.0.1` 不在本机缓存，此次会从 nuget.org 拉取；失败就重试（本机到 github.com 的链路不稳，nuget.org 正常）。**不要**手工放包或降级版本。

- [x] **Step 3: 构建**

Run: `dotnet build D-player.sln -c Debug --nologo -v q`
Expected: 0 错误 0 警告。测试代码预期**零改动**（无 `Xunit.Abstractions`/`ITestOutputHelper`/`IAsyncLifetime`/`IClassFixture` 用法；断言集合全在 v3 内）。（此预期已被推翻：`xunit.v3` 带入的 `xunit.analyzers` 2.1.0 用 `xUnit1051` 在 8 处报警，0 警告门禁是靠**改代码**满足的——3 个文件把 `TestContext.Current.CancellationToken` 传给既有的 `Task.Delay` / `Task.WhenAny` / `File.WriteAllTextAsync`（`Tests/Services/JsonLibraryCacheTests.cs:100`、`Tests/Services/NAudioPlaybackServiceConcurrencyTests.cs:57,76,106`、`Tests/Services/NAudioPlaybackServiceStopSemanticsTests.cs:72,74,97,99`），未用 `NoWarn`/pragma 压制，断言、延时数值、特性与测试条数一条未变。可说"零语义改动"，不可说"零改动"。）

若出现编译错误：
- 断言歧义（`CS0121`）→ 按错误处显式化重载。
- 命名空间缺失（`CS0246`，例如旧 `Xunit.Abstractions`）→ 换成 v3 的命名空间（`Xunit`）。
- **不要**为通过编译而改测试语义（断言值、测试数量）。

- [x] **Step 4: MTP 路径跑一遍**

Run: `dotnet run --project Tests/D-player.Tests.csproj -c Debug`
Expected: MTP 原生输出（不是 VSTest 的输出格式：无 "测试运行" 汇总行，而是 MTP 自己的进度/统计面板），**161 条通过 / 0 失败**。若输出看起来仍是 VSTest 格式，说明 `UseMicrosoftTestingPlatformRunner` 没生效——检查属性拼写后重跑。

- [x] **Step 5: VSTest 路径跑一遍（命令必须不变）** —— 命令未变 ✓，但抵达的**不是 VSTest**：`xunit.v3 4.0.1` 带入 Microsoft.Testing.Platform 2.4.0 并置 `IsTestingPlatformApplication=true`，MTP 2.x 在 .NET 10 SDK 上取消 VSTest 目标的重定向（原文 "Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later"）。真正让这条命令可用的是仓根 `global.json` 的 `{"test":{"runner":"Microsoft.Testing.Platform"}}`（Microsoft 为 .NET 10 SDK 记录的原生 opt-in，Ruling P8 批准的计划外新增）；`TestingPlatformDotnetTestSupport` 是 .NET 9 及更早版本的路由开关，在本 SDK 上**不参与执行路径**。下方"回退到仅 VSTest"那条也经实测无效——移除两个属性并不能恢复 `dotnet test`（该标志来自包本身），故未走回退

Run: `dotnet test D-player.sln -c Debug --nologo -v q`
Expected: **161 通过 / 0 失败**，且命令与迁移前一致（`TestingPlatformDotnetTestSupport` 让 `dotnet test` 也路由到 MTP）。落地校正：路由机制是仓根 `global.json`（见上方标题注记），不是该属性；上一行的 `--nologo` 写法在新配置下**一条测试都不跑**（实测：摘要 `成功: 0`、"运行了零个测试"、退出码 5），161 绿来自不带该旗标的 `dotnet test D-player.sln -c Debug`。

若 MTP 与 WPF 测试工程冲突（`dotnet run` 起不来 / 报宿主错误）：**回退到仅 VSTest** —— 移除 `UseMicrosoftTestingPlatformRunner` 与 `TestingPlatformDotnetTestSupport` 两个属性（保留 `OutputType=Exe` 与 `xunit.v3`），确认 `dotnet test` 仍 161 绿，并在提交信息与 Task 4 的 PROJECT 文字里如实记录"MTP 因 <具体错误> 延后"。这是 spec §8 允许的回退路径。

- [x] **Step 6: 并行度实测（5 次）**

Run（PowerShell 或 bash 循环均可，逐次记录结果）：

```bash
for i in 1 2 3 4 5; do echo "--- run $i ---"; dotnet run --project Tests/D-player.Tests.csproj -c Debug 2>&1 | tail -3; done
```

Expected: 5 次都 161 通过。记录每次的通过/失败数与总耗时（写入本次提交信息）。

- [x] **Step 7: 抖动时的处置（仅在 Step 6 出现失败时执行）** —— 未触发：Step 6 实测 xunit v3 默认并行稳定，(a) `[Collection("AudioDevice")]` 与 (b) 全局 `DisableTestParallelization` 两条均未应用（结论落在 `bf6068c` 的提交信息与 `docs/PROJECT.md` §9）

按顺序，只做到达稳定的那一级：

**(a) 集合级串行**——给两个音频测试类加上同一个集合名（v3 里同一集合的类不并行）：

```csharp
[Collection("AudioDevice")]
public sealed class NAudioPlaybackServiceConcurrencyTests : IDisposable
```

```csharp
[Collection("AudioDevice")]
public sealed class NAudioPlaybackServiceStopSemanticsTests : IDisposable
```

（两文件顶部已有 `using Xunit;` ✓ 无需新增。）改完重跑 Step 6 的 5 次。

**(b) 全局串行**——若 (a) 仍抖动，新建 `Tests/AssemblyInfo.cs`：

```csharp
using Xunit;

// 音频集成测试真实占用 WASAPI 设备；v3 的默认并行调度下出现过时序抖动，
// 故恢复 v2 时代的串行语义。代价是整套测试变慢（当前规模可忽略）。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

改完重跑 Step 6 的 5 次。

**把最终停在的那一级写进提交信息**（Task 4 要据此写 PROJECT）。

- [x] **Step 8: 行尾核对 + 提交** —— 实际提交 `bf6068c` 除下方正文外还记了三件事：新增仓根 `global.json`（`dotnet test` 的真正路由）、8 处 `xUnit1051` 改传 `TestContext.Current.CancellationToken`（故本文件"No test code changed"那句须按 Step 3 注记读）、两个音频测试类的类头注释纠错；正文里那处三选一的实测空位已按结果填为 "stable as-is"。—— 落地校正（收尾修订补齐）：下方模板正文的 "Both runners report 161/161" 与 subject 的 "with both runners enabled" 沿用草稿口径，事实只有一个 runner（MTP）与到达它的两条命令，两条命令各跑全量均为 161 通过 / 0 失败（见设计稿文末勘误第 3 条）；该措辞已随 `bf6068c` 进入历史，历史不可改写，故只在此登记，模板与本文件均按实测理解

Run: `git ls-files --eol Tests/D-player.Tests.csproj`（以及 Step 7 改过的文件）
Expected: `w/crlf`；不是就 `unix2dos` 后重新 `git add`。

```bash
git add Tests/D-player.Tests.csproj   # 视 Step 7 的改动再加上测试文件 / AssemblyInfo.cs
git commit -m "test: migrate the suite to xunit.v3 with both runners enabled

xunit.v3 4.0.1 replaces xunit 2.9.3 (same release train as the already-pinned
runner 4.0.0); the test project becomes an executable as v3 requires, and
UseMicrosoftTestingPlatformRunner + TestingPlatformDotnetTestSupport turn on
the MTP runner while keeping `dotnet test D-player.sln -c Debug` working
unchanged.

No test code changed: the suite never used Xunit.Abstractions,
ITestOutputHelper, IAsyncLifetime or the class/collection fixture interfaces,
and every assertion in use exists in v3.

Parallelism: ran the full suite five times under the v3 defaults - stable as-is
(the draft's other two options - serializing the audio integration tests via
[Collection("AudioDevice")] or disabling parallelization globally - were
neither applied) because the five audio tests hold a real
WASAPI device. Both runners report 161/161."
```

---

### Task 4: 文档与契约同步

**Files:**
- Modify: `README.md`、`docs/PROJECT.md`、`docs/COUPLING.md`、本计划文件

**Interfaces:**
- Consumes: Task 2 的新注释文本（COUPLING 条目要与之同口径）、Task 3 的并行度结论与 runner 事实
- Produces: 文档与代码一致；本计划全部勾选 —— 落地校正（收尾修订）："全部勾选"不成立：交付态是除 Task 5 Step 3（GUI 真机冒烟，无法自动化的项按 Phase 18 先例留 NOT VERIFIED 并移交用户）外全部勾选，该步刻意保持 `- [ ]`（见 Task 4 Step 7 与 Task 5 Step 3 的注记）

- [x] **Step 1: `README.md` 测试段** —— 已落地（`README.md` §测试：`:77-85`），但**未照抄**下方 after 块里"VSTest 路径（命令保持兼容；经 TestingPlatformDotnetTestSupport 同样路由到 MTP）"那句：README 写的是同一个 MTP runner 的两个入口、路由机制是仓根 `global.json`、两条命令都不得加 `--nologo`

把：

```bash
dotnet test D-player.sln -c Debug
```

```markdown
当前共 **155** 个单元测试（Models / Services / ViewModels 全覆盖 + View 层纯字符串函数 `PlaylistImportReportFormatter`；其余 View 层代码按项目惯例不做单测，由手动验收把关）。
```

改为（测试栈已迁 xunit v3 + MTP；计数修正为真实值 161）：

````markdown
```bash
# MTP（默认路径，xunit v3 原生输出）
dotnet run --project Tests/D-player.Tests.csproj -c Debug
# VSTest 路径（命令保持兼容；经 TestingPlatformDotnetTestSupport 同样路由到 MTP）
dotnet test D-player.sln -c Debug
```

当前共 **161** 个单元测试（Models / Services / ViewModels 全覆盖 + View 层纯字符串函数 `PlaylistImportReportFormatter`；其余 View 层代码按项目惯例不做单测，由手动验收把关）。
````

- [x] **Step 2: `docs/PROJECT.md` 构建/测试章节（§8）** —— 已落地（§8.2：`:902-903` 两条命令 + `:908-914` 测试栈段），但草稿的"双路径"框架未照抄：`:903` 的行内注释写的是"跑测试（同一个 runner 的另一条命令）"而非下方 after 块里的 `# 跑测试（VSTest 兼容路径）`，`:908` 写明 runner 只有一个（MTP）、两条命令是同一 runner 的两个入口（即下方那句"MTP 与 VSTest 双路径，两条命令等价"不成立），并补记 `global.json` 才是路由机制、`TestingPlatformDotnetTestSupport` 不在执行路径上、`--nologo` 禁令。（收尾修订注：本文件对 `docs/PROJECT.md` 的行号引用是当时的快照，§3 目录树与 §9 的收尾修订让行号整体有偏移，核对时以节标题定位，别按行号硬套）

在 §8.2 的命令块里补一行 MTP 跑法，并在其后加一段说明（按实际落地的 runner 情况写）：

````markdown
```bash
dotnet restore D-player.sln
dotnet build   D-player.sln -c Debug
dotnet run     --project D-player.csproj
dotnet run     --project Tests/D-player.Tests.csproj -c Debug   # 跑测试（MTP）
dotnet test    D-player.sln -c Debug                            # 跑测试（VSTest 兼容路径）
```

测试栈：xunit.v3（测试工程是**可执行程序**，`OutputType=Exe`）；MTP 与 VSTest 双路径，两条命令等价。音频集成测试（`NAudioPlaybackService*Tests`）真实占用 WASAPI 设备，并行度结论见 §9。
````

- [x] **Step 3: `docs/PROJECT.md` 目录树与计数**

- 第 164 行的 `├── Tests/  # xUnit 测试项目 (Phase 6+，共 155 个测试)` → `共 161 个测试`，并把 `xUnit` 写成 `xUnit v3`。
- 依赖清单处（NAudio 相关行）改成 `NAudio.Core` + `NAudio.Wasapi`（若 Task 1 走了兜底，把例外子包一并列出）。

- [x] **Step 4: `docs/PROJECT.md` §9 的播放链条目改写** —— 已落地（`docs/PROJECT.md:942-943`）；下方 after 块里留给 Task 3 的并行度空位已按实测取第一项填为真实结论（level 0：沿用 v3 默认并行）。—— 收尾修订追加：下方模板末尾那句"回归测试…再停止/卸载，确定性钉住"与"阻塞在输出类的 `Stop()` 上"的归因后被改写（5 条里只有 `Stop_WhenPlayheadIsAtTrackEnd_…` 会被 `_stopRequested` 的判定分支读到，`Unload_…` 因 `DisposePlayback` 先解订阅再停止而只钉住解订阅顺序；阻塞面按 `Stop()`/`Dispose()` 两者计），以 `docs/PROJECT.md` §9 现文为准，勿照抄本模板

把 Task 2 之前的播放链陷阱条目（"播放链生命周期必须串行化；'停止'意图必须显式标记"那一条）里所有 **NAudio 2.x 内部细节**替换为新口径，并在末尾补并行度结论。改写后的条目：

```markdown
- **播放链生命周期必须串行化；"停止"意图必须显式标记**（Phase 18 后修复，Phase 19 迁移到 WasapiPlayer）：`NAudioPlaybackService.LoadAsync` 在线程池上重建整条播放链，而 `Unload` / `Dispose` / 传输命令来自 UI 线程。缺串行化时，后一次重建会释放前一次正在使用或构建的链（`_reader` / `_volumeProvider` / `_wavePlayer` 三个共享字段被交错读写）→ 异常从链内部抛出并冒进 `async void` 事件处理器（进程崩溃）。修复是单闸门 `_chainGate`，唯一例外是 `OnPlaybackStopped`（播放线程回调）——持锁方可能正阻塞在输出类的 `Stop()` 上等待播放线程退出，回调里取锁即死锁，故其读取一律防御式。**NAudio 3 的 guarded dispose 不使这条失效**：它只保护 NAudio 自己的对象，管不到我们的共享字段。同源的第二条陷阱：用户停止与自然播完都只表现为一次 `PlaybackStopped`，输出类不告知发起方，仅凭播放头位置无法区分（曲尾 200ms 内按停止会被误判为播完并自动推进下一首）；修复是 `_stopRequested` 意图标记（`Stop`/`DisposePlayback` 置位、`Play` 清零、`OnPlaybackStopped` 命中即提前返回）——该不确定性不随实现变化，故换类后依然必要。回归测试：`NAudioPlaybackServiceConcurrencyTests` 与 `NAudioPlaybackServiceStopSemanticsTests`（后者用 `Seek` 把播放头推到距曲尾 100ms 再停止/卸载，确定性钉住"停止不得冒充播完"；`PlayToNaturalEnd_ThenAdvance` 钉住反向）。
- **音频集成测试的并行度**（Phase 19）：仓库有 5 条测试真实占用 WASAPI 设备（`NAudioPlaybackServiceConcurrencyTests` + `NAudioPlaybackServiceStopSemanticsTests`）。迁移到 xunit v3 时实测了默认并行下的稳定性；最终选择为 **level 0 —— 沿用 xunit v3 的默认并行**（草稿此处留给 Task 3 的三选一取第一项：未加 `[Collection("AudioDevice")]` 集合串行、未启用全局 `DisableTestParallelization`、也未新建 `Tests/AssemblyInfo.cs`；证据是 10 次 MTP + 3 次 `dotnet test` 每次 161 通过 / 0 失败 / 0 跳过，墙钟 5.875–6.249 s、runner 内 1.69–1.95 s）。改这两个测试类或引入新的音频测试时沿用同一约束。
```

（`<...>` 处必须填 Task 3 实测得到的真实结论，不许照抄占位。）落地校正：两处空位（本步与 Step 5 的 after 块）以及 Task 3 Step 8 提交正文里的三选一，均已按实测结论填写，本文件不再留未填写的空位。

- [x] **Step 5: `docs/COUPLING.md` §5 两条条目改写 + 新增测试栈条目** —— 已落地（`docs/COUPLING.md` §5：`:296-298` 三条改写 + `:299-300` 新增 Phase 19 子表头与测试栈条目）；下方 after 块首格的"MTP/VSTest 双路径"落地写成"单一 runner（MTP）的两个入口"，"（后者经 `TestingPlatformDotnetTestSupport` 路由到 MTP）"写成"经仓根 `global.json` 路由（删之即坏）"，并补上 `--nologo` 禁令；该行留给 Task 3 的并行度空位同样已按实测填为 level 0

把 Phase 18 之后登记的这两行：

```markdown
| 播放链生命周期（`LoadAsync` 重建 / `Unload` / `Dispose` / 传输命令）统一受 `_chainGate` 串行化；持锁期间不得 await | `NAudioPlaybackService` 全部取锁点（`LoadAsync` 的 lambda 体、`Play`/`Pause`/`Stop`/`Seek`/`Unload`/`Dispose`） | 缺串行化时，重叠的 `LoadAsync`（或并发 `Unload`）会释放另一个正在 `Init` 的 `WasapiOut` 实例 —— 异常从 NAudio 内部（`WasapiOut.Init` / provider 构造）抛出并冒进 `async void` 事件处理器（进程崩溃）。commit `cb5ca1a` |
| `OnPlaybackStopped`（NAudio 播放线程）**不得**取 `_chainGate` | `NAudioPlaybackService.OnPlaybackStopped` | 持锁方可能正阻塞在 `_wavePlayer.Stop()` 的 `Join(playThread)` 上；在播放线程回调里取同一把锁会立即死锁。该方法对 `_reader` 的读取因此是防御式的（并发拆除中的 reader 会抛） |
| `TrackEnded` 只代表"曲目自然播完"：任何主动停止（`Stop()` / `DisposePlayback()`）都必须**先**置 `_stopRequested = true`，`Play()` 开新播放会话时清零 | `NAudioPlaybackService._stopRequested`（`OnPlaybackStopped` 命中即提前返回） | NAudio 播放线程退出时**同步**回调 PlaybackStopped（WasapiOut 建在线程池线程上、捕获的 `SynchronizationContext` 为 null），而 `Stop()` 内部 Join 该线程 —— 用户按停止与自然播完走同一个回调，仅凭"播放头距 TotalTime ≤ 200ms"无法区分。漏置位 = 曲尾 200ms 内按停止被误判为播完，停止后立刻自动推进下一首 |
```

改写为（去除 NAudio 2.x 细节，口径与 `NAudioPlaybackService` 的注释一致）：

```markdown
| 播放链生命周期（`LoadAsync` 重建 / `Unload` / `Dispose` / 传输命令）统一受 `_chainGate` 串行化；持锁期间不得 await | `NAudioPlaybackService` 全部取锁点（`LoadAsync` 的 lambda 体、`Play`/`Pause`/`Stop`/`Seek`/`Unload`/`Dispose`） | 缺串行化时，重叠的 `LoadAsync`（或并发 `Unload`）会释放前一次正在使用或构建的链 —— `_reader` / `_volumeProvider` / `_wavePlayer` 三个共享字段被交错读写，异常从链内部抛出并冒进 `async void` 事件处理器（进程崩溃）。**NAudio 3 的 guarded dispose 不使这条失效**（它只保护 NAudio 自己的对象）。commit `cb5ca1a`，Phase 19 迁移到 `WasapiPlayer` 后重述 |
| `OnPlaybackStopped`（播放线程回调）**不得**取 `_chainGate` | `NAudioPlaybackService.OnPlaybackStopped` | 持锁方可能正阻塞在输出类的 `Stop()` 上等待播放线程退出；在回调里取同一把锁会立即死锁。该方法对 `_reader` 的读取因此是防御式的（并发拆除中的 reader 会抛） |
| `TrackEnded` 只代表"曲目自然播完"：任何主动停止（`Stop()` / `DisposePlayback()`）都必须**先**置 `_stopRequested = true`，`Play()` 开新播放会话时清零 | `NAudioPlaybackService._stopRequested`（`OnPlaybackStopped` 命中即提前返回） | 用户停止与自然播完都只表现为一次 `PlaybackStopped`，输出类不告知发起方；仅凭"播放头距 TotalTime ≤ 200ms"无法区分，漏置位 = 曲尾 200ms 内按停止被误判为播完并自动推进下一首。该不确定性不随实现（同步/异步派发、`WasapiOut`/`WasapiPlayer`）变化 |
| 测试栈 = xunit.v3（`OutputType=Exe`）+ MTP/VSTest 双路径；音频集成测试受并行度约束 | `Tests/D-player.Tests.csproj` + `Tests/Services/NAudioPlaybackService*Tests.cs` | 测试工程是可执行程序，`dotnet run --project Tests/…` 与 `dotnet test D-player.sln` 等价（后者经 `TestingPlatformDotnetTestSupport` 路由到 MTP）。5 条音频测试真实占用 WASAPI 设备：实测取三选一的第一项 = **xunit v3 默认并行即稳定（level 0）**——未加 `[Collection("AudioDevice")]`、未全局禁用并行、未建 `Tests/AssemblyInfo.cs`（10 次 MTP + 3 次 `dotnet test` 每次 161/0/0 跳过）——新增音频测试必须沿用同一约束 |
```

- [x] **Step 6: `docs/COUPLING.md` §7 补一条 ❌** —— 实际落地：新增"别把 `StubAudioOutputFactory` 的 `WasapiOut` 顺手迁走"一条（措辞按实现期实测结论，见设计稿文末勘误，Step 6 原文里"`WasapiPlayer` 不实现 `IWavePlayer`"那句已证伪）；"不要在输出类 `PlaybackStopped` 回调里取 `_chainGate`"与 §7 既有条目重复，故就地改写既有条目（去掉 `Join(playThread)` 这类 NAudio 2.x 细节）而不是追加重复条目

在 §7 末尾（Phase 14 那条之后、Phase 18 之后的三条之后）追加：

```markdown
- ❌ **在输出类的 `PlaybackStopped` 回调（播放线程）里取 `_chainGate`**（Phase 19 起输出类为 `WasapiPlayer`）—— 持锁方可能正阻塞在其 `Stop()` 上等待播放线程退出，取锁即死锁
- ❌ **给 `StubAudioOutputFactory` 的 `WasapiOut` 用法"顺手"迁到 `WasapiPlayer`** —— 该工厂是刻意保留的"未来多后端"接缝（见 §7 首条），其契约 `IWavePlayer` 的重设计属于那件事本身；`WasapiPlayer` 不实现 `IWavePlayer`，硬换会破坏这个抽象的语义 —— 落地校正（收尾）：此条**已作废**且其理由句（"`WasapiPlayer` 不实现 `IWavePlayer`"）**已被反射证伪**；用户拍板后该工厂已迁到 `WasapiPlayer`，`COUPLING.md §7` 的对应条目已改写成"不要重新引入 `#pragma warning disable CS0618`"。
```

- [x] **Step 7: 勾选本计划** —— Task 1–4 的步骤已全部勾选；Task 5（全量验收）的 4 个步骤保持未勾选，因为该任务尚未执行，不能提前记为完成 —— 落地校正（收尾修订）：本句写于 Task 4 执行时，其中"该任务尚未执行"已过期：Task 5 其后已执行，Step 1/2/4 已勾选并附执行记录，**Step 3（GUI 真机冒烟）刻意保持未勾选**（无法自动化的项按 Phase 18 先例标为 NOT VERIFIED 并移交用户），所以全计划的真实状态是"仅 Task 5 Step 3 一项留开"，不是"全部勾选"

把本文件所有 `- [ ]` 改成 `- [x]`（用 Edit 工具逐处改，**不要**用 `sed -i`）。改完确认：

Run: `git ls-files --eol docs/superpowers/plans/2026-10-06-d-player-phase19-dependency-migration-implementation.md`
Expected: `i/lf    w/crlf`（不是就先 `unix2dos`）。

- [x] **Step 8: 提交**

```bash
unix2dos README.md docs/PROJECT.md docs/COUPLING.md docs/superpowers/plans/2026-10-06-d-player-phase19-dependency-migration-implementation.md
git add README.md docs/PROJECT.md docs/COUPLING.md docs/superpowers/plans/
git commit -m "docs: sync Phase 19 into PROJECT/COUPLING/README

- The two playback invariants are re-stated without NAudio 2.x internals
  (the chain gate guards our own shared fields; the stop-intent flag guards
  a signal that is indistinguishable no matter who dispatches it), matching
  the rewritten comments in NAudioPlaybackService
- COUPLING §5 gains the test-stack entry (xunit.v3, dual runners, the audio
  tests' parallelism constraint) and §7 two new don't-dos
- README/PROJECT: MTP and VSTest commands, the test project is an exe, test
  count corrected 155 -> 161, dependency list trimmed to NAudio.Core/Wasapi"
```

---

### Task 5: 全量验收

**Files:**
- 无代码改动（纯验证）；若发现缺陷则回到对应 Task 修复

**Interfaces:**
- Consumes: Task 1-4 的全部产物
- Produces: 验收证据（命令输出 + GUI 结论）

- [x] **Step 1: 构建与两条测试路径** —— **警告（执行前必读，勿照抄本步命令原文）**：本步 `dotnet test` 命令里的 `--nologo` 在当前配置下会让 `dotnet test` **一条测试都不跑**，而摘要打印 `成功: 0`（MTP 不识别该参数，把它当未知选项拒收，退出码 5）——只扫一眼汇总行会误读成"全绿"。验收一律用 `dotnet test D-player.sln -c Debug`（要安静就只加 `-v q`），并核对摘要里确实出现 `总计: 161`；`--filter "FullyQualifiedName~X"` 仍可用。`dotnet build … --nologo` 与 `dotnet run --project Tests/…` 不受影响

Run: `dotnet build D-player.sln -c Debug --nologo -v q`
Expected: 0 错误 **0 警告**。

Run: `dotnet test D-player.sln -c Debug --nologo -v q` → **161 通过 / 0 失败**。（草稿原文如此，其中 `--nologo` 已不可用：实测 0 条测试、摘要 `成功: 0`、退出码 5。执行时改用 `dotnet test D-player.sln -c Debug -v q`，并核对摘要确实出现 `总计: 161`。）
Run: `dotnet run --project Tests/D-player.Tests.csproj -c Debug` → **161 通过 / 0 失败**。

> **执行记录**：已执行。`dotnet build -c Debug --nologo -v q` 增量与 `--no-incremental` 各跑一次，均为 `0 个警告 / 0 个错误`；`dotnet test D-player.sln -c Debug -v q`（不带 `--nologo`）摘要 `总计: 161 / 失败: 0 / 成功: 161`；`dotnet run --project Tests/D-player.Tests.csproj -c Debug` 同一份摘要 `总计: 161 / 失败: 0 / 成功: 161`。`--filter "FullyQualifiedName~NAudioPlaybackServiceStopSemanticsTests"` 单独跑亦可用：`总计: 2 / 成功: 2`。

- [x] **Step 2: 依赖图与 pragma 核验**

Run: `dotnet list D-player.csproj package --include-transitive`
Expected: 有 `NAudio.Core` / `NAudio.Wasapi`；无 `NAudio.WinForms` / `Midi` / `Asio` / `WinMM`（若 Task 1 走了兜底，`NAudio.Dmo` 允许出现且需在文档里注明）。

Run: `grep -rn "pragma warning disable CS0618" --include=*.cs . | grep -v "/obj/\|/bin/"`
Expected: **只剩 `Services/StubAudioOutputFactory.cs` 一处**（刻意保留）。── 收尾后为 **0 处**（该文件随后也迁到 `WasapiPlayer`，见 Global Constraints 注记）。

Run: `grep -rn "MessageBox.Show" --include=*.cs . | grep -v "/obj/\|/bin/"`
Expected: 无输出。

> **执行记录**：已执行，三项全部符合预期。`dotnet list D-player.csproj package --include-transitive` 里 `NAudio.Core 3.1.0` / `NAudio.Wasapi 3.1.0` 在列，`NAudio.WinForms` / `NAudio.Midi` / `NAudio.Asio` / `NAudio.WinMM` / `NAudio.Dmo` 一个都没有（可传递包列表里没有任何 NAudio 条目，Task 1 未走兜底）。`pragma warning disable CS0618` 全仓只剩 `Services/StubAudioOutputFactory.cs:19` 一处（**收尾后为 0 处**，该文件随后经用户拍板迁到 `WasapiPlayer`）。`MessageBox.Show` grep 无输出（退出码 1）。另核 `grep -n "WasapiOut" Services/NAudioPlaybackService.cs` 只命中 `:154-155` 两行历史叙述（"WasapiOut 已降级为 legacy placeholder"、"对齐原 WasapiOut(Shared, 100) 里 useEventSync=true 的语义"），没有任何句子声称当前实现用 WasapiOut。

- [ ] **Step 3: GUI 真机冒烟（ComputerUse，或按惯例移交用户）**

用 `bin/Debug/net10.0-windows/D-player.exe` 起应用，逐项确认（截图留档到临时目录即可，不必入库）：

1. 播放一首 → 出声、进度条推进、频谱有柱（EQ 链与频谱链在收窄包引用后仍工作）。
2. 切歌（下一首 / 双击另一首）→ 正常切换，无异常。
3. **播完自动推进**：让一首歌播到自然结束 → 自动播下一首（`TrackEnded` 仍按预期触发）。
4. **曲尾按停止**：把播放头拖到距曲尾约 100ms 内再点停止 → 播放停止且**不**自动推进下一首（Phase 19 前的回归点）。
5. EQ 对话框开关一次 → 生效且无异常；设置对话框保存一次 → 正常。
6. 若工作站处于锁屏状态无法驱动原生输入：按 Phase 18 的先例把第 4 项（以及任何无法自动化的项）**如实标为 NOT VERIFIED 并移交用户手动确认**，不得凭空报通过。

> **执行记录（本步不整项勾选）**：工作站未锁屏，已用 `bin/Debug/net10.0-windows/D-player.exe` 起应用并经 UIA 驱动完成三项有硬证据的检查——① 播放：双击第 1 首，播放进度 0:00 → 0:15 → 0:36 → 1:05 持续推进；② 切歌：双击第 2 首，播放头归零、右侧信息面板与播放指示随之切换，无异常；③ 播完自动推进：第 2 首（3:40）自然结束后自动切到第 3 首 KILLERMOON 并继续推进到 0:39，全程进程不崩，最后点 `CloseButton` 干净退出。**保持 NOT VERIFIED 并移交用户**：第 1 项的"出声"与"频谱外观是否正确"（需人耳/人眼判断，截图只证明有柱状物在渲染）、第 4 项"曲尾 100ms 内按停止不自动推进"与进度条单击跳转（100ms 时序无法靠 UIA 轮询可靠复现）、第 5 项 EQ/设置对话框（超出本次验收给 GUI 的有界范围）。第 4 项并非无证据：它由 `NAudioPlaybackServiceStopSemanticsTests` 两条确定性单测钉住（本次单独跑 `总计: 2 / 成功: 2`，已含在 161 内），GUI 一项只是复核。

- [x] **Step 4: 收尾**

确认工作树干净（`git status --short` 无输出）、`master` 领先 `origin/master` 的提交数已记录。**不自动推送**（等用户发话）。

> **执行记录**：已执行。本步的收尾提交（`docs: record Phase 19 acceptance and advance the phase status`：勾选 Task 5 的 Step 1/2/4、补 PROJECT/COUPLING/README 的阶段状态与 HEAD pin）落定后 `git status --short` 无输出，`git status -sb` 为 `master...origin/master [ahead 7]`（验收开始时是 `[ahead 6]`）。**未推送**，等用户发话。

---

## Self-Review 记录

**1. 设计稿覆盖**

| 设计稿章节 | 实现于 |
|-----------|--------|
| §1 Goals 1a（WasapiPlayer 迁移） | Task 2 |
| §1 Goals 1b（包引用收窄） | Task 1 |
| §1 Goals 1c（两条论证重推） | Task 2 Step 4（注释）+ Task 4 Step 4/5（PROJECT/COUPLING） |
| §1 Goals xunit 2（v3 工程迁移） | Task 3 Step 1-3 |
| §1 Goals xunit 3（并行度风险） | Task 3 Step 6-7 + Task 4 Step 4/5 的记录 |
| §1 Non-Goals（不重构预留工厂、不引入新能力、不 MTP-only） | Global Constraints + Task 2 Step 3（pragma 只剩 1 处）+ Task 4 Step 6（§7 新 ❌ 条目） |
| §2 决策 2（双 runner） | Task 3 Step 1/4/5 |
| §2 决策 5（两条论证保留） | Task 2 Step 4 + Step 5 的回归证据 |
| §2 决策 6（保留预留工厂） | Task 2 Step 3 核验 + Task 4 Step 6 |
| §4.1 API 对照 | Task 2 Step 1-2 |
| §4.5 包引用收窄 + 依赖图核验 | Task 1 Step 1/4 + Task 5 Step 2 |
| §5.1 工程形态与包 | Task 3 Step 1-2 |
| §5.2 代码零改动 | Task 3 Step 3（含失败处置） |
| §5.3 并行度处置顺序 | Task 3 Step 6-7 |
| §5.4 双 runner 行为 | Task 3 Step 4-5 + Task 5 Step 1 |
| §6 文档与契约同步 | Task 4 |
| §7 验收标准 1-7 | Task 5 Step 1-3（第 3 条并行度记录落在 Task 3 Step 8 的提交信息 + Task 4 Step 4/5 的文档） |
| §8 风险（teardown 语义未知、断言歧义、MTP+WPF、联网 restore、收窄后类型缺失） | Task 2 Step 2 的"实施时确认"、Task 3 Step 3/5、Global Constraints 的联网条、Task 1 Step 2 的兜底 |

无遗漏项。注：上表"§2 决策 2（双 runner）""§5.2 代码零改动""§5.4 双 runner 行为"三行的措辞沿用设计稿原文，其前提已被实现期实测推翻（一个 runner / 两条命令；8 处 `xUnit1051` 需改代码才满足 0 警告门禁）——覆盖关系不变，事实口径见 Task 3 Step 3/5 的注记与设计稿文末勘误。

**2. 占位符扫描**：无 TODO / "补充适当的…"一类模板记号。Task 4 里那两处"按 Task 3 的实际结论填写"的空位是**有意的实测占位**：该值来自 Task 3 Step 6-7 的运行结果，Step 里已明确"不许照抄占位、必须填真实结论"，且 Task 3 Step 8 要求把结论写进提交信息供 Task 4 引用。落地校正：两处空位与 Task 3 Step 8 提交正文里的三选一，均已按实测填为真实结论（level 0：沿用 xunit v3 默认并行），本文件不再留未填写的空位。除此之外每个代码步骤都给了可直接粘贴的代码块与确切命令。

**3. 类型一致性**：`WasapiPlayer? _wavePlayer`（Task 2 Step 1 定义）在 Task 2 其余步骤与 Task 4 的文档里统一使用；`WithSharedMode()/WithEventSync()/WithLatency(int)/Build()` 与 `Init(IWaveProvider)`、`ToWaveProvider()` 均先在 Task 2 的 Interfaces 块声明、再在 Step 2 使用；`[Collection("AudioDevice")]` 字符串在 Task 3 Step 7(a) 与 Task 4 Step 5 的条目里一致；测试计数 161 在 Global Constraints、各 Task 的 Expected、Task 4 的文档改写处取值一致；`xunit.v3 4.0.1` / `runner 4.0.0` / `Test.Sdk 18.10.1` 的版本号在 Task 3 Step 1、Global Constraints 与 Task 4 Step 1/5 处一致。 —— 落地校正：本段首句的两处"统一"都不成立。`WasapiPlayer? _wavePlayer` 从未落地，也未被 Task 4 的文档采用：字段至今是 `IWavePlayer?`（`Services/NAudioPlaybackService.cs:23`），`docs/PROJECT.md:57,378-379` 与 `docs/COUPLING.md:59` 写的是"经 `WasapiPlayerBuilder` 构造、仍按 `IWavePlayer` 持有与调用"；`ToWaveProvider()` 也未进入落地代码（`Services/NAudioPlaybackService.cs:162` 是 `_wavePlayer.Init(_volumeProvider);`）。本段其余列举（builder 四法、`[Collection("AudioDevice")]` 字符串、161 计数、三处版本号）与落地一致。
