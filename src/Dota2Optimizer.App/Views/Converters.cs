using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.App.Views;

/// <summary>Risk tag colour: low = neutral, medium = amber, high = red. Parameter "soft" gives the tag background.</summary>
public sealed class RiskBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var soft = parameter as string == "soft";
        var key = value switch
        {
            TweakRisk.High => soft ? "CritSoft" : "Crit",
            TweakRisk.Medium => soft ? "WarnSoft" : "Warn",
            _ => soft ? "NeutralSoft" : "Muted",
        };
        return Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Icon geometry for a tweak category.</summary>
public sealed class CategoryIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Application.Current.Resources[value switch
    {
        TweakCategory.Gpu => "IconChip",
        TweakCategory.Privacy => "IconEyeOff",
        TweakCategory.Interface => "IconLayout",
        TweakCategory.Network => "IconWifi",
        TweakCategory.Services => "IconGear",
        TweakCategory.Game => "IconGamepad",
        _ => "IconCpu",
    }];

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Hides an element when a count is zero.</summary>
public sealed class ZeroToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int i && i == 0 ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Fraction (0..1) × available width → bar width.</summary>
public sealed class FractionWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is double f && values[1] is double w && !double.IsNaN(w) ? Math.Max(0, f * w) : 0d;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
