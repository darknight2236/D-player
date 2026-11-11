# Phase 26 设计：音乐库 → 歌单（Core + WPF）

- 日期：2026-11-08
- 状态：已由用户确认并实施；验收记录见 [PHASE26-ACCEPTANCE](../../PHASE26-ACCEPTANCE.md)。
- 代码基线：`master` / `adfcb8f`。Phase 25 已交付并验收；WinUI 壳继续冻结。
- 前置：[Phase 25 设计](2026-11-06-d-player-phase25-library-search-watch-design.md) · [Phase 25 验收](../../PHASE25-ACCEPTANCE.md) · [README 当前限制](../../../README.md)。历史设计中的初始状态不替代当前代码事实。

## 1. 已确认的范围

本阶段把音乐库与可见歌单接通：在库视图右键把单曲或整张专辑追加到普通歌单，或在同一次操作中新建歌单并加入。

| 决策 | 本期约定 |
|---|---|
| 开发范围 | 共享 Core + WPF 主壳；不新增 WinUI UI |
| 入口 | 右键菜单，仅一个平铺项「加入歌单…」 |
| 粒度 | 单曲 + 整张专辑；不提供艺术家整位加入 |
| 目标选择 | 模态对话框：普通歌单可选、文件夹歌单置灰；对话框内可新建并加入 |
| 重复 | 允许同一路径重复追加，与现有文件拖入 / 播放列表导入一致 |
| 新建行为 | 不切换视图、不改变侧栏选中、不打断播放 |
| 持久化 | 复用 `queue.json` 现有 schema；不新增字段、不升版本 |

不做：拖拽入歌单、艺术家整位加入、去重、撤销、从歌单反向移除、库播放队列管理 UI、搜索 / 监听改动、播放历史、输出设备、WinUI 补齐、封面缓存与专辑网格虚拟化。

不修改音频输出链、EQ / 频谱、播放推进算法、歌单排序，以及文件夹绑定歌单的扫描与差分语义。

## 2. 当前实现与承重点

| 当前事实 | 设计约束 |
|---|---|
| [PlaylistsViewModel](../../../D-player.Core/ViewModels/PlaylistsViewModel.cs) 的 `AddPlaylist` 创建后设置 `ViewedPlaylist` | 库视图下必须保持 `ViewedPlaylist == null`（Phase 25 侧栏高亮回归点）；新建路径不得复用会选中新歌单的入口 |
| [PlaylistViewModel](../../../D-player.Core/ViewModels/PlaylistViewModel.cs) 的 `Queue` 是 `ObservableCollection<Track>`；容器已订阅集合变化并抛出 `StateChanged` | 追加 / 新建不需要新事件；`MainViewModel` 的 500ms debounce 自动覆盖落盘 |
| `PlaylistViewModel.HasSourceFolder` 标识文件夹绑定歌单，其成员由目录扫描与差分驱动 | 手动追加与目录事实冲突，本期一律拒绝；UI 置灰并说明原因 |
| [LibraryViewModel](../../../D-player.Core/ViewModels/LibraryViewModel.cs) 已持有 `PlaylistsViewModel` 并调用 `PlayLibraryTracksAsync` | 新增转发方法不产生新的层边，不改构造链与 DI 注册 |
| [LibraryView.xaml](../../../Views/Controls/LibraryView.xaml) 有两套专辑模板与两套曲目模板：`AlbumTileTemplate`（专辑页 / 艺术家详情）、`TrackRowTemplate`（专辑详情），以及搜索结果用的隐式 `LibraryAlbum` / `Track` 模板 | 把右键菜单放进模板即可覆盖对应页面，避免逐行事件处理器；专辑详情头部需单独挂菜单 |
| [Themes/Controls.xaml](../../../Themes/Controls.xaml) 的 `MenuItem` 深色模板没有 `Popup` | 本期只用平铺菜单项 + 对话框；不扩子菜单模板，也不依赖未验证的弹出层绑定 |
| 库中的 `Track` 已带完整元数据；`AlbumArt` 可能为 null，由现有封面惰性加载补齐 | 追加直接复用 `Track` 实例，不重读标签、不强制读封面 |
| 搜索结果的 `ContentControl` 使用隐式模板，行 `DataContext` 即 `LibraryAlbum` 或 `Track` | 菜单点击从 `DataContext` 取目标；虚拟化列表回收不影响该解析 |

## 3. 交互设计

### 3.1 右键入口

| 位置 | 数据上下文 | 加入内容 |
|---|---|---|
| 专辑页的专辑卡片 | `LibraryAlbum` | `album.Tracks` 全量，保持现有顺序 |
| 艺术家详情的专辑卡片 | `LibraryAlbum` | 同上 |
| 专辑详情头部（封面 / 标题区域） | `SelectedAlbum` | 同上 |
| 专辑详情的曲目行 | `Track` | 单曲 |
| 搜索结果的专辑行 | `LibraryAlbum` | `album.Tracks` |
| 搜索结果的曲目行 | `Track` | 单曲 |

- 右键作用于指针下的行，不起播、不切换视图；WPF 可能让该行获得选中态（2026-11-11 验收确认）。
- 菜单只有一项「加入歌单…」，点击后打开模态对话框；专辑曲目为空时不打开对话框（当前 UI 不会显示空专辑，仅作防御）。
- 目标曲目列表在**打开对话框时取数组快照**。对话框是模态的，期间库监听可能更新分组；本期不重新解析快照，用户确认的就是点击时的集合。

### 3.2 选择对话框

- 新窗口 `AddToPlaylistDialog`，沿用现有无边框深色对话框模式；宽 360、高 460，居中于主窗，不可缩放，600×500 主窗下完整可见。
- 标题「加入歌单」，副标题显示本次将加入的曲目数（如「将加入 12 首曲目」）。
- 列表：全部可见歌单，每行显示名称与当前曲目数。
  - 普通歌单：可选；双击，或选中后按 Enter /「加入」确认。
  - 文件夹歌单：置灰不可选，行内注明「文件夹歌单 · 由文件夹管理」，`ToolTip` 说明不能手动加入。
  - 隐藏库播放队列不在 `Playlists` 集合内，天然不出现。
- 新建区：名称输入框（预填「新歌单」并全选）+「新建并加入」按钮。
  - 名称 `Trim()`；空或全空白回落「新歌单」；允许与现有歌单重名（沿用现有新建语义）。
  - 输入框内 Enter 等同「新建并加入」；列表内 Enter 等同「加入」。
- 默认焦点：存在普通歌单时落在第一个可选歌单；没有普通歌单时落在名称输入框。列表为空时显示「还没有普通歌单」。
- 名称框获得焦点即进入“新建”意图：清空列表选中、「加入」禁用、无高亮；点回列表任一项恢复选中与「加入」（2026-11-11 交付后修复）。
- 未选中普通歌单时「加入」禁用；「取消」与 Esc 不写盘、不改变任何状态。
- 确认后对话框直接关闭，不弹二次确认，也不弹成功提示；模态关闭本身就是反馈。

### 3.3 不改变的既有行为

- 不切出库视图，不把新歌单设为 `ViewedPlaylist`，不改变 `CurrentPlaylistId`，不暂停 / 重载当前曲目。
- 不改动侧栏选中态；库视图期间侧栏仍不高亮任何歌单。
- 追加只影响目标歌单的内容与后续自动保存；不改变其排序、当前索引、Shuffle / Repeat。

## 4. Core 契约

### 4.1 PlaylistsViewModel

```csharp
public bool AppendTracks(PlaylistViewModel target, IReadOnlyList<Track> tracks)
public PlaylistViewModel? CreatePlaylistWithTracks(string? name, IReadOnlyList<Track> tracks)
```

- `AppendTracks` 条件：`target` 在 `Playlists` 中、`!target.HasSourceFolder`、`tracks.Count > 0`；否则返回 `false` 且不修改状态。
- 追加按传入顺序逐项 `Queue.Add`，不去重、不排序、不改 `CurrentIndex`、不调用播放服务。
- `CreatePlaylistWithTracks` 与 `AddPlaylist` 使用相同默认值（新 `Guid`、空 Items、`CurrentIndex = -1`、Shuffle 关、Repeat Off），完成 Hook 与加入集合后追加曲目，返回新 VM。
- 新建路径**不设置** `ViewedPlaylist`，也不设置 `CurrentPlaylistId`。
- 名称空白回落「新歌单」，与 `AddPlaylist` 一致；`tracks.Count == 0` 时返回 `null`，不创建空歌单。
- 两者都通过现有集合通知触发 `StateChanged`；不新增事件、不改 `BuildSnapshot`。

### 4.2 LibraryViewModel

```csharp
public IReadOnlyList<PlaylistViewModel> Playlists { get; } // 对话框的选择集
public bool AddTracksToPlaylist(PlaylistViewModel target, IReadOnlyList<Track> tracks)
public PlaylistViewModel? AddTracksToNewPlaylist(string? name, IReadOnlyList<Track> tracks)
```

- 两个方法只做转发，不复制规则；`Playlists` 是容器集合的只读视图，不复制、不排序。
- 不新增构造依赖、不新增服务注册、不新增分层边。

### 4.3 持久化与恢复

- 追加 / 新建后 `StateChanged` → 500ms debounce → `queue.json`；关闭时 `BuildSnapshot` 已包含新曲目路径。
- 重启时可见歌单按现有 hydration 恢复路径；元数据来自缓存 / 扫描，封面仍按需加载。
- 不写 `settings.json` 与 `library-cache.json`，不改变 `QueueState` v3。
- 追加不校验文件当前是否存在（与现有导入一致）；播放时沿用既有失败跳过。

## 5. 错误与边界

| 情况 | 行为 |
|---|---|
| 目标为文件夹歌单 | UI 置灰；Core 拒绝；不产生「下次扫描被抹掉」的假成功 |
| 目标歌单不在容器（当前 UI 不可达） | 返回 `false`，无异常 |
| 曲目列表为空 | 不打开对话框；Core 拒绝 |
| 重复曲目 | 允许，按现有导入语义 |
| 没有任何歌单 | 列表显示空态「还没有普通歌单」，焦点落在新建区 |
| 只有文件夹歌单 | 文件夹歌单仍置灰展示并注明原因；焦点落在新建区 |
| 歌单名超长 / 含非法字符 | 名称仅用于显示，不参与文件路径；行内省略，完整名称放 `ToolTip` |
| 歌单很多 | 列表滚动；本期不提供搜索 |
| 对话框打开期间库监听更新 | 使用点击时快照；不重新解析、不修改库集合 |
| 600×500 主窗 | 对话框完整可见，按钮不被遮挡 |
| 取消 / Esc / 关闭窗口 | 不写盘、不改变状态 |

## 6. 预期实现落点

以下是设计职责，不是本次已产生的代码，也不替代后续实施计划。

| 模块 | 变化 |
|---|---|
| `D-player.Core/ViewModels/PlaylistsViewModel.cs` | 新增追加与「新建并加入」两个入口；抽出不选中新歌单的私有创建路径 |
| `D-player.Core/ViewModels/LibraryViewModel.cs` | 暴露歌单选择集合并转发两个方法 |
| `Views/Controls/LibraryView.xaml(.cs)` | 五处 `ContextMenu`（专辑卡片、搜索专辑行、专辑详情头部、专辑曲目行、搜索曲目行）；解析目标、打开对话框、调用 VM |
| `Views/Dialogs/AddToPlaylistDialog.xaml(.cs)` | 新模态对话框：歌单列表、文件夹歌单禁用说明、新建并加入、键盘与空态 |
| `Tests/ViewModels/PlaylistsViewModelTests.cs` / `LibraryViewModelTests.cs` | 新契约与回归测试 |
| `README.md` / `docs/PROJECT.md` / `docs/COUPLING.md` / 新增 `docs/PHASE26-ACCEPTANCE.md` | 交付后同步；不回改 Phase 21–25 的历史 PASS |

## 7. 测试与验收

### 7.1 默认自动测试

| 组 | 必须覆盖 |
|---|---|
| 追加 | 顺序一致；文件夹歌单拒绝；目标不在容器拒绝；空列表拒绝；重复允许；不改 `CurrentIndex` / `ViewedPlaylist` / `CurrentPlaylistId`；不调用播放服务 |
| 新建 | 默认值正确；正确追加；空名回落「新歌单」；重名允许；不设 `ViewedPlaylist` / `CurrentPlaylistId`；空曲目返回 `null` |
| 持久化 | 追加 / 新建触发 `StateChanged`；`BuildSnapshot` 含新曲目路径 |
| 转发 | `LibraryViewModel` 把接收到的曲目列表按原顺序原样传给容器 |
| 回归 | 既有歌单增删改、拖拽入队、导入 / 导出、库播放与断点续播测试保持通过 |

使用既有测试替身与 `Track` 实例，不依赖真实文件。基线为 283 默认 + 5 音频 = 288（2026-11-08 记录，不在本文重跑）；新增用例后以实际输出核对非零测试数。

### 7.2 WPF 人工验收

1. 专辑页右键专辑卡片 → 对话框显示曲目数 → 加入普通歌单 → 切到该歌单，顺序与专辑顺序一致。
2. 专辑详情右键单曲 → 加入；同一首重复加入两次，歌单出现两条。
3. 搜索结果专辑行 / 曲目行右键 → 同一流程；滚动后右键仍作用于当前行。
4. 「新建并加入」→ 新歌单出现、内容正确、库视图不切换、侧栏无高亮；重启后内容仍在。
5. 文件夹歌单置灰、不可选、有原因说明。
6. 没有可选普通歌单时焦点落在新建区；列表为空时显示空态；空名回落「新歌单」。
7. 600×500、长歌单名 / 长专辑名；对话框不被遮挡；长名称省略且 `ToolTip` 完整。
8. 加入时不打断正在播放的曲目；当前歌单、查看歌单、侧栏高亮均不变。
9. 取消 / Esc 不产生任何写盘或歌单变化。
10. 关闭重启后 `queue.json` 新内容恢复，schema 版本不变。

仅使用合成库与临时数据；破坏性操作前后按项目规则备份 / 还原 `settings.json`、`queue.json`、`library-cache.json`，不修改用户音乐文件。

### 7.3 门禁

- 每个实现步骤：`powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-gates.ps1 -Fast`。
- Core 公开面 / 构造链变化与最终收尾：同命令使用 `-Full`，额外检查冻结 WinUI 壳编译。
- 构建 0 警告 0 错误；默认测试排除 `Category=AudioDevice`。
- `dotnet test` 不加 `--nologo`；真实音频测试只在显式 `-Audio` 时运行。

## 8. 限制与交付顺序

仅支持普通歌单、单曲与整张专辑；没有拖拽、去重、通知 toast、多选与撤销。对话框列表没有搜索，歌单极多时需要滚动。追加使用点击时快照，不保证库在对话框期间变化后曲目仍存在。

先完成本文审阅，再写实施计划；实现后同步 README、PROJECT、COUPLING 与新增 Phase 26 验收记录，届时才更新当前能力与阶段状态。

本次只交付这份设计文档，不修改代码、不编写实施计划、不推送；两份刻意未入库的 Phase 24 / 25 本地实施计划保持原样。
