using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Dota2Optimizer.App.Views;

/// <summary>Ring indicator (CPU/GPU/RAM load, temperature). The arc eases to a new value; nothing redraws while the value is steady.</summary>
public sealed class RingGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(RingGauge), new PropertyMetadata(0d, OnValueChanged));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(RingGauge), new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty =
        DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(RingGauge), new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(RingGauge), new FrameworkPropertyMetadata(Brushes.OrangeRed, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty =
        DependencyProperty.Register(nameof(Track), typeof(Brush), typeof(RingGauge),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty ShownProperty =
        DependencyProperty.Register("Shown", typeof(double), typeof(RingGauge), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush Track
    {
        get => (Brush)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var g = (RingGauge)d;
        var to = double.IsNaN((double)e.NewValue) ? 0 : (double)e.NewValue;
        var shown = (double)g.GetValue(ShownProperty);
        // Live values twitch by a percent or two every sample: those are drawn at once, only real changes animate.
        if (!Motion.Enabled || Math.Abs(to - shown) < g.Maximum * 0.03)
        {
            g.BeginAnimation(ShownProperty, null);
            g.SetValue(ShownProperty, to);
            return;
        }

        g.BeginAnimation(ShownProperty, new DoubleAnimation(to, Motion.Slow + Motion.Base) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Thickness * 2)
        {
            return;
        }

        var r = (size - Thickness) / 2;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        var track = new Pen(Track, Thickness);
        dc.DrawEllipse(null, track, c, r, r);

        var fraction = Math.Clamp((double)GetValue(ShownProperty) / Math.Max(Maximum, 1e-6), 0, 0.9999);
        if (fraction <= 0.001)
        {
            return;
        }

        var start = new Point(c.X, c.Y - r);
        var angle = fraction * 2 * Math.PI;
        var end = new Point(c.X + r * Math.Sin(angle), c.Y - r * Math.Cos(angle));
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(start, isFilled: false, isClosed: false);
            ctx.ArcTo(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }

        geo.Freeze();
        dc.DrawGeometry(null, new Pen(Stroke, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geo);
    }
}

public enum MarkState
{
    None,
    Busy,
    Done,
    Undone,
    Error,
}

/// <summary>
/// Feedback for applying a tweak: a pulsing dot while busy, then a check that draws itself (or an undo arrow).
/// The pulse runs only while busy; the finished mark is static.
/// </summary>
public sealed class StatusMark : FrameworkElement
{
    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(nameof(State), typeof(MarkState), typeof(StatusMark), new PropertyMetadata(MarkState.None, OnStateChanged));

    public static readonly DependencyProperty BrushProperty =
        DependencyProperty.Register(nameof(Brush), typeof(Brush), typeof(StatusMark), new FrameworkPropertyMetadata(Brushes.MediumSeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register("Progress", typeof(double), typeof(StatusMark), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty PulseProperty =
        DependencyProperty.Register("PulseValue", typeof(double), typeof(StatusMark), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public MarkState State
    {
        get => (MarkState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public Brush Brush
    {
        get => (Brush)GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var m = (StatusMark)d;
        m.BeginAnimation(PulseProperty, null);
        m.BeginAnimation(ProgressProperty, null);
        switch ((MarkState)e.NewValue)
        {
            case MarkState.Busy when Motion.Enabled:
                // The only repeating animation in the app, and it stops as soon as the operation ends.
                m.BeginAnimation(PulseProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                break;
            case MarkState.Done or MarkState.Undone or MarkState.Error when Motion.Enabled:
                m.BeginAnimation(ProgressProperty, new DoubleAnimation(0, 1, Motion.Slow) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                break;
            default:
                m.SetValue(ProgressProperty, 1d);
                break;
        }

        m.InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var s = Math.Min(ActualWidth, ActualHeight);
        if (s <= 0)
        {
            return;
        }

        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        var r = s / 2 - 1;
        var p = (double)GetValue(ProgressProperty);
        switch (State)
        {
            case MarkState.Busy:
                var pulse = (double)GetValue(PulseProperty);
                dc.PushOpacity(0.25 + 0.5 * (1 - pulse));
                dc.DrawEllipse(Brush, null, c, r * (0.55 + 0.45 * pulse), r * (0.55 + 0.45 * pulse));
                dc.Pop();
                dc.DrawEllipse(Brush, null, c, r * 0.35, r * 0.35);
                break;
            case MarkState.Done:
                dc.PushOpacity(0.18);
                dc.DrawEllipse(Brush, null, c, r * (0.7 + 0.3 * p), r * (0.7 + 0.3 * p));
                dc.Pop();
                DrawPartial(dc, new[] { new Point(c.X - r * 0.45, c.Y + r * 0.02), new Point(c.X - r * 0.12, c.Y + r * 0.34), new Point(c.X + r * 0.48, c.Y - r * 0.34) }, p, s / 9);
                break;
            case MarkState.Undone:
                dc.PushOpacity(0.18);
                dc.DrawEllipse(Brush, null, c, r, r);
                dc.Pop();
                DrawPartial(dc, new[] { new Point(c.X + r * 0.45, c.Y + r * 0.3), new Point(c.X + r * 0.45, c.Y - r * 0.1), new Point(c.X - r * 0.4, c.Y - r * 0.1), new Point(c.X - r * 0.12, c.Y - r * 0.38) }, p, s / 10);
                break;
            case MarkState.Error:
                dc.PushOpacity(0.18);
                dc.DrawEllipse(Brush, null, c, r, r);
                dc.Pop();
                DrawPartial(dc, new[] { new Point(c.X, c.Y - r * 0.45), new Point(c.X, c.Y + r * 0.1) }, p, s / 9);
                dc.DrawEllipse(Brush, null, new Point(c.X, c.Y + r * 0.38), s / 16, s / 16);
                break;
        }
    }

    /// <summary>Draws the first <paramref name="progress"/> of a polyline, so the mark "writes itself".</summary>
    private void DrawPartial(DrawingContext dc, Point[] pts, double progress, double thickness)
    {
        var lengths = pts.Zip(pts.Skip(1), (a, b) => (b - a).Length).ToArray();
        var remaining = lengths.Sum() * progress;
        var pen = new Pen(Brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(pts[0], false, false);
            for (var i = 0; i < lengths.Length && remaining > 0; i++)
            {
                var t = Math.Min(1, remaining / lengths[i]);
                ctx.LineTo(pts[i] + (pts[i + 1] - pts[i]) * t, true, true);
                remaining -= lengths[i];
            }
        }

        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }
}

/// <summary>Line chart with a soft fill. A new series morphs from the previous one instead of jumping.</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(nameof(Values), typeof(IReadOnlyList<double>), typeof(Sparkline), new PropertyMetadata(null, OnValuesChanged));

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(Sparkline), new FrameworkPropertyMetadata(Brushes.OrangeRed, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(Sparkline), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty MorphProperty =
        DependencyProperty.Register("Morph", typeof(double), typeof(Sparkline), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    private double[] _from = Array.Empty<double>();

    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>Lower bound of the scale; NaN = from the data.</summary>
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var s = (Sparkline)d;
        s._from = (e.OldValue as IReadOnlyList<double>)?.ToArray() ?? Array.Empty<double>();
        if (Motion.Enabled && s._from.Length > 1)
        {
            s.BeginAnimation(MorphProperty, new DoubleAnimation(0, 1, Motion.Slow) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        else
        {
            s.BeginAnimation(MorphProperty, null);
            s.SetValue(MorphProperty, 1d);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var v = Values;
        if (v is null || v.Count < 2 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var morph = (double)GetValue(MorphProperty);
        var pts = new double[v.Count];
        for (var i = 0; i < v.Count; i++)
        {
            var old = _from.Length > 1 ? _from[(int)Math.Round(i * (_from.Length - 1) / (double)(v.Count - 1))] : v[i];
            pts[i] = old + (v[i] - old) * morph;
        }

        var max = Math.Max(pts.Max(), 1e-6);
        var min = double.IsNaN(Minimum) ? pts.Min() : Minimum;
        var span = Math.Max(max - min, 1e-6);
        Point P(int i) => new(i * ActualWidth / (pts.Length - 1), ActualHeight - 2 - (pts[i] - min) / span * (ActualHeight - 4));

        var line = new StreamGeometry();
        var fill = new StreamGeometry();
        using (var l = line.Open())
        using (var f = fill.Open())
        {
            l.BeginFigure(P(0), false, false);
            f.BeginFigure(new Point(0, ActualHeight), true, true);
            f.LineTo(P(0), false, false);
            for (var i = 1; i < pts.Length; i++)
            {
                l.LineTo(P(i), true, true);
                f.LineTo(P(i), false, false);
            }

            f.LineTo(new Point(ActualWidth, ActualHeight), false, false);
        }

        line.Freeze();
        fill.Freeze();
        var color = (Stroke as SolidColorBrush)?.Color ?? Colors.OrangeRed;
        var gradient = new LinearGradientBrush(Color.FromArgb(0x40, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B), 90);
        gradient.Freeze();
        dc.DrawGeometry(gradient, null, fill);
        dc.DrawGeometry(null, new Pen(Stroke, 2) { LineJoin = PenLineJoin.Round }, line);
    }
}

/// <summary>A button that looks like an on/off switch. The state comes from the view model (<see cref="IsOn"/>), the click runs a command,
/// so a refused or cancelled change never leaves the switch in a wrong position.</summary>
public sealed class SwitchButton : System.Windows.Controls.Button
{
    public static readonly DependencyProperty IsOnProperty =
        DependencyProperty.Register(nameof(IsOn), typeof(bool), typeof(SwitchButton), new PropertyMetadata(false));

    public static readonly DependencyProperty IsBusyProperty =
        DependencyProperty.Register(nameof(IsBusy), typeof(bool), typeof(SwitchButton), new PropertyMetadata(false));

    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }
}
