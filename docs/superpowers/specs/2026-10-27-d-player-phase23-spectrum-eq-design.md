# Phase 23 设计：WinUI 频谱可视化与 10 段均衡器

- 日期：2026-10-27
- 状态：已评审的范围与方案（用户已拍板：频谱 + EQ 同阶段、频谱放信息栏底部、底栏独立 EQ 钮、Canvas + 32 Rectangle 绘制）
- 前置：Phase 22 功能补齐与验收已完成；TrackEnded 竞态已修；WinUI 折叠钮/底栏/滑块提示/窄窗音量弹层已交付

## 1. 目标与非目标

把 WPF 壳已有的两类音频能力补到 WinUI 壳，且不改 WPF 行为：

- 频谱：32 柱实时频谱、四主题、灵敏度/平滑度/启用开关，放在 InfoPanel 内容底部。
- EQ：底栏独立 EQ 钮 + 独立对话框（启用、9 预设 + Custom、Preamp + 10 段、恢复 Flat / 取消 / 保存）。

非目标：

- 不改 Core 的 FFT、EQ 处理链、公开数据模型（数据与处理已 WPF-free）。
- 不引入 Win2D 或任何新图形库；不改 WPF 壳的任何行为与外观。
- 不做拖拽、表头排序、文件夹绑定歌单管理（留 Phase 24）。
- 不给 WinUI 加自动化 UI 测试（WinUI 不在 `D-player.slnf`，验收仍靠门禁 + 真机 UIA/手测）。

## 2. 现状事实（实施时以此为准，不再重新论证）

### 频谱

- `PlayerViewModel.SpectrumData`：`float[32]`，对数频桶（20Hz–16kHz）归一化幅度，已乘灵敏度并做 EMA 平滑；`SpectrumDataAvailable` 由 FFT（8192 点、50% 重叠）驱动，44.1kHz 下约 10.8 次/秒，渲染帧率与之解耦。
- `PlayerViewModel` 可观察属性：`SpectrumEnabled`（默认 true）、`SpectrumSensitivity`（默认 1.0，范围 0.5–2.0）、`SpectrumColorTheme`（默认 0）、`SpectrumSmoothing`（默认 0.8，范围 0–0.95）。`On…Changed` 已负责写回 `AppSettings` 并持久化。
- `PlayerViewModel.SpectrumColorThemes`：`["紫色","蓝色","绿色","彩虹"]`，供设置下拉直接使用。
- WPF `SpectrumView` 的形态：`Canvas` + 32 个圆角 `Rectangle`，柱距 2、高度 2–118，`CompositionTarget.Rendering` 驱动高度插值（系数 0.3）。WinUI 3 的 `Microsoft.UI.Xaml.Media.CompositionTarget.Rendering` 已确认存在（本机 WinAppSDK 2.3.9/2.5.1 元数据）。

### EQ

- `IPlaybackService.EqualizerConfig { get; set; }`：NAudioPlaybackService 的 setter 调 `_equalizer?.Update`，链路为 解码 → `EqualizerSampleProvider` → `SampleAggregator`；禁用时旁路。
- `EqualizerConfig`：`Enabled / PreampDb / BandGainsDb / Preset / PreampLinearGain / Create`。
- `EqualizerPresets`：`CenterFrequencies`（31/62/125/250/500/1k/2k/4k/8k/16kHz）、`All / Names / TryGet / Match`；9 个预设（Flat、Rock、Pop、Jazz、Classical、Dance、Bass Boost、Treble Boost、Vocal）+ Custom。
- `PlayerViewModel.EqualizerEnabled`：可观察启用态，`OnEqualizerEnabledChanged` 同步播放链并持久化，供底栏按钮高亮。
- `AppSettings` 的 EQ 字段：`EqualizerEnabled / EqualizerPreamp / EqualizerBands / EqualizerPreset`，`PlayerViewModel.LoadEqualizerSettings` 已在启动时下发到播放服务。

### 壳侧接线

- WinUI `SettingsDialog(PlayerViewModel)` 目前只有音量/输出/日志；新增频谱行可直接读写上述 `PlayerViewModel` 属性（VM 自动持久化）。
- WinUI `App` 没有公开 `GetService<T>`；`MainWindow` 目前拿 `MainViewModel` 与 `ISettingsPersistence`，`PlayerBar` 拿 `PlayerViewModel` 与 `PlaylistsViewModel`。EQ 需要 `IPlaybackService` 与 `ISettingsPersistence`，按 §4 的构造链传入。
- WinUI 已有 `PathIcon` 可用；WPF `Themes/Icons.xaml` 的 `Icon.Equalizer` 矢量路径可直接复用。

## 3. Task 1：频谱与设置

### 3.1 SpectrumView（新文件 `D-player.WinUI/Views/SpectrumView.xaml(.cs)`）

- 形态：根 `Canvas`，代码里创建 32 个圆角 `Rectangle`（半径 2），柱距 2px；高度按 `SpectrumData[i]` 映射到 2–118px，帧间以 0.3 系数向目标高度插值（与 WPF 一致）。
- 数据：构造收 `PlayerViewModel`；`SpectrumData` 变化只更新目标高度数组，渲染事件（`CompositionTarget.Rendering`）负责逐帧逼近，与 FFT 更新频率解耦。
- 主题：`SpectrumColorTheme` 0/1/2 为竖向渐变刷（紫/蓝/绿），3 为逐柱红→紫彩虹刷；主题切换时重建画刷，不重建柱子。
- 生命周期：`Loaded` 订阅 `Rendering`，`Unloaded` 退订；`SizeChanged` 重算柱宽与 x 坐标。无音乐/无数据时柱子保持最小高度 2px。
- 布局：InfoPanel 的 `ContentHost` 末尾（采样率行之后）新增本控件，`Height=120`、`Margin="0,8,0,0"`，`Visibility` 随 `SpectrumEnabled` 与右栏现有折叠/淡出逻辑（右栏收起时内容不可见，不另做处理）。
- 依赖：只用 WinUI 自带类型，不加 NuGet。

### 3.2 SettingsDialog 追加频谱区

在现有音量行之后追加四行，读写 `PlayerViewModel`（持久化由 VM 负责）：

- 频谱启用 `CheckBox` ↔ `SpectrumEnabled`。
- 灵敏度 `Slider` 0.5–2.0（步进 0.05）↔ `SpectrumSensitivity`，旁显当前值。
- 主题 `ComboBox`（项源 `SpectrumColorThemes`）↔ `SpectrumColorTheme`。
- 平滑度 `Slider` 0–0.95（步进 0.05）↔ `SpectrumSmoothing`，旁显当前值。

对话框高度随内容自然增长；不引入新设置字段，不改 Core。

## 4. Task 2：EQ 按钮与对话框

### 4.1 依赖传入（构造链微调，壳侧）

- `App.OnLaunched` 额外解析 `IPlaybackService`，以第三参传给 `MainWindow`。
- `MainWindow` 把它与已有的 `ISettingsPersistence` 一并传给 `PlayerBar`。
- `PlayerBar` 构造签名变为 `(PlayerViewModel, PlaylistsViewModel, IPlaybackService, ISettingsPersistence)`；其余三个控件签名不变。

### 4.2 底栏 EQ 钮

- 位置：右侧 `StackPanel` 内、`SettingsButton` 之前。
- 图标：复用 WPF `Icon.Equalizer` 的矢量路径（`PathIcon`），默认次级色；`Player.EqualizerEnabled == true` 时 Accent 色。
- 行为：点击打开 `EqualizerDialog`（`ContentDialog`，`XamlRoot` 取 PlayerBar 的，与 `SettingsDialog` 同模式）；启用态变化只刷新高亮，不自动弹窗。
- 空格守卫沿用现有规则：EQ 钮是普通 Button，不抢空格。

### 4.3 EqualizerDialog（新文件 `D-player.WinUI/Views/Dialogs/EqualizerDialog.xaml(.cs)`）

构造：`(IPlaybackService, ISettingsPersistence, PlayerViewModel)`。

内容：

- 启用 `CheckBox`（初值 `Player.EqualizerEnabled`）。
- 预设 `ComboBox`：9 预设 + Custom，初值取当前 `IPlaybackService.EqualizerConfig.Preset`（不在预设表内则 Custom）。
- 11 个竖直滑块：Preamp + 10 段，范围按 WPF 同语义（Preamp −12…+12dB、各段 −12…+12dB），频率标签用 `EqualizerPresets.CenterFrequencies`。
- 按钮：恢复 Flat / 取消 / 保存。

行为：

- 打开时快照当前 `EqualizerConfig` 与 `Player.EqualizerEnabled`。
- 任一滑块/启用/预设变化：立即写 `IPlaybackService.EqualizerConfig` 实时预览；预设选择把该预设的 Preamp/10 段写入滑块；手动改动任一段后预设标记为 Custom（用 `EqualizerPresets.Match` 回认预设）。
- 恢复 Flat：把 Flat 写入滑块并实时下发（仍只是预览，未保存）。
- 取消/关闭：恢复快照到 `IPlaybackService.EqualizerConfig`，并把 `Player.EqualizerEnabled` 恢复为打开时状态；不写设置。
- 保存：以当前预览值更新 `AppSettings`（`EqualizerEnabled / EqualizerPreamp / EqualizerBands / EqualizerPreset`），同步 `Player.EqualizerEnabled`，然后关闭。播放链已是预览态，不重复下发。
- 启用 CheckBox 同时驱动 `Player.EqualizerEnabled`（保证底栏高亮即时一致）；取消时随快照回滚。

### 4.4 边界

- 无当前曲目时也可打开并编辑，配置在下次播放生效（与 WPF 一致）。
- 对话框为 ContentDialog，竖直滑块用 WinUI 默认 `Slider Orientation="Vertical"`；弹层尺寸由内容决定，不做拖拽。
- 异常只写 `AppLog`，不打断播放。

## 5. 验收、测试与文档

### 5.1 门禁（每个 Task 结束都跑）

- `dotnet build D-player.slnf -c Debug --nologo -v q`：0/0。
- `dotnet test D-player.slnf -c Debug -v q`（不带 `--nologo`）：186/0。
- `dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q`：0/0。
- `powershell -File tools/verify-gates.ps1 -Full`：3/3。

Core 公开面没有新增成员，测试总数不变；如实现中不得不动 Core，先回到本 spec 增补并跑全量回归。

### 5.2 真机验收清单

- A 频谱：播放中 32 柱跳动；四主题切换即时换色；灵敏度/平滑度改变有可见差异；关闭启用后整区隐藏且重开恢复；右栏折叠/展开不残留；重启后设置保留。
- B EQ：底栏按钮启用高亮；打开对话框当前值正确；拖段/改 Preamp 实时生效（用户听感确认）；选预设写入各段；手动改段变 Custom；恢复 Flat 归零；取消后听感与打开前一致；保存后重启仍保留；无曲目时也能编辑且不报错。
- C 回归：上一首/下一首/随机/循环、音量条与窄窗悬停弹层、折叠钮、进度拖动与提示、空格播放/暂停、设置对话框原有行均不回退。

### 5.3 文档

- 新建 `docs/PHASE23-ACCEPTANCE.md` 记录逐项结果。
- `docs/PROJECT.md`、`docs/COUPLING.md`、`README.md` 补频谱/EQ 能力与契约；`docs/PHASE20-COMPARISON.md` §3 的频谱/EQ 缺口销项。
- 实施计划：`docs/superpowers/plans/2026-10-27-d-player-phase23-spectrum-eq-implementation.md`（Task 1 频谱 → Task 2 EQ → Task 3 验收与文档）。

## 6. 风险与缓解

- **逐帧 32 柱更新成本**：与 WPF 同量级；插值只改 `Height`，不逐帧分配画刷/柱子。若实测掉帧，先把渲染降到 30fps 再议，不预先优化。
- **InfoPanel 高度压力**：已有 `ScrollViewer`；小窗高下频谱可滚动到，不裁切采样率/文本。
- **ContentDialog 竖滑块模板差异**：用 WinUI 默认模板，不自定义；若默认值异常，先在验收记录，再考虑局部样式。
- **无自动 UI 回归**：所有新增交互按 §5.2 真机走查并留 UIA/截图证据，不拿“构建通过”冒充行为正确。
