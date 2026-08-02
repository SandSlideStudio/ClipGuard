using System.Windows;
using ClipGuard.Services;

namespace ClipGuard.Views;

/// <summary>
/// Guardrail for the one rule that can quietly ruin someone's day.
///
/// With process-level attribution a rule covers a whole executable. For a
/// standalone app that's proportionate; for a browser it means every copy you
/// make from it gets reverted, which almost nobody wants and which is hard to
/// connect back to a button pressed days earlier. So say so, plainly, first.
/// </summary>
public static class RuleConfirmation
{
    public static bool ConfirmBlock(string processName)
    {
        if (!ProtectionService.IsBrowser(processName)) return true;

        var answer = MessageBox.Show(
            $"Blocking {processName} stops every clipboard write from your browser — "
            + "including the things you copy yourself. Copying from web pages will "
            + "look broken until you remove the rule.\n\n"
            + "If a page tried to swap your clipboard, keeping your original is "
            + "usually the better response.\n\n"
            + $"Block {processName} anyway?",
            "Block your browser?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        return answer == MessageBoxResult.Yes;
    }
}
