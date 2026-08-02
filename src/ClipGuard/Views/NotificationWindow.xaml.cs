using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ClipGuard.Models;
using ClipGuard.Services;

namespace ClipGuard.Views;

/// <summary>
/// One toast. The three layouts live in the same window and are selected
/// by severity, plus two warning variants for content that arrived dangerous
/// rather than swapped. Only the informational and blocked toasts auto-dismiss.
/// </summary>
public partial class NotificationWindow : Window
{
    private static readonly TimeSpan AllowedLifetime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BlockedLifetime = TimeSpan.FromSeconds(9);

    private readonly ProtectionService _protection;
    private readonly ClipboardEvent _event;
    private readonly DispatcherTimer? _dismissTimer;

    public event EventHandler? Dismissed;

    public NotificationWindow(ProtectionService protection, NotificationRequest request)
    {
        _protection = protection;
        _event = request.Event;

        InitializeComponent();

        switch (request.Kind)
        {
            case NotificationKind.Allowed:
                SmallCard.Visibility = Visibility.Visible;
                SmallProcess.Text = _event.ProcessName;
                SmallMessage.Text = "wrote to your clipboard · allowed";
                _dismissTimer = StartDismissTimer(AllowedLifetime);
                break;

            case NotificationKind.Blocked:
                MediumCard.Visibility = Visibility.Visible;
                MediumProcess.Text = _event.ProcessName;
                _dismissTimer = StartDismissTimer(BlockedLifetime);
                AnimateProgress(BlockedLifetime);
                break;

            case NotificationKind.HighRiskSwap:
                LargeCard.Visibility = Visibility.Visible;
                LargeProcess.Text = _event.ProcessName;
                LargeBody.Text = BuildHighRiskBody();
                DiffOriginal.Text = string.IsNullOrEmpty(_event.ReplacedPreview)
                    ? "(nothing)"
                    : _event.ReplacedPreview;
                DiffReplacement.Text = _event.ContentPreview;
                ConfigureHighRiskActions();
                // Sticky by design — no timer.
                break;

            case NotificationKind.ContentWarning:
                ShowWarning(
                    title: RiskAnalyzer.IsVerificationLure(_event.ContentPreview)
                        ? "This is a scam. Don't paste it."
                        : "That's a download-and-run command.",
                    body: BuildContentWarningBody());
                break;

            case NotificationKind.PasteWarning:
                ShowWarning(
                    title: "Hold on — don't paste that here.",
                    body: BuildPasteWarningBody(request.TargetApp));
                break;
        }
    }

    /// <summary>
    /// Tailors the high-risk actions. Blocking a browser would stop every copy
    /// you make from it, so that option simply isn't offered there — the useful
    /// response to a hostile page is keeping your original, not disabling Chrome.
    /// </summary>
    private void ConfigureHighRiskActions()
    {
        // Nothing to restore if the full text has aged out of memory.
        LargeUseTheirs.IsEnabled = _protection.CanRestore(_event);
        if (!LargeUseTheirs.IsEnabled)
            LargeUseTheirs.ToolTip = "The blocked content is no longer available to restore";

        if (ProtectionService.IsBrowser(_event.ProcessName))
        {
            LargeBlockApp.Visibility = Visibility.Collapsed;
            return;
        }

        LargeBlockApp.Content = $"Always block {_event.ProcessName}";
    }

    private void ShowWarning(string title, string body)
    {
        WarnCard.Visibility = Visibility.Visible;
        WarnTitle.Text = title;
        WarnBody.Text = body;
        WarnContent.Text = _event.ContentPreview;

        if (_event.Defused)
        {
            // The command is already off the clipboard, so there's nothing left
            // to clear. The only open question is whether they actually wanted it.
            WarnContentLabel.Text = "REMOVED FROM YOUR CLIPBOARD";
            WarnPrimary.Content = "Got it";
            WarnSecondary.Content = "I need that command";
            WarnSecondary.IsEnabled = _protection.CanRestore(_event);

            if (!WarnSecondary.IsEnabled)
                WarnSecondary.ToolTip = "The original is no longer available to restore";
        }
        else
        {
            WarnPrimary.Content = "Clear clipboard";
            WarnSecondary.Content = "I copied it on purpose";
        }

        // Sticky: this is the one the user must actually read.
    }

    private void OnWarnPrimary(object sender, RoutedEventArgs e)
    {
        // Defused: nothing left to clear, so this is just an acknowledgement.
        if (!_event.Defused) _protection.ClearClipboardNow();
        Dismiss();
    }

    private void OnWarnSecondary(object sender, RoutedEventArgs e)
    {
        // Either way this is the user telling us we were wrong — the only
        // labelled false positive we ever get. Record it before acting.
        _protection.RecordOverride(_event);

        // Defused: hand the real command back. Otherwise they meant to copy it,
        // and it's still on the clipboard — nothing to do but dismiss.
        if (_event.Defused) _protection.RestoreWithheldWrite(_event);
        Dismiss();
    }

    private string BuildHighRiskBody()
    {
        var what = _event.Category == EventCategory.CommandInjection
            ? "a command that downloads and runs a script"
            : "a different wallet address";

        // The process name is a separate mono run in the XAML.
        return $"replaced your copied text with {what}.";
    }

    private string BuildContentWarningBody()
    {
        var lure = RiskAnalyzer.IsVerificationLure(_event.ContentPreview);

        var what = lure
            ? $"{_event.ProcessName} put a command on your clipboard dressed up as a CAPTCHA "
            + "or verification step. Real verification never asks you to paste anything into Windows."
            : $"{(ProtectionService.IsBrowser(_event.ProcessName) ? $"A page in {_event.ProcessName}" : _event.ProcessName)} "
            + "put a command on your clipboard that downloads and runs code from the internet.";

        // Say plainly that the danger is already gone — that's the reassurance
        // worth leading with once it's been defused.
        var outcome = _event.Defused
            ? " I've taken it off your clipboard and left something harmless in its place, so "
            + "pasting it now does nothing."
            : " If you didn't copy this yourself, don't paste it anywhere.";

        return what + outcome;
    }

    private string BuildPasteWarningBody(string? targetApp)
    {
        var target = string.IsNullOrWhiteSpace(targetApp) ? "a terminal" : targetApp;

        return $"You're about to paste into {target}, and there's a download-and-run command "
             + "on your clipboard. If a website told you to do this to prove you're human, "
             + "it's an attack — clear it instead.";
    }

    private DispatcherTimer StartDismissTimer(TimeSpan lifetime)
    {
        var timer = new DispatcherTimer { Interval = lifetime };
        timer.Tick += (_, _) => Dismiss();
        timer.Start();
        return timer;
    }

    /// <summary>Drains the 4px bar left-to-right over the toast's lifetime.</summary>
    private void AnimateProgress(TimeSpan lifetime)
    {
        var animation = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = new Duration(lifetime)
        };
        ProgressScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, animation);
    }

    /// <summary>
    /// Keeps the medium card's content inside its 14px corners. WPF clips to a
    /// Border's rectangular bounds, never to its CornerRadius, so the flush
    /// progress bar needs an explicit rounded clip.
    /// </summary>
    private void OnMediumContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 13 = the 14px card radius less its 1px border.
        MediumContent.Clip = new System.Windows.Media.RectangleGeometry(
            new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 13, 13);
    }

    private void OnDismiss(object sender, RoutedEventArgs e) => Dismiss();

    /// <summary>The safe way out of both warnings.</summary>
    private void OnClearClipboard(object sender, RoutedEventArgs e)
    {
        _protection.ClearClipboardNow();
        Dismiss();
    }

    /// <summary>"Actually I need that" — hand back what we withheld.</summary>
    private void OnRestore(object sender, RoutedEventArgs e)
    {
        _protection.RecordOverride(_event);
        _protection.RestoreWithheldWrite(_event);
        Dismiss();
    }

    private void OnBlockApp(object sender, RoutedEventArgs e)
    {
        if (!RuleConfirmation.ConfirmBlock(_event.ProcessName)) return;

        _protection.SetRule(_event.ProcessName, RuleKind.Blocked);
        Dismiss();
    }

    private void Dismiss()
    {
        _dismissTimer?.Stop();
        Dismissed?.Invoke(this, EventArgs.Empty);
        Close();
    }
}
