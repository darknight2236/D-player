# Phase 19：依赖迁移（NAudio 3 新 API + xunit v3）设计

> 设计日期：2026-10-06 · 对应分支：`master` · 状态：**待实现**
>
> 目标：把代码结构迁移到已升级的依赖上新版本 API —— ① NAudio 的播放输出类从 legacy 的 `WasapiOut` 换成 3.x 推荐的 `WasapiPlayerBuilder`/`WasapiPlayer`，并把包引用从 meta 包收窄到真正用到的两个子包；② 测试栈从 xunit v2 迁到 **xunit v3**，同时打通 Microsoft.Testing.Platform（MTP）与 VSTest 两条运行方式。**不改任何产品行为**：161 条测试全绿是硬门。

---

## 0. 背景与动机

`40338f5` 已把依赖升到 NAudio 3.1.0 / z440.atl.core 7.18.0 / Test.Sdk 18.10.1 / xunit 2.9.3 / xunit.runner.visualstudio 4.0.0 / NSubstitute 6.2.0 / coverlet.collector 10.1.0，并完成了 NAudio 3 的**必改项**（`ISampleProvider` 数组重载被移除 → 两个自定义 provider 改实现 `Read(Span<float>)`）。当时按最小风险原则，把 `WasapiOut` 过时警告以两处**窄范围 `#pragma warning disable CS0618`** 的方式压住，并约定"迁移另开任务"——本阶段即是该任务。

同时：`xunit.runner.visualstudio 4.0.0` 与 `Microsoft.NET.Test.Sdk 18.10.1` 已具备 xunit v3 与 MTP 的支持能力，而 xunit v2（2.9.3）是其最后一条线；本阶段一并把测试栈推到 v3。

**迁移面普查结论**（本阶段的调研成果，见 §3）：7 个升级的包中，**只有 NAudio 需要真正的代码迁移**；atldotnet / xunit（同 major）/ runner / NSubstitute / Test.Sdk / coverlet 均无需改代码。

## 1. 目标与范围

### Goals

- **NAudio 1a**：`NAudioPlaybackService` 的播放输出类从 `WasapiOut` 换成 `WasapiPlayerBuilder` + `WasapiPlayer`，去掉该文件的 pragma。
- **NAudio 1b**：`D-player.csproj` 的 `NAudio`（meta 包，拉起 Core/Wasapi/Dmo/WinMM/WinForms/Midi/Asio 七个）收窄为 `NAudio.Core` + `NAudio.Wasapi`（各 3.1.0）。
- **NAudio 1c**：在**新类上重推**播放链闸门（`_chainGate`）与停止意图标记（`_stopRequested`）两条论证，并把注释/契约改写成不依赖 NAudio 2.x 内部实现的表述。
- **xunit 2**：测试工程迁到 `xunit.v3`（工程变为可执行程序），并同时打通 MTP 与 VSTest 两种运行方式。
- **xunit 3**：处理 v3 并行策略差异带来的音频测试稳定性风险（§5.3）。
- 文档与契约同步（README 的测试命令、PROJECT 的构建/测试章节、COUPLING §5 的论证改写）。

### Non-Goals

- **不重构预留的 `IAudioOutputFactory` / `StubAudioOutputFactory`**：`COUPLING.md §7` 明确要求保留（"删了未来加多设备支持还要写回来"）。其契约是 `IWavePlayer`，而 `WasapiPlayer` 不实现该接口 —— 重新设计它的契约属于"未来多后端"那件事的一部分，本阶段只**记录**这个事实（§4.3）。
- **不改任何产品行为**：不引入 `WasapiPlayer` 的新能力（零拷贝缓冲、MMCSS 线程优先级、`IAudioClient3` 低延迟、`WithCategory`/`WithRawMode`/`WithMmcsThreadPriority`）。
- **不迁移到 MTP-only**：保留 VSTest 兼容路径，`dotnet test D-player.sln -c Debug` 必须继续可用。
- 不动 z440.atl.core（7.14–7.18 零 API 变更，纯解析修复）、NSubstitute、Test.Sdk、coverlet 的用法。
- 不动 `WasapiPlayer` 之外的播放链结构（`EqualizerSampleProvider` / `SampleAggregator` / `VolumeSampleProvider` 链路保持不变）。

## 2. 决策记录

| # | 议题 | 决定 | 理由 |
|---|------|------|------|
| 1 | 阶段范围 | NAudio 迁移 + xunit v3 迁移 | 用户拍板；普查确认 NAudio 是唯一需要真迁移的包，xunit v3 是"适配新版本能力"的自然延伸（runner 4.0.0 与 Test.Sdk 18 已就位） |
| 2 | 测试运行方式 | **两者都开**：MTP 为默认执行方式，同时用 `TestingPlatformDotnetTestSupport` 让 `dotnet test` 也走 MTP | 用户拍板；换来更丰富的输出与筛选能力，同时保住文档里既有的命令习惯 |
| 3 | `WasapiOut` vs `WasapiPlayer` | 迁移到 `WasapiPlayer`（去掉 `NAudioPlaybackService` 的 pragma） | 3.x 官方明确把 `WasapiOut` 定位为 legacy placeholder；继续依赖它等于把技术债留到 NAudio 4 被强拆 |
| 4 | 包引用 | meta `NAudio` → `NAudio.Core` + `NAudio.Wasapi` | 实测只用到这两个包的类型，且 `NAudio.Wasapi 3.1.0` 自身只依赖 `NAudio.Core`；收窄可把 WinForms/Midi/Asio/WinMM/Dmo 移出依赖图（对 WPF 应用还避免拖入 WinForms 程序集） |
| 5 | 两条并发论证 | **都保留**，只改写理由 | 见 §4.4：NAudio 3 的 guarded dispose 只保证"NAudio 内部对象不被双重释放"，不解决**我们自己的**共享字段被交错重建（闸门）与"停止/播完同一信号"（意图标记） |
| 6 | 预留输出工厂 | 保留原样（其 `WasapiOut` 用法与 pragma 不动） | COUPLING §7 的明确要求；契约重构属于未来多后端工作。**本阶段结束后仓库里仍会余留 1 处 pragma**，位置与理由写进注释与 COUPLING |
| 7 | 音频测试并行度 | 先在 v3 下实测；若出现设备争用/时序漂移，把音频集成测试收进同一 `[Collection]` 或整体禁用并行 | v3 的并行策略与 v2 不同；这 5 条测试真实占用 WASAPI 设备，并行会 flaky（§5.3） |

## 3. 迁移面普查结果（证据表）

| 包 | 版本变化 | 需迁移？ | 依据 |
|---|---------|---------|------|
| **NAudio** | 2.2.* → 3.1.0 | **是**（§4） | 官方公告：`WasapiOut` 等 WASAPI 包装降级为 legacy placeholder；`ISampleProvider`/`IWaveProvider` 契约改 Span（数组重载移除，已完成）；包族拆分为 Core/Wasapi/Dmo/WinMM/WinForms/Midi/Asio |
| z440.atl.core | 7.13 → 7.18 | 否 | 官方 releases：7.14–7.18 全是解析层修复（FLAC 注释、ID3v2 扩展头、M4A 时长），**零 API 删除/弃用/改名**；本仓只用一个 `new ATL.Track(filePath)` + 10 个属性 |
| xunit | 2.* → 2.9.3 | 否（v2 线内） | 同 major；v3 迁移见 §5 |
| xunit.runner.visualstudio | 2.* → 4.0.0 | 否 | 4.0.0 的 build props 仍随包分发 `xunit.abstractions.dll`（v2 抽象）→ 同时支持 v2/v3 项目；本机 161 条实测通过 |
| NSubstitute | 5.* → 6.2.0 | 否 | 6.0 的破坏面是目标框架（.NET 8 / NS2.0，我们 net10 满足）、移除已废弃例程、旧 C# 兼容包装打 obsolete、更严类型注解（6.1 回退了 public API 的 nullability）—— 0 警告说明未触及；用法仅 `For/Arg.Any/Returns/Received/DidNotReceive` |
| Microsoft.NET.Test.Sdk | 17.* → 18.10.1 | 否 | 工具链；VSTest 路径实测正常，且为 v3 双模式的必要组成 |
| coverlet.collector | 6.* → 10.1.0 | 否 | 仅在 `--collect` 时生效 |

API 归属（决定包引用的最小集合）：

```
NAudio.Core   : WaveFormat, ISampleProvider, IWaveProvider, VolumeSampleProvider,
                BiQuadFilter, FastFourierTransform, BufferHelpers, WaveExtensionMethods
NAudio.Wasapi : WasapiOut, WasapiPlayer(+Builder), MediaFoundationReader, AudioClientShareMode
```

## 4. NAudio 迁移设计

### 4.1 API 对照

| 现在 | 迁移后 | 说明 |
|------|--------|------|
| `new WasapiOut(AudioClientShareMode.Shared, 100)` | `new WasapiPlayerBuilder().WithSharedMode().WithEventSync().WithLatency(100).Build()` | 2 参构造等价于 `(Shared, useEventSync:true, 100)`；`WithEventSync()` 显式保持事件同步语义 |
| `IWavePlayer? _wavePlayer` | `WasapiPlayer? _wavePlayer` | **`WasapiPlayer` 不实现 `IWavePlayer`**（XML 文档零提及）→ 字段与局部变量类型都必须改 |
| `_wavePlayer.Init(_volumeProvider)`（经 `WaveExtensionMethods.Init(IWavePlayer, ISampleProvider)`） | `_wavePlayer.Init(_volumeProvider.ToWaveProvider())` | `WasapiPlayer.Init` 只接受 `IWaveProvider`；`WaveExtensionMethods.ToWaveProvider(this ISampleProvider)` 仍在 Core 3.1.0 中 ✓。实施时若该扩展方法不便，退路是显式 `new SampleToWaveProvider(_volumeProvider)` |
| `_wavePlayer.PlaybackStopped` / `.PlaybackState` / `.Play()` / `.Pause()` / `.Stop()` / `.Dispose()` | 同名成员均存在 | 逐个核对过 XML 文档 ✓；`PollPositionAsync` 继续用 `PlaybackState` |
| `IWaveProvider.Read(Span<byte>)` | —— | 3.x 的 Span 化；我们**不实现** `IWaveProvider`（只有 NAudio 自己实现），无代码影响 |

涉及文件：`Services/NAudioPlaybackService.cs`（唯一改动点）。`Services/StubAudioOutputFactory.cs` 见 §4.3。

### 4.2 `_wavePlayer` 的类型收窄与访问控制

字段类型改为 `WasapiPlayer?` 后，所有使用点（`LoadAsync` 内构造与 `Init`、`Play/Pause/Stop/Seek/DisposePlayback/Unload/OnPlaybackStopped`）的静态类型同步收窄。**不改**各方法的锁语义与调用顺序（§4.4）。

### 4.3 预留输出工厂（保留现状）

`IAudioOutputFactory.CreateOutput() → IWavePlayer` 与返回 `new WasapiOut(Shared, 100)` 的 `StubAudioOutputFactory` **本次不动**：它们是 COUPLING §7 明确要求保留的"未来多后端"接缝，而 `WasapiPlayer` 不实现 `IWavePlayer` 意味着该接缝的契约需要整体重新设计——那件事应当在做多后端时、把两类实现放在一起看。处置：在该文件顶部注释里写明"本类保留 `WasapiOut` 是刻意的（3.x 仍可用），与 `NAudioPlaybackService` 已迁到 `WasapiPlayer` 的现状不矛盾；契约重设计属于多后端工作"，并在 COUPLING 登记。

### 4.4 两条论证的重推（本阶段的技术核心）

**闸门 `_chainGate` —— 保留，改写成"我们自己的"理由。**
2.x 时代的论据是"`WasapiOut.Dispose()` 把 `audioClient` 置空 → 在已释放实例上 `Init` 在 NAudio 内部抛 NRE / `InvalidComObjectException`"。3.x 已把 `AudioClient.Dispose()` 改成 guarded release（NAudio issue #1183 的修复），**这条具体机制不再成立**。但闸门要解决的是另一层问题，与 NAudio 内部是否防护无关：
- 两次 `LoadAsync` 交错时，后者会重建共享字段（`_reader`/`_volumeProvider`/`_wavePlayer`），并释放前者正在使用或正在构建的链；
- `Unload()`/`Dispose()` 撞上 in-flight 的 `LoadAsync` 同理。
⇒ 保留闸门，注释理由改写为**生命周期串行化**（不再引用 `audioClient` 置空细节），并把"持锁不 await""`OnPlaybackStopped` 不取锁（`Stop()` 可能 `Join` 播放线程 → 回调里取锁即死锁）"两条纪律原样保留（后者在新类上的 `Stop()` 是否仍 Join 需实施时确认；无论结论如何，"不从回调里取锁"都是安全默认）。
**证据**：`NAudioPlaybackServiceConcurrencyTests` 的 2 条用例（重叠 Load / Unload 撞 Load）换类后必须仍绿。

**停止意图 `_stopRequested` —— 保留，改写成"信号不可区分"的理由。**
原论据是"播放线程退出时**同步**回调 `PlaybackStopped`（`WasapiOut` 建在线程池线程上、捕获的 `SynchronizationContext` 为 null），而 `Stop()` 内部 `Join` 该线程"。`WasapiPlayer` 的文档说"若构造时捕获了 `SynchronizationContext`，事件在该上下文上引发"——即回调可能是 `Post` 而不是同步调用。**但这不改变结论**：无论同步还是异步，"用户按停止"与"曲目自然播完"仍然是同一个 `PlaybackStopped`，仅凭播放头位置无法区分（异步回调时位置可能已被归零，反而让判定更不确定）。
⇒ 保留标记与"停止方在调用 NAudio **之前**置位、`Play()` 清零、`OnPlaybackStopped` 命中即提前返回"的纪律；注释按新类的实际行为改写。
**证据**：`NAudioPlaybackServiceStopSemanticsTests` 的 2 条用例（曲尾 100ms 内停止/卸载不得发 `TrackEnded`）+ `PlayToNaturalEnd_ThenAdvance`（真自然播完仍须发）三向钉住。

### 4.5 包引用收窄

`D-player.csproj`：

```xml
<PackageReference Include="NAudio.Core"   Version="3.1.0" />
<PackageReference Include="NAudio.Wasapi" Version="3.1.0" />
```

验收时用 `dotnet list D-player.csproj package --include-transitive` 确认 WinForms/Midi/Asio/WinMM/Dmo 已不在图内。

## 5. xunit v3 迁移设计

### 5.1 工程形态与包

`Tests/D-player.Tests.csproj`：

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>                                        <!-- v3 测试工程是可执行程序 -->
  <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
  <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="xunit.v3" Version="4.0.1" />
  <PackageReference Include="xunit.runner.visualstudio" Version="4.0.0" />   <!-- 保留：IDE 测试浏览器 + VSTest 兼容路径 -->
  <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />    <!-- 保留：dotnet test 的 VSTest 路径 -->
  <PackageReference Include="NSubstitute" Version="6.2.0" />
  <PackageReference Include="coverlet.collector" Version="10.1.0" />
</ItemGroup>
```

- `xunit.v3` 钉 **4.0.1**（2026-10-06 查 nuget.org 的最新稳定版；与已 pin 的 `xunit.runner.visualstudio` 4.0.0 属同一条发行线，v3 的包版本号在 4.x）。
- `xunit`（v2 元包）与 `xunit.abstractions` 从引用中移除；`xunit.v3` 会带入 `xunit.v3.core` / `xunit.v3.assert` / v3 运行器。
- 需要一次联网 restore（本机缓存目前只有 v2 系与 `xunit.runner.visualstudio`；`xunit.v3` 尚未缓存）——若 restore 失败则本阶段阻塞，不降级为手工放包（§8）。

### 5.2 代码影响：零

盘点结论（本机实测）：全仓测试代码**没有** `Xunit.Abstractions` / `ITestOutputHelper` 用法；无 `IAsyncLifetime` / `IClassFixture` / `ICollectionFixture`；属性只用 `[Fact]`（153）与 `[Theory]`+`[InlineData]`（1+8）；断言全集 `Equal/Empty/True/False/Null/NotNull/Same/Contains/DoesNotContain/All/ThrowsAny/StartsWith/Record.Exception/Fail` 均为 v3 保留 API；fixture 是 `IDisposable`（v3 仍支持）。

### 5.3 并行度风险与处置（必须实测）

v3 的并行策略与 v2 不同（默认按测试集合并行）。本仓有 5 条**真实占用音频设备**的集成测试（3 条并发/播完 + 2 条停止语义），并行调度会引发设备争用与播放时序漂移 → 不稳定。处置顺序：
1. 迁移后先在 v3 默认并行度下跑全量 5 次，记录是否有抖动；
2. 若抖动，则给音频测试类加同一个 `[Collection("AudioDevice")]`（类间串行）——优先于全局禁用并行；
3. 仍不稳则退到 `[assembly: CollectionBehavior(DisableTestParallelization = true)]`（恢复 v2 的默认串行语义）。
无论走哪条，**必须在 spec 的验收里留下实测记录**（跑几次、结果、最终选择）。

### 5.4 双 runner 的行为

- MTP（默认）：`dotnet run --project Tests/D-player.Tests.csproj -c Debug`，输出为 MTP 的原生 UI，支持 `--filter` 等原生参数。
- VSTest：`dotnet test D-player.sln -c Debug` —— 经 `TestingPlatformDotnetTestSupport` 也路由到 MTP；**命令不变**。
- 两条路径都必须在验收里各跑一遍并全绿（161 条）。
- 若 IDE 测试发现出现异常，官方逃生开关是 `DisableTestingPlatformServerCapability`；**默认不加**，出现问题再说。

## 6. 文档与契约同步

| 文档 | 改动 |
|------|------|
| `docs/COUPLING.md` §5 | ① 闸门条目改为"生命周期串行化"表述（去掉 `audioClient` 置空的具体机制，保留两条纪律）；② 停止意图条目按新类的回调行为改写；③ 新增"测试栈 = xunit v3 + MTP/VSTest 双模式"的说明（含音频测试的集合串行约束） |
| `docs/COUPLING.md` §7 | 补一条 ❌：不要在 `WasapiPlayer` 的 `PlaybackStopped` 回调里取 `_chainGate`；并说明预留工厂保留 `WasapiOut` 是刻意为之 |
| `docs/PROJECT.md` | 构建/测试章节补 MTP 跑法；§9 的播放链陷阱条目按新类改写；依赖清单更新（NAudio.Core/Wasapi）；测试工程形态说明 |
| `README.md` | 测试命令段补 `dotnet run --project Tests/D-player.Tests.csproj -c Debug`（`dotnet test` 保留） |
| 本 spec + 实现计划 | 按阶段惯例落盘并勾选 |

## 7. 验收标准

1. `dotnet build D-player.sln -c Debug` → 0 错误 **0 警告**（含"没有新的 pragma"：`NAudioPlaybackService` 与 `StubAudioOutputFactory` 之外的 pragma 数为零；后者是 §4.3 明确保留的）。
2. **两种方式各跑一遍**：`dotnet test D-player.sln -c Debug` 与 `dotnet run --project Tests/D-player.Tests.csproj -c Debug` → 各 **161 条全绿**。
3. 并行度稳定性：按 §5.3 跑 5 次并记录；最终选择（默认并行 / 集合串行 / 全局串行）写入 PROJECT 的测试章节。
4. 依赖图：`dotnet list D-player.csproj package --include-transitive` 不再出现 NAudio.WinForms / Midi / Asio / WinMM / Dmo。
5. 音频集成测试 5 条全绿（并发 3 + 停止语义 2），它们是两条论证的直接证据。
6. GUI 真机冒烟：播放 → 切歌 → 播完自动推进 → 曲尾按停止（不得自动推进）→ EQ 开关 → 频谱显示正常；`MessageBox.Show` grep 仍为 0。
7. 文档同步完成（§6 全部条目）。

## 8. 风险与边界

- **`WasapiPlayer` 的 teardown 语义未知**：`Stop()` 是否仍 `Join` 播放线程、`Dispose()` 的阻塞行为、`PlaybackStopped` 的回调上下文——都要在实施时用实验确认，再据此定稿注释与 COUPLING 措辞（死锁纪律无论结论如何都保留）。
- **v3 的 `Assert` 重载扩张**可能与本地同名 helper 冲突：本仓断言调用全部是直接调用，无自定义同名 helper ✓；若有编译期歧义，按编译错误逐处显式化。
- **MTP + WPF 工程**（`UseWPF=true` 的测试工程转为 Exe）需要实测通过；这是 v3 在本仓的首次落地，失败则以 VSTest 路径为准回退（保留 `dotnet test`），MTP 延后。
- **联网 restore**：`xunit.v3` 需从 nuget.org 拉取；本机网络对 github.com 不稳定但 nuget.org 可用。若 restore 失败，本阶段阻塞（不降级为手工放包）。
- **收窄包引用后**若出现"类型找不到"（例如 WasapiOut 的 legacy 实现需要 Dmo），以编译错误为准补回**最小**必要的子包，并把这个例外写进依赖清单说明。
- 本阶段**不改产品行为**，因此没有数据/持久化兼容问题；`playlists.json` / `settings.json` / library cache 均不受影响。

## 9. 参考

- NAudio 3 迁移要点：[Announcing NAudio 3 Preview](https://markheath.net/post/2026/5/22/announcing-naudio-3-preview) · [RELEASE_NOTES.md](https://github.com/naudio/NAudio/blob/main/RELEASE_NOTES.md) · NAudio issue #1183（guarded dispose 的由来）
- xunit v3：[迁移指南](https://xunit.net/docs/getting-started/v3/migration) · [Microsoft.Testing.Platform 配置](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)
- 其它包：[atldotnet releases](https://github.com/Zeugma440/atldotnet/releases) · [NSubstitute releases](https://github.com/nsubstitute/NSubstitute/releases) · [xunit.runner.visualstudio 4.0.0](https://xunit.net/releases/visualstudio/4.0.0)
- 本仓相关：`Services/NAudioPlaybackService.cs`（闸门 + 停止意图）、`Tests/Services/NAudioPlaybackServiceConcurrencyTests.cs`、`Tests/Services/NAudioPlaybackServiceStopSemanticsTests.cs`、`Tests/Services/TestAudio.cs`、`docs/COUPLING.md` §5/§7、`40338f5`（前置升级）
