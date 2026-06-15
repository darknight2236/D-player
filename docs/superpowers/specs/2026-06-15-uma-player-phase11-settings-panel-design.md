# Phase 11 设计规格：设置面板

> 日期：2026/06/15 · 分支：`feature/phase11-settings-panel` · 前置：Phase 10（文件夹绑定歌单）

---

## 1. 目标

为 UmaPlayer 添加设置面板入口，让用户可以调整默认音量。音频输出设置（设备/模式）以灰色占位文本呈现，为 Phase 12 多设备切换铺路。

**核心价值**：将音量调整从"只能拖 PlayerBar 滑块"升级为"可精确设置默认启动音量"，同时建立设置 UI 基础设施。

---

## 2. 设计决策

| 决策点 | 选择 | 理由 |
|--------|------|------|
| UI 形式 | 模态对话框（WindowStyle="ToolWindow"） | 与现有 PromptDialog 模式一致，开发最快 |
| 入口 | PlayerBar ⚙ 按钮 + Ctrl+, 快捷键 | 靠近音量控制，符合直觉；快捷键给高级用户 |
| 范围 | 仅 DefaultVolume 可编辑 | 最小可用，OutputMode/DeviceId 无消费者（Phase 12） |
| 是否需要 SettingsViewModel | 不需要（YAGNI） | 仅一个可编辑控件，对话框直接读写 ISettingsPersistence |
| 音量同步 | 通过 ISettingsPersistence 中转 | PlayerVM 每次拖音量都写盘，对话框读盘即最新值；模态阻塞避免并发 |

---

## 3. 入口设计

### 3.1 PlayerBar ⚙ 按钮

位置：`PlayerBar.xaml` Row 2 右侧 `StackPanel`（line 124），在音量滑块之后。

样式：与静音按钮一致（32×32, Background="Transparent", BorderThickness="0"）。图标使用 Unicode ⚙ (U+2699)，`ForegroundSecondary` 色（#A0A0B0），视觉上弱于 transport 按钮。

### 3.2 键盘快捷键

`Ctrl+,`（`Key.OemComma` + `ModifierKeys.Control`）。在 `MainWindow.xaml` 的 `KeyDown` 事件中处理。与 Visual Studio / VS Code 的设置快捷键一致。

---

## 4. SettingsDialog 规格

### 4.1 窗口属性

| 属性 | 值 |
|------|------|
| 类型 | `Window`（同 PromptDialog） |
| 尺寸 | 420 × 280 |
| `WindowStyle` | `ToolWindow` |
| `ResizeMode` | `NoResize` |
| `ShowInTaskbar` | `False` |
| `WindowStartupLocation` | `CenterOwner` |
| Background | `{StaticResource BackgroundPrimary}` |
| Foreground | `{StaticResource ForegroundPrimary}` |

### 4.2 布局

```
┌─ Settings ──────────────────────────┐
│                                     │
│  General                            │
│  ┌───────────────────────────────┐  │
│  │ Default volume   ═══●═══ 80%  │  │
│  └───────────────────────────────┘  │
│                                     │
│  Audio Output                       │
│  ┌───────────────────────────────┐  │
│  │ Output   System default —      │  │
│  │          WASAPI Shared         │  │
│  └───────────────────────────────┘  │
│  Audio output settings will be      │
│  available in Phase 12              │
│                                     │
│                       [Cancel][Save] │
└─────────────────────────────────────┘
```

**General 区域：**
- 标题 `TextBlock`：`HeaderText` 样式（16px SemiBold）
- 卡片 `Border`：`BackgroundSecondary` (#2A2A3C), CornerRadius=8, Padding=16
- 行：`Default volume` 标签（`BodyText` 样式，13px Regular）+ 百分比 `TextBlock`（`CaptionText` 样式）
- `Slider`：0..1, `IsMoveToPointEnabled="True"`，继承 Controls.xaml 隐式样式（紫色填充轨道 + 圆形 Thumb）

**Audio Output 区域：**
- 标题 `TextBlock`：`HeaderText` 样式
- 卡片 `Border`：`BackgroundSecondary`
- 行：`Output` 标签（`BodyText`）+ `System default — WASAPI Shared`（`ForegroundDisabled` #606070）
- 底部说明 `TextBlock`：`CaptionText` 样式 + 斜体，"Audio output settings will be available in Phase 12"

**按钮栏：**
- `StackPanel Orientation="Horizontal" HorizontalAlignment="Right"`
- Cancel 按钮：`IsCancel="True"`（Escape 关闭）
- Save 按钮：`IsDefault="True"`（Enter 保存），`Click="Save_Click"`

### 4.3 XAML 纪律

- 所有 inline `<Style TargetType="...">` 必须 `BasedOn="{StaticResource {x:Type ...}}"`（COUPLING.md 隐式契约）
- 使用 `{StaticResource BackgroundPrimary/Secondary}`, `{StaticResource ForegroundPrimary/Disabled}` 等主题资源
- 使用 `{StaticResource BodyText}` 作为标签样式（Fonts.xaml 已预留，当前未使用）

---

## 5. 数据流

### 5.1 打开对话框

```
用户点击 ⚙ / Ctrl+,
  → SettingsDialog.Show(owner, persistence)
  → Loaded: persistence.LoadAsync().GetAwaiter().GetResult()
  → 读取 DefaultVolume → 设为 Slider.Value
  → 百分比 TextBlock 显示 $"{value:P0}"
```

### 5.2 调整音量

```
用户拖动 Slider
  → ValueChanged 事件 → 更新百分比 TextBlock
  → 无持久化（仅预览）
```

### 5.3 保存

```
用户点击 Save / Enter
  → Save_Click: 读 Slider.Value (float)
  → persistence.UpdateAsync(s => s with { DefaultVolume = volume })
  → DialogResult = true → Close()
```

### 5.4 取消

```
用户点击 Cancel / Esc / 关闭窗口
  → DialogResult = false → Close()
  → 无持久化
```

### 5.5 音量同步机制

PlayerViewModel.OnVolumeChanged 每次拖音量都调用 `persistence.UpdateAsync(s => s with { DefaultVolume = value })`。SettingsDialog 打开时调用 `persistence.LoadAsync()` 读取最新持久化值。

因为对话框是模态的，用户无法同时操作 PlayerBar 滑块，所以不存在并发写入窗口。

---

## 6. 文件清单

### 6.1 新建文件

| 文件 | 说明 |
|------|------|
| `Views/Dialogs/SettingsDialog.xaml` | 对话框 XAML 布局 |
| `Views/Dialogs/SettingsDialog.xaml.cs` | Code-behind：静态 Show() 工厂、加载/保存逻辑、ValueChanged 处理 |

### 6.2 修改文件

| 文件 | 变更 |
|------|------|
| `Views/Controls/PlayerBar.xaml` | Row 2 右侧 StackPanel 加 ⚙ 按钮（音量滑块之后） |
| `Views/Controls/PlayerBar.xaml.cs` | 加 `SettingsBtn_Click` handler（调 `App.GetService<ISettingsPersistence>()` + `SettingsDialog.Show()`） |
| `Views/MainWindow.xaml` | 加 `KeyDown="MainWindow_KeyDown"` |
| `Views/MainWindow.xaml.cs` | 加 `MainWindow_KeyDown` handler（Ctrl+, 逻辑） |
| `docs/PROJECT.md` | Phase 11 描述、特性表、目录结构、设计决策 |
| `docs/COUPLING.md` | 依赖表、隐式契约、启动检查清单 |

### 6.3 不修改的文件

- `AppSettings.cs` — 字段已存在，无需变更
- `ISettingsPersistence.cs` — 接口不变
- `JsonSettingsPersistence.cs` — 实现不变
- `ServiceCollectionExtensions.cs` — 无新 DI 注册（SettingsDialog 直接实例化，不走容器）
- `PlayerViewModel.cs` — 不变（音量同步已通过 persistence 中转）

---

## 7. Code-behind 模式

### 7.1 静态工厂方法

```csharp
public static bool Show(Window? owner, ISettingsPersistence persistence)
{
    var dlg = new SettingsDialog(persistence) { Owner = owner };
    return dlg.ShowDialog() == true;
}
```

与 PromptDialog.Show 模式一致：创建实例 → 设 Owner → ShowDialog() → 返回结果。

### 7.2 构造与加载

```csharp
public SettingsDialog(ISettingsPersistence persistence)
{
    InitializeComponent();
    _persistence = persistence;
    Loaded += OnLoaded;
}

private void OnLoaded(object sender, RoutedEventArgs e)
{
    var settings = _persistence.LoadAsync().GetAwaiter().GetResult();
    VolumeSlider.Value = settings.DefaultVolume;
    VolumePercent.Text = $"{settings.DefaultVolume:P0}";
}
```

同步阻塞读盘：与 PlayerViewModel.Initialize() 和 MainWindow 构造函数同模式。文件极小（几百字节），阻塞 < 1ms。

### 7.3 保存

```csharp
private async void Save_Click(object sender, RoutedEventArgs e)
{
    var volume = (float)VolumeSlider.Value;
    await _persistence.UpdateAsync(s => s with { DefaultVolume = volume }).ConfigureAwait(true);
    DialogResult = true;
    Close();
}
```

`ConfigureAwait(true)` 回到 UI 线程设 DialogResult。`with` 表达式保持其他字段不变（OutputMode, PreferredDeviceId, WindowLeft/Top/Width/Height）。

### 7.4 百分比更新

```csharp
private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
{
    if (VolumePercent != null)
        VolumePercent.Text = $"{e.NewValue:P0}";
}
```

null 检查防止 InitializeComponent 期间的初始触发。

---

## 8. 不做的事 🚫

- ❌ 不加 SettingsViewModel（仅一个可编辑控件，YAGNI）
- ❌ 不做 Output 实际切换（Phase 12：NAudioPlaybackService 改用 IAudioOutputFactory）
- ❌ 不做设备枚举（Phase 12：IAudioDeviceManager 真实实现）
- ❌ 不做主题/外观设置
- ❌ 不做启动行为设置（LastPlayedPath 恢复等）
- ❌ 不加专门的对话框单元测试（persistence 交互已被 PlayerVM 测试覆盖）

---

## 9. 已知约束

- **同步读盘**：`LoadAsync().GetAwaiter().GetResult()` 在 UI 线程阻塞。与现有模式一致（PlayerVM.Initialize、MainWindow ctor），文件极小可接受。
- **音量同步依赖 persistence**：如果 PlayerVM 写盘失败（IO 错误），对话框会显示旧值。这是可接受的——persistence 失败已在 PlayerVM 的 fire-and-forget UpdateAsync 中被捕获。
- **Key.OemComma 跨键盘布局**：标准 WPF 映射，与 Visual Studio / VS Code 一致，所有主流键盘布局兼容。
- **⚙ 图标字体支持**：Segoe UI 包含 U+2699 (GEAR)，无需 Emoji 字体。

---

## 10. 验收清单

- [ ] 点击 ⚙ 按钮打开设置对话框
- [ ] 对话框显示当前默认音量（与 PlayerBar 滑块一致）
- [ ] 拖动滑块实时更新百分比
- [ ] 点击 Save 后 PlayerBar 音量滑块同步更新
- [ ] 重启后设置持久化
- [ ] Ctrl+, 打开对话框
- [ ] Escape 关闭对话框（不保存）
- [ ] Audio Output 区域显示灰色占位文本
