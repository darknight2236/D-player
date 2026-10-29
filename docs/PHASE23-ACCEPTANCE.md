# Phase 23 验收记录：WinUI 频谱与 EQ

## 范围与基线

- 日期：2026-10-28 首记；2026-10-29 补充 C1/听感实测与宽窗静音缺陷修复。状态：**实现完成（已提交）/ 验收仍有缺口**，不是全面闭环。
- 实现基线：Task1 `af2e944`（含暂停守卫）；Task2 `898f0a7`。2026-10-29 缺陷修复只改 `PlayerBar.xaml.cs` 一处（未改 Core/WPF/包）；此前的记录与文档提交不含代码改动。
- [实施计划 Task3](./superpowers/plans/2026-10-27-d-player-phase23-spectrum-eq-implementation.md)；[设计](./superpowers/specs/2026-10-27-d-player-phase23-spectrum-eq-design.md)。原计划不回改，勘误见 B 组。
- 依据：[Task1 报告](../.superpowers/sdd/2026-10-27-d-player-phase23-spectrum-eq-implementation/task-1-report.md)、[Task2 报告](../.superpowers/sdd/2026-10-27-d-player-phase23-spectrum-eq-implementation/task-2-report.md)、[Task3 报告](../.superpowers/sdd/2026-10-27-d-player-phase23-spectrum-eq-implementation/task-3-report.md)。SDD 被 Git 忽略，链接仅本地可用；A/B 结果为报告转录，本次没有重跑整组或逐张复核旧截图。
- [Phase 21](./PHASE21-ACCEPTANCE.md)、[Phase 22](./PHASE22-ACCEPTANCE.md) 是历史验收；[Phase 20 对比材料](./PHASE20-COMPARISON.md) §2 是历史快照、§3 是当前能力。历史 PASS 不自动成为 Phase 23 PASS。

## A 频谱

实现已交付：InfoPanel 底部 32 柱、四主题、启用/灵敏度/平滑度；Loaded/Unloaded 对称订阅，逐帧高度插值及 Top 更新，不逐帧建柱或画刷。Paused/Stopped 将目标设 2px，Loaded 非 Playing 不读旧数据，晚到 SpectrumData 仅在 Playing 时接受。

| 项 | 结果与证据边界 |
|---|---|
| A1 播放/暂停回落 | Task1 最终修复复测 PASS（沿用）：`a1-review-fix-final-frames.txt` / `a1-review-fix-measurements.txt`，175ms 起至 966ms 稳定 2px，识别 32 柱；原暂停失败保留于旧报告，不再列为未修实现缺陷 |
| A2 紫/蓝/绿/彩虹即时切换 | Task1 PASS（沿用）：`a2-purple.png` / `a2-blue.png` / `a2-green.png` / `spectrum-rainbow.png` |
| A3 灵敏度/平滑度 | Task1 PASS（沿用）：灵敏度 2/0.5 对比、平滑度 0/0.9 连续帧与 UIA 读回 |
| A4 启用与重启持久化 | Task1 PASS（沿用）：`a4-disabled.png` / `a4-enabled.png` / `a4-persisted-settings.txt` |
| A5 折叠/窄窗/小窗高滚动 | Task1 修复负柱宽后 PASS（沿用）：`a5-narrow-collapsed.png` / `a5-wide-restored.png` / `a5-small-height-scrolled.png` |

上述 A 组证据不是 Task2 合入工作树之后的全组重验；最终组合态仍随 C 回归补齐。

## B EQ

实现：底栏描边 EQ 入口，10 段 + Pre（−12…+12dB），9 内置预设 + Custom；实时预览、Flat 归零、取消恢复、保存与失败提示、保存中关闭守卫。

**原计划勘误**：预览/取消写 `Player.EqualizerEnabled` 会触发 Core setter 下发与持久化，原示例错误。当前打开时深拷贝实时 service 配置，不用 LoadAsync 的磁盘配置覆盖；预览仅写 service 并发壳侧高亮事件；取消/未保存关闭恢复打开时实时快照，VM 未被预览修改，无需写 setter 恢复。完整配置 UpdateAsync 成功后才同步 VM。此时启用值变化仍可能由既有 VM setter 追加一次同值持久化，不宣称单次写盘。保存前冻结值，保存中禁止编辑/重复保存/ESC/主窗清理，失败留窗并内联提示/记录日志；这些防护的动态故障测试仍待补。

| 项 | Task2 已有证据 | 未验证 / 待补 |
|---|---|---|
| B1 初值与启用高亮 | UI 可观察部分 PASS：初值启用/Custom/Pre=2/首段8其余0；预览禁用变次级色，取消恢复 | 未独立读取服务内部/磁盘比较；不以截图证明内部一致性 |
| B2 Rock / Custom / Flat | 预设与键盘部分 PASS：Rock=[5,4,3,1,-1,-1,0,2,3,4]；首段键盘5→6变Custom；Flat含Pre全0 | 最终 Custom 标记补充后的手选 Custom 保存/重开分支待专项复测；此前合成拖拽未改变值的现象已被 2026-10-29 用户真实鼠标拖动推翻（见 B3），不列为实现缺陷 |
| B3 播放中实时调节 | PARTIAL（用户实测 2026-10-29）：播放中用真实鼠标拖动频段与 Pre，听感有变化；Task2 键盘路径 Pre 2→−1、同曲进度继续；`task-2-b3-preamp-key.png` | 实时内部配置/音频输出未仪器化；取消后听感是否恢复未确认 |
| B4 取消/保存/重启 | UI 可见值 PASS：取消恢复原四字段，保存禁用/Flat/Pre−1/十段0并重启保留，随后UI恢复原值并重启核对 | 不调用持久化属静态结论，未实测文件写次数；失败/慢存储竞态未验 |
| B5 无曲目/播放中 | PARTIAL：播放中开关不切歌不停播；暂停态保存不自启 | 无当前曲目未验，不删除/清空用户数据来构造 |
| B6 既有功能 | PARTIAL：Task2按钮焦点Space播放/暂停、设置打开关闭已观察；窄窗悬停弹层与宽窗静音点击已由 2026-10-29 专项复验（见文末） | 全局Space及其它焦点、设置全部行交互未全验 |

Task2 主要 B 交互发生在最后“手选 Custom 同步标记”补充之前；补充后仅四门禁及原值重开确认，不能称最终构建完整 B 组 PASS。实时/磁盘不一致、未知预设与外部可变数组也未动态注入验证。

## C 回归

2026-10-28 那轮使用 computer-use SDK；没有 PowerShell UIA、没有直接访问 AppData。只完成非破坏设置冒烟，**未执行完整 C 组**，不是工具不可用，也不以静态未改替代实测。2026-10-29 依据用户实测结果补充 C1，并用窄窗专项脚本复验音量弹层修复。

| 项 | 本次结果 | 剩余操作 |
|---|---|---|
| C1 播放控制/进度/音量 | PARTIAL（用户实测 2026-10-29）：双击/首播、上一首/下一首、随机/循环三态、进度拖动/单击/时间提示均正常；宽窗点静音误弹层、窄窗点静音被吞均已修复（见文末），修复后宽窗点击无弹层、窄窗弹层保持时点击可切换静音 | 音量滑块拖动与设置内音量交互未单独复验 |
| C2 布局/移除 | NOT VERIFIED；UIA观察到双栏按钮、表头、行与移除入口，不等于布局验收 | 双栏折叠、<960自动收起与手动优先、视觉对齐、移除确认框（只取消，不执行删除）待实测 |
| C3 Space/设置/日志 | PARTIAL：打开设置成功，音量55%、灵敏度0.50、平滑度0.90、输出与日志按钮可见；焦点在设置音量滑块按Space，进度仍02:20/03:00、音量55%；点击完成关闭 | 该次暂停态下对话框滑块未观察到起播，不覆盖全部焦点组合；按钮/文本框/非交互焦点、播放态守卫、日志入口实际打开、音量修改交互均待补 |
| C4 Full门禁 | PASS（2026-10-28 本次实跑）：Full 3/3，exit 0；slnf 0警告/0错误（1.24s），测试186成功/0失败/0跳过（2.916s），WinUI 0警告/0错误（11.77s）；2026-10-29 修复后复跑仍 3/3（测试 186/0/0） | `powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-gates.ps1 -Full`；test未加--nologo。输出末行 `all requested steps passed.`；仅编译/测试，不等于UI通过 |

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

## 交给用户的听感项

- **B3 听感由用户确认，非仪器化 PASS**：用户已用真实鼠标拖动频段及 Pre，确认听感有变化；取消/未保存关闭后听感是否恢复仍未确认。避免大增益/过高音量。
- 代理未听音，不填听感 PASS。失败/竞态与无曲目场景应在独立可控环境补验，不能篡改用户数据或权限。

## 副作用与证据路径

- A 旧证据：`%TEMP%\p23-acc\`，路径及测量值来自 Task1 报告，本次未直接读取 AppData/Temp 下旧资产。
- B 旧证据：`D:\CodingProjects\D-player\.superpowers\sdd\2026-10-27-d-player-phase23-spectrum-eq-implementation\task-2-*.png`、`task-2-ui-evidence.txt`；本地证据不在 Git 发布面。
- C 本次原始 UIA 在本会话 computer-use 工具输出；关键读数/操作写入 Task3 报告，没有新增截图，不冒充截图级验收。
- 本次启动既有 Debug exe，观察已有两个歌单，不播放、不改EQ/音量/频谱、不删除/导入/清空数据；正常关闭后 SDK 窗口列表无 D-player.WinUI 窗口。正常启动/关闭可能由应用自身写日志/几何/状态，未读取磁盘验证无写入。
- Task1 调试曾留历史异常；Task2 曾临时保存测试EQ后经UI恢复，播放位置推进至02:20。历史副作用见各任务报告，不回改历史验收。
- 2026-10-28 收尾：经设置 UI 将 WinUI 频谱还原为启用 / 灵敏度 1.00 / 紫色 / 平滑度 0.80；窗口几何还原为 1231×602@572,294；音量保持 55%，EQ 原值已由 Task2 恢复。应用正常关闭。
- 2026-10-29 缺陷复验：启动 Debug exe，静音开/关各一次并还原（最终未静音）；未改音量/EQ/频谱；脚本临时把窗口缩到 950 宽再还原为 1231×602@572,294，验证后正常关闭。另注：computer-use SDK 在关闭会话时会一并结束它启动的 app，本轮改为普通启动后再跑脚本。
- 2026-10-29 第二轮复验：窄窗弹层保持打开时用真实鼠标点击静音钮两次（取消静音 → 静音），最终未静音；几何同样还原为 1231×602@572,294；未改音量/EQ/频谱，应用正常关闭。
- 完整 C 回归、B 组缺口及最终组合复测未完成前，不宣布 Phase 23 全验收或与 WPF 全面等价；拖拽/排序/文件夹绑定管理仍为未来实现缺口。
