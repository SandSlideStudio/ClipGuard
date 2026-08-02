using System.IO;
using System.Windows;
using ClipGuard.Services;

namespace ClipGuard.Views;

public enum RelocateChoice
{
    /// <summary>Copied and relaunched elsewhere; this process should exit.</summary>
    Moved,
    /// <summary>Carry on from here, without startup registration.</summary>
    TestMode
}

public partial class RelocateWindow : Window
{
    private string _destination = InstallLocation.SuggestedDirectory;

    public RelocateChoice Choice { get; private set; } = RelocateChoice.TestMode;

    public RelocateWindow(LocationProblem problem)
    {
        InitializeComponent();

        Headline.Text = $"ClipGuard is running from {InstallLocation.Describe(problem)}.";
        Explanation.Text = InstallLocation.ExplainRisk(problem)
                           + "\n\nMove it somewhere permanent and it can protect you from "
                           + "every sign-in onwards. Test mode works fully for now, but "
                           + "can't start with Windows.";

        Destination.Text = _destination;
    }

    private void OnChooseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Where should ClipGuard live?",
            InitialDirectory = Directory.Exists(_destination)
                ? _destination
                : Path.GetDirectoryName(_destination) ?? string.Empty
        };

        if (dialog.ShowDialog() != true) return;

        // Picking somewhere just as volatile would defeat the point.
        var chosen = Path.Combine(dialog.FolderName, "ClipGuard");
        var problem = InstallLocation.Diagnose(Path.Combine(chosen, "ClipGuard.exe"));

        if (problem != LocationProblem.None)
        {
            ShowFailure($"That's still {InstallLocation.Describe(problem)}. Pick somewhere permanent.");
            return;
        }

        _destination = chosen;
        Destination.Text = _destination;
        Failure.Visibility = Visibility.Collapsed;
    }

    private void OnMove(object sender, RoutedEventArgs e)
    {
        MoveButton.IsEnabled = false;

        var moved = InstallLocation.MoveTo(_destination);

        if (moved is null)
        {
            ShowFailure("Couldn't copy ClipGuard there. Choose a folder you can write to, "
                        + "or continue in test mode.");
            MoveButton.IsEnabled = true;
            return;
        }

        Choice = RelocateChoice.Moved;
        Close();
    }

    private void OnTestMode(object sender, RoutedEventArgs e)
    {
        Choice = RelocateChoice.TestMode;
        Close();
    }

    private void ShowFailure(string message)
    {
        Failure.Text = message;
        Failure.Visibility = Visibility.Visible;
    }
}
