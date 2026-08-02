using System.IO;
using System.Windows;
using ClipGuard.Models;
using ClipGuard.ViewModels;

namespace ClipGuard.Views;

public partial class AppRulesView : UserControl
{
    public AppRulesView() => InitializeComponent();

    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>Pick an executable to add a rule for.</summary>
    private void OnAddApp(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pick an app to add a rule for",
            Filter = "Applications (*.exe)|*.exe",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true) return;

        // New rules default to Blocked — that's the reason to add one by hand.
        var processName = Path.GetFileName(dialog.FileName);
        if (!RuleConfirmation.ConfirmBlock(processName)) return;

        Vm.Protection.SetRule(processName, RuleKind.Blocked);
    }

    private void OnToggleRule(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not Button { Tag: AppRule rule }) return;

        var kind = rule.Rule == RuleKind.Blocked ? RuleKind.AlwaysAllow : RuleKind.Blocked;

        if (kind == RuleKind.Blocked && !RuleConfirmation.ConfirmBlock(rule.ProcessName)) return;

        Vm.Protection.SetRule(rule.ProcessName, kind);
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not Button { Tag: AppRule rule }) return;
        Vm.Protection.RemoveRule(rule);
    }

    private void OnRemoveTrustedSource(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not Button { Tag: string host }) return;
        Vm.Protection.RemoveTrustedSource(host);
    }
}
