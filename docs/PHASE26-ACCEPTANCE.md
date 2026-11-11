# Phase 26 验收记录：音乐库 → 歌单

- 日期：2026-11-11
- 代码基线：`dd366e8`（库视图五处右键入口）；UI 修复 `47f7d87`
- 范围：共享 Core + WPF 主壳；WinUI 壳继续冻结，只参与门禁 3 编译
- 设计：[Phase 26 spec](superpowers/specs/2026-11-08-d-player-phase26-library-to-playlist-design.md)

## 1. 自动化门禁

最终执行命令：

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-gates.ps1 -Full
```

| 门禁 | 结果 |
|---|---|
| 1/3 解决方案筛选器构建（Core + WPF + Tests） | 通过，0 警告 / 0 错误 |
| 2/3 默认测试（`Category!=AudioDevice`） | **294/294 通过**，0 失败，0 跳过 |
| 3/3 WinUI 3 构建 | 通过，0 警告 / 0 错误 |

计数口径：

- Phase 25 收尾默认 **283**；Phase 26 新增 **11** 条（9 条容器规则 + 2 条 Library 转发），当前默认 **294**。
- 真实音频集成仍为 **5** 条；本次未执行 `-Audio`，不把 299 写成实跑结果。若执行，总数应为 **294 + 5 = 299**。
- 未修改 `D-player.slnf`、WinUI 工程或任何持久化 schema。

新增覆盖：

| 文件 | 覆盖 |
|---|---|
| `PlaylistsViewModelTests` | 普通歌单顺序追加、文件夹歌单拒绝、目标不在容器拒绝、空列表拒绝、重复保留且不触发播放、`StateChanged` 与快照路径、新建默认值、平空白名称、空曲目返回 null、新建不设置 `ViewedPlaylist` / `CurrentPlaylistId` |
| `LibraryViewModelTests` | 曲目列表按原顺序转发；新建并加入不改变查看/播放指针 |

## 2. WPF 交互验收

环境：Windows 11（10.0.26300）/ .NET 10 / Debug；Computer Use 驱动真实窗口。使用临时合成库（2 个有标签专辑 + 1 个未知专辑，共 6 首，含 1 首 60 秒曲目）；验收前备份 `settings.json` / `queue.json` / `library-cache.json`，结束后按字节还原并核对 SHA-256 相等，临时库、备份与脚本已删除；未修改用户音乐文件。

| # | 结果 | 操作与观测 |
|---|---|---|
| 1 | PASS | 专辑页右键「Album One…」→「加入歌单…」→ 对话框显示「将加入 2 首曲目」→ 默认选中 Acceptance Playlist →「加入」；切到该歌单后 2 首按专辑顺序排列。库视图未切换。 |
| 2 | PASS | 专辑详情右键曲目行 → 单曲加入；同一首连续加入两次，歌单出现两条相同曲目（允许重复）。 |
| 3 | PASS | 搜索「Second」得到曲目组 2 条；右键结果行加入成功；滚动/列表复用后右键仍作用于当前行。 |
| 4 | PASS | 专辑卡片右键 → 名称框输入「Fresh List」→「新建并加入」；新歌单出现、库视图保持、侧栏无高亮；重启后 Fresh List 仍有 2 首。 |
| 5 | PASS | 对话框内 Folder Playlist 置灰，行内显示「文件夹歌单 · 由文件夹管理」，不可选中；Core 守卫另有单测。 |
| 6 | PASS | 仅保留文件夹歌单时：焦点落在名称框、「加入」禁用、文件夹行仍置灰；清空名称后「新建并加入」回落创建「新歌单」。 |
| 7 | PASS | Win32 `MoveWindow` 精确 600×500：主窗口与 360×460 对话框完整可见；长专辑/艺术家/曲目名省略显示；对话框在窄窗下不被遮挡。600px 下歌单表格列被压得很窄属既有布局限制（本阶段未改表格）。 |
| 8 | PASS | 播放 60 秒曲目后，从库右键加入另一张专辑；对话框关闭后播放器仍显示同一曲、进度从 0:12 继续前进，未被暂停或重载。 |
| 9 | PASS | 对话框「取消」关闭后无歌单变化；`queue.json` 计数不变。Esc 未单独注入：Computer Use 会把注入的 Escape 视为用户停止，`IsCancel` 行为仅由代码与对话框模式覆盖。 |
| 10 | PASS | 关闭重启：`queue.json` schema 仍为 3；Acceptance Playlist 7 首、Folder Playlist 5 首、Fresh List 2 首全部恢复；600×500 窗口几何与库视图恢复。 |
| 11 | PASS | 专辑详情头部右键（Spec §3.1 第 3 行）打开同一菜单，对话框显示「将加入 1 首曲目」；取消后无变化。 |
| 12 | PASS | 修复后复测：全部五个入口的菜单文字完整、无白色图标栏；悬停高亮为深色 `BackgroundTertiary`。 |

## 3. 发现与处理

| 发现 | 级别 | 处理 |
|---|---|---|
| 右键菜单项左侧白色遮挡，盖住「加入」二字 | Important | `47f7d87`：LibraryView 增加本地 `LibraryMenuItemStyle` / `LibraryContextMenuStyle`，五处菜单显式套用；scoped re-review 通过，截图复测白块消失。 |
| 文件夹行 `ToolTip` 因禁用元素默认不显示 | Minor | 同一提交加入 `ToolTipService.ShowOnDisabled=True`；行内原因文本本就可见。 |
| 双击歌单列表空白处会接受当前选中歌单 | Minor | 同一提交用 `ItemsControl.ContainerFromElement` 限定双击必须落在列表项上。 |
| 右键会让行获得选中态 | 接受 | WPF 列表默认行为；不起播、不切换视图，spec §3.1 已注明。 |
| 600px 下歌单表格列极窄 | 既有 | 与 Phase 26 无因果关系；本阶段不扩展表格布局，记入已知限制。 |

## 4. 交付后修复（2026-11-11）

用户实测发现：点击名称输入框时，上方歌单列表仍保持上一项高亮，无法判断回车将加入高亮歌单还是新建。修复：`AddToPlaylistDialog.NewNameBox_GotFocus` 清空 `PlaylistList.SelectedItem`；点回列表任一项由既有 `SelectionChanged` 恢复高亮与「加入」。复测：聚焦名称框后列表无选中、「加入」禁用；点回 Acceptance Playlist 后恢复高亮与「加入」。提交 `57b580a`；门禁 1/2 通过 294/294。

## 5. 限制

仅支持普通歌单、单曲与整张专辑；允许重复路径；无拖拽、去重、撤销、多选与 toast；对话框列表无搜索；追加使用点击时数组快照，不重新解析对话框打开期间的库变化。不改变 `queue.json` / `settings.json` / `library-cache.json` schema。
