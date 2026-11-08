# UmaPlayer 设计文档

**日期：** 2026-04-23
**状态：** 待审批

## 1. 项目概述

UmaPlayer 是一个本地音乐播放器，类似 foobar2000/Winamp，专注本地音乐文件播放。

### 技术栈

| 层 | 技术选型 |
|---|---------|
| 音频引擎 | NAudio |
| 格式解码 | MediaFoundationReader |
| UI 框架 | WPF (.NET 10) |
| 架构模式 | MVVM + 依赖注入 |
| MVVM 框架 | CommunityToolkit.Mvvm |
| DI 容器 | Microsoft.Extensions.DependencyInjection |
| 配置读取 | IOptions\<AppSettings\>（启动默认值快照，绑定 Player Section） |
| 配置持久化 | ISettingsPersistence（运行时读写，存入 AppData） |
| 事件绑定 | Code-behind 事件转发 / Microsoft.Xaml.Behaviors.Wpf |

### 支持格式

MP3, WMA, FLAC, AAC, WAV（通过 Windows Media Foundation 原生解码）

> **注意：** OGG/Vorbis 不受 Windows Media Foundation 原生支持（依赖系统是否安装第三方 Codec），首期暂不支持，移至后续增量。

### 首期功能范围

- 基本歌曲导入（文件对话框，单文件）
- 核心播放控制（播放/暂停/停止）
- 进度条（拖拽 + 实时更新）
- 音量控制
- 窗口状态持久化（位置、大小、音量）

### 后续增量

- 播放列表管理（创建/保存/加载/编辑）
- 上一首/下一首、播放模式（顺序/随机/单曲循环）— 依赖播放列表
- 音频可视化（频谱/波形）
- 音乐库扫描（文件夹扫描、按艺术家/专辑组织）

---

## 2. 项目结构

```
UmaPlayer/
├── Models/
│   ├── Track.cs                    # 音轨信息（不可变 record）
│   ├── PlayState.cs                # 播放状态枚举
│   └── AudioDeviceInfo.cs          # 音频设备信息（预留）
├── Services/
│   ├── IPlaybackService.cs         # 核心播放抽象
│   ├── NAudioPlaybackService.cs    # NAudio 实现
│   ├── IAudioOutputFactory.cs      # 输出模式工厂（预留）
│   ├── IAudioDeviceManager.cs      # 设备枚举/切换（预留）
│   ├── ISettingsPersistence.cs     # 配置持久化服务
│   └── IFileDialogService.cs       # 文件对话框服务
├── Configuration/
│   └── AppSettings.cs              # 配置模型
├── ViewModels/
│   └── MainViewModel.cs
├── Views/
│   ├── MainWindow.xaml
│   ├── MainWindow.xaml.cs
│   └── Controls/
│       └── PlayerBar.xaml
├── Converters/
│   └── TimeSpanToStringConverter.cs
├── Themes/
│   ├── Colors.xaml
│   ├── Fonts.xaml
│   └── Controls.xaml
├── Extensions/
│   └── ServiceCollectionExtensions.cs
├── appsettings.json                # Copy if newer，部署默认值
├── App.xaml
└── App.xaml.cs
```

---

## 3. 分层原则

| 原则 | 说明 |
|------|------|
| **Models** | 纯数据类，无依赖，不可变优先（record） |
| **Services** | 接口在上层定义，实现细节封装 |
| **ViewModels** | 依赖 Services 接口，不依赖 Views |
| **Views** | 只做数据绑定，Code-behind 仅 InitializeComponent() + 事件转发 |
| **⚡ 异步/后台线程** | WasapiOut.Init/Play、格式协商、文件解码严禁阻塞 UI 线程，使用 Task.Run 或专用音频线程 |
| **🔒 非托管资源管理** | IAudioOutput 实现类注册为 Transient，由 IPlaybackService 显式 Dispose()，避免设备独占锁死 |
| **📡 UI 线程同步** | IPlaybackService 事件通过 SynchronizationContext.Post 封送到 UI 线程触发 |

---

## 4. 数据模型

```csharp
// Models/Track.cs
namespace UmaPlayer.Models;

public sealed record Track(
    string FilePath,
    string Title,
    string? Artist,
    string? Album,
    TimeSpan Duration);

// Models/PlayState.cs
namespace UmaPlayer.Models;

public enum PlayState { Stopped, Playing, Paused }

// Models/AudioDeviceInfo.cs（预留）
namespace UmaPlayer.Models;

public sealed record AudioDeviceInfo(
    string Id,
    string Name,
    bool IsDefault);
```

---

## 5. 配置系统

### 5.1 配置模型

```csharp
// Configuration/AppSettings.cs
namespace UmaPlayer.Configuration;

public sealed record AppSettings
{
    public float DefaultVolume { get; init; } = 0.8f;
    public string OutputMode { get; init; } = "WasapiShared";
    public string? PreferredDeviceId { get; init; }
    public string? LastPlayedPath { get; init; }
    public double WindowLeft { get; init; }
    public double WindowTop { get; init; }
    public double WindowWidth { get; init; } = 800;
    public double WindowHeight { get; init; } = 450;
}
```

### 5.2 appsettings.json 结构

```json
{
  "Player": {
    "DefaultVolume": 0.8,
    "OutputMode": "WasapiShared",
    "PreferredDeviceId": null,
    "LastPlayedPath": null,
    "WindowLeft": 100,
    "WindowTop": 100,
    "WindowWidth": 800,
    "WindowHeight": 450
  }
}
```

- `Player` Section 限定绑定范围，未来可加入 `Logging`、`Update` 等 Section 不冲突
- 此文件仅提供部署默认值，运行时用户配置存入 `%LocalAppData%/UmaPlayer/settings.json`

### 5.3 配置读写分离

- **读取**：`IOptions<AppSettings>` — 仅作为启动时的默认值快照，不可变
- **运行时**：`ISettingsPersistence` — 统一负责运行时状态的读写
- **初始化流程**：VM 启动时 `var settings = await _persistence.LoadAsync()` 覆盖 IOptions 默认值

### 5.4 持久化服务

```csharp
// Services/ISettingsPersistence.cs
namespace UmaPlayer.Services;

public interface ISettingsPersistence
{
    Task<AppSettings> LoadAsync();
    Task SaveAsync(AppSettings settings);
}
```

**实现要点：**
- JSON 写入必须加锁（SemaphoreSlim），防止快速拖拽音量/窗口时并发写入抛 `IOException: 文件被占用`
- 文件路径：`%LocalAppData%/UmaPlayer/settings.json`（用户配置独立存放，不覆盖部署文件）
- 序列化使用 System.Text.Json

```csharp
// 实现示例
public sealed class JsonSettingsPersistence : ISettingsPersistence
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _path;

    public JsonSettingsPersistence()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "UmaPlayer");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
    }

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(_path))
            return new AppSettings();   // 返回默认值

        await _lock.WaitAsync();
        try
        {
            var json = await File.ReadAllTextAsync(_path).ConfigureAwait(false);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        await _lock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(_path, json).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }
}
```

**关闭时 Last-Write-Wins：**
Window_Closing 的几何保存与 OnVolumeChanged 的异步保存理论上存在最后一次写入覆盖。极低概率丢失关闭前 0.1s 的音量变更，对本地播放器可接受。若追求极致一致性，后续可追加 `PatchGeometryAsync()` 方法避免全量读写。

**为什么不用 appsettings.json：**
- `appsettings.json` 标记为 Copy if newer，属于应用部署文件
- 运行时覆盖会导致：开发期配置被篡改、应用更新时用户设置丢失、Program Files 目录权限不足

---

## 6. 核心服务接口

### 6.1 IPlaybackService

```csharp
// Services/IPlaybackService.cs
namespace UmaPlayer.Services;

public interface IPlaybackService : IDisposable
{
    // 状态（只读）
    PlayState State { get; }
    Track? CurrentTrack { get; }
    TimeSpan Position { get; }          // 只读，程序更新进度
    TimeSpan Duration { get; }
    float Volume { get; set; }

    // 命令
    Task LoadAsync(Track track);
    void Play();
    void Pause();
    void Stop();
    void Seek(TimeSpan position);       // 显式方法，用户拖拽走这里

    // 事件（UI 线程触发，通过 SynchronizationContext.Post 封送）
    // 使用 Action<T> 而非 EventHandler<T>，T 无需继承 EventArgs
    event Action<PlayState> StateChanged;
    event Action<TimeSpan> PositionChanged;
    event Action<TimeSpan> DurationChanged;   // LoadAsync 解码完成后触发
    event Action<string>? PlaybackError;
}
```

**设计要点：**
- `Position` 只读 — WPF 绑定必须显式声明 `Mode=OneWay`，否则 WPF 会尝试回写并报绑定错误
- `Seek(TimeSpan)` — 显式方法，内部处理线程安全，避免 UI/音频线程竞争
- `DurationChanged` — LoadAsync 内部 MediaFoundationReader 初始化后才可知实际时长，通过事件推送到 VM
- **PositionChanged 节流** — 音频底层回调频率 >100Hz，事件推送必须节流至 20~30Hz（33~50ms 间隔），避免 UI 线程过载、进度条卡顿
- 事件通过 `SynchronizationContext.Post` 封送到 UI 线程，ViewModel 无需感知线程

### 6.2 IAudioOutputFactory（预留）

```csharp
// Services/IAudioOutputFactory.cs
namespace UmaPlayer.Services;

public interface IAudioOutputFactory
{
    IWavePlayer CreateOutput();         // 根据 AppSettings.OutputMode 创建
    // WaveOut | WasapiShared | WasapiExclusive | Asio
}
```

### 6.3 IAudioDeviceManager（预留）

```csharp
// Services/IAudioDeviceManager.cs
namespace UmaPlayer.Services;

public interface IAudioDeviceManager
{
    IReadOnlyList<AudioDeviceInfo> EnumerateDevices();
    AudioDeviceInfo? CurrentDevice { get; }
    void SelectDevice(string deviceId);
}
```

### 6.4 IFileDialogService

```csharp
// Services/IFileDialogService.cs
namespace UmaPlayer.Services;

/// <summary>
/// [STA Thread Required] — OpenFileDialog 必须在 STA 线程调用。
/// 当前由 VM 命令在 UI 线程调用，后续若从后台线程调用会抛 InvalidOperationException。
/// </summary>
public interface IFileDialogService
{
    IReadOnlyList<string> OpenFiles(string filter);
}
```

---

## 7. ViewModel 设计

```csharp
// ViewModels/MainViewModel.cs
namespace UmaPlayer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IPlaybackService _player;
    private readonly IFileDialogService _fileDialog;
    private readonly ISettingsPersistence _persistence;
    private readonly IOptions<AppSettings> _options;
    private AppSettings _settings;
    private bool _isInitializing = true;    // 跳过启动期的持久化调用

    // 拖拽状态旗标：防止拖拽时 PositionChanged 导致滑块"鬼畜"跳动
    [ObservableProperty]
    private bool _isSeeking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionNormalized))]
    private TimeSpan _position;

    [ObservableProperty]
    private TimeSpan _duration;

    [ObservableProperty]
    private PlayState _playState;

    [ObservableProperty]
    private Track? _currentTrack;

    [ObservableProperty]
    private float _volume;

    // 进度条百分比（0~1），用于 Slider 绑定
    public double PositionNormalized =>
        Duration.TotalSeconds > 0 ? Position.TotalSeconds / Duration.TotalSeconds : 0;

    public MainViewModel(
        IPlaybackService player,
        IFileDialogService fileDialog,
        ISettingsPersistence persistence,
        IOptions<AppSettings> options)
    {
        _player = player;
        _fileDialog = fileDialog;
        _persistence = persistence;
        _options = options;             // 保留引用，用于 fallback
        _settings = options.Value;      // 初始默认值

        // 订阅事件（已封送到 UI 线程）
        _player.PositionChanged += OnPositionChanged;
        _player.StateChanged += OnStateChanged;
        _player.DurationChanged += OnDurationChanged;
        _player.PlaybackError += OnPlaybackError;

        // 同步初始化 — 1KB JSON 读取无性能损耗，避免 async void 带来的异常吞没
        Initialize();
    }

    private void Initialize()
    {
        try
        {
            // 同步读取 — 启动期单次 1KB I/O，可接受
            _settings = _persistence.LoadAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // JSON 损坏等情况 — fallback 到 IOptions 默认值
            _settings = _options.Value;
        }
        Volume = _settings.DefaultVolume;   // 触发 OnVolumeChanged，但 _isInitializing 跳过持久化
        _isInitializing = false;
    }

    // PositionChanged 回调 — 拖拽时不更新
    private void OnPositionChanged(TimeSpan position)
    {
        if (!IsSeeking)
            Position = position;
    }

    // DurationChanged 回调 — MediaFoundationReader 解码后获知实际时长
    private void OnDurationChanged(TimeSpan duration)
        => Duration = duration;

    private void OnStateChanged(PlayState state)
        => PlayState = state;

    private void OnPlaybackError(string error)
    {
        // TODO: 首期仅记录，后续可弹出通知
    }

    // 用户拖拽开始
    [RelayCommand]
    private void SeekStarted() => IsSeeking = true;

    // 用户拖拽结束 — position 为 Slider.Value (0~1 归一化值)
    [RelayCommand]
    private void SeekCompleted(double normalized)
    {
        IsSeeking = false;
        var target = TimeSpan.FromSeconds(normalized * Duration.TotalSeconds);
        _player.Seek(target);
    }

    // 单一命令，根据 PlayState 路由
    [RelayCommand]
    private void PlayPause()
    {
        if (PlayState == PlayState.Playing)
            _player.Pause();
        else
            _player.Play();
    }

    [RelayCommand]
    private void Stop() => _player.Stop();

    [RelayCommand]
    private async Task OpenFilesAsync()
    {
        var files = _fileDialog.OpenFiles("Audio Files|*.mp3;*.wma;*.flac;*.aac;*.wav");
        if (files.Count > 0)
        {
            var file = files[0];
            var track = new Track(file, Path.GetFileNameWithoutExtension(file), null, null, TimeSpan.Zero);
            await _player.LoadAsync(track);
            _player.Play();
        }
    }

    // 音量变化时持久化 — 启动期跳过
    partial void OnVolumeChanged(float value)
    {
        _player.Volume = value;
        if (_isInitializing) return;    // 避免启动瞬间无故写入
        _settings = _settings with { DefaultVolume = value };
        _ = _persistence.SaveAsync(_settings);  // 内部有锁
    }

    public async Task CleanupAsync()
    {
        _player.PositionChanged -= OnPositionChanged;
        _player.StateChanged -= OnStateChanged;
        _player.DurationChanged -= OnDurationChanged;
        _player.PlaybackError -= OnPlaybackError;
        _player.Dispose();
        await _persistence.SaveAsync(_settings);
    }
}
```

---

## 8. Views 设计

### 8.1 MainWindow

```xml
<!-- Views/MainWindow.xaml -->
<Window x:Class="UmaPlayer.Views.MainWindow"
        Title="UmaPlayer"
        WindowStartupLocation="Manual"
        Closing="Window_Closing">
    <Grid>
        <views:PlayerBar DataContext="{Binding}" />
    </Grid>
</Window>
```

```csharp
// Views/MainWindow.xaml.cs
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ISettingsPersistence _persistence;

    public MainWindow(MainViewModel vm, ISettingsPersistence persistence)
    {
        InitializeComponent();
        _vm = vm;
        _persistence = persistence;
        DataContext = _vm;

        // 同步读取配置恢复窗口几何 — 1KB JSON 无性能损耗，彻底避免闪烁
        try
        {
            var settings = _persistence.LoadAsync().GetAwaiter().GetResult();
            Left = settings.WindowLeft;
            Top = settings.WindowTop;
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
            EnsureVisible();    // 多屏越界保护：副屏拔除后校正到可见区域
        }
        catch
        {
            // 配置损坏 — 使用 XAML 默认尺寸，窗口居中
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    // 窗口关闭时主动释放 — 不依赖 App.OnExit
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        // 窗口几何由 View 层直接持久化
        try
        {
            var settings = await _persistence.LoadAsync();
            settings = settings with
            {
                WindowLeft = Left, WindowTop = Top,
                WindowWidth = Width, WindowHeight = ActualHeight
            };
            await _persistence.SaveAsync(settings);
        }
        catch { /* 关闭时不应因保存失败阻塞 */ }

        // 播放资源由 VM 释放
        await _vm.CleanupAsync();
    }

    /// <summary>
    /// 多屏越界保护：确保窗口中心点落在虚拟屏幕范围内。
    /// 副屏拔除后，上次保存的位置可能落在屏幕外导致窗口"消失"。
    /// </summary>
    private void EnsureVisible()
    {
        var centerX = Left + Width / 2;
        var centerY = Top + Height / 2;
        // VirtualScreen 覆盖所有显示器的联合区域
        if (centerX < SystemParameters.VirtualScreenLeft ||
            centerX > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth ||
            centerY < SystemParameters.VirtualScreenTop ||
            centerY > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
        {
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;
        }
    }
}
```

### 8.2 PlayerBar 绑定要点

**播放按钮：** 使用单一 `PlayPauseCommand`，VM 内部根据 PlayState 路由逻辑。

```csharp
// MainViewModel 中
[RelayCommand]
private void PlayPause()
{
    if (PlayState == PlayState.Playing)
        _player.Pause();
    else
        _player.Play();
}
```

```xml
<!-- XAML：单一命令，Converter 切换图标 -->
<Button Content="{Binding PlayState, Converter={StaticResource PlayStateToIconConverter}}"
        Command="{Binding PlayPauseCommand}" />
```

**进度条 Slider：** Thumb 没有原生 Command 属性，使用 Code-behind 事件转发（符合原则"Code-behind 仅做事件转发"）。

```xml
<!-- Views/Controls/PlayerBar.xaml -->
<Slider x:Name="SeekBar"
        Minimum="0" Maximum="1"
        Value="{Binding PositionNormalized, Mode=OneWay}"
        Thumb.DragStarted="SeekBar_DragStarted"
        Thumb.DragCompleted="SeekBar_DragCompleted" />
```

```csharp
// Views/Controls/PlayerBar.xaml.cs — 仅事件转发
private void SeekBar_DragStarted(object sender, DragStartedEventArgs e)
    => (DataContext as MainViewModel)?.SeekStartedCommand.Execute(null);

private void SeekBar_DragCompleted(object sender, DragCompletedEventArgs e)
    => (DataContext as MainViewModel)?.SeekCompletedCommand.Execute(SeekBar.Value);
```

> 备选方案：使用 `Microsoft.Xaml.Behaviors.Wpf` 的 `EventToCommandBehavior` 绑定事件，避免 Code-behind。但 Code-behind 事件转发更轻量，且符合项目原则。

---

## 9. DI 注册

```csharp
// Extensions/ServiceCollectionExtensions.cs
namespace UmaPlayer.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUmaPlayerServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 配置 — 绑定 Player Section，避免与其他配置冲突
        services.Configure<AppSettings>(configuration.GetSection("Player"));

        // 服务（单例 — 管理音频设备生命周期）
        services.AddSingleton<IPlaybackService, NAudioPlaybackService>();
        services.AddSingleton<IAudioDeviceManager, AudioDeviceManager>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<ISettingsPersistence, JsonSettingsPersistence>();

        // 音频输出工厂（Transient — 每次创建新实例，由 IPlaybackService 显式 Dispose）
        services.AddTransient<IAudioOutputFactory, AudioOutputFactory>();

        // ViewModel
        services.AddTransient<MainViewModel>();

        return services;
    }
}
```

---

## 10. App.xaml 配置

```xml
<!-- App.xaml — 必须删除 StartupUri，否则 OnStartup 手动创建窗口会导致弹出两个窗口 -->
<Application x:Class="UmaPlayer.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- 注意：无 StartupUri 属性 -->
</Application>
```

---

## 11. App.xaml.cs 启动流程

```csharp
// App.xaml.cs
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var services = new ServiceCollection();
        services.AddUmaPlayerServices(configuration);
        _services = services.BuildServiceProvider();

        // MainWindow 不注册到 DI — 手动创建并注入依赖
        var vm = _services.GetRequiredService<MainViewModel>();
        var persistence = _services.GetRequiredService<ISettingsPersistence>();
        var mainWindow = new MainWindow(vm, persistence);
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 备用释放路径（MainWindow.Closing 是主路径）
        (_services as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
```

---

## 12. 关键设计决策总结

| 决策 | 方案 | 原因 |
|------|------|------|
| MVVM 框架 | CommunityToolkit.Mvvm | 官方维护，源码生成器减少样板代码 |
| DI 容器 | MS.Extensions.DependencyInjection | 轻量标准 |
| MainWindow 注入 | DI 解析 VM，手动赋值 DataContext | WPF 窗口生命周期难控，不放 DI |
| 配置读取 | IOptions\<AppSettings\> 仅作启动默认值 | 不可变，避免运行时篡改 |
| 配置持久化 | ISettingsPersistence + SemaphoreSlim | 存入 AppData，加锁防并发 IOException |
| 配置绑定范围 | IOptions 绑定 Player Section | 避免与未来 Logging/Update 等配置冲突 |
| 用户配置路径 | %LocalAppData%/UmaPlayer/settings.json | Local 非 Roaming，避免域环境同步冲突 |
| Position 绑定 | 只读属性 + Mode=OneWay + Seek() 方法 | 防止 UI/音频线程竞争、拖拽卡顿、爆音 |
| 拖拽防抖 | IsSeeking 旗标 | 防止拖拽时 PositionChanged 导致滑块跳动 |
| 资源释放 | MainWindow.Closing 主动触发 | 避免设备独占锁死、配置丢失 |
| 窗口几何 | MainWindow 直接依赖 ISettingsPersistence | View 层关注点，保持 VM 纯净 |
| 窗口恢复方式 | 构造函数同步读取 + WindowStartupLocation=Manual | 避免 async Loaded 导致布局闪烁 |
| 启动韧性 | try-catch + fallback 到 IOptions 默认值 | JSON 损坏时应用仍可启动 |
| Duration 同步 | DurationChanged 事件 | MediaFoundationReader 解码后才可知实际时长 |
| App.xaml | 删除 StartupUri | 避免 OnStartup 手动创建窗口导致双窗口 |
| OGG 格式 | 首期不支持，移至后续增量 | WMF 不原生支持，依赖第三方 Codec |
| 启动音量持久化 | _isInitializing 旗标跳过 | 避免启动瞬间无故写入 settings.json |
| AppSettings 类型 | sealed record | 支持 with 表达式，不可变设计 |
| UI 线程同步 | SynchronizationContext.Post | 事件在 UI 线程触发，VM 无需感知线程 |
| 音频输出 | IAudioOutputFactory（预留） | 后期切换 WASAPI/ASIO 时 VM 零修改 |
| 设备管理 | IAudioDeviceManager（预留） | 为设备下拉框预留 |
| 播放按钮 | 单一 PlayPauseCommand | VM 内部路由，XAML 不支持三元表达式 |
| Slider 事件 | Code-behind 事件转发 | Thumb 无原生 Command，Code-behind 最轻量 |
