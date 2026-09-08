using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using DPlayer.Configuration;
using DPlayer.Models;
using DPlayer.Services;

namespace DPlayer.ViewModels;

/// <summary>
/// 播放器 ViewModel —— 负责 transport 状态（播放/暂停/位置/音量）和当前曲信息展示。
///
/// 与 PlaylistViewModel 的边界：
///   - 本 VM 订阅 IPlaybackService 的 Position/Duration/State/Track/Error 事件
///   - PlaylistViewModel 单独订阅 TrackEnded 推进队列
///   - 两个 VM 互不持引用；通过 IPlaybackService 单例共享底层状态
///
/// Phase 7: 债务 #1 已完整偿还 —— AlbumArt 为 byte[]，XAML 通过 BytesToBitmapImageConverter 转为 BitmapImage。
/// </summary>
public partial class PlayerViewModel : ObservableObject
{
    private readonly IPlaybackService _player;
    private readonly ISettingsPersistence _persistence;
    private readonly IOptions<AppSettings> _options;

    // 标记构造期间：避免 OnVolumeChanged 在初始化时写磁盘
    private bool _isInitializing = true;

    /// <summary>拖动进度条时为 true —— 抑制 PositionChanged 回写，避免滑块被服务"拽回"。</summary>
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
    [NotifyPropertyChangedFor(nameof(SampleRateText))]
    private Track? _currentTrack;

    /// <summary>封面原始字节数组；XAML 通过 BytesToBitmapImageConverter 转为 Frozen BitmapImage。</summary>
    [ObservableProperty]
    private byte[]? _albumArtBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeIcon))]
    private float _volume;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeIcon))]
    private bool _isMuted;

    /// <summary>静音前的音量快照，用于"取消静音"时恢复。</summary>
    private float _volumeBeforeMute;

    // ====== Phase 13: 频谱可视化 ======

    [ObservableProperty]
    private float[] _spectrumData = new float[32];

    [ObservableProperty]
    private bool _spectrumEnabled = true;

    [ObservableProperty]
    private double _spectrumSensitivity = 1.0;  // 0.5 ~ 2.0

    [ObservableProperty]
    private int _spectrumColorTheme = 0;  // 0=Purple, 1=Blue, 2=Green, 3=Rainbow

    [ObservableProperty]
    private double _spectrumSmoothing = 0.8;  // 0.0 ~ 0.95

    // ====== Phase 14: 均衡器 ======

    /// <summary>EQ 启用态；供 PlayerBar EQ 按钮激活态高亮（镜像 Shuffle/Repeat）。</summary>
    [ObservableProperty]
    private bool _equalizerEnabled;

    /// <summary>平滑后的频谱数据（避免 UI 抖动）</summary>
    private float[] _smoothedSpectrum = new float[32];

    /// <summary>颜色主题列表（供 UI 绑定）</summary>
    public string[] SpectrumColorThemes { get; } = ["紫色", "蓝色", "绿色", "彩虹"];

    // —— 派生只读属性，供 XAML 绑定 ——

    public string VolumeIcon => IsMuted ? "\U0001F507" : "\U0001F50A"; // 🔇 / 🔊

    public string SampleRateText =>
        CurrentTrack?.SampleRate is { } sr ? $"{sr:N0} Hz" : "";

    /// <summary>进度条用归一化 [0,1] 值；Duration 为 0 时返回 0 防止除零。</summary>
    public double PositionNormalized =>
        Duration.TotalSeconds > 0 ? Position.TotalSeconds / Duration.TotalSeconds : 0;

    public PlayerViewModel(
        IPlaybackService player,
        ISettingsPersistence persistence,
        IOptions<AppSettings> options)
    {
        _player = player;
        _persistence = persistence;
        _options = options;

        // 订阅播放服务事件 —— 所有事件已由服务封送到 UI 线程，handler 可直接更新属性
        _player.PositionChanged += HandlePositionChanged;
        _player.StateChanged += HandleStateChanged;
        _player.DurationChanged += HandleDurationChanged;
        _player.TrackChanged += HandleTrackChanged;
        _player.PlaybackError += HandlePlaybackError;

        // Phase 13: 订阅频谱事件
        InitializeSpectrum();

        Initialize();
    }

    /// <summary>从持久化加载音量；失败回落到 appsettings.json 默认值。</summary>
    private void Initialize()
    {
        AppSettings settings;
        try
        {
            // 构造期同步阻塞读盘（小文件、毫秒级），避免 async 构造器复杂度
            settings = _persistence.LoadAsync().GetAwaiter().GetResult();
        }
        catch
        {
            settings = _options.Value;
        }
        Volume = settings.DefaultVolume;

        // Phase 13: 加载频谱设置
        LoadSpectrumSettings(settings);

        // Phase 14: 加载均衡器设置并应用到播放链
        LoadEqualizerSettings(settings);

        _isInitializing = false;
    }

    /// <summary>订阅频谱事件（在构造函数中调用）</summary>
    private void InitializeSpectrum()
    {
        _player.SpectrumDataAvailable += HandleSpectrumData;
    }

    /// <summary>频谱数据处理（带平滑）</summary>
    private void HandleSpectrumData(float[] rawData)
    {
        if (!SpectrumEnabled) return;

        // rawData 已由 SampleAggregator 完成 FFT → 对数分桶 → 32 bars 映射，
        // 这里只需应用灵敏度增益 + 指数平滑即可，无需二次 MapToBars。
        var data = rawData.ToArray();

        // 应用灵敏度增益
        float sensitivity = Math.Clamp((float)SpectrumSensitivity, 0.5f, 2.0f);
        for (int i = 0; i < data.Length; i++)
        {
            data[i] *= sensitivity;
        }

        // 应用平滑（指数移动平均），限制在 [0, 0.95] 防止异常值导致不收敛
        float smoothing = Math.Clamp((float)SpectrumSmoothing, 0f, 0.95f);
        for (int i = 0; i < data.Length; i++)
        {
            _smoothedSpectrum[i] = _smoothedSpectrum[i] * smoothing
                                 + data[i] * (1 - smoothing);
        }

        // 更新属性（触发 UI 绑定）
        SpectrumData = _smoothedSpectrum.ToArray();
    }

    // —— 播放服务事件 handler ——

    private void HandlePositionChanged(TimeSpan position)
    {
        // 拖动时不更新 Position，否则用户拖到的位置会被服务每 33ms 覆盖回去
        if (!IsSeeking)
            Position = position;
    }

    private void HandleDurationChanged(TimeSpan duration)
        => Duration = duration;

    private void HandleTrackChanged(Track? track)
    {
        CurrentTrack = track;
        AlbumArtBytes = track?.AlbumArt;
    }

    private void HandleStateChanged(PlayState state)
        => PlayState = state;

    private void HandlePlaybackError(string error)
    {
        // TODO: 首期暂不处理；后续可弹 toast 或写日志
    }

    // —— UI 命令 ——

    /// <summary>用户开始拖动进度条 thumb 时由 View code-behind 调用。</summary>
    [RelayCommand]
    private void SeekStarted() => IsSeeking = true;

    /// <summary>拖动完成 / 单击跳转 —— 把归一化位置 [0,1] 转回 TimeSpan 并通知播放服务。</summary>
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

    /// <summary>切换静音：静音时记忆当前音量，恢复时还原。</summary>
    [RelayCommand]
    private void ToggleMute()
    {
        if (IsMuted)
        {
            IsMuted = false;
            Volume = _volumeBeforeMute;
        }
        else
        {
            _volumeBeforeMute = Volume;
            IsMuted = true;
            Volume = 0f;
        }
    }

    /// <summary>
    /// 音量变化钩子（源生成器自动调用）：
    ///   1) 同步到播放服务  2) 拖滑块时若处于静音则自动取消静音  3) 锁内持久化到磁盘
    /// </summary>
    partial void OnVolumeChanged(float value)
    {
        _player.Volume = value;
        if (_isInitializing) return; // 跳过初始化期间的写盘

        // 静音时直接拖动滑块到非零 → 自动解除静音状态
        if (IsMuted && value > 0f)
            IsMuted = false;

        _ = _persistence.UpdateAsync(s => s with { DefaultVolume = value });
    }

    /// <summary>切换频谱启用状态（属性变更触发 OnSpectrumEnabledChanged → SaveSpectrumSettings，无需重复调用）</summary>
    [RelayCommand]
    private void ToggleSpectrum()
    {
        SpectrumEnabled = !SpectrumEnabled;
    }

    // 属性变更时自动保存
    partial void OnSpectrumEnabledChanged(bool value)
    {
        // 同步到 SampleAggregator.Enabled，避免禁用后 FFT 仍在空转
        _player.SpectrumConfig = _player.SpectrumConfig with { Enabled = value };
        SaveSpectrumSettings();
    }

    partial void OnSpectrumSensitivityChanged(double value)
        => SaveSpectrumSettings();

    partial void OnSpectrumColorThemeChanged(int value)
        => SaveSpectrumSettings();

    partial void OnSpectrumSmoothingChanged(double value)
        => SaveSpectrumSettings();

    /// <summary>持久化频谱设置</summary>
    private void SaveSpectrumSettings()
    {
        if (_isInitializing) return;

        _ = _persistence.UpdateAsync(s => s with
        {
            SpectrumEnabled = SpectrumEnabled,
            SpectrumSensitivity = SpectrumSensitivity,
            SpectrumColorTheme = SpectrumColorTheme,
            SpectrumSmoothing = SpectrumSmoothing
        });
    }

    /// <summary>加载频谱设置</summary>
    private void LoadSpectrumSettings(AppSettings settings)
    {
        SpectrumEnabled = settings.SpectrumEnabled;
        SpectrumSensitivity = settings.SpectrumSensitivity;
        SpectrumColorTheme = settings.SpectrumColorTheme;
        SpectrumSmoothing = settings.SpectrumSmoothing;
    }

    /// <summary>启动加载 EQ 设置：设启用态 observable + 把完整配置应用到播放服务。</summary>
    private void LoadEqualizerSettings(AppSettings settings)
    {
        // 先设 observable（构造期 _isInitializing=true，OnEqualizerEnabledChanged 不写盘）
        EqualizerEnabled = settings.EqualizerEnabled;
        // 再把完整配置（含 preamp/10 段/预设）下发到 service，首次播放即生效
        _player.EqualizerConfig = EqualizerConfig.Create(
            settings.EqualizerEnabled, settings.EqualizerPreamp,
            settings.EqualizerBands, settings.EqualizerPreset);
    }

    /// <summary>EQ 启用态变更：传播到播放链 + 持久化（构造期跳过写盘）。</summary>
    partial void OnEqualizerEnabledChanged(bool value)
    {
        _player.EqualizerConfig = _player.EqualizerConfig with { Enabled = value };
        if (_isInitializing) return;
        _ = _persistence.UpdateAsync(s => s with { EqualizerEnabled = value });
    }

    /// <summary>
    /// 由 MainViewModel.CleanupAsync 调用 —— 解绑事件并持久化最后一次音量。
    /// 注意：不在这里 Dispose IPlaybackService（PlaylistViewModel 还在用，
    /// Facade 层统一 Dispose）。
    /// </summary>
    public async Task CleanupAsync()
    {
        _player.PositionChanged -= HandlePositionChanged;
        _player.StateChanged -= HandleStateChanged;
        _player.DurationChanged -= HandleDurationChanged;
        _player.TrackChanged -= HandleTrackChanged;
        _player.PlaybackError -= HandlePlaybackError;

        // Phase 13: 解绑频谱事件
        _player.SpectrumDataAvailable -= HandleSpectrumData;

        await _persistence.UpdateAsync(s => s with { DefaultVolume = Volume });
    }
}
