using System.Windows;
using Dota2Optimizer.Core.Backup;

namespace Dota2Optimizer.App.Views;

/// <summary>Shows every change before it is made and offers a restore point (checked by default before the first apply).</summary>
public partial class PreviewDialog : Window
{
    public PreviewDialog(string title, IReadOnlyList<PreviewLine> lines, bool firstApply, bool needsAdmin)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        HeaderText.Text = title;
        Lines.ItemsSource = lines;
        var admin = lines.Count(l => l.RequiresAdmin);
        SummaryText.Text = $"Изменений: {lines.Count}" + (admin > 0 ? $", из них {admin} с правами администратора (один запрос UAC)" : string.Empty);
        RestorePointCheck.IsChecked = firstApply || needsAdmin;
        RestoreNote.Text = firstApply
            ? "Это первое применение: рекомендуем точку восстановления — она откатывает Windows целиком, даже если что-то пойдёт не так с самой программой. Нужны права администратора."
            : "Необязательно: копии всех изменяемых значений программа делает сама. Для точки нужны права администратора; Windows создаёт не больше одной точки в сутки.";
    }

    public bool CreateRestorePoint => RestorePointCheck.IsChecked == true;

    private void OnApply(object sender, RoutedEventArgs e) => DialogResult = true;
}
