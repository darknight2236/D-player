# Phase 30 验收记录（音频实时路径性能重写，core 部分）

- 日期：2026-11-13
- 分支：`phase30-audio-realtime`（起点 `master` / `b92d3d6`）
- 基线：`docs/PHASE30-AUDIO-BASELINE.md` / `.json`（测得于 `e46579e`，产物提交于 `06cd9db`）
- 本阶段提交区间：`e8678a9..a231649`（22 条：设计稿 / 基线与目标 / 工具 / 产品 / 测试；逐条见 `git log master..phase30-audio-realtime`）
- 本轮验收测量点：**`a231649`（干净已提交工作树，无代码改动）**；本文档自身的提交另计
- 设计：[2026-11-13-d-player-phase30-audio-realtime-design.md](./superpowers/specs/2026-11-13-d-player-phase30-audio-realtime-design.md)
- 范围：音频实时路径（频谱 / EQ / 播放链 / 位置轮询）+ `tools/AudioBench`；WinUI 壳与 WPF 视图零改动
- **本文档是 core 验收（门禁 / 基线 / 独立核对 / 文档）。GUI 走查由 Task 13b 单独执行，见 §6「GUI pending (Task 13b)」。**

## 1. 门禁读数

入口：`powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-gates.ps1 -Full -Audio`，末行：

```
all requested steps passed.
```

| 关 | 命令 | 实测（2026-11-13，`a231649`） |
|---|---|---|
| 门禁 1 | `dotnet build D-player.slnf -c Debug --nologo -v q` | **0 警告 / 0 错误**（1.37 s） |
| 门禁 2 | `dotnet test D-player.slnf -c Debug -v q --filter "Category!=AudioDevice"` | **363 通过 / 0 失败 / 0 跳过**（runner 1.694 s，总 1.948 s） |
| 音频 | `dotnet test D-player.slnf -c Debug -v q --filter "Category=AudioDevice"` | **5 通过 / 0 失败 / 0 跳过**（3.644 s） |
| 门禁 3 | `dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q` | **0 警告 / 0 错误**（14.27 s） |

- 全量测试口径：**368 = 363 默认 + 5 音频**。Phase 29 验收基线为 342（337 + 5），本阶段默认测试 **+26**（逐条见 §3），音频 5 条未增减。
- 测试命令未加 `--nologo`；MTP 零匹配退出码 8 未被当作成功。
- 门禁 1/3 的 0 警告是在**零 `#pragma`/`NoWarn`** 前提下取得的（§4 核对）。

## 2. AudioBench 改写前后对照

### 2.1 命令与口径

- 官方命令（run 1，产出 `docs/PHASE30-AUDIO-AFTER.md` / `.json`）：

```bash
dotnet run -c Release --project tools/AudioBench/AudioBench.csproj -- --seconds 10 --warmup 2 --runs 5 --sample-rate 48000 --buffer-frames 480 --commit "$(git rev-parse --short HEAD)" --out docs/PHASE30-AUDIO-AFTER.md --json docs/PHASE30-AUDIO-AFTER.json
```

- run 2：**同一条命令、同一参数、同一提交**（`a231649`），仅换输出路径到 `docs/PHASE30-AUDIO-AFTER-RUN2.md` / `.json`。两次运行之间没有改任何代码/文件。
- 机器：Windows NT 10.0.26300.0，AMD64 Family 25 Model 68（与基线同一台机器），Release，.NET 11.0.0。
- **音频线程口径**：`thread alloc` 是音频线程（执行 `Read` 的线程）的分配；`process alloc` 是进程总分配增量，**包含 FFT 分析线程的每帧 32-float 快照**（数值大小 ≈ 帧数 × 152 B）。
- **`aggregator` 场景改写后口径变化（必须并列读）**：基线里 `aggregator` 的 `Read` 内含 FFT/加窗/分桶；改写后 FFT 已移到独立分析线程，`aggregator` 行的 CPU/p99 只代表 **producer Read 成本（混音 + 写环形缓冲 + 发信号）**，不能再与基线 `aggregator` 行 1:1 相减。`full-chain` 行是**包含 EQ + 聚合器 producer + Volume 的音频线程总成本**，是本阶段的主对照。

### 2.2 读数（48 kHz / 2ch / 480 帧，5×10 音频秒）

基线（`docs/PHASE30-AUDIO-BASELINE.md`，`e46579e`）：

| Scenario | p50 ms | p95 ms | p99 ms | max ms | CPU ms/audio-s | thread alloc B/run | process alloc B/run |
|---|---|---|---|---|---|---|---|
| eq-off | 0.0001 | 0.0002 | 0.0003 | 0.0024 | 0.016 | 40 | 40 |
| eq-flat | 0.0463 | 0.1061 | 0.1169 | 0.7657 | 6.251 | 40 | 40 |
| eq-bass-boost | 0.0463 | 0.0516 | 0.0696 | 0.2738 | 4.740 | 40 | 40 |
| aggregator | 0.0044 | 0.2415 | 0.2531 | 0.3848 | 3.111 | 17,824 | 17,824 |
| full-chain | 0.0523 | 0.2801 | 0.3371 | 3.9939 | 7.885 | 17,824 | 17,824 |

改写后（`a231649`，run 1 = `docs/PHASE30-AUDIO-AFTER.md`）：

| Scenario | p50 ms | p95 ms | p99 ms | max ms | CPU ms/audio-s | thread alloc B/run | process alloc B/run |
|---|---|---|---|---|---|---|---|
| eq-off | 0.0001 | 0.0002 | 0.0003 | 0.0057 | 0.018 | 40 | 40 |
| eq-flat | 0.0001 | 0.0002 | 0.0002 | 0.0081 | 0.180 | 40 | 40 |
| eq-bass-boost | 0.0376 | 0.0451 | 0.0572 | 0.5063 | 3.164 | 40 | 40 |
| aggregator | 0.0049 | 0.0080 | 0.0091 | 0.0956 | 0.533 | 40 | 4,113 |
| full-chain | 0.0195 | 0.0282 | 0.0312 | 0.1639 | **2.031** | **40** | 16,942 |

改写后（`a231649`，run 2 = `docs/PHASE30-AUDIO-AFTER-RUN2.md`）：

| Scenario | p50 ms | p95 ms | p99 ms | max ms | CPU ms/audio-s | thread alloc B/run | process alloc B/run |
|---|---|---|---|---|---|---|---|
| eq-off | 0.0001 | 0.0002 | 0.0003 | 0.0025 | 0.019 | 40 | 40 |
| eq-flat | 0.0001 | 0.0002 | 0.0003 | 0.0053 | 0.203 | 40 | 40 |
| eq-bass-boost | 0.0380 | 0.0386 | 0.0461 | 0.5196 | 3.041 | 40 | 40 |
| aggregator | 0.0048 | 0.0070 | 0.0076 | 0.1110 | 0.506 | 40 | 3,596 |
| full-chain | 0.0193 | 0.0290 | 0.0330 | 0.1274 | **2.028** | **40** | 17,459 |

差值（run 1 / run 2）：

| 指标 | 基线 | 改写后 | 变化 |
|---|---|---|---|
| full-chain CPU | 7.885 ms/audio-s | 2.031 / 2.028 | **−74.2% / −74.3%** |
| full-chain p99 | 0.3371 ms | 0.0312 / 0.0330 | **−90.7% / −90.2%** |
| full-chain thread alloc | 17,824 B/run | 40 / 40 | 降到 harness 地板 |
| aggregator CPU（口径已变，producer Read） | 3.111 ms/audio-s | 0.533 / 0.506 | 不可与基线相减，见 §2.1 |
| aggregator p99 | 0.2531 ms | 0.0091 / 0.0076 | FFT 已不在音频线程 |
| eq-flat CPU | 6.251 ms/audio-s | 0.180 / 0.203 | 0 dB 频段跳过 + 场景接近旁路 |
| eq-bass-boost CPU | 4.740 ms/audio-s | 3.164 / 3.041 | −33.2% / −35.8% |
| eq-off CPU（未改写路径） | 0.016 | 0.018 / 0.019 | 噪声量级，harness 地板 |

- 两次运行全程稳定：`full-chain` 2.031 vs 2.028、p99 0.0312 vs 0.0330、分配 40 vs 40。
- `eq-off` 与基线一致（0.016→0.018/0.019）：该场景不经过本阶段改写的主路径，可作为 harness 噪声参照。
- **40 B/run 是 harness 地板而非产品分配**：它在基线里就出现在 `eq-off`/`eq-flat`/`eq-bass-boost`（旁路也报 40），改写后所有场景统一报 40；`aggregator`/`full-chain` 从 17,824 降到同一地板即音频线程稳态零产品分配。
- `process alloc` 在 `aggregator`/`full-chain` 仍有 3.6–17.5 KB/run，是 FFT 分析线程每帧新建的 32-float 快照（≈152 B/帧），按设计 §4.1 不计入音频线程；它反而证明快照分配确实发生在另一条线程上（同场景 thread alloc = 40 B）。

### 2.3 目标对照（设计 §10.2 激进档）

| 目标 | 阈值 | 实测（run 1 / run 2） | 结论 |
|---|---|---|---|
| 音频线程 `Read` CPU/音频秒 | ≤ 3.94 ms | 2.031 / 2.028（full-chain） | **达成**（降幅 74%，阈值是 ≥50%） |
| 音频线程 `Read` p99 | ≤ 0.15 ms | full-chain 0.0312 / 0.0330；全部场景 p99 ≤ 0.0572 | **达成** |
| 音频线程稳态分配 | 0 B/轮 | full-chain/aggregator thread alloc = 40 B/run（= 基线 harness 地板） | **达成（按地板口径）** |
| FFT 线程每帧成本 | < 2 ms | signal→event p50 **0.149 / 0.150 ms**，p99 0.247 / 0.272 ms，max 0.274 / 0.338 ms | **达成** |
| FFT 帧率 | ≥ 10 Hz | **11.60 帧/音频秒**（理论 11.72 = 48000/4096），10.00 s 音频产出 116/116 帧无丢帧 | **达成** |
| SIMD 备用手段 | 仅当标量重排不达标 | 未启用 | 不需要 |

- FFT 两个子目标 **不是 AudioBench 的指标**（harness 只测 `Read`），实测来自一个**临时探针**（`%TEMP%` 下 net11.0-windows 控制台 + `ProjectReference` 指向 `D-player.Core`，跑完已删除，未入仓）：① 按实时节奏（480 帧/10 ms、`timeBeginPeriod(1)`）喂 10.00 s 音频，统计 `SpectrumDataReady` 次数；② 逐 hop 突发喂数据，量「数据就绪 → 事件回调」延迟作为每帧成本的上界（含线程唤醒）。探针只消费公开 API，不改产品代码。
- 与设计附录 A 的机器本机探针一致（FFT 8192 点 0.160–0.176 ms/帧），合成视界内每帧仍远低于 2 ms。
- **没有降低任何目标值，也没有为达标删功能或跳过门禁。**

## 3. 新增测试清单（26 条，全部在默认门禁内）

| 文件 | 新增 | 用例 |
|---|---|---|
| `Tests/Services/SampleAggregatorTests.cs`（新） | 8 | `Overlap_UsesTimeDomainSamples_NotPreviousFftOutput`、`KnownSine_PeaksInExpectedBar`、`PrecomputedTables_MatchReferenceBars`、`Silence_NoNaN_AllZero`、`Disabled_PassesThroughExactly_NoEvent`、`SubscriberMutation_DoesNotAffectNextFrame`、`Dispose_StopsThread_NoEventsAfterDispose`、`EnabledToggle_SkipsStaleFrames` |
| `Tests/Services/NAudioPlaybackServiceDeterministicTests.cs`（新） | 14 | `LoadAsync_WithFakes_PublishesTrackAndDuration`、`TwoOverlappingLoads_Serialized_LastChainWins`、`Stop_RaisesPlaybackStoppedSynchronously_DoesNotDeadlock`、`Stop_WhenPlayheadAtEnd_DoesNotRaiseTrackEnded`、`NaturalEnd_RaisesTrackEndedOnce`、`ReplacedChain_LatePlaybackStopped_IsIgnored`、`ChainDispose_UnsubscribesBeforeStop`、`Events_ArePostedToCapturedSynchronizationContext`、`Unload_DuringLoad_NewChainNotInstalled`、`Dispose_DuringLoad_DisposesNewChain`、`Load_DuringLoad_PlayIsNoOp`、`Load_BlocksInInit_UnloadCancelsInstall`、`Load_WhileOldChainPlaying_OldIsDetachedBeforeBuild`、`Play_PostsPositions_AndPauseStopsPolling` |
| `Tests/Services/EqualizerSampleProviderTests.cs`（扩充，7 条既有保留） | 4 | `FlatEnabled_IsExactPassthrough`、`Update_AppliedAtBufferBoundary_MatchesReferenceFilterBank`、`PendingConfig_WhileDisabled_EnablesOnNextRead`、`Update_ConcurrentWithRead_NoExceptions_FiniteOutput` |

- 合计 8 + 14 + 4 = **26**，与 363 − 337 = 26 一致。
- 测试支撑类型（非用例）：`Tests/TestSupport/FakeAudioSource.cs`、`FakeWavePlayer.cs`、`QueuedSynchronizationContext.cs` 扩充。
- **"无 sleep 同步"核对**：生命周期/频谱/EQ 的编排全部用 `ManualResetEventSlim`/`Barrier` + 门闩；`grep -n "Task.Delay\|Thread.Sleep"` 在新增测试里只有 4 处，且都不是竞态窗口：
  - `NAudioPlaybackServiceDeterministicTests.Play_PostsPositions_AndPauseStopsPolling` 的 3 处 `Task.Delay(100–150)`：给 33 ms 轮询 3–4 个 tick 的**观察窗口**，XML 注释明写"行为钉，不是竞态证据"；
  - `SampleAggregatorTests.Dispose_StopsThread_NoEventsAfterDispose` 的 1 处 `Thread.Sleep(100)`：Dispose 之后的**观察窗口**，用于证明"不再有晚到事件"。
- 设计的 §6.3 十条矩阵全部落地（含 Task 10 补的 `Load_BlocksInInit_UnloadCancelsInstall`），另加 `Load_DuringLoad_PlayIsNoOp` 与 `Load_WhileOldChainPlaying_OldIsDetachedBeforeBuild`。

## 4. 独立核对

| 项 | 命令 | 结果 |
|---|---|---|
| Core WPF-free | `grep -rn "System.Windows\|PresentationFramework\|ICollectionView\|CollectionViewSource" D-player.Core --include=*.cs --exclude-dir=obj --exclude-dir=bin` | **零命中**（exit 1） |
| 抑制项 | `grep -rn "#pragma warning disable\|NoWarn" D-player.Core D-player.csproj D-player.WinUI Tests --include=*.cs --include=*.csproj --exclude-dir=obj --exclude-dir=bin` | **零命中**（exit 1） |
| 耦合审计 | `powershell -NoProfile -ExecutionPolicy Bypass -File tools/coupling-audit/Invoke-CouplingAudit.ps1` | **0 cycles / 0 violations**；`tools/AudioBench` 不在审计源集合内，无需 Phase 29 式的口径说明 |
| 发布 | `dotnet publish D-player.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true` | 成功；`bin/Release/net11.0-windows/win-x64/publish/D-player.exe` **3,226,706 bytes**（2026-11-13 20:20:25），publish 目录 4 个文件 |
| 输出目录 | `bin/Debug/net11.0-windows/D-player.exe` | **73,728 bytes**（2026-11-13 20:19:42，门禁 1 产物）；`bin/Debug/net10.0-windows/` 仍有旧二进制（10 月的 `D-player.exe`/`UmaPlayer.exe`），GUI 必须启动 net11 路径 |

- 耦合审计附加读数（非门禁）：`NAudioPlaybackService.cs` 560 行（Phase 29 时 549 行以下，未进 >600 行观察名单）；M6 未消费服务只剩 `IAudioDeviceManager`（`IAudioOutputFactory` 已由 `NAudioPlaybackService` 生产消费，从 Phase 29 的"预注册未消费"名单中移除）。
- 产品面改动范围（`git diff --stat master..HEAD`）：Core 音频 6 个文件 + `D-player.csproj`（glob 排除 `tools/**`）+ 3 个测试文件/3 个测试支撑文件 + `tools/AudioBench` + 设计/基线文档。**`Views/**`、WinUI、`D-player.slnf`、`verify-gates.ps1`、`global.json` 零改动。**

## 5. 行为与契约变化（复核用速查）

| 项 | 变化 |
|---|---|
| 频谱 | 音频线程只混音 + 写 SPSC 环形缓冲；FFT/加窗/分桶/事件在 `DPlayer.SpectrumFft` 后台线程（BelowNormal）；重叠帧改用真实时域样本（修复 Phase 13 起的缓冲复用缺陷） |
| EQ | 删除音频 `Read` 里的 `lock`；UI 只 `Volatile.Write` 一份不可变 config，音频线程在 `Read` 开头 `Interlocked.Exchange` 取走并在缓冲边界应用；0 dB（单位增益）频段不进级联 |
| 播放链 | private `PlaybackChain`；reader 打开与输出 `Init` 移到 `_chainGate` 之外（`_loadGate` 串行化装载）；旧链 stop/dispose 在锁外；`StopRequested` 改为每链标记，`OnPlaybackStopped` 先判当前链再判意图 |
| 位置轮询 | `PeriodicTimer(33 ms)` + `_playSession` 代际替代 `Task.Delay(33)` + `DateTime` 双节流；暂停/停止/切歌后最多一个 tick 退出 |
| 语义变化 | 装载期间的传输命令从"UI 线程阻塞到装载结束"变为 **no-op**（调用方都 `await LoadAsync` 后再 `Play`） |

## 6. GUI pending（Task 13b）

**本 core 验收**不包含 GUI 走查，以下由 Task 13b（ComputerUse + 主代理复核）执行并回填本节：

- [ ] 走查前备份 `%LOCALAPPDATA%\D-player` 的 `settings.json` / `queue.json` / `library-cache.json` 到 `%TEMP%\phase30-data-backup`，记录 SHA-256。
- [ ] 合成库（5 个 20 秒 WAV、中文路径、多碟目录、GBK `中文歌单.m3u`）启动 `bin/Debug/net11.0-windows/D-player.exe`（注意 §4 的 net10 旧目录陷阱）。
- [ ] 播放/暂停、进度单击与拖动、上一首/下一首、自然播完自动推进、曲尾 200 ms 内停止不误推进。
- [ ] 频谱可见、播放时变化、暂停后停止更新、切歌后继续。
- [ ] EQ 启用、1k 段 +8 dB、保存落盘、取消不落盘、拖动过程人工听感（无爆音）。
- [ ] 关闭落盘/重启续播；关闭中刷新不挂起。
- [ ] 结束后按字节还原三份 JSON，SHA-256 与备份完全一致；删除合成库/备份/临时脚本。

**结论位置**：GUI 未完成前，Phase 30 的验收状态为 **core 完成 / GUI 待 Task 13b**；若 GUI 发现缺陷，以回填记录为准。

## 7. 已知边界

- **WinUI 只有编译覆盖**：壳冻结，门禁 3 只保证编译；Core 音频改动流入 WinUI 的运行时行为未走查（WinUI 壳不启动 watcher、不参与音频实时路径的真机验证）。
- **无 underrun/回调抖动计数器**：真实 WASAPI 设备下的欠载、回调延迟没有任何自动读数；性能以合成链（AudioBench）+ GUI 走查为准。
- **AudioBench 是合成链 + 墙面时钟**：`CPU ms/audio-s` 是读循环的 `Stopwatch` 墙钟（含每次 `Read` 的计时开销），只用于同机相对对照，不是进程 CPU 时间；40 B/run 是 harness 地板。
- **FFT 帧率/每帧成本来自临时探针**（§2.3），非仓库内指标；探针已删除，复现需按同口径重写。
- **EQ 听感只做人工走查**，不做客观音质断言；**`SetPeakingEq` 在 NAudio 3.3.0 会清零 x1/x2/y1/y2**（独立探针 0.5 → 0），Phase 30 保留"缓冲边界生效"但**不保留跨缓冲滤波状态**；拖 EQ 仍有复位型爆音风险，状态保持/交叉淡化是后续候选（非本阶段范围），详见设计稿 §4.2/§7.3/§10.4/§11 的修正。
- **装载期传输命令 no-op** 是明示行为变化；GUI 走查需覆盖切歌/双击的装载窗口。
- **预存在且未扩大**：in-flight `PlaybackStopped` 在"摘下当前链"与"置 `StopRequested`"之间的极窄窗口仍可能读到非空链，表现为偶发自动推进；只影响自动推进，不影响数据，属后续独立修复。
- **`FftSize`/`BarCount` 运行时修改仍被忽略**（构造时校验，与 Phase 13 现状一致）。
- **WPF 壳的 EQ 弹窗仍是"实时预览 + 取消回滚"**，本阶段未改其交互。

## 8. 结论

- Phase 30 的音频实时路径重写**达成设计 §10.2 的全部量化目标**：音频线程 full-chain **7.885 → 2.031/2.028 ms/音频秒（−74%）**，p99 **0.337 → 0.031/0.033 ms**，音频线程稳态分配降到 harness 地板 40 B/run（产品分配 0），FFT 分析线程 **11.60 帧/音频秒、每帧 ≤0.34 ms**。
- `verify-gates.ps1 -Full -Audio` 三关全过（0/0、363 + 5、0/0），末行 `all requested steps passed.`；Core WPF-free、抑制项、耦合审计、发布与输出目录核对全部通过；26 条新增默认测试全部落在门禁内，无 sleep 竞态窗口。
- **GUI 走查（Task 13b）待执行**，数据 SHA-256 还原结果待回填 §6；在此之前验收状态为 core 完成。
- **未推送**：分支停留在 `phase30-audio-realtime` 本地，推送等用户明确指示。
