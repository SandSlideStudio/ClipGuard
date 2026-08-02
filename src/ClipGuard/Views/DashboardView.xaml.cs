using System.Windows;
using ClipGuard.ViewModels;

namespace ClipGuard.Views;

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnClearClipboard(object sender, RoutedEventArgs e) =>
        Vm?.Protection.ClearClipboardNow();

    private void OnTogglePause(object sender, RoutedEventArgs e) =>
        Vm?.Protection.TogglePause();
}
