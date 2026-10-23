# D-player Phase 22 — WinUI 壳功能补齐（日常可用闭环）设计

> 日期：2026-10-23 · 状态：待用户审阅 · 代码基线：`e6da798`（Phase 21 收尾）
> 关联文档：[`PHASE20-COMPARISON.md`](../../PHASE20-COMPARISON.md) §3 差距表、[`PHASE21-ACCEPTANCE.md`](../../PHASE21-ACCEPTANCE.md)、[`COUPLING.md`](../../COUPLING.md)
> 后续阶段：Phase 23 = 频谱可视化 + EQ 对话框 + 设置里的频谱参数；Phase 24 = 拖拽（外部入队/队列重排/侧栏重排）、表头排序、文件夹绑定歌单管理与刷新、TrackList 当前曲 ▶ 标记。

---

## 0. 决策记录

用户 2026-10-23 的四项拍板：

| 决策点 | 结论 |
|---|---|
| Phase 22 范围 | **日常可用闭环**：播放控制 + 导入/导出 + 歌单增删改 + 设置基础版 + 窗口几何 + 日志出口 |
| 文件对话框契约 | **Core 接口改异步**：`IFileDialogService` 三个方法改为 `Task` 返回，两壳各自实现 |
| 底栏布局 | **保持 64px 单行紧凑**，三档响应式，不推翻 Phase 21 的 `PlayerBarHeight` |
| WinUI 自动化保护 | **单壳构建纳入 `verify-gates.ps1 -Full`**，作为 `-Full` 的第三道门禁 |

方案选型：**契约先行 + 壳侧切片**（先改异步契约并让两壳/测试全绿，再做 UI 切片）。被否的两个方案：
- 界面先行 + WinUI 异步旁路：会产生两条导入路径，契约迁移时重做一遍。
- Core 引入通用对话框抽象（确认/输入/错误进 Core）：违反 Core 无 UI 类型的分层纪律，为 4 个对话框引入过度抽象。

---

## 1. 背景与目标

Phase 20 交付了 WinUI 壳的第一条纵向切片；Phase 21 完成了三栏 IA、令牌层、六态、键盘/无障碍与 9 项动效，23 项验收全部通过。现在的 WinUI 壳能起窗、能播、能用键盘操作，但**不能自助导入曲目、不能管理歌单、没有上一首/下一首/随机/循环/音量、不保存窗口位置**——它还是一款“演示壳”，不是日常播放器。

Phase 22 的目标是闭合这条差距，让 WinUI 壳达到**日常可用**：

1. 首启空状态下能通过 UI 导入本地音频（文件/文件夹）与播放列表文件（M3U/M3U8/PLS）。
2. 能新建、重命名、删除歌单，能切换到任意歌单并看到哪个歌单正在播放。
3. 底栏能完成上一首/下一首、随机、循环、音量/静音与设置入口，且保持 64px 高度。
4. 关闭再打开，窗口位置/尺寸、音量与断点续播都还在。
5. 崩溃/未处理异常有本地日志可查；WinUI 构建从“手跑检查”升级为 `-Full` 门禁。

**成功标准**：一个只用过 WPF 壳的用户，在 WinUI 壳里不需要手工复制 `queue.json` 就能完成“导入 → 建歌单 → 播放 → 调音量 → 关窗 → 重开继续”的完整闭环，且 Phase 21 的视觉/键盘/折叠/动效没有回退。

---

## 2. 范围

### 2.1 本阶段做

| # | 能力 | 现状 |
|---|---|---|
| 1 | `IFileDialogService` 异步契约 + 两壳实现 | 同步接口；WinUI 是空实现 |
| 2 | 添加文件、导入文件夹（递归追加）、导入播放列表文件（追加/新建） | WinUI 全无入口 |
| 3 | 导出当前歌单为 M3U8 | WinUI 全无入口 |
| 4 | 新建/重命名/删除歌单、▶ 活跃歌单标记 | WinUI 只读导航 |
| 5 | 上一首/下一首/随机/循环/音量/静音/设置入口 | WinUI 只有播放/暂停与进度 |
| 6 | 设置基础版（音量立即生效 + 音频输出只读 + 日志入口） | WinUI 无设置对话框 |
| 7 | 窗口位置/尺寸持久化 + 屏外自愈 | WinUI 每次默认位置 |
| 8 | 本地日志（未处理异常/启动失败/播放错误） | WinUI 只有 `Debug.WriteLine`（WinExe 无出口） |
| 9 | WinUI 构建纳入 `verify-gates.ps1 -Full` | 目前是“手跑的壳侧检查” |

### 2.2 本阶段不做

| 能力 | 去向 / 理由 |
|---|---|
| 频谱可视化、EQ 与 EQ 对话框 | Phase 23；数据与实时下发已在 Core |
| 设置里的频谱参数（启用/灵敏度/颜色/平滑度） | Phase 23；避免设置里出现没有可视化对应的死控件 |
| 拖拽（外部文件入队、队列内重排、侧栏歌单重排） | Phase 24；WinUI DragDrop 与 WPF Adorner 机制不同，需单独设计 |
| 表头排序 | Phase 24；Phase 21 的表头是静态标签行 |
| 文件夹绑定歌单的新建/刷新/扫描状态/图标 | Phase 24；本阶段的“导入文件夹”是**递归扫描后追加到当前歌单**，不是新建文件夹绑定歌单 |
| TrackList 当前曲 ▶ 标记、行删除按钮/Delete 键 | Phase 24 |
| InfoPanel 采样率读数 | Phase 24（数据在 Core，纯 UI 活） |
| Mica 三行对照复测 | 证据类任务，不属于“日常可用”；单独排期，不塞进本阶段 |
| WPF 壳的任何行为变更 | 本阶段 WPF 只跟随异步契约做适配，用户可见行为必须不变 |
| WPF `App.OnExit` 同步 Dispose、二次关闭截断 | 两壳对称性问题，单独拍板；本阶段只在 WinUI 关闭路径加守卫，记录不对称 |

### 2.3 事实更正

Phase 20 §5 起点里的“**进度条键盘步进饱和**”已在 Phase 21 的 `9657f95` 修掉：`SmallChange=0.005`、`LargeChange=0.05`，PageUp/PageDown 通过 `PlayerBar.Position_KeyDown` 映射到 `LargeChange`；Phase 21 验收 C2 已实测通过。Phase 22 **不包含**该任务。

---

## 3. 架构与契约

### 3.1 异步文件对话框

Core 接口（`D-player.Core/Services/IFileDialogService.cs`）：

```csharp
public interface IFileDialogService
{
    Task<IReadOnlyList<string>> OpenFilesAsync(string filter, bool multiselect = false);
    Task<string?> OpenFolderAsync();
    Task<string?> SaveFileAsync(string filter, string defaultFileName, string defaultExtension);
}
```

语义：

- `filter` 仍是 WPF 风格的 `"描述|*.ext;*.ext2"` 字符串；参数格式不变，只是返回类型变 `Task`。
- 取消、用户关闭、picker 失败、返回虚拟路径（`StorageFile.Path` 为空）统一返回空集合/`null`，与切片期的“取消”语义一致。
- 不加 `CancellationToken`：picker 是用户驱动的模态操作，本阶段没有取消来源；未来需要时再扩契约。
- 接口 XML 文档里的 `[STA Thread Required]` 保留，补充“异步方法不保证续延线程；VM 侧一律 `ConfigureAwait(true)` 后再改 `ObservableCollection`”。

调用点（全部改为 `await`）：

- `PlaylistViewModel`：`AddToQueue`、`ImportFolderToCurrent`、`ImportPlaylistFileAsync`、`ExportPlaylistFileAsync`。
- `PlaylistsViewModel`：`ImportFolderAsync`、`ImportPlaylistFileAsync`；内部 `NullFileDialogService` 桩同步改为返回 `Task.FromResult`。

`Win32FileDialogService`（WPF）：

- 保留现有的 `Microsoft.Win32.OpenFileDialog` / `OpenFolderDialog` / `SaveFileDialog` 私有同步方法；三个接口方法返回 `Task.FromResult(...)`。
- 对话框仍在 UI 线程同步弹出，行为与 Phase 20/21 完全一致。

`WinUiFileDialogService`：

- 构造函数收 `Func<IntPtr> hwndProvider`；每次弹 picker 前调用 `WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd)`。
- App 持有 `private IntPtr _hwnd;`，在 `new MainWindow(...)` 之后用 `WindowNative.GetWindowHandle(_window)` 写入；DI 工厂捕获 `() => _hwnd`。窗口尚未创建时 `_hwnd == 0`：写日志并返回取消语义，不抛给 VM。
- 三个 picker：
  - `FileOpenPicker`：`SuggestedStartLocation = PickerLocationId.MusicLibrary`；`FileTypeFilter` 由 filter 解析；`PickMultipleFilesAsync` / `PickSingleFileAsync` 后取 `StorageFile.Path`。
  - `FolderPicker`：必须先 `FileTypeFilter.Add("*")`，再 `PickSingleFolderAsync`。
  - `FileSavePicker`：`SuggestedFileName = defaultFileName`；`FileTypeChoices` 由 filter 的描述与扩展名生成；`DefaultFileExtension = defaultExtension`；`PickSaveFileAsync`。
- filter 解析（壳内侧，一个纯函数）：
  - 取第一个 `|` 后的段，按 `;` 拆分，trim，保留以 `*` 开头的模式。
  - 给 `OpenFilesAsync` 用时把 `*.mp3` 转成 `.mp3` 再加入 `FileTypeFilter`——WinUI 的 `FileTypeFilter` 只接受 `.ext` 或 `*`，传 `*.mp3` 会抛。
  - 给 `SaveFileAsync` 时把 `*.m3u8` 转成 `".m3u8"` 作为 `FileTypeChoices` 的扩展名；没有合法扩展名则写日志并返回 `null`（不猜）。
  - 解析失败或为空时，`OpenFilesAsync` 退回 `FileTypeFilter.Add("*")`；`SaveFileAsync` 返回 `null` 并写日志。
- picker 调用抛异常（无窗口、COM 失败等）一律捕获、写日志、返回取消语义；UI 不崩。

**契约边界**：Core 只依赖异步接口，不引用任何 WinUI 类型；WinUI 的 HWND/picker 细节全部留在壳内。

### 3.2 对话框层

新增壳内静态 `D-player.WinUI/Services/WinUiDialogService.cs`（只被 View 调用，不进 Core）：

```csharp
internal static class WinUiDialogService
{
    static Task<string?> ShowPromptAsync(FrameworkElement owner, string title, string label, string initial);
    static Task<bool> ShowConfirmAsync(FrameworkElement owner, string title, string message);
    static Task ShowErrorAsync(FrameworkElement owner, string title, string message);
    static Task ShowInfoAsync(FrameworkElement owner, string title, string message);
}
```

- `owner.XamlRoot` 为空时返回取消/`false` 并写日志；绝不抛。
- Prompt：`TextBox` +「确定」/「取消」；Primary 默认；返回原始文本（由 Core 决定空字符串如何回落）。
- Confirm：Primary 是危险动作（删除/清空），**默认焦点在「取消」**（`DefaultButton = ContentDialogButton.Close`），防误触。
- Error/Info：单一「关闭」。
- 所有对话框标题/正文中文，按钮文案与现有 WPF 中文口径一致。

### 3.3 全局空格守卫

`MainWindow.RootGrid_KeyDown` 现在只排除 `Button` / `Slider` / `ListViewItem`。加入文本输入后，在歌单名里输入空格会触发播放/暂停。本阶段修：

- 焦点在 `TextBox` / `PasswordBox` / `RichEditBox` / `ComboBox` 上（含其可视化后代）→ 不处理。
- 任一 `ContentDialog` 打开时 → 不处理（扫 `VisualTreeHelper.GetOpenPopupsForXamlRoot`，检查弹层内是否有 `ContentDialog`）。
- 其余逻辑不变（空格仍只切换播放/暂停）。

### 3.4 门禁升级

`tools/verify-gates.ps1`：

- `-Fast`：gate 1 = `.slnf` 构建 0 警告、gate 2 = `.slnf` 测试 180 通过。
- `-Full`：在 1+2 之后增加 **gate 3 = `dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q`**，同样断言 0 警告。
- 文档口径从“gate + 壳侧检查（手跑）”改为“`-Fast` 两道门禁；`-Full` 三道门禁”。`-Full` 是 Phase 22 每个任务与最终验收的必跑命令。
- 脚本继续保持纯 ASCII（PowerShell 5.1 按 GBK 读无 BOM 文件）。
- 不把 WinUI 塞进 `D-player.slnf`：`.slnf` 仍是 Core + WPF + Tests，保住 `-Fast` 的秒级速度。

### 3.5 分层与数据归属

- Core 继续不出现 WPF/WinUI 类型；本阶段 Core 的公开面变化**只有** `IFileDialogService`。
- 歌单增删改、播放控制、导入/导出、音量持久化的业务逻辑全部复用 Core 现有命令/方法，壳侧不复制算法、不新增第二份状态。
- 窗口几何、日志、对话框、HWND 均属壳侧，不进 Core。

---

## 4. UI 设计

### 4.1 NavRail（左栏）

保持 200 ↔ 48px 折叠与 Phase 21 的六态/动效。

**顶部工具栏**（展开态）：`[折叠钮 24] [歌单] ········ [新建 +][导入列表][删除 −]`。
- 三个动作按钮各 24×24，`IconButtonStyle`，ToolTip + `AutomationProperties.Name`。
- 折叠态（48px）：隐藏文字与三个动作按钮，只留折叠钮与徽标；折叠逻辑复用现有 `Tag="Fade"` 收集机制，动作按钮放进同一个可淡出容器。

**条目模板**：`[▶ 活跃标记 12] [徽标 24] [名称 *]`。
- ▶ 只在 `IsActivePlaylist == true` 时可见；用新增的 `Views/Converters.cs` 里的 `BoolToVisibilityConverter` 绑定，OneWay。
- 折叠态下 12 + 24 + padding 仍落在 48px 内；标记不淡出，保证折叠时也能看到哪个歌单在播。

**交互**：
- `+` → PromptDialog「新建歌单」，默认值 `新歌单`；确认后 `AddPlaylistCommand.Execute(text)`。
- 双击条目 → PromptDialog「重命名歌单」，默认当前名称；确认后 `RenamePlaylistCommand.Execute((target, text))`。
- `−` → 对 `ViewedPlaylist` 弹确认框；确认后 `RemovePlaylistCommand.Execute(target)`。最后一个歌单由 Core 自动重建“默认歌单”。
- 导入列表 → `Playlists.ImportPlaylistFileAsync()`（**新建歌单**语义），完成后用报告文案弹 Info 对话框。
- 所有命令都在 UI 线程执行；操作后保留当前选择（Core 的指针语义已覆盖）。

### 4.2 TrackList（中栏）

保持 36px 行高与现有四列（`#` / 标题 / 艺术家 / 时长）。

**新增工具栏行**（列表上方，40px）：

`[添加文件][导入文件夹][导入列表] ····· [导出][清空]`

- 全部图标按钮 + ToolTip + UIA Name；`导出` / `清空` 在 `Queue.Count == 0` 时禁用。
- 语义：
  - 添加文件 → `ViewedPlaylist.AddToQueueCommand`（多选）。
  - 导入文件夹 → `ViewedPlaylist.ImportFolderToCurrentCommand`（递归扫描，追加）。
  - 导入列表 → `ViewedPlaylist.ImportPlaylistFileAsync()`（**追加到当前歌单**），完成后显示报告。
  - 导出 → `ViewedPlaylist.ExportPlaylistFileAsync()`；返回错误文案时弹 Error，成功或取消不打扰。
  - 清空 → 确认框后 `ViewedPlaylist.ClearQueueCommand`。
- 忙碌态：`_isImporting` 标志；导入期间工具栏按钮禁用 + 工具栏右侧显示 `ProgressRing`（16px），导入结束恢复。防连点，不做取消。
- 空状态文案从「导入入口将在下一阶段加入」改为「点击上方“添加文件”或“导入文件夹”开始」；空状态动画保持不变。
- `ViewedPlaylist` 为空（启动水化前）时所有按钮禁用；`ResyncView` 统一更新。

### 4.3 PlayerBar（底栏，64px 单行）

```
┌────────────────────────────────────────────────────────────────────────────────────┐
│ 当前曲目 180px │ [上一首][播放/暂停 44][下一首] [随机][循环] │ 进度 ─────── 01:23 / 04:56 │ [静音][音量 88][设置] │
└────────────────────────────────────────────────────────────────────────────────────┘
```

- 根 `Grid Height="{StaticResource PlayerBarHeight}"`（64），`Padding="12,6"`，`ColumnSpacing="8"`；单行、所有控件 32px（播放键 44px），64px 内不裁切。
- 列：`Auto(当前曲目，Wide 时 TextBlock 宽 180、截断) | Auto(传输簇) | *(进度+时间) | Auto(音量+设置)`。
- 传输簇：上一首 32 / 播放暂停 44 / 下一首 32 / 随机 32 / 循环 32，间距 4。
- 进度组：`Slider *` + 时间 84px；进度条沿用 Phase 21 的两条输入路径（模板 Thumb 拖动 + 轨道 PointerPressed 单击）与 `StepFrequency=0.001`，**不重写**。
- 右侧：静音 32 + 音量滑块 88 + 设置 32。

**响应式三档**（`VisualStateManager` + `AdaptiveTrigger`，按窗口宽度）：

| 状态 | 阈值 | 变化 |
|---|---|---|
| Wide | ≥1100 | 全部可见 |
| Medium | ≥860 | 收起音量滑块（保留静音钮） |
| Narrow | <860 | 再收起“当前曲目”文本 |

窄窗下传输簇、进度、时间、静音、设置必须仍可见；不允许横向滚动或控件重叠。

**状态与命令**：

- 上一首/下一首 → `Playlists.ViewedPlaylist.PrevTrackCommand` / `NextTrackCommand`；`HasCurrentTrack == false` 时禁用。
- 随机 → `Playlists.ToggleShuffleCommand`；开=强调色，关=次要色；ToolTip「随机播放：开/关」。
- 循环 → `Playlists.CycleRepeatCommand`；Off=次要色 `RepeatAll`、List=强调色 `RepeatAll`、One=强调色 `RepeatOne`；ToolTip「循环：关/列表/单曲」。
- 音量：`Player.Volume`；滑块 OneWay 回显 + `ValueChanged` 写回（`_syncingVolume` 守卫，避免循环）；静音 → `Player.ToggleMuteCommand`；静音或音量为 0 时图标显示静音态。
- 播放/暂停：现有按钮、图标交叉淡入、按下缩放全部保留；`HasPlayableSource` 的启用逻辑不变。
- 设置 → 打开 4.4 的 SettingsDialog（本按钮在 Task 5 加入）。
- EQ 按钮不在本阶段出现。
- 所有新按钮都要有 `AutomationProperties.Name` 与 ToolTip；状态变化时同步更新 Name/ToolTip 文本（随机、循环、静音）。

### 4.4 设置对话框

`ContentDialog`（`Views/Dialogs/SettingsDialog.xaml(.cs)`），由 PlayerBar 设置按钮打开，接收 `PlayerViewModel`。

- 通用：音量滑块 + 百分比（立即生效；沿用 `Player.Volume` 的现有持久化，不新增字段）。
- 音频输出：只读「系统默认 — WASAPI 共享」+ 说明「输出设备选择将在后续版本开放」。
- 日志：显示日志文件路径 +「打开日志文件夹」按钮（`Process.Start("explorer.exe", logsDir)`）。
- 单一「完成」按钮（`CloseButtonText`）；不做“保存/取消”假语义。
- 不出现频谱/EQ 设置（Phase 23）。

### 4.5 窗口几何

启动：

1. `MainWindow` 构造时同步读取 `ISettingsPersistence.LoadAsync()`（与 WPF 同款 `GetAwaiter().GetResult()`，文件极小）。
2. 有效条件：`WindowWidth >= 800 && WindowHeight >= 500`；否则用默认 **1200×800**。
3. 中心点不在任何 `DisplayArea` 的 `WorkArea` 内（外接屏被拔掉）→ 主屏居中。
4. `AppWindow.MoveAndResize(new RectInt32(left, top, width, height))`，在 `Activate()` 之前执行，避免起窗跳动。
5. 单位：**物理像素**（WinUI 自己的 `settings.json`；与 WPF 的 DIP 不共享目录，不会互相污染）。COUPLING 中写明；跨 DPI 屏迁移时窗口物理尺寸不变，不额外换算。

关闭：

1. 只在 `AppWindow.Presenter is OverlappedPresenter { State: Restored }` 时读取 `AppWindow.Position` / `AppWindow.Size` 并 `UpdateAsync` 写回四个字段；最大化/最小化不覆盖上次的普通几何。
2. 几何保存失败只写日志，不阻挡关闭。
3. 二次关闭请求：`if (_isClosing) { args.Cancel = true; return; }`，保证清理/flush 完成前窗口不提前消失。WPF 侧同形缺陷记录在案，本阶段不改。

---

## 5. 日志

新增 `D-player.WinUI/Services/AppLog.cs`：

- 路径：`%LOCALAPPDATA%\D-player-winui\logs\app.log`；目录常量与 `DPlayerDataPaths` 的 `D-player-winui` 同源（壳内 `WinUiAppPaths` 常量）。
- `Initialize(logDirectory)` 幂等；`Write(string message, Exception? ex = null)` 加锁追加；单文件超过 1 MiB 时把 `app.log` 改名为 `app.1.log`（覆盖旧 `.1`）。
- 所有 IO 异常静默吞掉；日志本身不得成为崩溃源。
- 内容：`yyyy-MM-dd HH:mm:ss.fff` + 线程 id + 消息 + 异常全文。

挂钩（`App` 启动时）：

- `Application.UnhandledException` → 记录；**不设置 `Handled`**，不把崩溃变成半死状态。
- `AppDomain.CurrentDomain.UnhandledException` → 记录。
- `TaskScheduler.UnobservedTaskException` → 记录；不调用 `SetObserved`。
- `IPlaybackService.PlaybackError` → 记录（Core 的 `PlayerViewModel.HandlePlaybackError` 目前吞掉该事件，本阶段不改 Core；壳侧额外订阅一份用于日志）。
- `StartAsync` 捕获的初始化异常、断点续播被跳过的警告、picker 失败、几何保存失败 → 记录。

用户可见错误仍走对话框；日志是排障出口，不是用户提示。

---

## 6. 数据流与持久化

| 数据 | 真源 | 写入时机 | 读取时机 |
|---|---|---|---|
| 歌单/队列 | Core `PlaylistsViewModel` | 现有 debounce + 关闭 flush | 启动 `InitializeAsync` |
| 音量 | Core `PlayerViewModel.Volume` | 现有 `OnVolumeChanged` → `DefaultVolume` | 启动 `PlayerViewModel` |
| 断点续播 | `LastPlayedPath` / `LastPlayedPositionSeconds` | 现有 30s 节流 + 暂停/切歌/关闭 | 启动 `RestoreLastPlayedAsync` |
| 窗口几何 | WinUI 壳 | 关闭且处于 Restored 状态 | 启动、`Activate` 前 |
| 日志 | WinUI 壳 | 事件发生时 | 用户/开发者打开日志目录 |

导入/导出：

```
UI click
  → IFileDialogService (async picker)
  → Core VM (读元数据 / 播放列表文件服务)
  → Queue.CollectionChanged
  → 现有 debounce save queue.json
  → 对话框展示报告/错误
```

---

## 7. 任务分解

每个任务结束都必须：`.slnf` 构建 0 警告 0 错误；`dotnet test D-player.slnf -c Debug -v q` 180 通过 / 0 失败（**不加 `--nologo`**）；`dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q` 0/0；`powershell -File tools/verify-gates.ps1 -Full` 全绿。

### Task 1 — 异步文件对话框契约 + 门禁升级

**Files**
- Modify: `D-player.Core/Services/IFileDialogService.cs`
- Modify: `D-player.Core/ViewModels/PlaylistViewModel.cs`
- Modify: `D-player.Core/ViewModels/PlaylistsViewModel.cs`
- Modify: `D-player/Services/Win32FileDialogService.cs`
- Modify: `D-player.WinUI/Services/WinUiFileDialogService.cs`
- Modify: `D-player.WinUI/App.xaml.cs`
- Modify: `Tests/ViewModels/PlaybackResumeTests.cs`
- Modify: `Tests/ViewModels/PlaylistsViewModelTests.cs`
- Modify: `Tests/ViewModels/PlaylistViewModelTests.cs`
- Modify: `Tests/Extensions/AddDPlayerCoreTests.cs`
- Modify: `tools/verify-gates.ps1`
- Modify: `README.md`（门禁口径；完整文档同步在 Task 7）

**Produces**：三个 `Task` 方法；WPF 行为不变；WinUI picker 真实现；`-Full` 成为三道门禁。

### Task 2 — 对话框基建 + NavRail 歌单管理 + 空格守卫

**Files**
- Create: `D-player.WinUI/Services/WinUiDialogService.cs`
- Modify: `D-player.WinUI/Views/NavRail.xaml(.cs)`
- Modify: `D-player.WinUI/MainWindow.xaml.cs`（空格守卫）
- Create: `D-player.WinUI/Views/Converters.cs`（`BoolToVisibilityConverter`；现有两个转换器住在 `TrackList.xaml.cs`，不往那里再塞）
- Modify: `D-player.WinUI/Theme/Tokens.xaml`、`Theme/Styles.xaml`（工具栏尺寸/样式）

**Produces**：新建/重命名/删除、▶ 活跃标记、Prompt/Confirm/Info/Error 对话框、文本输入与 ContentDialog 不再触发全局空格。

### Task 3 — TrackList 导入/导出工具栏

**Files**
- Modify: `D-player.WinUI/Views/TrackList.xaml(.cs)`
- Modify: `D-player.WinUI/Theme/Tokens.xaml`、`Theme/Styles.xaml`
- Modify: `D-player.WinUI/Views/TrackList.xaml.cs`（空状态文案、忙碌态）

**Produces**：添加文件、导入文件夹、导入列表、导出、清空；报告/错误对话框；忙碌态。

### Task 4 — PlayerBar 控制补齐

**Files**
- Modify: `D-player.WinUI/Views/PlayerBar.xaml(.cs)`
- Modify: `D-player.WinUI/Theme/Tokens.xaml`、`Theme/Styles.xaml`

**Produces**：上一首/下一首、随机、循环、音量/静音、64px 单行 + 三档响应式；保留现有进度条两条输入路径与播放/暂停动效。

### Task 5 — 设置对话框 + 窗口几何

**Files**
- Create: `D-player.WinUI/Views/Dialogs/SettingsDialog.xaml(.cs)`
- Create: `D-player.WinUI/Services/WinUiAppPaths.cs`（数据目录/日志目录常量）
- Modify: `D-player.WinUI/Views/PlayerBar.xaml(.cs)`（设置按钮）
- Modify: `D-player.WinUI/MainWindow.xaml(.cs)`（构造参数 `ISettingsPersistence`、几何恢复/保存/自愈、二次关闭守卫）
- Modify: `D-player.WinUI/App.xaml.cs`（解析并传入 `ISettingsPersistence`）

**Produces**：设置对话框；窗口几何恢复/保存/自愈；最大化不覆盖普通几何；二次关闭不截断 flush。

### Task 6 — 日志出口

**Files**
- Create: `D-player.WinUI/Services/AppLog.cs`
- Modify: `D-player.WinUI/App.xaml.cs`（初始化 + 三类未处理异常 + `PlaybackError` 订阅 + 启动失败）
- Modify: `D-player.WinUI/Services/WinUiFileDialogService.cs`（picker 失败）
- Modify: `D-player.WinUI/MainWindow.xaml.cs`（几何保存失败）

**Produces**：本地日志文件、轮转、异常钩子；日志 IO 失败不影响主流程。

### Task 7 — 验收与文档

**Files**
- Create: `docs/PHASE22-ACCEPTANCE.md`
- Modify: `docs/PHASE20-COMPARISON.md`（§2/§3 能力行）
- Modify: `docs/PROJECT.md`（Phase 22 行、门禁口径、文件对话框契约）
- Modify: `docs/COUPLING.md`（异步文件对话框、窗口几何像素单位、日志路径、二次关闭不对称）
- Modify: `README.md`（特性、构建/运行、已知限制、阶段表）

**Produces**：约 20 项验收清单（A 导入/歌单、B 播放控制、C 持久化、D 日志/门禁、E 回归），最终 `-Full` 全绿。

---

## 8. 测试策略

- **Core 回归**：异步契约由现有 180 条测试覆盖（导入/导出路径的 NSubstitute 桩改为 `Task.FromResult`）；不新增业务逻辑，测试数保持 180。若更新桩时发现某条测试实际没有 await（假绿），补成真正 await 的断言。
- **WPF 回归**：`Win32FileDialogService` 行为不变；由 `.slnf` 构建 + 现有测试 + 用户手测对话框共同覆盖。
- **WinUI 壳**：`-Full` 构建门禁 + 真机验收（无 UI 自动化框架；沿用 Phase 21 的 UIA/截图取证方式）。
- **不可由代理验证、必须交用户的项目**：日志文件内容（代理按纪律不读写 `%LOCALAPPDATA%\D-player-winui`）、屏外自愈（需要改窗口位置/拔屏）、真实 picker 的系统对话框交互。

---

## 9. 验收清单（草案，Task 7 落成文档）

**A 导入与歌单管理**
1. 空状态 → 添加文件（多选）→ 曲目、时长、序号正确显示。
2. 导入文件夹（递归）→ 只接收 5 种音频后缀，追加到当前歌单。
3. 导入 M3U/M3U8/PLS 到当前歌单 → 追加 + 报告含导入/跳过计数。
4. 左栏导入播放列表 → 新建歌单并切换查看。
5. 导出当前歌单 M3U8 → 文件存在、`#EXTINF` 与绝对路径正确。
6. 新建/重命名/删除歌单；删除最后一个自动重建默认歌单；删除当前播放歌单后播放器停止。
7. ▶ 活跃标记随播放歌单切换；折叠态仍可见。

**B 播放控制**
8. 上一首/下一首在当前曲目为空时禁用，有曲目时切歌正确。
9. 随机开/关：图标强调态、ToolTip、实际随机行为。
10. 循环三态：Off/List/One 图标与 ToolTip 正确；单曲循环播完重播。
11. 音量滑块与静音：即时生效、图标状态正确、重启后恢复。
12. 底栏 64px 实测；宽/中/窄三档无重叠、无横向滚动；窄窗仍可播放/切歌/调音量。
13. 设置对话框：音量立即生效；音频输出只读；日志入口可打开日志目录。

**C 持久化与窗口**
14. 移动/缩放窗口 → 关闭 → 重开：位置与尺寸恢复。
15. 最大化关闭 → 重开：回到上次的普通几何，不是最大化尺寸。
16. 断点续播回归：播放中关窗 → 重开按 ▶ 从断点继续。
17. 屏外自愈（用户手测）：窗口中心不在任何显示器内 → 主屏居中。

**D 日志与门禁**
18. 启动后日志文件存在且含启动记录；播放一个已损坏的音频文件（或断点路径指向的文件被删除）→ 有 `PlaybackError` 记录；日志 IO 失败不影响应用。未处理异常钩子由代码审阅确认，实际崩溃路径属于**不主动制造**的项，标 NOT VERIFIED。
19. `powershell -File tools/verify-gates.ps1 -Full` → 三道门禁全绿。
20. WPF 壳的导入/导出/文件夹对话框行为不变（用户手测）。

**E 回归**
21. `.slnf` 180 通过 / 0 失败；WinUI 单壳 0 警告 0 错误。
22. Phase 21 的折叠、Tab 顺序、空格播放/暂停、方向键步进、9 项动效抽样不回退。
23. Core 除 `IFileDialogService` 外无公开面变化；WPF 用户可见行为零变化。

---

## 10. 风险与已知缺口

| 风险 | 缓解 |
|---|---|
| WinUI 3 unpackaged 的 picker 需要 HWND，`InitializeWithWindow` 失败会抛 | 壳侧捕获 + 日志 + 返回取消；真机验收必须实际打开三种 picker |
| filter 字符串解析只覆盖现有两种格式 | 解析失败退回 `*`（打开）或返回 null（保存）；不猜扩展名 |
| NSubstitute 对未配置的 `Task` 方法返回 `null`，`await null` 会 NRE | 三个测试文件的桩全部显式返回 `Task.FromResult`；跑全量测试 |
| ContentDialog 依赖 `XamlRoot` | 统一从 owner 取；为空时返回取消并记日志 |
| 空格守卫的新范围可能误伤输入框之外的键 | 只扩大“不处理”的焦点类型，不改播放/暂停逻辑；验收回归 C3/C4 |
| 窗口几何用物理像素，跨 DPI 屏尺寸语义不同 | COUPLING 写明；本阶段不换算；默认尺寸与屏外自愈覆盖单屏/多屏 |
| 最大化关闭覆盖普通几何 | 只在 `Restored` 状态保存 |
| 日志文件代理不可读 | 验收第 18 项交用户；应用内提供“打开日志文件夹” |
| Core 的 `PlaybackError` 仍被 `PlayerViewModel` 吞掉 | 壳侧额外订阅 `IPlaybackService.PlaybackError` 写日志；不改变 Core 行为，记入已知缺口 |
| 二次关闭截断 flush 只在 WinUI 修 | 记录为两壳不对称，WPF 单独拍板 |
| 导入大文件夹耗时、无取消 | 忙碌态 + Plan 中不做取消；与 WPF 当前行为一致 |

---

## 11. 全局约束

- 直接在 `master` 上开发，不新建分支。
- **CRLF**：每次编辑后 `git ls-files --eol <file>` 必须是 `i/lf w/crlf`；用 `unix2dos` 修复；禁止 `sed -i`。
- **`dotnet test` 绝不加 `--nologo`**（会静默跑 0 条并打印“成功: 0”）；`--nologo` 只用于 `dotnet build`。
- 每个任务结束跑 `tools/verify-gates.ps1 -Full`；`.slnf` 0/0、测试 180/0、WinUI 0/0。
- **不自动推送**；`git push` 只在用户单独发话时执行。
- 代理**不读写** `%LOCALAPPDATA%\D-player\*` 与 `%LOCALAPPDATA%\D-player-winui\*`；需要读写这些路径的验收项交用户。
- WPF 壳用户可见行为零变化；Core 只允许 `IFileDialogService` 的契约变更，且不得引入 UI 类型。
- 不新增 NuGet 包；不把 WinUI 加入 `D-player.slnf`。
- 不修改 Phase 20/21 的历史实现计划与验收报告；只更新能力状态类文档（PHASE20 对比表、PROJECT、COUPLING、README）并新增 Phase 22 文档。

---

## 12. 文档同步

Task 7 必须完成：

- `docs/PHASE20-COMPARISON.md` §3：把“文件/文件夹对话框、播放列表导入/导出、音量与静音、上一首/下一首/随机/循环、设置对话框（基础版）、歌单增删/重命名、窗口几何”改为已具备；仍缺的频谱/EQ/拖拽/排序/文件夹绑定管理保持 ❌ 并注明 Phase 23/24。
- `docs/PROJECT.md`：Phase 22 行；门禁口径（`-Fast` 两道 / `-Full` 三道）；`IFileDialogService` 异步契约。
- `docs/COUPLING.md`：异步对话框契约、窗口几何物理像素、日志路径与轮转、二次关闭不对称、Core 无 UI 类型。
- `README.md`：特性、构建/运行、已知限制、开发阶段表。
- 新建 `docs/PHASE22-ACCEPTANCE.md`，格式与 Phase 21 一致（项 | 结果 | 证据 | 原始路径）。
