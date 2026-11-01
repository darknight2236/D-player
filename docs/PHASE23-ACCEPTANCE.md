# Phase 23 验收记录：WinUI 频谱与 EQ

## 范围与基线

- 日期：2026-10-28 首记；2026-10-29 补充 C1/听感实测与宽窗静音缺陷修复；2026-11-02 用户确认 B、C 组全部通过并完成 A、B 组仪器化复验。状态：**实现完成 / 验收通过（2026-11-02）**；平台级缩放撕裂见 2026-11-01 一节，不阻塞 Phase 23。
- 实现基线：Task1 `af2e944`（含暂停守卫）；Task2 `898f0a7`。2026-10-29 缺陷修复只改 `PlayerBar.xaml.cs` 一处（未改 Core/WPF/包）；此前的记录与文档提交不含代码改动。
- [实施计划 Task3](./superpowers/plans/2026-10-27-d-player-phase23-spectrum-eq-implementation.md)；[设计](./superpowers/specs/2026-10-27-d-player-phase23-spectrum-eq-design.md)。原计划不回改，勘误见 B 组。
- 依据：[Task1 报告](../.superpowers/sdd/2026-10-27-d-player-phase23-spectrum-eq-implementation/task-1-report.md)、[Task2 报告](../.superpowers/sdd/2026-10-27-d-player-phase23-spectrum-eq-implementation/task-2-report.md)、[Task3 报告](../.superpowers/sdd/2026-10-27-d-player-phase23-spectrum-eq-implementation/task-3-report.md)。SDD 被 Git 忽略，链接仅本地可用；A/B 结果为报告转录，本次没有重跑整组或逐张复核旧截图。
- [Phase 21](./PHASE21-ACCEPTANCE.md)、[Phase 22](./PHASE22-ACCEPTANCE.md) 是历史验收；[Phase 20 对比材料](./PHASE20-COMPARISON.md) §2 是历史快照、§3 是当前能力。历史 PASS 不自动成为 Phase 23 PASS。

## A 频谱

实现已交付：InfoPanel 底部 32 柱、四主题、启用/灵敏度/平滑度；Loaded/Unloaded 对称订阅，逐帧高度插值及 Top 更新，不逐帧建柱或画刷。Paused/Stopped 将目标设 2px，Loaded 非 Playing 不读旧数据，晚到 SpectrumData 仅在 Playing 时接受。

| 项 | Task1 证据 | 2026-11-02 仪器化复验 |
|---|---|---|
| A1 播放/暂停回落 | Task1 最终修复复测 PASS（沿用）：`a1-review-fix-final-frames.txt` / `a1-review-fix-measurements.txt`，175ms 起至 966ms 稳定 2px，识别 32 柱；原暂停失败保留于旧报告，不再列为未修实现缺陷 | PASS：播放态峰值 63px（32 柱，1k 柱 index 18）；暂停 150ms→2px、400/800/1200ms→1px（min）；`a1-*.png` |
| A2 紫/蓝/绿/彩虹即时切换 | Task1 PASS（沿用）：`a2-purple.png` / `a2-blue.png` / `a2-green.png` / `spectrum-rainbow.png` | PASS：紫 127,82,255 / 蓝 40,153,243 / 绿 81,177,85；彩虹峰 0,255,230，柱 0/8/16/24/31 = 红(255,0,0)/黄绿(175,248,1)/青(0,255,148)/蓝(2,32,243)/品红(235,3,236)；即时切换；`a2-*.png` |
| A3 灵敏度/平滑度 | Task1 PASS（沿用）：灵敏度 2/0.5 对比、平滑度 0/0.9 连续帧与 UIA 读回 | PASS：灵敏度 0.5→maxH 30px、2.0→103px；2Hz 调幅音下平滑度 0 的 maxH 跨度 18px（36..54）、0.9→3px（43..46）；`a3-*.png` |
| A4 启用与重启持久化 | Task1 PASS（沿用）：`a4-disabled.png` / `a4-enabled.png` / `a4-persisted-settings.txt` | PASS：关闭→bars 0/磁盘 False；开启→bars 32/磁盘 True；重启启用→bars 32/checkbox On；重启禁用→bars 0/checkbox Off；`a4-*.png` |
| A5 折叠/窄窗/小窗高滚动 | Task1 修复负柱宽后 PASS（沿用）：`a5-narrow-collapsed.png` / `a5-wide-restored.png` / `a5-small-height-scrolled.png` | PASS：折叠 scrollW 260→48、trackRight 1332→1544、柱体 32→0；手动展开优先（950 仍 260）；清除手动后 950→48、1200→260；460 高顶部 bars 0，滚到 100% 后 32 柱可见；`a5-*.png` |

上述 A 组证据不是 Task2 合入工作树之后的全组重验；2026-11-02 已重跑 A1–A5（WinUI Debug / Windows App SDK 2.6.4 / 96 DPI，窗口 1200×700，1kHz 稳态与 2Hz 调幅测试音，截屏 + 像素分析：基线虚线定位、32 柱底边连通段、柱高与颜色采样），结果见右列；Task1 证据保留作历史对照。证据在 `%TEMP%\p23-inst\a\`。

## B EQ

实现：底栏描边 EQ 入口，10 段 + Pre（−12…+12dB），9 内置预设 + Custom；实时预览、Flat 归零、取消恢复、保存与失败提示、保存中关闭守卫。

**原计划勘误**：预览/取消写 `Player.EqualizerEnabled` 会触发 Core setter 下发与持久化，原示例错误。当前打开时深拷贝实时 service 配置，不用 LoadAsync 的磁盘配置覆盖；预览仅写 service 并发壳侧高亮事件；取消/未保存关闭恢复打开时实时快照，VM 未被预览修改，无需写 setter 恢复。完整配置 UpdateAsync 成功后才同步 VM。启用值变化时实测 2 次写盘（UpdateAsync + VM setter 追加同值持久化），不宣称单次写盘。保存前冻结值，保存中禁止编辑/重复保存/ESC/主窗清理，失败留窗并内联提示/记录日志；这些防护的动态故障测试已在 2026-11-02 仪器化复验中覆盖（写入计数/失败注入/慢解析守卫）。

| 项 | Task2 已有证据 | 2026-11-02 最终状态 |
|---|---|---|
| B1 初值与启用高亮 | UI 可观察部分 PASS：初值启用/Custom/Pre=2/首段8其余0；预览禁用变次级色，取消恢复 | PASS（用户手测 + 仪器化）：UIA 11 滑块/启用/预设与 settings.json 全字段一致；取消无写盘。服务内部字段未直接读取（对话框快照/磁盘/输出交叉验证）。 |
| B2 Rock / Custom / Flat | 预设与键盘部分 PASS：Rock=[5,4,3,1,-1,-1,0,2,3,4]；首段键盘5→6变Custom；Flat含Pre全0 | PASS（用户手测 + 仪器化）：Rock/Custom/Flat 保存/重开；磁盘注入 `Bogus` → 对话框 Custom、保存写回 Custom；外部数组隔离/未知/空白预设有单测（仪器化时 190/0/0；精简后默认 176/0/0 + 音频 5/0/0）。 |
| B3 播放中实时调节 | PARTIAL（用户实测 2026-10-29）：播放中用真实鼠标拖动频段与 Pre，听感有变化；Task2 键盘路径 Pre 2→−1、同曲进度继续；`task-2-b3-preamp-key.png` | PASS（用户听感 + 仪器化）：回环 1kHz +12dB 预览 3.981×、取消 1.000×；播放中保存位置继续推进。 |
| B4 取消/保存/重启 | UI 可见值 PASS：取消恢复原四字段，保存禁用/Flat/Pre−1/十段0并重启保留，随后UI恢复原值并重启核对 | PASS（用户手测 + 仪器化）：取消 0 写、启用不变 1 写、启用变化 2 写；锁文件失败留窗+报错+哈希不变；150MB 慢解析下保存中禁用；重启读回一致。物理慢 I/O 未注入。 |
| B5 无曲目/播放中 | PARTIAL：播放中开关不切歌不停播；暂停态保存不自启 | PASS（用户手测 + 仪器化）：空歌单保存不自启；播放中保存位置 0.3280→0.3402→0.3603。 |
| B6 既有功能 | PARTIAL：Task2按钮焦点Space播放/暂停、设置打开关闭已观察；窄窗悬停弹层与宽窗静音点击已由 2026-10-29 专项复验（见文末） | PASS（用户 2026-11-02 手测：全局 Space、按钮焦点 Space、设置行、窄窗回归）；无独立仪器化（键盘/焦点交互）。 |

Task2 主要 B 交互发生在最后“手选 Custom 同步标记”补充之前；补充后仅四门禁及原值重开确认。→ 2026-11-02 用户在最终构建上手动复验 B1–B6 全部通过，并完成 B1–B5 仪器化复验（见 2026-11-02 仪器化复验）：实时/磁盘一致性、未知预设、外部数组、故障注入均已覆盖；物理慢 I/O 与内部字段直读仍为边界。

## C 回归

2026-10-28 那轮使用 computer-use SDK；没有 PowerShell UIA、没有直接访问 AppData。只完成非破坏设置冒烟，**未执行完整 C 组**，不是工具不可用，也不以静态未改替代实测。2026-10-29 依据用户实测结果补充 C1，并用窄窗专项脚本复验音量弹层修复。2026-11-01 用户确认 C1–C3 通过；2026-11-02 确认 C4，C 组收口。

| 项 | 2026-10-28/29 快照 | 2026-11-02 最终状态 |
|---|---|---|
| C1 播放控制/进度/音量 | PARTIAL（用户实测 2026-10-29）：双击/首播、上一首/下一首、随机/循环三态、进度拖动/单击/时间提示均正常；宽窗点静音误弹层、窄窗点静音被吞均已修复（见文末），修复后宽窗点击无弹层、窄窗弹层保持时点击可切换静音 | PASS（用户 2026-11-01 手动复验：播放控制、进度、音量滑块与设置内音量均已覆盖）。 |
| C2 布局/移除 | NOT VERIFIED；UIA观察到双栏按钮、表头、行与移除入口，不等于布局验收 | PASS（用户 2026-11-01 手动复验：双栏折叠、<960 自动收起与手动优先、视觉对齐、移除确认框取消）。 |
| C3 Space/设置/日志 | PARTIAL：打开设置成功，音量55%、灵敏度0.50、平滑度0.90、输出与日志按钮可见；焦点在设置音量滑块按Space，进度仍02:20/03:00、音量55%；点击完成关闭 | PASS（用户 2026-11-01 手动复验：Space/设置/日志入口与焦点组合）。 |
| C4 Full门禁 | PASS（2026-10-28 本次实跑）：Full 3/3，exit 0；slnf 0警告/0错误（1.24s），测试186成功/0失败/0跳过（2.916s），WinUI 0警告/0错误（11.77s）；2026-10-29 修复后复跑仍 3/3（测试 186/0/0） | PASS（代理 2026-11-01 实跑 -Full 3/3；2026-11-02 仪器化新增 4 条测试后快速门禁 190/0/0；随后精简为默认 176/0/0 + 音频 5/0/0（`-Full -Audio` 3/3）；WinUI 构建同一生产代码 0/0）。后续改动仍跑 -Full。 |

C3 另观察：设置音量滑块按 Space 出现原始值工具提示 `0.5500`，而行标签是55%。此次没有实测底栏拖动提示，不据此判定底栏回退；设置提示是否需统一百分比留后续确认，本任务不改代码。

## 2026-10-29 缺陷修复：静音钮与纵向音量弹层

用户实测发现：横向音量条仍显示（宽窗）时按下静音钮，纵向音量弹层也会出现。

### 1. 宽窗点静音误弹层

- 根因：弹层以 `Button.Flyout` 挂在静音钮上。WinUI `Button` 在点击/键盘激活时会**无条件打开**自己的 Flyout，这条路径不经过 `UpdateVolumeFlyout` 的“仅横向音量条收起时才算窄窗”判定；`PointerEntered` 定时器只能管住悬停，管不住点击。
- 修复：`PlayerBar` 构造时摘除 `MuteButton.Flyout`（`D-player.WinUI/Views/PlayerBar.xaml.cs`），弹层只由窄窗悬停路径 `ShowAt(MuteButton)` 打开；点击/空格/回车仍是静音切换，与 XAML 注释“点击仍是静音/取消静音”一致。未改 Core/WPF/包。
- 验证：宽窗（1231）UIA Invoke 点击静音 → 只出现提示气泡，未出现 `VolumePopupSlider`；随后再次点击恢复静音状态。窄窗（950）脚本复验：横向 `VolumeSlider` 不在树中，悬停静音钮 1.3s 后 `VolumePopupSlider` 出现且 `offscreen=False`，指针移开后消失；`mute-fix-narrow-hover.png` / `.txt`。窗口几何临时缩至 950 宽后还原为 1231×602@572,294。
- 门禁（第一轮修复后）：`.slnf` 0 警告/0 错误；测试 186 成功/0 失败/0 跳过；WinUI 0 警告/0 错误；`tools/verify-gates.ps1 -Full` 3/3，末行 `all requested steps passed.`。

### 2. 窄窗点静音无反应且弹层反复重开

摘除 `Button.Flyout` 后用户复测窄窗：悬停弹出弹层，点击静音钮不静音，弹层先消失又立刻弹出。

- 根因：Flyout 打开时自带 light-dismiss 覆盖层，锚点上的鼠标点击被覆盖层当成“点外部”吃掉；弹层关闭后 250ms 定时器看到指针仍在钮上，又把它打开，于是既静音不了也稳定不了。
- 修复：`VolumeFlyout.OverlayInputPassThroughElement = MuteButton`（`PlayerBar` 构造时设置），让指针输入穿透覆盖层送到静音钮；弹层保持打开，点击/键盘激活都能切换静音。未改 Core/WPF/包。
- 验证（950 宽，真实 `mouse_event` 按下/抬起）：弹层打开 → 第 1 次点击后 UIA 名称 `取消静音`、弹层仍打开；第 2 次点击后名称 `静音`、弹层仍打开；指针移开后弹层关闭。`mute-fix2-click.txt`、`mute-fix2-after-click1.png`。窗口几何还原为 1231×602@572,294，应用正常关闭。
- 证据边界：宽窗点击用 UIA Invoke；窄窗悬停/点击用相对 `mouse_event` 脚本（本会话 SDK 坐标/键盘注入仍被前台锁拒绝，`SetCursorPos` 0x800700CB）。
- 门禁（第二轮修复后，最终态）：`.slnf` 0/0、测试 186/0/0、WinUI 0/0、`verify-gates.ps1 -Full` 3/3。

## 2026-10-29 设置对话框修复

用户报告三项：设置内音量拖动提示仍是 0..1 小数；音量从 99% 拖到 100% 时拖动条回跳；主窗口高度较低时设置面板底部被裁且无法滚动。

- 拖动提示：设置内三个滑块（音量/灵敏度/平滑度）尾部已有数值，统一 `IsThumbToolTipEnabled="False"`。真实鼠标拖动音量（1.00→0.81）期间 UIA 无 ToolTip，释放后也没有（`settings-tooltip-test.txt`）。
- 99%→100% 回跳：`VolumePercent` 原在 `Auto` 列，文本 "99%"→"100%" 变宽会挤压滑块；改为固定 48px 右对齐列。99/100 两个状态截图的轨道右缘像素位置一致（`settings-volume-99.png` / `settings-volume-100.png`）。注：UIA 的 `Slider.BoundingRectangle.Width` 会随值等比变化（304/305/306），是自动化 peer 读数而非布局宽度，不能用它判断。
- 低窗滚动：ContentDialog 模板的 `ContentScrollViewer` 默认 `VerticalScrollBarVisibility=Disabled`；`SettingsDialog.OnLoaded` 取该模板部件改为 `Auto` + `VerticalScrollMode=Auto`。1000×460 下日志按钮初始 `IsOffscreen=True`（inf），一次 LargeIncrement 后滚动到 100%、按钮 `off=False`（`892,633,122x32`），截图 `settings-scroll-bottom.png`。
- 证据边界：窗口由脚本临时缩到 1000×460/1000×620，验证后还原；音量在 1.00/0.81/0.995 间变化后还原为初始 1.00。
- 门禁（修复后）：`.slnf` 0/0、测试 186/0/0、WinUI 0/0、`verify-gates.ps1 -Full` 3/3。

## 2026-11-01 WinUI 3 平台限制：实时缩放时的底栏抖动与黑缝（非布局缺陷）

用户报告：WinUI 壳拖动底边调整窗口高度时，底部控制栏“剧烈抖动”并出现黑色缝隙，期望底栏固定在窗口底部。

结论：底栏布局本身正确。`MainWindow` 的 `RootGrid` 第 2 行为 `Auto`，`PlayerBar` 根 `Grid` 固定 `PlayerBarHeight`（64px）；拖拽结束后底栏完全贴底。黑缝来自 WinUI 3 的平台渲染方式：窗口内容托管在独立交换链（content island），交互式缩放时 XAML 帧与窗口帧不同步，未绘制区域露出窗口底色（深色主题为纯黑），底栏因此随内容岛在旧/新尺寸之间瞬时跳动。

- 复现（2026-11-01，Windows App SDK 2.6.4（本次一并从 2.5.1 升级），96 DPI，窗口 1160×607@572,289；真实鼠标拖底边，逐帧截屏）：
  - 缩小 45 步（每步 5px）：45 帧中 2 帧在底栏下方出现 5px 纯黑（0,0,0）横带；
  - 拉大 18 步（每步 8px）：18 帧中 8 帧出现 2px 黑带；
  - 快速拖拽 8 步（每步 35px）：首帧出现 8px 黑带，底栏内容被截断（播放钮只露出上半）；
  - 每次拖拽结束后的截图与拖拽前一致，底栏贴底；窗口几何已还原为 572,289–1732,896，应用未关闭。
- 依据：WinUI 项目 [issue #5148](https://github.com/microsoft/microsoft-ui-xaml/issues/5148)（2021 至今 open，长期平台问题；内容岛独立交换链、与 DWM 不同步是根因说明），以及微软 2026-05 承认 WinUI 3 缩放撕裂、计划平台级修复的公开表态。本项目使用的 Windows App SDK 2.6.4（2026-10-27，当前最新 stable）仍可复现。
- 边界：未用“关闭 Mica / 根背景改为不透明”规避——会牺牲 Mica，且底栏相对窗口的位移不变；未验证未来 WinAppSDK/OS 更新是否修复。截图为临时证据（`%TEMP%\winui-resize*`）。

## 2026-11-02 用户复验：B、C 组全部通过

用户在最终构建（Windows App SDK 2.6.4；源码即 `b679a9c` + `26aa8da` 后的工作树，UI 代码未再改动）上手动执行 B1–B6 与 C4，结果全部通过；C1–C3 已于 2026-11-01 确认。

| 项 | 结果 |
|---|---|
| B1 初值与启用高亮 | PASS（用户手动：初值与实时配置一致；勾选/取消启用时底栏 EQ 图标高亮切换；取消恢复） |
| B2 Rock / Custom / Flat | PASS（用户手动：Rock 十段值、手选 Custom 保存/重开、恢复 Flat 含 Pre 归零） |
| B3 播放中实时调节 | PASS（用户听感：拖动频段/Pre 有变化；取消后听感恢复；仪器化回环见下节） |
| B4 取消/保存/重启 | PASS（用户手动：取消回滚、保存生效、重启保留） |
| B5 无曲目/播放中 | PASS（用户手动：播放中保存不切歌不停播；空数据场景已由代理仪器化复验，见下节） |
| B6 既有功能 | PASS（用户手动：全局 Space、按钮焦点 Space、设置各行交互、窄窗回归） |
| C1 播放控制/进度/音量 | PASS（用户 2026-11-01） |
| C2 布局/移除 | PASS（用户 2026-11-01） |
| C3 Space/设置/日志 | PASS（用户 2026-11-01） |
| C4 Full 门禁 | PASS（代理 2026-11-01 实跑：3/3、slnf 0/0、测试 186/0/0、WinUI 0/0、末行 `all requested steps passed.`；用户 2026-11-02 确认） |

证据边界：上表是用户手测转录，代理随后完成了 B1–B5 的仪器化复验（见下节）；B3 音频输出、B1 磁盘一致性、B4 写入/失败、B5 空数据均已实测。C 组仍以用户手测 + C4 门禁实跑为准。用户手测与仪器化结果分开记录。

### 2026-11-02 仪器化复验（B1–B5）

在 B5 空数据目录上做了一次可复现测量：WinUI Debug / Windows App SDK 2.6.4 / 96 DPI；UIA 驱动 EQ 对话框、settings.json 全字段比对、WASAPI 回环录音（96 kHz/2ch，Goertzel 1 kHz）、写入脉冲计数（自旋轮询 LastWriteTime；本环境 FileSystemWatcher 不触发通知，计数器自检 3 写 = 3 次观测）。测试音 1 kHz、−26 dBFS、60 s。

| 项 | 仪器化结果 |
|---|---|
| B1 初值/启用 | 对话框 11 个滑块的 RangeValue、启用 ToggleState、预设 SelectionPattern 与 settings.json 全字段一致（On / Custom / 6 / [0,0,0,0,0,9,0,0,0,0]）；启用预览 Off→On；取消后 settings.json SHA-256 不变（`7E9213…11D45`）。 |
| B2 预设/未知值 | Rock → [5,4,3,1,-1,-1,0,2,3,4]；手选 Custom → 保存/重开仍是 Custom + Rock 曲线；恢复 Flat → 十段与 Pre 全 0 → 保存/重开一致；磁盘注入 `EqualizerPreset="Bogus"` → 对话框显示 Custom，保存写回 Custom。新增 4 条单测：外部数组不别名、未知预设保留、空白预设回落 Flat、preamp/运行时 Update 增益（仪器化时 190/0/0；精简后默认 176/0/0 + 音频 5/0/0）。 |
| B3 音频输出/取消 | 回环 1 kHz 幅度：基线 → +12 dB @1k 预览 → 取消 = **3.981 → 1.000**（理论 10^(12/20)=3.981）；取消后重开对话框为 Flat / 全 0。播放中保存位置 0.3280→0.3402→0.3603（继续推进）。 |
| B4 写入/失败/重启 | 脉冲计数：取消 0；启用值不变保存 1；启用值变化保存 2（量到 VM setter 的追加写）。锁 settings.json 注入失败 → 对话框留窗、SaveError 可见、取消后文件哈希不变。150 MB 尾随空格（慢解析代理）保存期间 SaveButton 名称长度 4（“保存中…”）且保存/取消均 disabled；完成后文件 656 B、bands[0]=4。保存后重启 UIA 读回与磁盘一致。 |
| B5 无曲目/播放中 | 空歌单 `PlayPauseButton` disabled、`PositionSlider=0`、时间 `00:00 / 00:00`；改 EQ 并保存后仍 disabled、位置 0；播放测试音时保存，位置继续推进；测试后已暂停。 |
| A 组 | 未重跑。A 表证据本身是 Task1 的帧/UIA/截图测量，不是口头结论；本轮没有频谱代码改动。 |

剩余边界：B1/B3 未直接读取 `IPlaybackService.EqualizerConfig` 字段（用对话框快照 + 磁盘 + 回环输出交叉验证）；B4 的“慢存储”是 150 MB JSON 读/解析变慢的代理，不是磁盘 I/O 延迟注入；A 组已重跑（A1–A5，见 A 表；A3 平滑度用 2Hz 调幅测试音，A5 分别测量手动优先与自动收起）。
证据：`%TEMP%\p23-inst\{b1-b2-b5,b4,b4-write-counts,b4-slow-save,b3-audio}.txt`、`b3-*.wav`、`spectrum-wide.png`；测试数据目录 `%LOCALAPPDATA%\D-player-winui.instrumentation-20261102`，原始数据已还原。

## 交给用户的听感项

- **B3 听感由用户确认，非仪器化 PASS**：用户已用真实鼠标拖动频段及 Pre，确认听感有变化；2026-11-02 用户确认取消后听感恢复，B3 收口。仪器化回环见 2026-11-02 仪器化复验（+12 dB 预览 3.981×、取消 1.000×）。避免大增益/过高音量。
- 代理未听音，不填听感 PASS；回环只测输出电平，不替代人耳听感。

## 副作用与证据路径

- A 旧证据：`%TEMP%\p23-acc\`，路径及测量值来自 Task1 报告，本次未直接读取 AppData/Temp 下旧资产。
- B 旧证据：`D:\CodingProjects\D-player\.superpowers\sdd\2026-10-27-d-player-phase23-spectrum-eq-implementation\task-2-*.png`、`task-2-ui-evidence.txt`；本地证据不在 Git 发布面。
- C 本次原始 UIA 在本会话 computer-use 工具输出；关键读数/操作写入 Task3 报告，没有新增截图，不冒充截图级验收。
- 本次启动既有 Debug exe，观察已有两个歌单，不播放、不改EQ/音量/频谱、不删除/导入/清空数据；正常关闭后 SDK 窗口列表无 D-player.WinUI 窗口。正常启动/关闭可能由应用自身写日志/几何/状态，未读取磁盘验证无写入。
- Task1 调试曾留历史异常；Task2 曾临时保存测试EQ后经UI恢复，播放位置推进至02:20。历史副作用见各任务报告，不回改历史验收。
- 2026-10-28 收尾：经设置 UI 将 WinUI 频谱还原为启用 / 灵敏度 1.00 / 紫色 / 平滑度 0.80；窗口几何还原为 1231×602@572,294；音量保持 55%，EQ 原值已由 Task2 恢复。应用正常关闭。
- 2026-10-29 缺陷复验：启动 Debug exe，静音开/关各一次并还原（最终未静音）；未改音量/EQ/频谱；脚本临时把窗口缩到 950 宽再还原为 1231×602@572,294，验证后正常关闭。另注：computer-use SDK 在关闭会话时会一并结束它启动的 app，本轮改为普通启动后再跑脚本。
- 2026-10-29 第二轮复验：窄窗弹层保持打开时用真实鼠标点击静音钮两次（取消静音 → 静音），最终未静音；几何同样还原为 1231×602@572,294；未改音量/EQ/频谱，应用正常关闭。
- 2026-10-29 设置复验：窗口临时缩到 1000×460/1000×620，音量在 1.00/0.81/0.995 间变化后还原为初始 1.00；未改 EQ/频谱/歌单，应用正常关闭。新证据曾放在 `%TEMP%\settings-fix\`，已于同日按用户要求删除。
- 2026-10-29 按用户要求删除 `%TEMP%\p21-acc`、`p22-acc`、`p23-acc`；上文 A/B/C 的旧 `%TEMP%` 证据引用仅供追溯，文件已不在本机。
- 2026-11-02 用户复验：B1–B6、C4 由用户手动执行；代理未新增 UI 自动化、截图或数据目录操作，结果按用户口头确认转录。
- 2026-11-02 仪器化复验：在 B5 空数据目录上种入 1 kHz/60 s 测试音并做 UIA/回环/写入计数；A 组另用 2 Hz 调幅 1 kHz 测试音做截屏/像素分析；测试目录保留在 `%LOCALAPPDATA%\D-player-winui.instrumentation-20261102`，原始数据已还原为 `%LOCALAPPDATA%\D-player-winui`（UIA 复验 25 行曲目、窗口 917,247–1922,854）。
- 2026-11-02 按用户要求删除 `%TEMP%\p23-inst\`、`%TEMP%\winui-resize*`、`%TEMP%\fswtest*`、`%TEMP%\dplayer-head.json`、`TestResults\fswtest` 与测试数据目录 `%LOCALAPPDATA%\D-player-winui.instrumentation-20261102`；上文仪器化证据路径仅供追溯，文件已不在本机。
- 2026-11-02 测试工程精简：删除/合并 9 条低风险用例（Smoke、WaveFormat、Update 只不抛、PreampLinearGain、Ctor_LoadsEqualizerEnabled、PlaylistViewModel 三条并一、DataPaths 三条并一）并移除测试工程未用的 `UseWPF`；默认 176/0/0 + 音频 5/0/0；`-Full -Audio` 3/3。
- 2026-11-02 测试工程精简（续）：移除 `Microsoft.NET.Test.Sdk`、`xunit.runner.visualstudio`、`coverlet.collector` 与 `TestingPlatformDotnetTestSupport`；5 条真实音频测试标记 `Category=AudioDevice` + `[Collection("AudioDevice")]`，默认门禁排除，`verify-gates.ps1 -Audio` 单独串行跑。
- 2026-11-02 用户确认 B、C 组全部通过，Phase 23 验收闭合（A/B 组均完成仪器化复验）；拖拽/排序/文件夹绑定管理仍为后续阶段实现缺口，不属于 Phase 23。WinUI 3 平台级缩放撕裂见 2026-11-01 一节。
