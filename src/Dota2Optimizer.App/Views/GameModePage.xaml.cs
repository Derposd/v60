using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Dota2Optimizer.App.Views;

public partial class GameModePage : UserControl
{
    /// <summary>The remove command for the chips of one list (processes or services), set on that ItemsControl.</summary>
    public static readonly DependencyProperty RemoveCommandProperty =
        DependencyProperty.RegisterAttached("RemoveCommand", typeof(ICommand), typeof(GameModePage), new PropertyMetadata(null));

    public GameModePage() => InitializeComponent();

    public static ICommand? GetRemoveCommand(DependencyObject d) => (ICommand?)d.GetValue(RemoveCommandProperty);
    public static void SetRemoveCommand(DependencyObject d, ICommand? value) => d.SetValue(RemoveCommandProperty, value);
}
