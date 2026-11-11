# Phase 28 设计：升级到 .NET 11（四个工程全抬 net11.0）

- 日期：2026-11-12
- 状态：设计已由用户逐节确认（2026-11-12），待实施。
- 代码基线：`master` / `54f57d2`；实施分支 `phase28-net11-upgrade`。Phase 27 已交付并验收；WinUI 壳继续冻结。
- 前置：[Phase 27 验收](../../PHASE27-ACCEPTANCE.md) · [PROJECT.md](../../PROJECT.md) · [COUPLING.md](../../COUPLING.md)。历史设计中的初始状态不替代当前代码事实。

## 1. 已确认的范围

本阶段只做一件事：把四个工程的目标框架从 net10.0 抬到 net11.0，让三关门禁在 .NET 11 上全绿，并把升级的证据与文档落定。**零行为变更意图**——不引入任何 .NET 11 新 API、不使用任何 C# 15 新语法。

| 决策 | 本期约定 |
|---|---|
| 目标框架 | 四个工程全抬：Core / WPF / Tests → `net11.0-windows`；WinUI → `net11.0-windows10.0.19041.0`（OS build 19041 不变） |
| 落地方式 | 分支上一次全抬 + 三关实测；不分步抬（Core 抬了 WinUI 必然立即编译不过，分步只制造明知故犯的红灯中间态） |
| 版本卫生 | `CommunityToolkit.Mvvm` 由浮动 `8.*` 钉死 `8.4.2`（已核实为最新版） |
| `global.json` | **不加 sdk 段**。用户已于 2026-11-12 卸载 SDK 10.0.201，本机只剩 11.0.100，无需显式钉住；现有 `test.runner` 段保持不变 |
| 发布形态 | **不改**。`--self-contained false` 保持原样；后果是 framework-dependent 发布要求目标机装 .NET 11 Desktop Runtime（见 §9） |
| 现代化改动 | 全部推迟。非音频路径现代化 → Phase 29；音频实时路径性能重写 → Phase 30 |
| 验收口径 | 门禁 1/2/3 + 5 条真实音频 + GUI 走查（ComputerUse 子代理执行 + 主代理独立复核） |
| Rider 红线 | 定性为 IDE 侧误报，**不改产品代码去迎合它**（见 §5） |

**不做**：不用任何 .NET 11 新 API / C# 15 新语法；不动音频实时路径；不删 `IAudioDeviceManager` / `IAudioOutputFactory` 两个预留 Stub；不改 `D-player.csproj` 的 glob 排除集；不改 `D-player.slnf` 组成（WinUI 仍不进日常门禁）；不修盘点发现的 `RootGates` 泄漏与 `FindAncestor` 三份副本（Phase 29）；不回改历史设计稿与验收记录。

**阶段拆分理由**：升级、全库现代化、音频实时路径重写是三件性质不同的工作。音频重写需要先有性能基线才谈得上"以性能为目标"，而仓库现有闸门侧回归是**随机证据**（见 §9），把它与低风险语法精简混在一份设计稿里会让回滚边界糊掉。故按用户决定拆为 Phase 28 / 29 / 30 三个大阶段，各走一轮设计→计划→实施。

## 2. 当前事实与核实结论

本节全部为 2026-11-11/12 实测或本地取证，**区分硬证据与推断**。

### 2.1 工具链现状（硬证据）

- SDK：仅 `11.0.100`（用户已卸载 `10.0.201`）。Roslyn 实测 `5.12.0-1.26529.118`（`csc -version`；`csc.deps.json` → `Microsoft.CodeAnalysis/5.12.0.0`）。
- 运行时：`Microsoft.WindowsDesktop.App` 11.0.0 / 10.0.15 / 10.0.3 / 8.0.x / 3.1.8 均在盘。
- Targeting pack：`Microsoft.NETCore.App.Ref` 与 `Microsoft.WindowsDesktop.App.Ref` 均有 `10.0.15` / `11.0.0` / `8.0.34`。**net10.0 的 pack 在 SDK 10 卸载后仍在盘上** → 回退到 net10.0 不需要重装 SDK 10（§4）。
- 四个工程 TFM 升级前均为 net10.0；`Microsoft.Extensions.*` 包已是 11.0.0；`CommunityToolkit.Mvvm` 浮动 `8.*` 实际解析 **8.4.2**。

### 2.2 升级前基线（硬证据，SDK 11 + net10.0 TFM）

- 门禁 1：`dotnet build D-player.slnf -c Debug --nologo -v q` → **0 警告 0 错误**；另跑 `--no-incremental` 强制全量重建，同样 0/0。
- 门禁 2：`dotnet test D-player.slnf -c Debug -v q --filter "Category!=AudioDevice"` → **310 通过 / 0 失败 / 0 跳过**，持续时间 2.104s。
- 结论：**光升 SDK 不抬 TFM 不打挂任何东西**；`global.json` 的 MTP runner opt-in 在 SDK 11 上仍有效。Phase 28 的真实风险全部集中在"抬 TFM"这一步。
- 测试计数口径纠正：当前是 **315 = 310 默认 + 5 音频**（Phase 27），不是旧记录里的 294（Phase 26 历史读数）。

### 2.3 IDE 报错定性（硬证据）

用户报告 Rider 中 `LibraryViewModel` / `PlaylistViewModel` 出现「`NotifyPropertyChangedForAttribute` 特性重复」。核实结论：**IDE 侧误报，产品代码正确**。

- CLI 两次构建（含强制全量）0 警告 0 错误 → Roslyn 11 接受这份代码。
- 用户确认 Rider 内**可以正常构建并运行**，只是编辑器红线。
- .NET 11 ref pack 中**不存在** `NotifyPropertyChangedForAttribute`（grep 10.0.15 与 11.0.0 的 ref 目录均无命中）→ 排除"BCL 新增同名特性造成冲突"。
- 代码用传统写法：`[ObservableProperty]` 打在字段上，`LibraryViewModel.cs:44-51` 的 `_page` 叠 8 个 `[NotifyPropertyChangedFor]`，`PlaylistViewModel.cs:68-71` 的 `_currentIndex` 叠 3 个 `[NotifyCanExecuteChangedFor]`。这些特性本就是 `AllowMultiple`。
- Rider 2026.3 的 `idea.log` / `RoslynWorker` / `MsBuildTask` 日志中无源生成器异常、无错误码记录。
- **未能核实**：Rider 那条红线的确切错误码与文本（用户未提供，日志中也没有）。因此误报的**具体机制**（ReSharper 检查误判 vs Roslyn 工作区重复跑生成器）仍未定性，只定性到"IDE 侧、非编译错误"这一层。

### 2.4 依赖兼容性（本地取证）

| 包 | lib 最高 TFM | ns2.0 兜底 | 生成器 / 结论 |
|---|---|---|---|
| Microsoft.WindowsAppSDK 2.6.5 | 子包均 `net6.0-windows10.0.17763.0`（ML 无 lib） | — | **无硬性阻止 net11.0**，见下 |
| CommunityToolkit.Mvvm 8.4.2 | net8.0（+net8.0-windows10.0.17763） | 有 | 显式带 `analyzers/dotnet/roslyn5.0/cs/` 变体（**硬证据**）；SDK 式工程会选中它（**选择机制为推断**） |
| NAudio.Core / NAudio.Wasapi 3.3.0 | net9.0（仅此） | 无 | 前向兼容（推断） |
| z440.atl.core 7.19.0 | net6.0（仅此） | 无 | 前向兼容（推断） |
| xunit.v3 4.0.3 | 元包 → mtp-v2/core：net8.0 / net472 | 无 | xunit.analyzers 单 cs 变体 |
| NSubstitute 6.2.0 | net8.0 | 有 | 无 |
| Microsoft.Extensions.* 11.0.0 | **net11.0**（+net10.0/net462/ns2.0/2.1） | 有 | 一等支持 |

**WASDK 的关键硬证据**：整棵包树唯一的 TFM 校验在 `microsoft.windowsappsdk.base/2.6.5/build/Microsoft.WindowsAppSDK.Base.targets`（第 39–40、57–60 行）：`_TargetFrameworkRequired = 6.0` + `VersionGreaterThanOrEquals($(TargetFrameworkVersion), 6.0)`，不满足才 `<Error>`。**这是下限，不是白名单也不是上限** → net11.0 必然通过。主包是元包，`build/*.targets|props` 为空壳；全树 grep 无 `net10/net11` 硬编码、无 `VersionLessThan` 上限校验。XAML 编译器按 `MSBuildRuntimeType` 选工具（Core → `tools/net6.0/`），**不按项目 TFM**；`Microsoft.WinUI.targets` 的 `<Error>` 只针对 Platform/UseUwp。

**MVVM 生成器选择机制**：SDK 式工程走 NuGet 组件版本化，会选 `roslyn5.0` 变体；生成器最低门槛 Roslyn ≥ 4.0（`CommunityToolkit.Mvvm.SourceGenerators.targets:44`）。该 targets 第 89–91 行的"手动回退"只到 roslyn4.12，但仅在 `SupportsRoslynComponentVersioning != true`（旧式工程）时触发，本仓为 SDK 式，不受影响。

**NU1510 风险的具体形状**：net11.0 的 ref pack 新 **inbox 了 9 个 `Microsoft.Extensions.*.Abstractions/Options/Primitives` DLL**（10.0.15 没有）。本仓直接引用的是 DI / Configuration.Json / Options.ConfigurationExtensions，不在这 9 个里，**大概率不触发**（推断）；但传递依赖裁剪行为变了，仍须实测门禁 1 的 0 警告。

**仍未实测（推断，非已证不支持）**：WinRT.SourceGenerator / xunit.analyzers / MVVM roslyn5.0 生成器在 Roslyn 5.12 下能否真正加载运行；WinUI 壳在 net11.0 下的 XAML 编译与 CsWinRT 投影端到端；`Microsoft.Windows.SDK.NET.Ref` 为 19041 解析到 .57（lib/net8.0）还是 .87（lib/net9.0）——两者均满足 Base.targets 的 `≥10.0.19041.38`，但版本变化会带动 CsWinRT 生成器版本变化。

### 2.5 C# 15 breaking change 对本仓零命中（硬证据）

抬 TFM 会让默认 LangVersion 从 14 升到 15。官方 breaking changes 中可能影响既有代码的四条：`closed`/`union`/`safe` 成为上下文关键字；集合表达式内 `with(...)` 在 LangVersion ≥ 15 时改绑为构造参数（同名方法须写 `@with(...)`）；attribute 中 `nameof(this.P)` 被禁（.NET 10.0.200 起已生效）；switch 臂中 `(X.Y) when` 解析变更。

全仓 `*.cs` grep `\b(union|closed|safe)\b|\[\s*with\s*\(|\bwith\s*\(` → **零命中**。故这几条不构成本阶段的改动面。

C# 15 特性清单与纠正见附录 A。

## 3. 改动清单

### 3.1 代码 / 配置（5 处，全是既有行）

| 位置 | 改动 |
|---|---|
| `D-player.csproj:5` | `net10.0-windows` → `net11.0-windows` |
| `D-player.Core/D-player.Core.csproj:4` | `net10.0-windows` → `net11.0-windows` |
| `Tests/D-player.Tests.csproj:3` | `net10.0-windows` → `net11.0-windows` |
| `D-player.WinUI/D-player.WinUI.csproj:5` | `net10.0-windows10.0.19041.0` → `net11.0-windows10.0.19041.0` |
| `D-player.Core/D-player.Core.csproj:14` | `CommunityToolkit.Mvvm` `Version="8.*"` → `Version="8.4.2"` |

`LangVersion` 不显式设置：net11.0 默认即 C# 15。Tests 现有的 `<LangVersion>latest</LangVersion>` 与之等价，保持不动（不做无谓清理）。

### 3.2 注释漂移（3 处，均为活文件而非历史记录）

| 位置 | 现状 | 改为 |
|---|---|---|
| `Tests/D-player.Tests.csproj:11` | ".NET 10 SDK 的原生 opt-in" | .NET 11 SDK（MTP opt-in 机制不变） |
| `D-player.WinUI/Views/CoverArtLoader.cs:26` | "本机 net10.0-windows10.0.19041 + WASDK 2.5.1 编译通过" | net11.0-windows10.0.19041 + WASDK 2.6.5（2.5.1 早已过时） |
| `tools/verify-gates.ps1:12` | "176 passed / 0 failed" | "310 passed / 0 failed" |

**改 `verify-gates.ps1` 必须保持纯 ASCII**：脚本第 48 行注明 PowerShell 5.1 在本机把无 BOM 文件按 GBK 读。`176` → `310` 是 ASCII 安全；加中文注释会坏掉门禁脚本。

### 3.3 文档同步

见 §8。

## 4. 失败域与回退

风险切成三个**独立失败域**，各有证据来源，出问题时知道该退哪一半。

### 域 A — Core/WPF/Tests 抬 net11.0（门禁 1 + 2 覆盖）

| 风险 | 抓它的那道关 |
|---|---|
| NU1510 / 框架隐含包集合变化 | 门禁 1 的 **0 警告断言**（`verify-gates.ps1:103` 从 MSBuild 文件日志数警告行，不只看退出码，压不住） |
| `System.Text.Encoding.CodePages` 不再框架隐含 → `Encoding.GetEncoding(936)` 运行时抛 → **GBK 编码 .m3u 导入坏掉** | `PlaylistFileServiceTests` 专测；另列独立核对项（§7） |
| NAudio（lib 仅 net9.0）/ atl（仅 net6.0）前向兼容 | 门禁 2 + 5 条真实音频 |
| MVVM roslyn5.0 生成器在 Roslyn 5.12 下加载 | 门禁 1 编译即验证——生成器不跑则 `[ObservableProperty]` 生成的属性不存在，会是一片 CS1061，不会静默 |

CodePages 这条要特别对待：`docs/PROJECT.md:1201` 记着它在 net10.0 是框架隐含，所以 csproj **刻意没有**这个包引用。若 net11.0 把它移出隐含集合，坏的是运行时行为而不只是警告。

### 域 B — WinUI 抬 net11.0（门禁 3 覆盖，本阶段最大未知）

WASDK 端到端（XAML 编译器 / CsWinRT 投影 / `WinRT.SourceGenerator`）静态证据说无 TFM 门禁，但**从未实测**。另有 `Microsoft.Windows.SDK.NET.Ref` 解析版本可能变化、self-contained + unpackaged 能否正常起窗两条。

### 域 C — 运行时行为（.NET 10 → 11 换了运行时）

三关全绿**不等于**行为不变。这一域只能靠 5 条真实音频 + GUI 走查覆盖，没有自动回归能替代。

### 回退

- 硬证据：net10.0 targeting pack（10.0.15）在 SDK 10 卸载后仍在盘上，SDK 11 能构建 net10.0 目标 → **回退不需要重装 SDK 10**。
- 分支上实质改动只有 4 行 TFM + 1 行版本号 + 3 处注释，`git revert` 成本极低。

### 域 B 不通时的处置流程

**不擅自降级路线。** 停下来给用户三样东西：① 门禁 3 的真实错误原文与失败阶段；② 静态取证与实测的差在哪；③ 三个候选路线的代价（WinUI 退出门禁 / 升 WASDK 版本 / 整体回退到 net10.0）。**由用户选。**

## 5. Rider 红线处置

定性见 §2.3：产品代码正确，是 IDE 侧诊断。

**处置顺序（IDE 侧，从轻到重）**：Invalidate Caches / Restart → 仍在则检查 Rider 的 MSBuild/Roslyn 是否指向 SDK 11 → 仍在则记录为已知误报，按需抑制该条 inspection。

**抬完 TFM 后必须重新观察**：Rider 会重载工程，红线可能自行消失也可能变样，不能沿用升级前的结论。

**三条不做**：
- 不为消红线改 `[NotifyPropertyChangedFor]` 的写法（合并特性、改 partial property 等）——那是拿产品代码去补 IDE 的缺陷。
- 不加 `#pragma warning disable`（仓库当前 pragma 数为 **0**，是登记在册的纪律）。
- **不用 `NoWarn` 压 NU1510**——若真触发，正确做法是移除冗余包引用，不是压警告。

结论与证据归档进 `PHASE28-ACCEPTANCE.md` 的已知边界。

## 6. 实施顺序与执行纪律

1. 构建前确认没有运行中的 `D-player.exe` / `D-player.WinUI.exe`——它们会锁 `bin\Debug\...\D-player.exe` 与 `D-player.Core.dll`，表现为 MSB3021/MSB3026/MSB3027，容易被误读成代码错误。**若有，请用户关闭，不擅自杀用户正在用的实例。**
2. 一次改完 §3.1 的 5 处 + §3.2 的 3 处注释。
3. 跑 `verify-gates.ps1 -Full -Audio`，逐关记录真实读数。
4. 域 A/B 全绿后做 GUI 走查与独立核对项（§7）。
5. 文档同步 + `PHASE28-ACCEPTANCE.md`，第二批提交。
6. 回来找用户决定推送。

**四条执行纪律**

- **走查必须启动新输出目录的 exe**。抬 TFM 后产物在 `bin/Debug/net11.0-windows/`，旧的 `net10.0-windows/` 目录**不会自动消失**（bin/obj 是 gitignored）。启到旧路径就等于在 .NET 10 运行时上做"升级验收"，整份证据作废。走查前核对 exe 路径与文件时间戳。
- **改 `verify-gates.ps1` 注释保持纯 ASCII**（理由见 §3.2）。
- **长命令给足前台超时**。WinUI 抬 TFM 可能触发新 restore，历史首次约 8.1 分钟；包虽在缓存仍按不可预估处理。不用后台跑再轮询，也不靠 `TaskStop`——它在本机不杀进程树。
- **失败即停，不叠修复**。某一关红了就停下取证、报告，不在同一轮里连试多个修法。域 B 尤其要先分清是 TFM 门禁、XAML 编译、CsWinRT 投影还是 self-contained 起窗，再谈修法。

## 7. 测试与验收

统一入口：`powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-gates.ps1 -Full -Audio`，末行必须是 `all requested steps passed.`

| 关 | 命令 | 判据 | 升级前基线 |
|---|---|---|---|
| 门禁 1 | `dotnet build D-player.slnf -c Debug --nologo -v q` | 0 警告 0 错误 | 0/0（含 `--no-incremental`） |
| 门禁 2 | `dotnet test D-player.slnf -c Debug -v q --filter "Category!=AudioDevice"` | **310 通过 / 0 失败 / 0 跳过** | 310/0/0，2.104s |
| 门禁 3 | `dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q` | 0 警告 0 错误 | 域 B 首次实测 |
| 音频 | `dotnet test D-player.slnf -c Debug -v q --filter "Category=AudioDevice"` | 5 通过（需 WASAPI 设备） | 历史未实跑，本次要跑 |

**验收陷阱（写进纪律）**

- `verify-gates.ps1` 的警告数来自"实际跑了的编译"，全增量时不重编译就报不出警告（脚本第 118–120 行自己注明）。抬 TFM 会换输出目录所以天然全新编译，但验收时仍要**确认编译真的跑了**，否则"0 警告"是假绿。
- 两条测试命令**都不能加 `--nologo`**：MTP 不识别，会跑零个测试却打印"成功: 0"，退出码 5。
- MTP **零匹配时退出码是 8 不是 0**，写脚本不要把 8 当成功。

### 7.1 独立核对项（刻意不混在"310 通过"里，各自对应一个具体风险）

- GBK 编码 .m3u 在 net11.0 上真能解码（单测 + GUI 各一次）——域 A 的 CodePages 风险
- 输出目录变为 `bin/Debug/net11.0-windows/`，`D-player.exe` 可起
- `dotnet publish D-player.csproj -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true` 仍能产出——只验证能发布，不要求分发
- Core WPF-free 自检 grep 仍零命中：`grep -rn "System.Windows\|PresentationFramework\|ICollectionView\|CollectionViewSource" D-player.Core --include=*.cs | grep -v "/obj/\|/bin/"`
- 耦合审计 `tools/coupling-audit/Invoke-CouplingAudit.ps1` 仍可跑，`0 cycles` / `0 violations` 基线不变
- Rider 红线重新观察并记录结论——**由用户在 Rider 里确认红线是否仍在并告知**（代理读不到 IDE 的诊断面板，日志里也没有错误码）；若仍在，按 §5 的顺序处置

### 7.2 GUI 走查（回归性质，非新功能验收）

ComputerUse 子代理执行 + 主代理独立复核，按项目既有规范。关键路径：

起窗 → 播放/暂停/上一首/下一首 → 进度条单击与拖动 → 音量/静音 → 频谱 → EQ 打开/拖动/保存/取消 → 库四级钻取 + 搜索 → 库右键加入歌单 → 歌单拖拽重排 → **导入含 GBK 编码的 .m3u** → 关闭落盘 → 重启断点续播。

**合成库要求**：必须含一个 **GBK(936) 编码、路径带中文的 .m3u 样本**。否则域 A 的 CodePages 风险在 GUI 侧没被真正覆盖——单测通过不等于端到端通过，而这条风险的失效模式恰恰是"运行时抛"。

**数据保护**：验收前备份 `settings.json` / `queue.json` / `library-cache.json` 并记 SHA-256，用临时合成库隔离真实数据；结束后按字节还原并核对三文件 SHA-256 相等；临时库、备份、脚本与截图删除；不修改用户音乐文件。

## 8. 文档同步与提交边界

### 8.1 只改当前状态文档

| 文件 | 改动 |
|---|---|
| `README.md` | 3 处：.NET 10 → 11、TFM 行、先决条件 |
| `docs/PROJECT.md` | 11 处改 + 2 处刻意保留，逐条见下表 |
| `docs/COUPLING.md` | 页首阶段状态（第 3、5 行）+ §5 CodePages 契约行（第 202 行）的 net10.0 措辞。测试计数口径行（第 43、269 行）**不动**——310/315 未变 |
| `docs/README.md` | 文档地图加 Phase 28 链接 |
| `docs/PHASE28-ACCEPTANCE.md` | 新增：环境、四步实测读数、走查清单与结果、数据还原 SHA-256 核对、Rider 红线定性与证据、已知边界 |

`docs/PROJECT.md` 逐条口径（行号为 2026-11-12 实测，实施前复核）：

| 行 | 内容 | 处置 |
|---|---|---|
| 3 | 页首".NET 10 + NAudio" | 改 .NET 11 |
| 5 | 页首阶段状态与测试数 | 改为 Phase 28；测试数按本次实跑填 |
| 57 | §2 技术栈运行时行 | 改 net11.0 TFM |
| 58 | §2 共享层 `net10.0-windows` | 改 net11.0-windows |
| 80 | §3 目录树 global.json 注释".NET 10 SDK 原生 opt-in" | 改 .NET 11 SDK |
| 82 | §3 目录树 Core `net10.0-windows` | 改 net11.0-windows |
| **418、420** | **§4.3 第 27 条：Phase 20 抽 Core 的历史决策，含 `net10.0-windows`** | **刻意保留原文**——那是历史决策记录，改它等于篡改当时的取舍依据。如需澄清，在该条末尾加一句"（TFM 已于 Phase 28 抬至 net11.0）"，不改原句 |
| 527 | §5.3b `PlaylistFileEncoding` 的"net10.0 框架隐含" | 改 net11.0 口径 + 附本次实测结论 |
| 789 | §5.7 WinUI TFM | 改 net11.0-windows10.0.19041.0 |
| 1106 | §8.1 先决条件".NET 10 SDK" | 改 .NET 11 SDK |
| 1131 | §8.2 输出目录 `bin/Debug/net10.0-windows/` | 改 net11.0-windows |
| 1152 | §8.2 global.json MTP 说明".NET 10 SDK" | 改 .NET 11 SDK（机制不变，已实测有效） |
| 1201 | §9 NU1510 条目 | 改 net11.0 口径 + 附实测结论（是否仍框架隐含） |
| §10 | 文档索引 | 加 Phase 28 设计与验收链接 |

**判定规则（消除"当前状态 vs 历史记录"的二义性）**：`PROJECT.md` / `COUPLING.md` 里**描述当前状态**的段落改；**记录某阶段历史决策**的段落（如 §4.3 各条）保留原文，只允许追加一句指向 Phase 28 的括注。

### 8.2 不回改

`docs/superpowers/specs/*`（含本稿）、`docs/archive/*`、`PHASE20~27-ACCEPTANCE.md`、`PHASE20-COMPARISON.md`。历史结论保留在当时基线。

### 8.3 提交边界

- 设计稿（本文件）单独提交一次。
- 实施改动分两批：① 升级落地（§3.1 + §3.2）；② 文档同步（§8.1）。
- **`docs/superpowers/plans/` 下三份计划刻意未跟踪**（`docs/PROJECT.md:1239`：保留在本地工作区，不属于仓库发布面），**不提交**。
- **推送等用户明确指示。**

## 9. 限制与移交

### 9.1 本阶段的已知限制

- **发布形态的运行时依赖变了**：framework-dependent 发布（`--self-contained false`）现在要求目标机装 **.NET 11 Desktop Runtime**，不再是 .NET 10。WinUI 壳是 self-contained，不受影响。若需分发给未装 .NET 11 的机器，那是另一个决策（改 self-contained，体积大增），不在本阶段。
- **域 B 与源生成器加载仍是推断**：静态取证表明无 TFM 门禁，但端到端未实测。本阶段的核心交付之一就是把它变成实测结论。
- **Rider 红线的具体机制未定性**：只定性到"IDE 侧、非编译错误"，确切错误码未取得。
- **门禁 3 只覆盖编译**：WinUI 的 XAML 运行时模板挂接、`NavigationView` 左栏逻辑、`AppWindow.Closing` 落盘没有自动回归；抬运行时后这些只能靠真机手测。本阶段 WinUI 走查不在 GUI 清单内（壳已冻结），这是一条明示的覆盖缺口。

### 9.2 移交 Phase 29（非音频路径现代化）

- 附录 A：C# 15 已核实特性清单 + **纠正清单**（扩展成员 / `field` / partial 属性都不是 C# 15）。
- 附录 B：.NET 11 BCL 新 API 的**证据强度分级**——只有 `AsyncEnumerable` 的 join 系列有完整签名，其余是名字级证据，**用前必须再核实**。
- 附录 C：代码库候选盘点，含两个真缺陷（`RootGates` 无界增长、`FindAncestor` 三份副本中三份未处理 `ContentElement`）。
- 用户已定的门槛：**激进**，允许基础设施级替换；音频实时路径不在 Phase 29 范围内。

### 9.3 移交 Phase 30（音频实时路径性能重写）

- 音频路径现状事实：无 `ArrayPool` / ring buffer / `Buffer.BlockCopy`，只有构造期一次性定长数组 + `Read(Span<float>)` 就地处理；热点是每 FFT 帧 3 次 `ToArray()`（各 128B），而那是换取"订阅者不能篡改共享缓冲"的安全契约，且 `SampleAggregator` 无直接单测。
- **安全网缺口**：`docs/PROJECT.md:1184` 明确记载闸门侧回归（`LoadAsync_TwoOverlappingCalls_DoNotThrow` / `Unload_DuringLoadAsync_DoesNotThrow`）以 1–5ms 错位窗口断言"不抛异常"，是真实但**随机**的证据——窗口未命中时只是没触发竞态。意图标记侧只有一条确定性用例。
- 用户已定：**允许以性能为目标重写**。故 Phase 30 **必须先量基线、并把随机证据升级为确定性证据，再谈重写**。
- 不可动的纪律（`COUPLING.md` §5/§7）：`_chainGate` 串行化与"持锁期间不得 await"、`OnPlaybackStopped` 不得取锁、`_stopRequested` 意图标记、EQ `SetPeakingEq` 就地重算与每声道独立滤波器、EQ 插入点在 `SampleAggregator` 之前、频谱事件经 UI 线程封送、`ReferenceEqualityComparer` 身份回找。

---

## 附录 A：C# 15 核实结论（移交 Phase 29）

核实方法：本机 `csc 5.12.0` + 官方 `dotnet/docs` 的 `csharp-15.md`（`ms.date: 11/10/2026`）+ `dotnet/roslyn` 的 `Language Feature Status.md` + `csharplang/proposals/csharp-15.0/` 原文，并在系统临时目录建 5 个最小探针工程跨 TFM 交叉验证。**未使用 WebFetch**（本机实测它会在 JS 渲染页面上编造正文）。

### A.1 已 shipped（net11.0 默认即 C# 15，无需 preview）

| # | 特性 | net11.0 | net10.0 | ns2.0 | 运行时依赖 |
|---|---|---|---|---|---|
| 1 | Collection expression arguments（`[with(...), .. x]`） | 通过 | **通过** | **通过** | 纯语法糖 |
| 2 | Union types（`union`） | 通过 | **失败** | 未测 | **需 .NET 11 BCL** |
| 3 | Closed hierarchies（`closed`） | 通过 | **失败** | 未测 | **需 .NET 11 BCL** |
| 4 | Extension indexers（`extension` 块内 `this[int]`） | 通过 | **通过** | **通过** | 纯语法糖 |
| 5 | Labeled break/continue（`break outer;`） | 通过 | **通过** | 未测 | 纯语法糖 |
| 6 | 旧运行时上的接口静态非虚成员 | 通过 | 通过 | **通过** | 纯语法糖 |

**#2/#3 在 net10.0 下的实测报错**：`CS0518: 预定义类型 System.Runtime.CompilerServices.IUnion 未定义或导入`、`CS0656: 缺少编译器要求的成员 UnionAttribute..ctor / IsClosedTypeAttribute..ctor`。提案明确决议**编译器不合成这些类型**（`unions.md` L975-979），必须由引用程序集提供。已确认 `UnionAttribute` / `IUnion` / `IsClosedTypeAttribute` 存在于本机 `Microsoft.NETCore.App.Ref/11.0.0/ref/net11.0/System.Runtime.dll`。理论上可在 net10.0 手写 polyfill，但官方不支持、**未验证**。

**关键结论**：#1/#4/#5/#6 在 net10.0 甚至 ns2.0 + `LangVersion=15` 就能用，**不必为它们抬 TFM**。#2/#3 才是抬 net11.0 的语言级理由（状态机 / 播放模式建模），收益需权衡新增 BCL 依赖。

最小语法示例（逐字抄自官方 `csharp-15.md`）：

```csharp
// 1
List<string> names = [with(capacity: values.Length * 2), .. values];
HashSet<string> set = [with(StringComparer.OrdinalIgnoreCase), "Hello", "HELLO", "hello"];
// 2
public union Pet(Cat, Dog, Bird);
Pet pet = new Dog("Rex");
string name = pet switch { Dog d => d.Name, Cat c => c.Name, Bird b => b.Name, };
// 3
public closed record class GateState;
public record class Closed : GateState;
public record class Open(float Percent) : GateState;
// 4
extension(IEnumerable<int> sequence) { public int this[int index] => sequence.ElementAt(index); }
int third = numbers[2];
// 5
outer: for (int row = 0; row < grid.Height; row++) { for (...) { continue outer; /* 或 */ break outer; } }
// 6
public interface IMeasurement { public static double ToMeters(double centimeters) => centimeters / 100; }
```

### A.2 preview / 未 shipped

- **Memory safety（unsafe evolution）**：唯一 C# 15 preview。需 `<LangVersion>preview</LangVersion>` + `AllowUnsafeBlocks` + `<Features>updated-memory-safety-rules</Features>`，三者齐备才通过。含指针声明/`&`/`fixed`/`stackalloc`→指针/`sizeof` 不再需要 unsafe；新增 `unsafe(expr)` 与 `safe` 上下文关键字。**对本仓无用。**
- **Working Set 仍 In progress（均未进 C# 15）**：Dictionary expressions、Null-conditional await、Chained relational comparison、Target-typed static member access、Relax modifier ordering、Compound assignment in initializers、Extension members on typeless receivers、Extension constants、Type Parameter Inference from Constraints。
- **Runtime Async / Runtime Async Streams**：已 merge 到 main 但状态为 preview，**不在 C# 15 shipped 列表**。

### A.3 纠正清单（原本以为有、核实后不成立）

1. **扩展成员不是 C# 15 新特性**——`extension` 块（扩展属性/方法/静态成员）**C# 14 已 shipped**；C# 15 只新增**索引器**这一种成员。
2. **`field` 关键字是 C# 14**，C# 15 无任何后续增强。
3. **partial 属性 = C# 13；partial 事件/构造器 = C# 14**，C# 15 无 partial 成员增强。
4. **null-conditional assignment（`a?.b = c`）= C# 14**。
5. **params collections = C# 13；对象初始化器内隐式索引器 `[i]` = C# 13；`Lock` 对象 = C# 13；`nameof` 无界泛型 = C# 14**。C# 15 无 params / 索引范围 / nameof / lock / using 语句级增强。
6. **spread `..` 无 C# 15 增强**；集合表达式唯一新增就是 `with(...)`。
7. **C# 15 无模式匹配新语法**——穷尽性提升来自 union/closed 类型系统，不是新 pattern。
8. **C# 15 无插值/原始字符串增强**。
9. **C# 15 无任何 `INotifyPropertyChanged` / MVVM / 源生成器语言级支持**。MVVM 精简仍须靠 CommunityToolkit.Mvvm 源生成器。
10. **字典表达式（`[key: value]`）未 shipped**，仍在开发中。

### A.4 未能核实

- Roslyn releases API 返回 2021–2022 旧 release（未按时间倒序），未能从 release body 交叉验证；改以 docs + 本机编译器实证为准。
- union/closed 在 net10.0 手写 polyfill 是否可行——未测。
- 各特性在 **`net11.0-windows`**（WPF/WinUI 实际 TFM）下的行为——未测，仅测了 `net11.0` / `net10.0` / `netstandard2.0`。

来源：`raw.githubusercontent.com/dotnet/docs/main/docs/csharp/whats-new/csharp-15.md`；`dotnet/roslyn` 的 `docs/Language Feature Status.md`；`dotnet/csharplang` 的 `proposals/csharp-15.0/{unions,closed-hierarchies,collection-expression-arguments,extension-indexers,labeled-break-continue}.md`；`dotnet/roslyn` 的 `docs/compilers/CSharp/Compiler Breaking Changes - DotNet 11.md`；本机 ref pack 与临时探针工程。

## 附录 B：.NET 11 BCL 新 API 证据分级（移交 Phase 29）

**方法论坑（硬证据）**：`Microsoft.NETCore.App.Ref/11.0.0/ref/net11.0` 的 XML 文档与 `10.0.15/ref/net10.0` 在 **107 个共有文件中 100 个字节相同**，仅 7 个不同；但 ref **DLL 确实变大**（`System.Runtime` 862544→943912B、`System.Linq` 33576→34600B、`System.Text.Json` 82216→87848B）。**新 API 真实存在，但陈旧 XML 无法提供其签名。** 故分两级，**B 级用前必须再核实**。

### B.1 A 级：有完整签名的硬证据

来自 7 个已更新 XML 之一 `System.Linq.AsyncEnumerable.xml`。.NET 11 给 `System.Linq.AsyncEnumerable` 新增 join 运算符（各 4 重载，含同步/异步 keySelector）：

- `Join<TOuter,TInner,TKey>(IAsyncEnumerable<TOuter>, IAsyncEnumerable<TInner>, Func<TOuter,TKey>, Func<TInner,TKey>, IEqualityComparer<TKey>)`
- `GroupJoin<...>`（同参数形状）
- `LeftJoin<...>`、`RightJoin<...>`
- `FullJoin<TOuter,TInner,TKey>(...)` 及 `FullJoin<TOuter,TInner,TKey,TResult>(..., Func<TOuter,TInner,TResult>, ...)`（带 resultSelector）
- 异步重载用 `Func<TOuter,CancellationToken,ValueTask<TKey>>` 作 keySelector

10.0.15 的 `AsyncEnumerable` 无这些 join。其余新条目（`AsyncIterator<T>` / `SingleLinkedNode<T>`）为内部实现，已剔除。

### B.2 B 级：仅名称硬证据，签名与声明类型未验证

取证法：11.0 ref DLL 元数据 `#Strings` 堆出现、10.0.15 DLL 无、且全部 10.0.15 XML 文档均无此名（以此过滤 TypeRef 噪声，例如 `PopCount` 实为旧 API）。**声明类型按程序集 + 命名推断**：

| 程序集 | 新名 | 推断归属 |
|---|---|---|
| System.Linq | `FullJoin` | 同步 `Enumerable` 新运算符 |
| System.Runtime | `ToLowerOrdinal`、`ToUpperOrdinal`、`EnumerateUtf8`、`EnumerateUtf16` | `System.String`（配套新类型 `Utf8Enumerator`/`Utf16Enumerator`/`RunePosition`/`StringBuilderRuneEnumerator`） |
| System.Runtime | `ReadOnlyMemoryStream`、`WritableMemoryStream`、`StringStream`、`CreateHardLink`、`CreateAnonymousPipe` | System.IO |
| System.Runtime | `IndexOfInvalidSubsequence` | `SearchValues` |
| System.Runtime | `Decimal128`/`Decimal32`/`Decimal64` | 新数值类型 |
| System.Memory | `ContainsAnyWhiteSpace`、`LastIndexOfAnyWhiteSpace`、`LastIndexOfAnyExceptWhiteSpace` | `MemoryExtensions` |
| System.Memory | `ReadOnlySequenceStream`；`TryRead/WriteBFloat16BigEndian\|LittleEndian` | 新流类型；`BinaryPrimitives` |
| System.Runtime.Numerics | `BFloat16` | 新类型 |
| System.Text.Json | `SerializeAsyncEnumerable` | `JsonSerializer` |
| System.Text.Json | `JsonUnionAttribute`、`JsonUnionCaseInfo`、`JsonTypeClassifier`(+Context/Factory/Kind)、`JsonNamingPolicyAttribute`、`JsonUnionTypeStructuralClassifier` | union / 多态序列化 |

### B.3 明确未找到

- **新 `Argument` / 校验 helper**：System.Runtime 新名中无 argument/throw/validate/guard 匹配。
- **新异步 / 取消 API**：`System.Threading.dll` 新旧**零新名**；System.Runtime 新名仅 `HandleAsyncEntryPoint`（内部入口 helper）→ 无新 Task/CancellationToken 公共 API。
- **新集合 / 字典公共 API**：`System.Collections.dll` 无新 PascalCase 名（仅 `keyComparer`/`keySelector` 参数名，疑似新重载但方法名未能确定）。

局限：`#Strings` 后缀共享可能漏掉部分新名；B 级不能区分声明成员与引用名。未反编译 DLL。

## 附录 C：代码库现代化候选盘点（移交 Phase 29 / 30）

**行号来源说明**：本附录由只读盘点代理产出，行号取自其 Read/Grep 实际输出；**Phase 29 实施前须逐条复核**（代码可能已变）。本阶段不据此做任何改动。

### C.1 规模统计（排除 bin/obj/.worktrees/TestResults/WinUI）

| 目录 | .cs 文件数 | 总行数 |
|---|---|---|
| Core/Configuration | 2 | 108 |
| Core/Extensions | 1 | 76 |
| Core/Models | 14 | 300 |
| Core/Services（含 PlaylistFiles 8 文件 376 行） | 35 | 3045 |
| Core/ViewModels | 13 | 4290 |
| **Core 合计** | **65** | **7819** |
| 壳 Converters | 7 | 194 |
| 壳 Views（Controls 10/2192 + Dialogs 5/645 + MainWindow） | 16 | 3046 |
| 壳 D-player/Services | 1 | 46 |
| 壳根 App.xaml.cs + AssemblyInfo.cs | 2 | 79 |
| **Tests 合计** | **31** | **6918** |

ViewModels + Services 占 Core 94%；壳侧 Views 3046 行几乎全是无测试的 code-behind 重复。

### C.2 高价值候选（按收益/风险粗排）

| 候选 | 证据 | 规模 | 风险 |
|---|---|---|---|
| **六个手写 Null Object + internal 兼容构造器** | `PlaylistsViewModel.cs:110-113`、`:845-913`；唯一使用者 `Tests/ViewModels/PlaylistsViewModelTests.cs:47` | ~75 行 | **高价值低风险**：删构造器、测试改 NSubstitute（已在依赖） |
| **`FindAncestor<T>` 4 份副本** | `PlaylistView.xaml.cs:523`、`PlaylistsSidebarView.xaml.cs:207`、`PlayerBar.xaml.cs:74`、`LibraryView.xaml.cs:493` | ~90→35 行（含 `FindChildByName` 3 份、`ComputeInsertIndex` 2 份） | **真缺陷**：只有 LibraryView 那份处理 `ContentElement`，其余 3 份裸 `VisualTreeHelper.GetParent` 会抛（`PROJECT.md:1197` 记过同源崩溃）。无测试 |
| **`RootGates` 无界增长** | `LibraryRootScanner.cs:10-11,32`，static `ConcurrentDictionary<string,SemaphoreSlim>` | — | **真缺陷**：永不回收，按根路径无界增长 |
| 34+5 处 `?? throw new ArgumentNullException` | `PlaylistsViewModel.cs:98-105`(8)、`LibraryViewModel.cs:115-122`(7)、`MainViewModel.cs:54-59`(6)、`PlaylistViewModel.cs:109-112`(4) 等；已用 `ThrowIfNull` 14 处 | ~39 处 | 纯机械、行为等价 |
| 三个 Json* 服务重复样板 | 读全文 `StreamReader` 包 `FileStream`（`JsonLibraryCache.cs:46-48,89-91`、`JsonPlaylistService.cs:46-48`）；原子写 tmp+Move **三份** | 抽 `JsonFileStore` | 原子写是防歌单全丢的关键，**顺序一字不改** |
| 手写"一次性 cleanup Task"模板 ×3（完全同构） | `MainViewModel.cs:260-285`、`LibraryViewModel.cs:167-180` + `Scanning.cs:443-468`、`PlaylistsViewModel.cs:115-155` | ~90 行 | Core 公开面（签名不变），关闭时序被测试锁定 |
| 后缀白名单循环 3 处同构 | `PlaylistFileFormats.cs:32-36`、`DragDropExtensions.cs:35-42`、`PlaylistFileService.cs:80` | ~30 行 | 一个 `FrozenSet<string>(OrdinalIgnoreCase)`；DragDropExtensions 无测试 |
| 手写去重/建桶/字典差集 | `LibraryGrouping.cs:32-55`、`LibraryScannerService.cs:104-154` | ~70 行 | "先到者胜"+顺序语义被测试锁定，中风险 |
| 5 个私有桶类手写构造器 | `LibraryGrouping.cs:16-28`、`LibraryRefreshScheduler.cs:506-568` | ~75 行 | primary constructor / record；internal |
| `SanitizeFileName` 每字符 `Array.IndexOf` O(46) | `PlaylistViewModel.cs:484-495` | 12 行 | `SearchValues<char>` |
| `LibraryIdentity` 的 `RegexOptions.Compiled` | `LibraryIdentity.cs:14-16` | — | 源生成正则；Core 公开面 |
| `JsonSettingsPersistence` 每次 `new JsonSerializerOptions` | `JsonSettingsPersistence.cs:71-74`（另两个是 static 缓存） | — | 静态缓存 |
| 全仓无 `JsonSerializerContext` 源生成 | 三个服务均反射序列化 | 激进候选 | JSON 契约（v1→v2 迁移、`JsonStringEnumConverter`）被测试锁定 |
| `Clear()+foreach Add` 重建集合 | `LibraryViewModel.cs:346-351`、`Scanning.cs:47-58,160-174,272-291`、`PlaylistViewModel.cs:194-196`、`PlaylistsViewModel.cs:607-609,660-662` | ~50 行 | **ObservableCollection 改批量替换会改通知次数**，`LibraryViewModelTests.cs:321,667-681` 有计数断言 → 会红 |
| 手写 debounce ×2 / throttle ×2 | `MainViewModel.cs:224-253`、`LibraryViewModel.Search.cs:74-79,283-337`；`NAudioPlaybackService.cs:64-68,333-347`（`Task.Delay(33)` 外再套 `DateTime.UtcNow` 节流，**冗余**）、`PlayerViewModel.cs:210-237` | — | MainViewModel 无专测；音频邻接两处有专测 |
| `LibraryViewModel.Search.cs:21-51` 手写 INPC | 三个属性手写 backing field + `OnPropertyChanged()`（private setter 绕开源生成器） | 31 行 | 核实 Toolkit 8.4 partial property 能否覆盖 private setter |
| CTS/Semaphore 未释放 | `LibraryViewModel.cs:28-29`、`Search.cs:17`、`FileSystemLibraryWatcher.cs:12`、三个 Json* 的 `_lock`；`LibraryViewModel`/`PlaylistsViewModel` 都不实现 `IAsyncDisposable` | — | 只有 `MainViewModel.cs:21` 实现 |

**全仓 0 命中**（激进门槛下最集中的基础设施级空白）：`FrozenDictionary` / `FrozenSet` / `SearchValues` / `PeriodicTimer` / `GeneratedRegex` / `JsonSerializable` / `System.Threading.Lock`。

**已经是现代写法、不要"顺手改"**：`LibraryRefreshScheduler` 的 Channel + `TimeProvider` + `ITimer` 骨架（时钟可注入、`ManualTimeProvider` 精确测时序）；Core 已普遍用 collection expression；无 `string.Format`、无手写 mm:ss（`TimeSpanToStringConverter.cs:19-20` 已用格式串）；`Extensions/` 只有 1 个 DI 注册文件，**无与 BCL 重复的扩展方法**；无手写 `ICommand`（31 处 `[RelayCommand]`）；无 finalizer / `Dispose(bool)` 模板。

### C.3 盘点判定不值得动的地方

1. `NAudioPlaybackService.cs:42`（`_chainGate`）、`:54`（`_stopRequested`）、`:275-319`（`OnPlaybackStopped`）——死锁纪律与"用户停止 vs 自然播完信号相同"的物理事实，两个专测锁定语义。
2. `EqualizerSampleProvider.cs:22-27,42-63,69-95`——lock 粒度 = 一个 buffer；`volatile bool` 锁外读是刻意权衡；`SetPeakingEq` 就地重算保留延迟线防爆音；Nyquist 旁路。
3. `SampleAggregator.cs:47-88,94-140`——DSP 热路径；每帧 3×128B 分配换取"订阅者不能篡改共享缓冲"的安全契约，不值得为省这点分配改公开事件签名（且无直接单测）。
4. `LibraryRefreshScheduler` 的 debounce/retry 时序常量——不要换成通用策略引擎。
5. `PlaylistViewModel.cs:500-509`（MoveTracks 引用身份算法）与 `PlaylistView.xaml.cs:304-314`——Track 是 record，结构相等会让同一文件的两份占位互相覆盖。
6. `PlaylistFileEncoding.cs:21-24,39-46`——静态构造注册 CodePages + 严格 UTF-8 试解码失败回退 GBK，是中文 Windows 上 .m3u 的现实兜底。
7. `JsonPlaylistService.cs:100-106,130-168`——原子写 + v1→v2 迁移 + 越界 `CurrentIndex` 钳制，防用户歌单全丢。抽公共助手可以，语义一字别改。
8. `AtlMetadataReader.cs:21` 的 `Task.Run`——第三方 ATL 无异步 API，这是正确写法。
9. `FileSystemLibraryWatcher.cs:123-150` 的三重复查与 `LibraryScanLease` 的 revoke 语义——看似冗余，实为竞态兜底。
10. `MainWindow.xaml.cs:33` / `PlayerViewModel.cs:130` 构造期同步读盘——小文件、启动路径、注释已权衡；改异步构造器会牵动整个启动时序。
11. `DropInsertionAdorner.cs`（74 行）——标准 WPF Adorner，无现代替代品。
12. `SpectrumView.xaml.cs:109-127` 的 `HsvToRgb`——BCL/WPF 均无 HSV→RGB，且只在初始化跑 32 次，非热路径。
13. 五个 async 临界区的 `SemaphoreSlim`（三个 Json* + watcher + 封面闸门）——`System.Threading.Lock` 不能 await，**不要换**。
14. `PlaylistViewModel.cs:121-127` 在 `CollectionChanged` 里手动 `NotifyCanExecuteChanged()` ×3——Toolkit 无集合感知特性，保留。

### C.4 测试覆盖缺口（Phase 29 改动前须知）

**无直接测试**：`SampleAggregator`、`AtlMetadataReader`、`LegacyDataMigration`、`LibraryScanLease`（间接）、`Stub*`、全部 `Converters` 与 `Views`。`MainViewModel` 仅经 `PlaybackResumeTests` / `AddDPlayerCoreTests` 间接覆盖。

壳侧 Views 3046 行几乎全是无测试的 code-behind 重复 → 该处的合并重构**必须靠 GUI 走查兜底**，不能只靠门禁 2。