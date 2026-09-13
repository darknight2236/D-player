# Phase 16：应用图标矢量化重构（Icon Refactor）设计

> 设计日期：2026/09/13 · 对应分支：`master` · 状态：**待实现**
>
> 目标：用统一的**描边矢量图标系统**替换应用内全部 UTF-8/emoji 字形图标，解决渲染依赖系统 emoji 字体、不可随主题着色、风格不协调的问题。纯表现层重构，不改功能行为。

---

## 0. 背景与动机

当前图标全部为 UTF-8 字符 / emoji：

- **PlayerBar**：⏮(`&#x23EE;`)、▶/⏸(`PlayStateToIconConverter`)、⏭(`&#x23ED;`)、🔀(`&#x1F500;`)、🔁/🔂/⇄(`RepeatModeToIconConverter`)、🔊/(`PlayerViewModel.VolumeIcon`)、🎚(`&#x1F39A;`)、⚙(`&#x2699;`)。
- **PlaylistView**：▶ 当前标记（code-behind `Text="▶"`）、🔄 刷新、× 删除、"+ 添加"、"清空"。
- **Sidebar**：`+` / `−`、▶ 活跃标记（code-behind）、📂 文件夹、🔄 扫描指示。

问题：emoji 由系统字体（Segoe UI Emoji）渲染，**彩色且不可用 Foreground/accent 着色**（活跃态只能换字符或靠转换器换色字符）、跨 Windows 版本渲染不一致、与深色紫主题的纤细矢量观感**不协调**。

## 1. 目标与范围

### Goals
- 新建矢量图标资源（`Themes/Icons.xaml`），**描边 outline** 风格、24×24 viewbox。
- **全量替换**所有字形/emoji 图标：PlayerBar 全套、▶ 标记、Sidebar +/−/📂/🔄、PlaylistView ×/🔄/"+ 添加"/"清空"。
- 图标可随主题着色：默认 `ForegroundPrimary`，活跃态 `AccentHover`/`AccentPrimary`（复用 `BoolToAccentBrushConverter`）。
- 移除 VM 中的 emoji 字符串（`VolumeIcon`），守住「VM 层无 WPF 类型/无 emoji」纪律。

### Non-Goals
- 不改任何功能行为 / 布局结构 / 交互。
- 不引入运行时图标字体或位图依赖（几何数据以资源形式内嵌）。
- 不做图标动画（保持静态；🔄 扫描指示亦静态）。
- 不重绘/自定义品牌图标；采用开放许可 outline 几何（见 §2）。

## 2. 图标系统架构

- **`Themes/Icons.xaml`**（新，ResourceDictionary）：每图标一条 `Geometry`，`x:Key="Icon.<Name>"`，24×24 viewbox、描边型（源几何 stroke≈2）。几何来源固定为 **Lucide（ISC license）**；文件头注释注明来源与许可署名。
- **共享 `Style x:Key="IconPath"`**（TargetType=`Path`，置于 Icons.xaml 或 Controls.xaml）：`Width/Height`（按上下文 16/20/24）、`Stretch=Uniform`、`Fill={x:Null}`、`StrokeThickness≈1.75`、`StrokeLineJoin/StrokeLineCap=Round`；**Stroke 不在 style 内固定**，由使用处绑定以便主题化。
- **`App.xaml`** 合并 `Icons.xaml`（与 Colors/Fonts/Controls 同级）。
- **活跃态着色**复用现有 `Converters/BoolToAccentBrushConverter`（绑到 `Path.Stroke`），不新造着色逻辑。
- 转换器返回 `Geometry`（View 层查 Icons 资源），XAML 由 `TextBlock.Text` 改 `Path.Data`。

## 3. 图标清单（key → 用点）

| Key | 用点 |
|-----|------|
| `Icon.Play` / `Icon.Pause` | PlayerBar 播放/暂停键（`PlayStateToIconConverter`） |
| `Icon.Prev` / `Icon.Next` | PlayerBar 上一首/下一首 |
| `Icon.Shuffle` | PlayerBar 随机（活跃态 accent） |
| `Icon.RepeatOff` / `Icon.RepeatList` / `Icon.RepeatOne` | PlayerBar 循环三态（`RepeatModeToIconConverter`，活跃态 accent） |
| `Icon.Volume` / `Icon.VolumeMuted` | PlayerBar 音量/静音（新 `BoolToVolumeIconConverter`） |
| `Icon.Equalizer` | PlayerBar 🎚 EQ（启用态 accent） |
| `Icon.Settings` | PlayerBar ⚙ 设置 |
| `Icon.Plus` | Sidebar 加歌单、PlaylistView "+ 添加" |
| `Icon.Minus` | Sidebar 删歌单 |
| `Icon.Close` | PlaylistView × 删除 |
| `Icon.Refresh` | PlaylistView 🔄 刷新、Sidebar 🔄 扫描指示 |
| `Icon.Folder` | Sidebar 📂 文件夹绑定歌单 |
| `Icon.PlayMarker` | PlaylistView/Sidebar ▶ 当前/活跃标记（**实心**小三角，`Fill=AccentPrimary`；小尺寸下描边不清晰，故为实心例外） |
| `Icon.Clear` | PlaylistView "清空"（trash） |
| `Icon.MusicNote` | Sidebar 手动歌单默认图标（描边） |

## 4. 组件改动

- **`Converters/PlayStateToIconConverter.cs`**：返回 `string`→`Geometry`（Playing→`Icon.Pause`，否则→`Icon.Play`，经 `Application.Current.FindResource` 查资源）。
- **`Converters/RepeatModeToIconConverter.cs`**：返回 `Geometry`（List→`Icon.RepeatList`、One→`Icon.RepeatOne`、Off→`Icon.RepeatOff`）。
- **新增 `Converters/BoolToVolumeIconConverter.cs`**：`IsMuted`→`Geometry`（true→`Icon.VolumeMuted`，false→`Icon.Volume`）。
- **`ViewModels/PlayerViewModel.cs`**：**删除** `VolumeIcon` 属性及其两处 `[NotifyPropertyChangedFor(nameof(VolumeIcon))]`（emoji 字符串移出 VM；无测试引用，安全）。
- **`Views/Controls/PlayerBar.xaml`**：上述各 `TextBlock`(emoji) 改 `Path`（`Style=IconPath` + `Data` 绑定/转换器）；shuffle/repeat/EQ 的 `Stroke` 绑 `BoolToAccentBrush`；音量按钮 `Path.Data` 绑 `BoolToVolumeIconConverter`（输入 `IsMuted`）、`Stroke=ForegroundPrimary`。
- **▶ 标记改 Path**：`PlaylistView.xaml` 的 `PART_Marker` 与 Sidebar marker 由 `TextBlock` 改 `Path`（`x:Name` 不变）；code-behind `PlaylistView.xaml.cs`/`PlaylistsSidebarView.xaml.cs` 由 `marker.Text="▶"/""` 改 `marker.Visibility=Visible/Collapsed`；`FindChildByName<TextBlock>` 改 `FindChildByName<Path>`（COUPLING.md 契约同步更新）。
- **文字按钮**：PlaylistView "+ 添加"→`StackPanel`(`Path Icon.Plus`+`TextBlock 添加`)；"清空"→`StackPanel`(`Path Icon.Clear`+`TextBlock 清空`)；Sidebar `+`/`−` Content→`Path Icon.Plus/Icon.Minus`；`×` Content→`Path Icon.Close`；🔄/📂 TextBlock→`Path Icon.Refresh/Icon.Folder`。

## 5. 主题化 / 活跃态

- 默认 `Stroke={StaticResource ForegroundPrimary}`；次要图标（⚙）用 `ForegroundSecondary`。
- 活跃态（shuffle/repeat/EQ 启用）`Stroke={Binding …, Converter={StaticResource BoolToAccentBrush}}`（→`AccentHover`）。
- ▶ 标记（`Icon.PlayMarker`）为实心：`Fill={StaticResource AccentPrimary}`、`Stroke=null`；其余图标均为描边：`Stroke` 默认 `ForegroundPrimary`、`Fill={x:Null}`。
- 全部随 Themes 资源变化；不再依赖系统 emoji 字体。

## 6. 测试与验收

- 已核实**无现有测试**引用被改转换器/`VolumeIcon` → 测试涟漪≈0；`dotnet test` 应保持 96 全绿。
- 可选：为 `PlayStateToIconConverter`/`RepeatModeToIconConverter`/`BoolToVolumeIconConverter` 加「各状态返回非空 Geometry」烟雾测试。
- 验证：`dotnet build` 0 错误 + 96 测试绿 + **ComputerUse GUI 截图**前后对比（图标渲染、活跃态着色、▶ 标记、确认无 emoji 残留）。

## 7. 风险与边界

- **几何许可**：内嵌 Lucide/Feather 几何须在 Icons.xaml 头注释署名（ISC/MIT）。
- **marker 类型变更涟漪**：TextBlock→Path 波及 `FindChildByName` 泛型与 COUPLING.md「x:Name 定位」契约，需同步更新契约描述（元素类型变为 Path）。
- **极小尺寸清晰度**：11px ▶ 标记需单独调 `StrokeThickness`/尺寸保证清晰。
- 转换器经 `Application.Current.FindResource` 取 Geometry：需保证 Icons.xaml 已在 App 合并（否则 FindResource 抛异常）——在验收中覆盖。

## 8. 参考

- 现图标用点：`Views/Controls/PlayerBar.xaml`、`PlaylistView.xaml(.cs)`、`PlaylistsSidebarView.xaml(.cs)`；`Converters/PlayStateToIconConverter.cs`、`RepeatModeToIconConverter.cs`；`ViewModels/PlayerViewModel.cs`(`VolumeIcon`)。
- 主题资源：`Themes/Colors.xaml`、`Controls.xaml`；着色转换器 `Converters/BoolToAccentBrushConverter.cs`。
