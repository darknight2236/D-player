# UmaPlayer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a local music player with NAudio/MediaFoundationReader, WPF dark theme, MVVM + DI architecture.

**Architecture:** Single-project WPF app with folder-based layering (Models → Services → ViewModels → Views). CommunityToolkit.Mvvm for MVVM plumbing, MS.Extensions.DI for dependency injection, IOptions for config reading, ISettingsPersistence for runtime config persistence to %LocalAppData%.

**Tech Stack:** .NET 10, WPF, NAudio, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Configuration.Json

**Spec:** `docs/superpowers/specs/2026-04-23-uma-player-design.md`

---

## File Map

| File | Responsibility |
|------|---------------|
| `UmaPlayer.csproj` | Add NuGet packages |
| `appsettings.json` | Deploy-time defaults (Player section) |
| `Models/Track.cs` | Immutable audio track record |
| `Models/PlayState.cs` | Playback state enum |
| `Models/AudioDeviceInfo.cs` | Audio device info record (reserved) |
| `Configuration/AppSettings.cs` | Configuration model (sealed record) |
| `Services/ISettingsPersistence.cs` | Config persistence interface |
| `Services/JsonSettingsPersistence.cs` | JSON file persistence to LocalAppData |
| `Services/IPlaybackService.cs` | Core playback abstraction |
| `Services/NAudioPlaybackService.cs` | NAudio implementation with thread marshalling |
| `Services/IAudioOutputFactory.cs` | Output mode factory interface (reserved) |
| `Services/IAudioDeviceManager.cs` | Device enumeration interface (reserved) |
| `Services/IFileDialogService.cs` | File dialog interface (STA required) |
| `Services/Win32FileDialogService.cs` | OpenFileDialog implementation |
| `Extensions/ServiceCollectionExtensions.cs` | DI registration |
| `ViewModels/MainViewModel.cs` | Main VM with playback commands |
| `Themes/Colors.xaml` | Dark theme color palette |
| `Themes/Fonts.xaml` | Typography definitions |
| `Themes/Controls.xaml` | Control style overrides |
| `Converters/TimeSpanToStringConverter.cs` | TimeSpan → mm:ss display |
| `Converters/PlayStateToIconConverter.cs` | PlayState → ▶/⏸ icon |
| `Views/Controls/PlayerBar.xaml` | Player control layout |
| `Views/Controls/PlayerBar.xaml.cs` | Slider event forwarding |
| `Views/MainWindow.xaml` | Main window layout |
| `Views/MainWindow.xaml.cs` | Window geometry + cleanup |
| `App.xaml` | Remove StartupUri, add theme resources |
| `App.xaml.cs` | DI container bootstrap |

---

## Task 1: Project Setup — NuGet Packages & Directory Structure

**Files:**
- Modify: `UmaPlayer.csproj`
- Create: `appsettings.json`
- Create: `Models/`, `Services/`, `Configuration/`, `ViewModels/`, `Views/Controls/`, `Converters/`, `Themes/`, `Extensions/`

- [ ] **Step 1: Add NuGet packages to csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <OutputType>WinExe</OutputType>
        <TargetFramework>net10.0-windows</TargetFramework>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <UseWPF>true</UseWPF>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="NAudio" Version="2.2.*" />
        <PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />
        <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.*" />
        <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.*" />
        <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="10.*" />
    </ItemGroup>

    <ItemGroup>
        <None Update="appsettings.json">
            <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
        </None>
    </ItemGroup>

</Project>
```

- [ ] **Step 2: Create directory structure**

```bash
mkdir -p Models Services Configuration ViewModels Views/Controls Converters Themes Extensions
```

- [ ] **Step 3: Create appsettings.json**

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

- [ ] **Step 4: Verify build**

```bash
dotnet build
```

Expected: Build succeeds with restored packages.

- [ ] **Step 5: Commit**

```bash
git add UmaPlayer.csproj appsettings.json Models/ Services/ Configuration/ ViewModels/ Views/ Converters/ Themes/ Extensions/
git commit -m "chore: add NuGet packages and create directory structure"
```

---

## Task 2: Models — Track, PlayState, AudioDeviceInfo

**Files:**
- Create: `Models/Track.cs`
- Create: `Models/PlayState.cs`
- Create: `Models/AudioDeviceInfo.cs`

- [ ] **Step 1: Create PlayState enum**

```csharp
// Models/PlayState.cs
namespace UmaPlayer.Models;

public enum PlayState { Stopped, Playing, Paused }
```

- [ ] **Step 2: Create Track record**

```csharp
// Models/Track.cs
namespace UmaPlayer.Models;

public sealed record Track(
    string FilePath,
    string Title,
    string? Artist,
    string? Album,
    TimeSpan Duration);
```

- [ ] **Step 3: Create AudioDeviceInfo record (reserved)**

```csharp
// Models/AudioDeviceInfo.cs
namespace UmaPlayer.Models;

public sealed record AudioDeviceInfo(
    string Id,
    string Name,
    bool IsDefault);
```

- [ ] **Step 4: Verify build**

```bash
dotnet build
```

Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add Models/
git commit -m "feat: add data models (Track, PlayState, AudioDeviceInfo)"
```

---

## Task 3: Configuration — AppSettings & Config System

**Files:**
- Create: `Configuration/AppSettings.cs`

- [ ] **Step 1: Create AppSettings record**

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

- [ ] **Step 2: Verify build**

```bash
dotnet build
```

- [ ] **Step 3: Commit**

```bash
git add Configuration/
git commit -m "feat: add AppSettings configuration model"
```

---

## Task 4: ISettingsPersistence — Config Persistence Service

**Files:**
- Create: `Services/ISettingsPersistence.cs`
- Create: `Services/JsonSettingsPersistence.cs`

- [ ] **Step 1: Create ISettingsPersistence interface**

```csharp
// Services/ISettingsPersistence.cs
namespace UmaPlayer.Services;

public interface ISettingsPersistence
{
    Task<AppSettings> LoadAsync();
    Task SaveAsync(AppSettings settings);
}
```

- [ ] **Step 2: Create JsonSettingsPersistence implementation**

```csharp
// Services/JsonSettingsPersistence.cs
using System.Text.Json;
using UmaPlayer.Configuration;

namespace UmaPlayer.Services;

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
            return new AppSettings();

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

- [ ] **Step 3: Verify build**

```bash
dotnet build
```

- [ ] **Step 4: Commit**

```bash
git add Services/ISettingsPersistence.cs Services/JsonSettingsPersistence.cs
git commit -m "feat: add settings persistence service with SemaphoreSlim locking"
```

---

## Task 5: IPlaybackService — Core Playback Interface

**Files:**
- Create: `Services/IPlaybackService.cs`
- Create: `Services/IAudioOutputFactory.cs` (reserved)
- Create: `Services/IAudioDeviceManager.cs` (reserved)

- [ ] **Step 1: Create IPlaybackService interface**

```csharp
// Services/IPlaybackService.cs
using UmaPlayer.Models;

namespace UmaPlayer.Services;

public interface IPlaybackService : IDisposable
{
    // State (read-only)
    PlayState State { get; }
    Track? CurrentTrack { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    float Volume { get; set; }

    // Commands
    Task LoadAsync(Track track);
    void Play();
    void Pause();
    void Stop();
    void Seek(TimeSpan position);

    // Events (UI thread, via SynchronizationContext.Post)
    event Action<PlayState> StateChanged;
    event Action<TimeSpan> PositionChanged;
    event Action<TimeSpan> DurationChanged;
    event Action<string>? PlaybackError;
}
```

- [ ] **Step 2: Create IAudioOutputFactory interface (reserved)**

```csharp
// Services/IAudioOutputFactory.cs
using NAudio.Wave;

namespace UmaPlayer.Services;

public interface IAudioOutputFactory
{
    IWavePlayer CreateOutput();
}
```

- [ ] **Step 3: Create IAudioDeviceManager interface (reserved)**

```csharp
// Services/IAudioDeviceManager.cs
using UmaPlayer.Models;

namespace UmaPlayer.Services;

public interface IAudioDeviceManager
{
    IReadOnlyList<AudioDeviceInfo> EnumerateDevices();
    AudioDeviceInfo? CurrentDevice { get; }
    void SelectDevice(string deviceId);
}
```

- [ ] **Step 4: Verify build**

```bash
dotnet build
```

- [ ] **Step 5: Commit**

```bash
git add Services/IPlaybackService.cs Services/IAudioOutputFactory.cs Services/IAudioDeviceManager.cs
git commit -m "feat: add playback service interfaces (core + reserved)"
```

---

## Task 6: NAudioPlaybackService — Audio Engine Implementation

**Files:**
- Create: `Services/NAudioPlaybackService.cs`

- [ ] **Step 1: Create NAudioPlaybackService**

```csharp
// Services/NAudioPlaybackService.cs
using NAudio.Wave;
using UmaPlayer.Models;

namespace UmaPlayer.Services;

public sealed class NAudioPlaybackService : IPlaybackService
{
    private readonly SynchronizationContext _syncContext;
    private IWavePlayer? _wavePlayer;
    private MediaFoundationReader? _reader;
    private Track? _currentTrack;
    private PlayState _state = PlayState.Stopped;
    private float _volume = 0.8f;

    // Throttle: 33ms ≈ 30Hz
    private static readonly TimeSpan PositionThrottle = TimeSpan.FromMilliseconds(33);
    private DateTime _lastPositionEvent = DateTime.MinValue;

    public PlayState State => _state;
    public Track? CurrentTrack => _currentTrack;
    public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_wavePlayer != null)
                _wavePlayer.Volume = _volume;
        }
    }

    public event Action<PlayState>? StateChanged;
    public event Action<TimeSpan>? PositionChanged;
    public event Action<TimeSpan>? DurationChanged;
    public event Action<string>? PlaybackError;

    public NAudioPlaybackService()
    {
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
    }

    public async Task LoadAsync(Track track)
    {
        await Task.Run(() =>
        {
            DisposePlayback();

            try
            {
                _reader = new MediaFoundationReader(track.FilePath);
                _wavePlayer = new WasapiOut(NAudio.CoreAudioApi.Shared, 100);
                _wavePlayer.Volume = _volume;
                _wavePlayer.Init(_reader);

                _wavePlayer.PlaybackStateChanged += OnPlaybackStateChanged;
                _currentTrack = track with { Duration = _reader.TotalTime };

                RaiseOnUIThread(DurationChanged, _reader.TotalTime);
            }
            catch (Exception ex)
            {
                RaiseOnUIThread(PlaybackError, ex.Message);
                throw;
            }
        });
    }

    public void Play()
    {
        if (_wavePlayer == null) return;
        _wavePlayer.Play();
    }

    public void Pause()
    {
        if (_wavePlayer == null) return;
        _wavePlayer.Pause();
    }

    public void Stop()
    {
        if (_wavePlayer == null) return;
        _wavePlayer.Stop();
        if (_reader != null)
            _reader.CurrentTime = TimeSpan.Zero;
    }

    public void Seek(TimeSpan position)
    {
        if (_reader == null) return;
        _reader.CurrentTime = position;
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        _state = e.NewState switch
        {
            PlaybackState.Playing => PlayState.Playing,
            PlaybackState.Paused => PlayState.Paused,
            _ => PlayState.Stopped
        };
        RaiseOnUIThread(StateChanged, _state);

        // Start position polling when playing
        if (e.NewState == PlaybackState.Playing)
            _ = PollPositionAsync();
    }

    private async Task PollPositionAsync()
    {
        while (_wavePlayer?.PlaybackState == PlaybackState.Playing)
        {
            await Task.Delay(33); // ~30Hz

            var now = DateTime.UtcNow;
            if (now - _lastPositionEvent >= PositionThrottle)
            {
                _lastPositionEvent = now;
                var pos = _reader?.CurrentTime ?? TimeSpan.Zero;
                RaiseOnUIThread(PositionChanged, pos);
            }
        }
    }

    private void RaiseOnUIThread<T>(Action<T>? handler, T value)
    {
        if (handler == null) return;
        _syncContext.Post(_ => handler(value), null);
    }

    private void DisposePlayback()
    {
        if (_wavePlayer != null)
        {
            _wavePlayer.PlaybackStateChanged -= OnPlaybackStateChanged;
            _wavePlayer.Stop();
            _wavePlayer.Dispose();
            _wavePlayer = null;
        }
        if (_reader != null)
        {
            _reader.Dispose();
            _reader = null;
        }
    }

    public void Dispose()
    {
        DisposePlayback();
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build
```

- [ ] **Step 3: Commit**

```bash
git add Services/NAudioPlaybackService.cs
git commit -m "feat: implement NAudioPlaybackService with throttled position updates"
```

---

## Task 7: IFileDialogService — File Dialog

**Files:**
- Create: `Services/IFileDialogService.cs`
- Create: `Services/Win32FileDialogService.cs`

- [ ] **Step 1: Create IFileDialogService interface**

```csharp
// Services/IFileDialogService.cs
namespace UmaPlayer.Services;

/// <summary>
/// [STA Thread Required] — OpenFileDialog must be called on STA thread.
/// Currently called from VM commands on UI thread. Will throw
/// InvalidOperationException if called from a background thread.
/// </summary>
public interface IFileDialogService
{
    IReadOnlyList<string> OpenFiles(string filter);
}
```

- [ ] **Step 2: Create Win32FileDialogService implementation**

```csharp
// Services/Win32FileDialogService.cs
using Microsoft.Win32;

namespace UmaPlayer.Services;

public sealed class Win32FileDialogService : IFileDialogService
{
    public IReadOnlyList<string> OpenFiles(string filter)
    {
        var dialog = new OpenFileDialog
        {
            Filter = filter,
            Multiselect = false
        };

        return dialog.ShowDialog() == true
            ? dialog.FileNames.ToList().AsReadOnly()
            : Array.Empty<string>();
    }
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build
```

- [ ] **Step 4: Commit**

```bash
git add Services/IFileDialogService.cs Services/Win32FileDialogService.cs
git commit -m "feat: add file dialog service with STA thread constraint"
```

---

## Task 8: DI Registration

**Files:**
- Create: `Extensions/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Create ServiceCollectionExtensions**

```csharp
// Extensions/ServiceCollectionExtensions.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UmaPlayer.Configuration;
using UmaPlayer.Services;
using UmaPlayer.ViewModels;

namespace UmaPlayer.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUmaPlayerServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configuration — bind Player section only
        services.Configure<AppSettings>(configuration.GetSection("Player"));

        // Services (Singleton — manage audio device lifecycle)
        services.AddSingleton<IPlaybackService, NAudioPlaybackService>();
        services.AddSingleton<IFileDialogService, Win32FileDialogService>();
        services.AddSingleton<ISettingsPersistence, JsonSettingsPersistence>();

        // Reserved (register stubs for future use)
        services.AddSingleton<IAudioDeviceManager, StubAudioDeviceManager>();

        // Audio output factory (Transient — created fresh, disposed by IPlaybackService)
        services.AddTransient<IAudioOutputFactory, StubAudioOutputFactory>();

        // ViewModel
        services.AddTransient<MainViewModel>();

        return services;
    }
}
```

- [ ] **Step 2: Create stub implementations for reserved interfaces**

```csharp
// Services/StubAudioDeviceManager.cs
using UmaPlayer.Models;

namespace UmaPlayer.Services;

public sealed class StubAudioDeviceManager : IAudioDeviceManager
{
    public IReadOnlyList<AudioDeviceInfo> EnumerateDevices() => Array.Empty<AudioDeviceInfo>();
    public AudioDeviceInfo? CurrentDevice => null;
    public void SelectDevice(string deviceId) { }
}
```

```csharp
// Services/StubAudioOutputFactory.cs
using NAudio.Wave;

namespace UmaPlayer.Services;

public sealed class StubAudioOutputFactory : IAudioOutputFactory
{
    public IWavePlayer CreateOutput() => new WasapiOut(NAudio.CoreAudioApi.Shared, 100);
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build
```

- [ ] **Step 4: Commit**

```bash
git add Extensions/ Services/StubAudioDeviceManager.cs Services/StubAudioOutputFactory.cs
git commit -m "feat: add DI registration with stub implementations for reserved interfaces"
```

---

## Task 9: Converters — TimeSpan & PlayState

**Files:**
- Create: `Converters/TimeSpanToStringConverter.cs`
- Create: `Converters/PlayStateToIconConverter.cs`

- [ ] **Step 1: Create TimeSpanToStringConverter**

```csharp
// Converters/TimeSpanToStringConverter.cs
using System.Globalization;
using System.Windows.Data;

namespace UmaPlayer.Converters;

[ValueConversion(typeof(TimeSpan), typeof(string))]
public sealed class TimeSpanToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TimeSpan ts)
            return ts.TotalHours >= 1
                ? ts.ToString(@"h\:mm\:ss")
                : ts.ToString(@"m\:ss");
        return "0:00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 2: Create PlayStateToIconConverter**

```csharp
// Converters/PlayStateToIconConverter.cs
using System.Globalization;
using System.Windows.Data;
using UmaPlayer.Models;

namespace UmaPlayer.Converters;

[ValueConversion(typeof(PlayState), typeof(string))]
public sealed class PlayStateToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is PlayState.Playing ? "⏸" : "▶";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build
```

- [ ] **Step 4: Commit**

```bash
git add Converters/
git commit -m "feat: add value converters for TimeSpan display and PlayState icon"
```

---

## Task 10: Themes — Dark Theme ResourceDictionaries

**Files:**
- Create: `Themes/Colors.xaml`
- Create: `Themes/Fonts.xaml`
- Create: `Themes/Controls.xaml`

- [ ] **Step 1: Create Colors.xaml**

```xml
<!-- Themes/Colors.xaml -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Background -->
    <SolidColorBrush x:Key="BackgroundPrimary" Color="#1E1E2E"/>
    <SolidColorBrush x:Key="BackgroundSecondary" Color="#2A2A3C"/>
    <SolidColorBrush x:Key="BackgroundTertiary" Color="#363649"/>

    <!-- Foreground -->
    <SolidColorBrush x:Key="ForegroundPrimary" Color="#E0E0E0"/>
    <SolidColorBrush x:Key="ForegroundSecondary" Color="#A0A0B0"/>
    <SolidColorBrush x:Key="ForegroundDisabled" Color="#606070"/>

    <!-- Accent -->
    <SolidColorBrush x:Key="AccentPrimary" Color="#7C4DFF"/>
    <SolidColorBrush x:Key="AccentHover" Color="#9E7CFF"/>
    <SolidColorBrush x:Key="AccentPressed" Color="#5C2DCF"/>

    <!-- Slider -->
    <SolidColorBrush x:Key="SliderTrack" Color="#363649"/>
    <SolidColorBrush x:Key="SliderThumb" Color="#7C4DFF"/>
</ResourceDictionary>
```

- [ ] **Step 2: Create Fonts.xaml**

```xml
<!-- Themes/Fonts.xaml -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <FontFamily x:Key="PrimaryFont">Segoe UI</FontFamily>
    <FontFamily x:Key="IconFont">Segoe MDL2 Assets</FontFamily>

    <Style x:Key="HeaderText" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{StaticResource PrimaryFont}"/>
        <Setter Property="FontSize" Value="16"/>
        <Setter Property="FontWeight" Value="SemiBold"/>
        <Setter Property="Foreground" Value="{StaticResource ForegroundPrimary}"/>
    </Style>

    <Style x:Key="BodyText" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{StaticResource PrimaryFont}"/>
        <Setter Property="FontSize" Value="13"/>
        <Setter Property="Foreground" Value="{StaticResource ForegroundPrimary}"/>
    </Style>

    <Style x:Key="CaptionText" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{StaticResource PrimaryFont}"/>
        <Setter Property="FontSize" Value="11"/>
        <Setter Property="Foreground" Value="{StaticResource ForegroundSecondary}"/>
    </Style>
</ResourceDictionary>
```

- [ ] **Step 3: Create Controls.xaml**

```xml
<!-- Themes/Controls.xaml -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Window base style -->
    <Style TargetType="Window">
        <Setter Property="Background" Value="{StaticResource BackgroundPrimary}"/>
        <Setter Property="Foreground" Value="{StaticResource ForegroundPrimary}"/>
        <Setter Property="FontFamily" Value="{StaticResource PrimaryFont}"/>
    </Style>

    <!-- Button base style -->
    <Style TargetType="Button">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Foreground" Value="{StaticResource ForegroundPrimary}"/>
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="Padding" Value="12,6"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="border" Background="{TemplateBinding Background}"
                            Padding="{TemplateBinding Padding}" CornerRadius="4">
                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="border" Property="Background" Value="{StaticResource BackgroundTertiary}"/>
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter TargetName="border" Property="Background" Value="{StaticResource AccentPressed}"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Foreground" Value="{StaticResource ForegroundDisabled}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Slider style -->
    <Style TargetType="Slider">
        <Setter Property="Height" Value="20"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Slider">
                    <Grid>
                        <Track x:Name="PART_Track">
                            <Track.DecreaseRepeatButton>
                                <RepeatButton Command="{x:Static Slider.DecreaseLarge}">
                                    <RepeatButton.Template>
                                        <ControlTemplate TargetType="RepeatButton">
                                            <Border Background="{StaticResource AccentPrimary}" Height="4" CornerRadius="2"/>
                                        </ControlTemplate>
                                    </RepeatButton.Template>
                                </RepeatButton>
                            </Track.DecreaseRepeatButton>
                            <Track.IncreaseRepeatButton>
                                <RepeatButton Command="{x:Static Slider.IncreaseLarge}">
                                    <RepeatButton.Template>
                                        <ControlTemplate TargetType="RepeatButton">
                                            <Border Background="{StaticResource SliderTrack}" Height="4" CornerRadius="2"/>
                                        </ControlTemplate>
                                    </RepeatButton.Template>
                                </RepeatButton>
                            </Track.IncreaseRepeatButton>
                            <Track.Thumb>
                                <Thumb x:Name="SliderThumb" Cursor="Hand">
                                    <Thumb.Template>
                                        <ControlTemplate TargetType="Thumb">
                                            <Ellipse Width="14" Height="14" Fill="{StaticResource SliderThumb}"/>
                                        </ControlTemplate>
                                    </Thumb.Template>
                                </Thumb>
                            </Track.Thumb>
                        </Track>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
</ResourceDictionary>
```

- [ ] **Step 4: Verify build**

```bash
dotnet build
```

- [ ] **Step 5: Commit**

```bash
git add Themes/
git commit -m "feat: add dark theme ResourceDictionaries (Colors, Fonts, Controls)"
```

---

## Task 11: MainViewModel — Playback Commands & Bindings

**Files:**
- Create: `ViewModels/MainViewModel.cs`

- [ ] **Step 1: Create MainViewModel**

```csharp
// ViewModels/MainViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using UmaPlayer.Configuration;
using UmaPlayer.Models;
using UmaPlayer.Services;

namespace UmaPlayer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IPlaybackService _player;
    private readonly IFileDialogService _fileDialog;
    private readonly ISettingsPersistence _persistence;
    private readonly IOptions<AppSettings> _options;
    private AppSettings _settings;
    private bool _isInitializing = true;

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
        _options = options;
        _settings = options.Value;

        _player.PositionChanged += OnPositionChanged;
        _player.StateChanged += OnStateChanged;
        _player.DurationChanged += OnDurationChanged;
        _player.PlaybackError += OnPlaybackError;

        Initialize();
    }

    private void Initialize()
    {
        try
        {
            _settings = _persistence.LoadAsync().GetAwaiter().GetResult();
        }
        catch
        {
            _settings = _options.Value;
        }
        Volume = _settings.DefaultVolume;
        _isInitializing = false;
    }

    private void OnPositionChanged(TimeSpan position)
    {
        if (!IsSeeking)
            Position = position;
    }

    private void OnDurationChanged(TimeSpan duration)
        => Duration = duration;

    private void OnStateChanged(PlayState state)
        => PlayState = state;

    private void OnPlaybackError(string error)
    {
        // TODO: first phase — log only, future: show notification
    }

    [RelayCommand]
    private void SeekStarted() => IsSeeking = true;

    [RelayCommand]
    private void SeekCompleted(double normalized)
    {
        IsSeeking = false;
        var target = TimeSpan.FromSeconds(normalized * Duration.TotalSeconds);
        _player.Seek(target);
    }

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
            var track = new Track(
                file,
                Path.GetFileNameWithoutExtension(file),
                null, null, TimeSpan.Zero);
            await _player.LoadAsync(track);
            _player.Play();
        }
    }

    partial void OnVolumeChanged(float value)
    {
        _player.Volume = value;
        if (_isInitializing) return;
        _settings = _settings with { DefaultVolume = value };
        _ = _persistence.SaveAsync(_settings);
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

- [ ] **Step 2: Verify build**

```bash
dotnet build
```

- [ ] **Step 3: Commit**

```bash
git add ViewModels/
git commit -m "feat: implement MainViewModel with playback commands and seek handling"
```

---

## Task 12: Views — PlayerBar Control

**Files:**
- Create: `Views/Controls/PlayerBar.xaml`
- Create: `Views/Controls/PlayerBar.xaml.cs`

- [ ] **Step 1: Create PlayerBar.xaml**

```xml
<!-- Views/Controls/PlayerBar.xaml -->
<UserControl x:Class="UmaPlayer.Views.Controls.PlayerBar"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:converters="clr-namespace:UmaPlayer.Converters">
    <UserControl.Resources>
        <converters:TimeSpanToStringConverter x:Key="TimeSpanToString"/>
        <converters:PlayStateToIconConverter x:Key="PlayStateToIcon"/>
    </UserControl.Resources>

    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Track info -->
        <StackPanel Grid.Row="0" VerticalAlignment="Center" HorizontalAlignment="Center">
            <TextBlock Text="{Binding CurrentTrack.Title, FallbackValue='No track loaded'}"
                       Style="{StaticResource HeaderText}" HorizontalAlignment="Center"/>
            <TextBlock Text="{Binding CurrentTrack.Artist, FallbackValue=''}"
                       Style="{StaticResource CaptionText}" HorizontalAlignment="Center" Margin="0,4,0,0"/>
        </StackPanel>

        <!-- Seek bar -->
        <Grid Grid.Row="1" Margin="0,12,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="45"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="45"/>
            </Grid.ColumnDefinitions>

            <TextBlock Grid.Column="0" Text="{Binding Position, Converter={StaticResource TimeSpanToString}}"
                       Style="{StaticResource CaptionText}" VerticalAlignment="Center" HorizontalAlignment="Right"/>

            <Slider x:Name="SeekBar" Grid.Column="1" Margin="8,0"
                    Minimum="0" Maximum="1"
                    Value="{Binding PositionNormalized, Mode=OneWay}"
                    Thumb.DragStarted="SeekBar_DragStarted"
                    Thumb.DragCompleted="SeekBar_DragCompleted"/>

            <TextBlock Grid.Column="2" Text="{Binding Duration, Converter={StaticResource TimeSpanToString}}"
                       Style="{StaticResource CaptionText}" VerticalAlignment="Center" HorizontalAlignment="Left"/>
        </Grid>

        <!-- Controls -->
        <Grid Grid.Row="2" Margin="0,8,0,0">
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
                <Button Command="{Binding PlayPauseCommand}" Width="48" Height="48">
                    <TextBlock Text="{Binding PlayState, Converter={StaticResource PlayStateToIcon}}"
                               FontSize="20" Foreground="{StaticResource ForegroundPrimary}"/>
                </Button>
                <Button Command="{Binding StopCommand}" Width="48" Height="48" Margin="8,0">
                    <TextBlock Text="⏹" FontSize="16" Foreground="{StaticResource ForegroundPrimary}"/>
                </Button>
                <Button Command="{Binding OpenFilesCommand}" Width="48" Height="48">
                    <TextBlock Text="📂" FontSize="16" Foreground="{StaticResource ForegroundPrimary}"/>
                </Button>
            </StackPanel>

            <!-- Volume slider -->
            <Slider Width="100" HorizontalAlignment="Right" VerticalAlignment="Center"
                    Minimum="0" Maximum="1"
                    Value="{Binding Volume, Mode=TwoWay}"/>
        </Grid>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Create PlayerBar.xaml.cs**

```csharp
// Views/Controls/PlayerBar.xaml.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using UmaPlayer.ViewModels;

namespace UmaPlayer.Views.Controls;

public partial class PlayerBar : UserControl
{
    public PlayerBar()
    {
        InitializeComponent();
    }

    // Code-behind: event forwarding only
    private void SeekBar_DragStarted(object sender, DragStartedEventArgs e)
        => (DataContext as MainViewModel)?.SeekStartedCommand.Execute(null);

    private void SeekBar_DragCompleted(object sender, DragCompletedEventArgs e)
        => (DataContext as MainViewModel)?.SeekCompletedCommand.Execute(SeekBar.Value);
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build
```

- [ ] **Step 4: Commit**

```bash
git add Views/Controls/
git commit -m "feat: add PlayerBar control with seek slider and playback buttons"
```

---

## Task 13: Views — MainWindow

**Files:**
- Create: `Views/MainWindow.xaml`
- Create: `Views/MainWindow.xaml.cs`

- [ ] **Step 1: Create MainWindow.xaml**

```xml
<!-- Views/MainWindow.xaml -->
<Window x:Class="UmaPlayer.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="clr-namespace:UmaPlayer.Views.Controls"
        Title="UmaPlayer"
        Width="800" Height="450"
        WindowStartupLocation="Manual"
        Closing="Window_Closing">
    <Grid>
        <controls:PlayerBar DataContext="{Binding}"/>
    </Grid>
</Window>
```

- [ ] **Step 2: Create MainWindow.xaml.cs**

```csharp
// Views/MainWindow.xaml.cs
using System.ComponentModel;
using System.Windows;
using UmaPlayer.Services;
using UmaPlayer.ViewModels;

namespace UmaPlayer.Views;

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

        // Synchronous config load — 1KB JSON, no perf cost, prevents layout flicker
        try
        {
            var settings = _persistence.LoadAsync().GetAwaiter().GetResult();
            Left = settings.WindowLeft;
            Top = settings.WindowTop;
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
            EnsureVisible();
        }
        catch
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        // Persist window geometry
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
        catch { /* don't block closing on save failure */ }

        // Release playback resources
        await _vm.CleanupAsync();
    }

    /// <summary>
    /// Multi-monitor bounds check: ensure window center is within virtual screen area.
    /// Prevents window "disappearing" after disconnecting a secondary monitor.
    /// </summary>
    private void EnsureVisible()
    {
        var centerX = Left + Width / 2;
        var centerY = Top + Height / 2;
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

- [ ] **Step 3: Verify build**

```bash
dotnet build
```

- [ ] **Step 4: Commit**

```bash
git add Views/MainWindow.xaml Views/MainWindow.xaml.cs
git commit -m "feat: add MainWindow with geometry persistence and multi-monitor protection"
```

---

## Task 14: App.xaml — Remove StartupUri & Wire Themes

**Files:**
- Modify: `App.xaml`
- Modify: `App.xaml.cs`

- [ ] **Step 1: Update App.xaml — remove StartupUri, add theme resources**

```xml
<!-- App.xaml -->
<Application x:Class="UmaPlayer.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Themes/Colors.xaml"/>
                <ResourceDictionary Source="Themes/Fonts.xaml"/>
                <ResourceDictionary Source="Themes/Controls.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

Note: `StartupUri` is intentionally removed. Window is created via DI in `OnStartup`.

- [ ] **Step 2: Update App.xaml.cs — DI bootstrap**

```csharp
// App.xaml.cs
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UmaPlayer.Extensions;
using UmaPlayer.Services;
using UmaPlayer.ViewModels;

namespace UmaPlayer;

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

        // MainWindow not registered in DI — manually create with injected dependencies
        var vm = _services.GetRequiredService<MainViewModel>();
        var persistence = _services.GetRequiredService<ISettingsPersistence>();
        var mainWindow = new Views.MainWindow(vm, persistence);
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Fallback disposal (MainWindow.Closing is primary path)
        (_services as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build
```

- [ ] **Step 4: Commit**

```bash
git add App.xaml App.xaml.cs
git commit -m "feat: wire DI bootstrap, remove StartupUri, add theme resources"
```

---

## Task 15: Cleanup Unused Files & Final Verification

**Files:**
- Modify: `MainWindow.xaml` (delete — replaced by Views/MainWindow.xaml)
- Modify: `MainWindow.xaml.cs` (delete — replaced by Views/MainWindow.xaml.cs)

- [ ] **Step 1: Delete old template files**

```bash
rm -f MainWindow.xaml MainWindow.xaml.cs
```

- [ ] **Step 2: Full build verification**

```bash
dotnet build
```

Expected: Build succeeds with zero errors.

- [ ] **Step 3: Run the application**

```bash
dotnet run
```

Expected: Dark-themed window appears at configured position. No crash on startup.

- [ ] **Step 4: Final commit**

```bash
git add -A
git commit -m "chore: remove old template files, verify clean build"
```

---

## Self-Review Checklist

- [x] **Spec coverage:** All spec sections mapped to tasks (models, config, services, VM, views, themes, DI, app bootstrap)
- [x] **No placeholders:** Every step has complete code, no TBD/TODO (except the `OnPlaybackError` TODO which is explicitly "first phase — log only")
- [x] **Type consistency:** `Action<T>` events throughout, `sealed record AppSettings`, `PlayState` enum, `Track` record — all consistent
- [x] **Spec alignment:** Position read-only + Seek(), Mode=OneWay binding, IsSeeking flag, _isInitializing flag, SemaphoreSlim locking, ConfigureAwait(false), EnsureVisible(), StartupUri removed, Player section binding, LocalAppData path, Position throttle documented
