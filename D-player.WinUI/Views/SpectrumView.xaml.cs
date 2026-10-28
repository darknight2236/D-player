using System.ComponentModel;
using DPlayer.Models;
using DPlayer.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace DPlayer.WinUI.Views;

/// <summary>
/// 32 柱音频频谱：数据变化只更新目标高度，CompositionTarget.Rendering 负责逐帧插值。
/// </summary>
public sealed partial class SpectrumView : UserControl
{
    private const int BarCount = 32;
    private const double BarSpacing = 2.0;
    private const double MinBarHeight = 2.0;
    private const double MaxBarHeight = 118.0;
    private const double AnimationSmoothFactor = 0.3;

    private readonly PlayerViewModel _player;
    private readonly Rectangle[] _bars = new Rectangle[BarCount];
    private readonly LinearGradientBrush[] _gradients = new LinearGradientBrush[3];
    private readonly SolidColorBrush[] _rainbowBrushes = new SolidColorBrush[BarCount];
    private readonly double[] _targetHeights = new double[BarCount];

    public SpectrumView(PlayerViewModel player)
    {
        _player = player;
        InitializeComponent();
        InitializeGradients();
        Loaded += (_, _) =>
        {
            RecalculateBarLayout();
            ApplyTheme();
            if (_player.PlayState == PlayState.Playing)
                UpdateTargets(_player.SpectrumData);
            else
                Array.Fill(_targetHeights, MinBarHeight);
            UpdateVisibility();
            _player.PropertyChanged += Player_PropertyChanged;
            CompositionTarget.Rendering += OnRendering;
        };
        Unloaded += (_, _) =>
        {
            CompositionTarget.Rendering -= OnRendering;
            _player.PropertyChanged -= Player_PropertyChanged;
        };
        SizeChanged += (_, _) => RecalculateBarLayout();
    }

    private void Player_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.SpectrumData))
            UpdateTargets(_player.SpectrumData);
        else if (e.PropertyName == nameof(PlayerViewModel.SpectrumColorTheme))
            ApplyTheme();
        else if (e.PropertyName == nameof(PlayerViewModel.SpectrumEnabled))
            UpdateVisibility();
        else if (e.PropertyName == nameof(PlayerViewModel.PlayState))
        {
            if (_player.PlayState == PlayState.Playing)
                UpdateTargets(_player.SpectrumData);
            else
                Array.Fill(_targetHeights, MinBarHeight);
        }
    }

    private void UpdateVisibility()
        => Visibility = _player.SpectrumEnabled ? Visibility.Visible : Visibility.Collapsed;

    private void RecalculateBarLayout()
    {
        if (ActualWidth <= 0) return;

        var barWidth = (ActualWidth - (BarCount - 1) * BarSpacing) / BarCount;
        if (barWidth <= 0) return;

        SpectrumCanvas.Children.Clear();
        for (var i = 0; i < BarCount; i++)
        {
            var bar = new Rectangle
            {
                Width = barWidth,
                Height = MinBarHeight,
                RadiusX = 2,
                RadiusY = 2,
                Fill = _player.SpectrumColorTheme == 3
                    ? _rainbowBrushes[i]
                    : _gradients[Math.Clamp(_player.SpectrumColorTheme, 0, 2)],
            };

            Canvas.SetLeft(bar, i * (barWidth + BarSpacing));
            Canvas.SetTop(bar, MaxBarHeight + MinBarHeight - bar.Height);
            SpectrumCanvas.Children.Add(bar);
            _bars[i] = bar;
        }
    }

    private void UpdateTargets(float[] data)
    {
        if (data.Length < BarCount) return;

        for (var i = 0; i < BarCount; i++)
            _targetHeights[i] = MinBarHeight + data[i] * (MaxBarHeight - MinBarHeight);
    }

    private void OnRendering(object? sender, object e)
    {
        for (var i = 0; i < BarCount; i++)
        {
            if (_bars[i] is not { } bar) continue;
            var target = Math.Clamp(_targetHeights[i], MinBarHeight, MaxBarHeight);
            bar.Height += (target - bar.Height) * AnimationSmoothFactor;
            Canvas.SetTop(bar, MaxBarHeight + MinBarHeight - bar.Height);
        }
    }

    private void InitializeGradients()
    {
        _gradients[0] = CreateGradient(
            ColorHelper.FromArgb(0xFF, 0x7C, 0x4D, 0xFF),
            ColorHelper.FromArgb(0xFF, 0x9E, 0x7C, 0xFF));
        _gradients[1] = CreateGradient(
            ColorHelper.FromArgb(0xFF, 0x21, 0x96, 0xF3),
            ColorHelper.FromArgb(0xFF, 0x64, 0xB5, 0xF6));
        _gradients[2] = CreateGradient(
            ColorHelper.FromArgb(0xFF, 0x4C, 0xAF, 0x50),
            ColorHelper.FromArgb(0xFF, 0x81, 0xC7, 0x84));

        for (var i = 0; i < BarCount; i++)
        {
            var hue = 300f / 31f * i;
            _rainbowBrushes[i] = new SolidColorBrush(HsvToRgb(hue, 1f, 1f));
        }
    }

    private static LinearGradientBrush CreateGradient(Color bottom, Color top)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 1),
            EndPoint = new Point(0, 0),
        };
        brush.GradientStops.Add(new GradientStop { Color = bottom, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = top, Offset = 1 });
        return brush;
    }

    private void ApplyTheme()
    {
        if (_player.SpectrumColorTheme == 3)
        {
            for (var i = 0; i < BarCount; i++)
            {
                if (_bars[i] is not null)
                    _bars[i].Fill = _rainbowBrushes[i];
            }
        }
        else
        {
            var brush = _gradients[Math.Clamp(_player.SpectrumColorTheme, 0, 2)];
            foreach (var bar in _bars)
            {
                if (bar is not null)
                    bar.Fill = brush;
            }
        }
    }

    /// <summary>HSV 转 RGB（h: 0-360, s: 0-1, v: 0-1）。</summary>
    private static Color HsvToRgb(float h, float s, float v)
    {
        var c = v * s;
        var x = c * (1 - MathF.Abs(h / 60 % 2 - 1));
        var m = v - c;
        float r, g, b;

        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return ColorHelper.FromArgb(0xFF, 
            (byte)((r + m) * 255),
            (byte)((g + m) * 255),
            (byte)((b + m) * 255));
    }
}
