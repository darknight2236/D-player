# Phase 17：UI 深度深色定制（自定义标题栏 + 控件深色化）设计

> 设计日期：2026/09/13 · 对应分支：`master` · 状态：**待实现**
>
> 目标：把仍为默认浅色的 UI 元素全部纳入深色主题——**自定义无边框标题栏**、**ComboBox**（含下拉 popup/选项）、**CheckBox**、**ScrollBar**、**ToolTip**、**ContextMenu/MenuItem**——消除与深紫主题的割裂感。纯表现层，不改功能行为。

---

## 0. 背景与动机

当前 `Themes/Controls.xaml` 仅有 `Window`（背景/前景/字体）、`Button`、`Slider` 的完整模板，以及 ListBox/TextBox 等的焦点框关闭。**ComboBox、CheckBox、ScrollBar、ToolTip、ContextMenu 仍为 OS 默认浅色**；标题栏为 OS 绘制浅色（`Window` 隐式样式只设 Background，不含 chrome）。在深色界面中这些浅色元素非常突兀。

## 1. 目标与范围

### Goals
- **自定义无边框标题栏**：`WindowStyle=None` + `WindowChrome` + 自绘 `TitleBar`，应用于 MainWindow 与 3 个对话框。
- **ComboBox** 深色：控件本体 + 下拉 `Popup` + `ComboBoxItem`（悬停/选中态）。
- **CheckBox** 深色：方框 + 选中 accent 勾。
- **ScrollBar** 深色：竖/横 thumb/track。
- **ToolTip / ContextMenu(+MenuItem)** 深色。
- 复用 `Colors.xaml` 画刷；新增图标 `Icon.Maximize` / `Icon.Restore`（加入 `Themes/Icons.xaml`）。

### Non-Goals
- 不重写布局/功能；不改交互逻辑。
- 不引入第三方 UI 库。
- 不做多主题切换（仍单一深紫主题）。
- 不定制 RadioButton/ProgressBar/TreeView/DatePicker 等当前未用控件。

## 2. 标题栏：WindowStyle=None + WindowChrome + 自绘 TitleBar

- **WindowChrome**（`System.Windows.Shell`）：`CaptionHeight=32`、`UseAeroCaptionButtons=False`、`GlassFrameThickness=0`、`ResizeBorderThickness` 默认；配合 `WindowStyle=None` 得到无边框外观，**OS 仍负责拖动/快照/系统菜单**（经 caption 高度），避免手写 DragMove/快照的边界 bug。
- **新建 `Views/Controls/TitleBar.xaml` UserControl**（可复用）：
  - 依赖属性：`Title`(string)、`ShowMaximize`(bool，默认 true)。
  - 内容：标题文本（ForegroundPrimary）+ 右侧按钮组：最小化(`Icon.Minus`)、最大化/还原(`Icon.Maximize`/`Icon.Restore`，仅 ShowMaximize 时)、关闭(`Icon.Close`)。
  - 按钮标 `WindowChrome.IsHitTestVisibleInChrome=True` 以接收点击；动作走 `SystemCommands.MinimizeWindow / MaximizeWindow / RestoreWindow / CloseWindow`(相对父 Window)。
  - 最大化/还原图标随 `WindowState` 切换（Maximized→Icon.Restore，否则 Icon.Maximize）。
- **最大化边距修正**：TitleBar 或 Window 监听 `StateChanged`；Maximized 时给窗口根容器加 `SystemParameters.WindowResizeBorderThickness` 边距（防内容顶到屏边/任务栏下），还原时清除。
- **应用**：MainWindow `ShowMaximize=true`（显示最小化/最大化/关闭）；SettingsDialog / EqualizerDialog / PromptDialog `ShowMaximize=false` 且**仅显示关闭按钮**（隐藏最小化/最大化）。各 Window 设 `WindowStyle=None` 并在顶部放 `<controls:TitleBar Title="…"/>`；移除原 OS 标题栏依赖。

## 3. 控件深色隐式样式（加入 Themes/Controls.xaml）

- **ComboBox**：`ControlTemplate`（深色 Border toggle + 右侧箭头 Path）；`Popup` 内 `Border` Background=BackgroundSecondary；`ComboBoxItem` ItemContainerStyle：默认 ForegroundPrimary、悬停 Background=BackgroundTertiary、选中 Background=AccentPrimary+前景对比。
- **CheckBox**：`ControlTemplate`（深色 Border 方框 + 选中时 accent 勾 Path）+ `ContentPresenter` Foreground=ForegroundPrimary；悬停边框提亮。
- **ScrollBar**：竖/横 `ControlTemplate`：track=BackgroundSecondary、thumb=BackgroundTertiary（悬停更亮）、**隐藏箭头按钮**（仅 track+thumb，简洁深色滚动条）。
- **ToolTip**：隐式 Style：Background=BackgroundSecondary、Foreground=ForegroundPrimary、Border 深色、圆角。
- **ContextMenu + MenuItem**：隐式 Style 深色 Background；MenuItem 悬停 Background=BackgroundTertiary、Foreground 对比；分隔符深色。

## 4. 应用范围

- 标题栏：`MainWindow`、`SettingsDialog`、`EqualizerDialog`、`PromptDialog`。
- 其余控件经 `Themes/Controls.xaml` 隐式样式全局生效（含 EqualizerDialog 内的 ComboBox/CheckBox、SettingsDialog 的 CheckBox/ComboBox、各 ScrollBar/ToolTip）。

## 5. 测试与验收

- 视觉改动无单测；验证 = `dotnet build` 0 错误 + `dotnet test` 96 全绿 + **ComputerUse 截图**核对：
  1. 主窗/设置窗/均衡器窗/输入窗标题栏为深色自绘（含关闭/最小化/最大化按钮矢量图标）；
  2. 拖动标题栏可移动；最大化/还原正常且边距正确；
  3. ComboBox 展开 popup 深色、选项悬停/选中态正确；
  4. CheckBox 勾选显示 accent 勾；
  5. ScrollBar 深色；ToolTip/ContextMenu 深色；
  6. 无残留浅色原生元素。

## 6. 风险与边界

- **WindowChrome 命中测试**：自绘按钮必须 `IsHitTestVisibleInChrome=True`，否则点击被 caption 拖动吞掉。
- **最大化边距**：Maximized 时需加 `WindowResizeBorderThickness` 边距，否则内容贴屏边/被任务栏遮。
- **模态对话框 chrome**：WindowStyle=None 的 ToolWindow 模态窗与 Owner 居中/置顶行为需实测兼容。
- **Snap/Aero 行为**：依赖 WindowChrome 保留（caption 高度），不自写拖动。
- 小尺寸对话框（PromptDialog）标题栏高度与按钮密度需协调。

## 7. 参考

- 现主题：`Themes/Colors.xaml`、`Themes/Controls.xaml`；图标：`Themes/Icons.xaml`。
- 窗口/对话框：`Views/MainWindow.xaml`、`Views/Dialogs/{SettingsDialog,EqualizerDialog,PromptDialog}.xaml`。
- 先例：Phase 16 图标矢量化（`Icon.*` + `IconPath`）。
