// Views/Controls/SpectrumView.xaml.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace UmaPlayer.Views.Controls;

/// <summary>
/// 音频频谱可视化控件 —— 显示 32 条垂直频谱柱，支持多种颜色主题。
/// 使用 CompositionTarget.Rendering 驱动 60fps 渲染循环。
/// </summary>
public partial class SpectrumView : UserControl
{
    private const int BarCount = 32;
    private const double BarSpacing = 2.0;
    private const double MinBarHeight = 2.0;
    private const double MaxBarHeight = 118.0;
    private const double AnimationSmoothFactor = 0.3;

    private readonly Rectangle[] _bars = new Rectangle[BarCount];
    private readonly LinearGradientBrush[] _gradients = new LinearGradientBrush[4];
    private readonly double[] _targetHeights = new double[BarCount];

    public SpectrumView()
    {
        InitializeComponent();
        InitializeBars();
        InitializeGradients();

        CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    private void InitializeBars()
    {
        // 等待 ActualWidth 可用
        SizeChanged += (_, _) => RecalculateBarLayout();
    }

    private void RecalculateBarLayout()
    {
        if (ActualWidth <= 0) return;

        SpectrumCanvas.Children.Clear();
        double barWidth = (ActualWidth - (BarCount - 1) * BarSpacing) / BarCount;

        for (int i = 0; i < BarCount; i++)
        {
            var bar = new Rectangle
            {
                Width = barWidth,
                Height = MinBarHeight,
                RadiusX = 2,
                RadiusY = 2,
                Fill = _gradients[0]
            };

            Canvas.SetLeft(bar, i * (barWidth + BarSpacing));
            Canvas.SetBottom(bar, 0);

            SpectrumCanvas.Children.Add(bar);
            _bars[i] = bar;
        }
    }

    private void InitializeGradients()
    {
        // 紫色主题 (index 0)
        _gradients[0] = CreateGradient(Color.FromRgb(0x7C, 0x4D, 0xFF), Color.FromRgb(0x9E, 0x7C, 0xFF));

        // 蓝色主题 (index 1)
        _gradients[1] = CreateGradient(Color.FromRgb(0x21, 0x96, 0xF3), Color.FromRgb(0x64, 0xB5, 0xF6));

        // 绿色主题 (index 2)
        _gradients[2] = CreateGradient(Color.FromRgb(0x4C, 0xAF, 0x50), Color.FromRgb(0x81, 0xC7, 0x84));

        // 彩虹主题 (index 3)
        _gradients[3] = CreateRainbowGradient();
    }

    private LinearGradientBrush CreateGradient(Color bottom, Color top)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 1),
            EndPoint = new Point(0, 0)
        };
        brush.GradientStops.Add(new GradientStop(bottom, 0));
        brush.GradientStops.Add(new GradientStop(top, 1));
        brush.Freeze();
        return brush;
    }

    private LinearGradientBrush CreateRainbowGradient()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 1),
            EndPoint = new Point(0, 0)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x00, 0x00), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xA5, 0x00), 0.2));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xFF, 0x00), 0.4));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x00, 0xFF, 0x00), 0.6));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x00, 0x00, 0xFF), 0.8));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x8B, 0x00, 0xFF), 1.0));
        brush.Freeze();
        return brush;
    }

    // 依赖属性：频谱数据
    public static readonly DependencyProperty SpectrumDataProperty =
        DependencyProperty.Register(
            nameof(SpectrumData),
            typeof(float[]),
            typeof(SpectrumView),
            new PropertyMetadata(new float[32], OnSpectrumDataChanged));

    public float[] SpectrumData
    {
        get => (float[])GetValue(SpectrumDataProperty);
        set => SetValue(SpectrumDataProperty, value);
    }

    // 依赖属性：颜色主题
    public static readonly DependencyProperty ColorThemeProperty =
        DependencyProperty.Register(
            nameof(ColorTheme),
            typeof(int),
            typeof(SpectrumView),
            new PropertyMetadata(0, OnColorThemeChanged));

    public int ColorTheme
    {
        get => (int)GetValue(ColorThemeProperty);
        set => SetValue(ColorThemeProperty, value);
    }

    private static void OnSpectrumDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SpectrumView view && e.NewValue is float[] data && data.Length >= BarCount)
        {
            for (int i = 0; i < BarCount; i++)
            {
                view._targetHeights[i] = MinBarHeight + data[i] * (MaxBarHeight - MinBarHeight);
            }
        }
    }

    private static void OnColorThemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SpectrumView view && e.NewValue is int theme)
        {
            var brush = view._gradients[Math.Clamp(theme, 0, 3)];
            foreach (var bar in view._bars)
            {
                if (bar != null)
                    bar.Fill = brush;
            }
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        for (int i = 0; i < BarCount; i++)
        {
            if (_bars[i] == null) continue;

            double targetHeight = Math.Clamp(_targetHeights[i], MinBarHeight, MaxBarHeight);
            double currentHeight = _bars[i].Height;
            double newHeight = currentHeight + (targetHeight - currentHeight) * AnimationSmoothFactor;

            _bars[i].Height = newHeight;
        }
    }
}
