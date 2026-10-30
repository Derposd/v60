using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Dota2Optimizer.App.Views;

/// <summary>
/// One line of text with letter-spacing (WPF's TextBlock has none) and a CSS-like line height: the mono caps labels (.14em),
/// the brand (.18em) and the big counter (−.05em, line-height .82). The line box is <see cref="LineHeight"/>×size and the glyphs are
/// centred in it the way a browser does, so values from the reference layout carry over unchanged.
/// </summary>
public sealed class SpacedText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = Reg(nameof(Text), typeof(string), string.Empty);
    public static readonly DependencyProperty TrackingProperty = Reg(nameof(Tracking), typeof(double), 0.0);
    public static readonly DependencyProperty LineHeightProperty = Reg(nameof(LineHeight), typeof(double), double.NaN);
    public static readonly DependencyProperty FontFamilyProperty = Reg(nameof(FontFamily), typeof(FontFamily), new FontFamily("Segoe UI"));
    public static readonly DependencyProperty FontSizeProperty = Reg(nameof(FontSize), typeof(double), 11.0);
    public static readonly DependencyProperty FontWeightProperty = Reg(nameof(FontWeight), typeof(FontWeight), FontWeights.Normal);
    public static readonly DependencyProperty ForegroundProperty = Reg(nameof(Foreground), typeof(Brush), Brushes.Black);

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    /// <summary>Extra space between letters in em, like CSS letter-spacing.</summary>
    public double Tracking { get => (double)GetValue(TrackingProperty); set => SetValue(TrackingProperty, value); }

    /// <summary>Line height in em (NaN = the font's own line spacing).</summary>
    public double LineHeight { get => (double)GetValue(LineHeightProperty); set => SetValue(LineHeightProperty, value); }

    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    private static DependencyProperty Reg(string name, Type type, object def) =>
        DependencyProperty.Register(name, type, typeof(SpacedText), new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private List<FormattedText> Glyphs()
    {
        var face = new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        return (Text ?? string.Empty).Select(c => new FormattedText(c.ToString(), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, FontSize, Foreground, dpi)).ToList();
    }

    private double Spacing => Tracking * FontSize;

    private double BoxHeight(IReadOnlyList<FormattedText> g) =>
        double.IsNaN(LineHeight) ? (g.Count > 0 ? g.Max(x => x.Height) : FontSize * 1.2) : LineHeight * FontSize;

    protected override Size MeasureOverride(Size availableSize)
    {
        var g = Glyphs();
        var width = g.Sum(x => x.WidthIncludingTrailingWhitespace) + Math.Max(0, g.Count - 1) * Spacing;
        return new Size(Math.Max(0, width), BoxHeight(g));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var g = Glyphs();
        if (g.Count == 0)
        {
            return;
        }

        var lineSpacing = g.Max(x => x.Height);
        var y = (BoxHeight(g) - lineSpacing) / 2;
        var x = 0.0;
        foreach (var ft in g)
        {
            dc.DrawText(ft, new Point(x, y));
            x += ft.WidthIncludingTrailingWhitespace + Spacing;
        }
    }
}
