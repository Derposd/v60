using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Dota2Optimizer.App.Views;

/// <summary>
/// Attached animations. Everything here animates only Opacity and RenderTransform (composited on the GPU, no layout passes),
/// runs once per trigger and never loops, so an idle window costs nothing. All of it is skipped when <see cref="Motion.Enabled"/> is off.
/// </summary>
public static class Fx
{
    private const int MaxStagger = 12;
    private static readonly IEasingFunction Out = new CubicEase { EasingMode = EasingMode.EaseOut };

    // ---------- Appear: fade + rise on load, staggered by position in the list ----------

    public static readonly DependencyProperty AppearProperty =
        DependencyProperty.RegisterAttached("Appear", typeof(bool), typeof(Fx), new PropertyMetadata(false, OnAppearChanged));

    public static bool GetAppear(DependencyObject d) => (bool)d.GetValue(AppearProperty);
    public static void SetAppear(DependencyObject d, bool value) => d.SetValue(AppearProperty, value);

    /// <summary>Explicit position for the stagger; otherwise the position inside the nearest ItemsControl or Panel is used.</summary>
    public static readonly DependencyProperty IndexProperty =
        DependencyProperty.RegisterAttached("Index", typeof(int), typeof(Fx), new PropertyMetadata(-1));

    public static int GetIndex(DependencyObject d) => (int)d.GetValue(IndexProperty);
    public static void SetIndex(DependencyObject d, int value) => d.SetValue(IndexProperty, value);

    private static void OnAppearChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement fe && (bool)e.NewValue)
        {
            fe.Loaded += (_, _) => PlayAppear(fe);
        }
    }

    public static void PlayAppear(FrameworkElement fe)
    {
        if (!Motion.Enabled)
        {
            return;
        }

        var index = GetIndex(fe);
        if (index < 0)
        {
            index = AutoIndex(fe);
        }

        var delay = TimeSpan.FromMilliseconds(Motion.Stagger.TotalMilliseconds * Math.Min(Math.Max(index, 0), MaxStagger));
        var shift = EnsureTranslate(fe);
        var fade = new DoubleAnimation(0, 1, Motion.Slow) { BeginTime = delay, EasingFunction = Out };
        var rise = new DoubleAnimation(10, 0, Motion.Slow) { BeginTime = delay, EasingFunction = Out };
        fe.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        shift.BeginAnimation(TranslateTransform.YProperty, rise, HandoffBehavior.SnapshotAndReplace);
    }

    private static int AutoIndex(FrameworkElement fe)
    {
        DependencyObject? child = fe;
        var parent = VisualTreeHelper.GetParent(fe);
        while (parent is not null)
        {
            if (parent is Panel panel)
            {
                var owner = ItemsControl.GetItemsOwner(panel);
                if (owner is not null && child is not null)
                {
                    var container = owner.ContainerFromElement(child) ?? child;
                    return owner.ItemContainerGenerator.IndexFromContainer(container);
                }

                return child is UIElement ui ? panel.Children.IndexOf(ui) : 0;
            }

            child = parent;
            parent = VisualTreeHelper.GetParent(parent);
        }

        return 0;
    }

    // ---------- Page transition for a TabControl ----------

    public static readonly DependencyProperty PageTransitionProperty =
        DependencyProperty.RegisterAttached("PageTransition", typeof(bool), typeof(Fx), new PropertyMetadata(false, OnPageTransitionChanged));

    public static bool GetPageTransition(DependencyObject d) => (bool)d.GetValue(PageTransitionProperty);
    public static void SetPageTransition(DependencyObject d, bool value) => d.SetValue(PageTransitionProperty, value);

    private static void OnPageTransitionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TabControl tabs && (bool)e.NewValue)
        {
            tabs.SelectionChanged += (s, args) =>
            {
                if (!ReferenceEquals(args.OriginalSource, tabs) || !Motion.Enabled)
                {
                    return;
                }

                if (tabs.Template?.FindName("PART_SelectedContentHost", tabs) is FrameworkElement host)
                {
                    var shift = EnsureTranslate(host);
                    host.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Motion.Base) { EasingFunction = Out });
                    shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(14, 0, Motion.Base) { EasingFunction = Out });
                }
            };
        }
    }

    // ---------- Hover lift and press for cards ----------

    public static readonly DependencyProperty LiftProperty =
        DependencyProperty.RegisterAttached("Lift", typeof(bool), typeof(Fx), new PropertyMetadata(false, OnLiftChanged));

    public static bool GetLift(DependencyObject d) => (bool)d.GetValue(LiftProperty);
    public static void SetLift(DependencyObject d, bool value) => d.SetValue(LiftProperty, value);

    private static void OnLiftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe || !(bool)e.NewValue)
        {
            return;
        }

        fe.MouseEnter += (_, _) => Move(fe, -2);
        fe.MouseLeave += (_, _) => Move(fe, 0);
        fe.PreviewMouseLeftButtonDown += (_, _) => Move(fe, 0);
        fe.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (fe.IsMouseOver)
            {
                Move(fe, -2);
            }
        };
    }

    private static void Move(FrameworkElement fe, double y)
    {
        var t = EnsureTranslate(fe);
        if (!Motion.Enabled)
        {
            t.BeginAnimation(TranslateTransform.YProperty, null);
            return;
        }

        t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, Motion.Fast) { EasingFunction = Out });
    }

    // ---------- Animated number for a TextBlock ----------

    public static readonly DependencyProperty NumberProperty =
        DependencyProperty.RegisterAttached("Number", typeof(double), typeof(Fx), new PropertyMetadata(double.NaN, OnNumberChanged));

    public static double GetNumber(DependencyObject d) => (double)d.GetValue(NumberProperty);
    public static void SetNumber(DependencyObject d, double value) => d.SetValue(NumberProperty, value);

    /// <summary>.NET format for the number, e.g. "0", "0.0", "0 'FPS'".</summary>
    public static readonly DependencyProperty FormatProperty =
        DependencyProperty.RegisterAttached("Format", typeof(string), typeof(Fx), new PropertyMetadata("0"));

    public static string GetFormat(DependencyObject d) => (string)d.GetValue(FormatProperty);
    public static void SetFormat(DependencyObject d, string value) => d.SetValue(FormatProperty, value);

    private static readonly DependencyProperty ShownProperty =
        DependencyProperty.RegisterAttached("Shown", typeof(double), typeof(Fx), new PropertyMetadata(0d, OnShownChanged));

    private static void OnNumberChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb)
        {
            return;
        }

        var to = (double)e.NewValue;
        if (double.IsNaN(to))
        {
            tb.BeginAnimation(ShownProperty, null);
            tb.Text = "—";
            return;
        }

        var from = e.OldValue is double old && !double.IsNaN(old) ? (double)tb.GetValue(ShownProperty) : 0;
        // A change of a few percent (live load) is shown at once; counting up is for real results.
        if (!Motion.Enabled || (from != 0 && Math.Abs(to - from) <= Math.Max(2, Math.Abs(from) * 0.03)))
        {
            tb.BeginAnimation(ShownProperty, null);
            tb.SetValue(ShownProperty, to);
            Render(tb, to);
            return;
        }

        tb.BeginAnimation(ShownProperty, new DoubleAnimation(from, to, Motion.Slow + Motion.Base) { EasingFunction = Out });
    }

    private static void OnShownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock tb)
        {
            Render(tb, (double)e.NewValue);
        }
    }

    private static void Render(TextBlock tb, double v) => tb.Text = v.ToString(GetFormat(tb), CultureInfo.CurrentCulture);

    // ---------- Pulse: a short "something happened" scale on the element ----------

    public static void Pulse(FrameworkElement fe)
    {
        if (!Motion.Enabled)
        {
            return;
        }

        fe.RenderTransformOrigin = new Point(0.5, 0.5);
        if (fe.RenderTransform is not ScaleTransform scale || scale.IsFrozen)
        {
            scale = new ScaleTransform(1, 1);
            fe.RenderTransform = scale;
        }

        var a = new DoubleAnimation(1, 1.08, Motion.Fast) { AutoReverse = true, EasingFunction = Out };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    private static TranslateTransform EnsureTranslate(UIElement el)
    {
        if (el.RenderTransform is TranslateTransform t && !t.IsFrozen)
        {
            return t;
        }

        t = new TranslateTransform();
        el.RenderTransform = t;
        return t;
    }
}
