using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Animation;

namespace Dota2Optimizer.App.Views;

public partial class SpecView : UserControl
{
    public SpecView()
    {
        InitializeComponent();
        // The counter digits change with a short slide (≤150 ms); off with «Уменьшить анимации».
        DependencyPropertyDescriptor.FromProperty(SpacedText.TextProperty, typeof(SpacedText)).AddValueChanged(Counter, (_, _) => AnimateCounter());
    }

    private void AnimateCounter()
    {
        if (!IsLoaded || !Motion.Enabled)
        {
            return;
        }

        var d = new Duration(TimeSpan.FromMilliseconds(120));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        CounterShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(10, 0, d) { EasingFunction = ease });
        Counter.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, d) { EasingFunction = ease });
    }
}

/// <summary>Upper-cases a label (CSS text-transform: uppercase).</summary>
public sealed class UpperConverter : IValueConverter
{
    public static UpperConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value as string ?? string.Empty).ToUpper(CultureInfo.CurrentCulture);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
