using System.IO;
using System.Text;
using System.Windows;
using ClipGuard.Models;
using ClipGuard.ViewModels;

namespace ClipGuard.Views;

public partial class HistoryView : UserControl
{
    public HistoryView() => InitializeComponent();

    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>Per-row CTA: "Block app" on blocked rows, "Always allow" otherwise.</summary>
    private void OnRowAction(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not Button { Tag: ClipboardEvent record }) return;

        var kind = record.Blocked ? RuleKind.Blocked : RuleKind.AlwaysAllow;

        if (kind == RuleKind.Blocked && !RuleConfirmation.ConfirmBlock(record.ProcessName)) return;

        Vm.Protection.SetRule(record.ProcessName, kind);
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"clipguard-history-{DateTime.Now:yyyy-MM-dd}.csv",
            Filter = "CSV file (*.csv)|*.csv",
            Title = "Export clipboard history"
        };

        if (dialog.ShowDialog() != true) return;

        var csv = new StringBuilder();
        csv.AppendLine("Event ID,Time,App,Action,Category,Blocked,Content preview");

        foreach (var ev in Vm.Protection.Events)
        {
            csv.AppendLine(string.Join(',',
                Quote(ev.Id),
                Quote(ev.Timestamp.ToString("u")),
                Quote(ev.ProcessName),
                Quote(ev.ActionText),
                Quote(ev.CategoryText),
                ev.Blocked ? "yes" : "no",
                Quote(ev.ContentPreview)));
        }

        try
        {
            File.WriteAllText(dialog.FileName, csv.ToString(), Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Couldn't write that file.\n\n{ex.Message}", "ClipGuard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
