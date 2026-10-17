using System.Windows;
using Dota2Optimizer.App.ViewModels;

namespace Dota2Optimizer.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
