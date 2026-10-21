using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using ClipGuard.Models;

namespace ClipGuard.Services;

public enum NotificationKind
{
    /// <summary>Informational pill, auto-dismisses. Allowed events.</summary>
    Allowed,
    /// <summary>A blocked write. Medium card, auto-dismisses with a progress bar.</summary>
    Blocked,
    /// <summary>High-risk swap. Large sticky card showing the before/after diff.</summary>
    HighRiskSwap,
    /// <summary>
    /// Dangerous content arrived without replacing anything. Not blocked - you
    /// may have copied it on purpose - but sticky, because the ClickFix attack
    /// looks exactly like this.
    /// </summary>
    ContentWarning,
    /// <summary>A terminal or the Run dialog took focus while a command sits on the clipboard.</summary>
    PasteWarning
}

public sealed class NotificationRequest
{
    public required NotificationKind Kind { get; init; }
    public required ClipboardEvent Event { get; init; }

    /// <summary>Paste warnings only: what is about to receive the paste.</summary>
    public string? TargetApp { get; init; }
}

/// <summary>
/// The brain. Turns raw clipboard changes into events, decides what to block,
/// restores originals, and owns protection state. Everything here runs on the
/// UI thread - clipboard APIs are STA-bound and the volumes are tiny.
/// </summary>
public sealed class ProtectionService : INotifyPropertyChanged, IDisposable
{
    /// <summary>A change landing this soon after the previous one smells like a swap.</summary>
    private static readonly TimeSpan SwapWindow = TimeSpan.FromSeconds(2.5);

    private const string EventsFile = "events.json";
    private const string RulesFile = "rules.json";
    private const string SettingsFile = "settings.json";
    private const string TrustedSourcesFile = "trusted-sources.json";

    private readonly ClipboardMonitor _monitor = new();
    private readonly ProcessResolver _resolver = new();
    private readonly DispatcherTimer _autoClearTimer;
    private readonly DispatcherTimer _pauseTimer;

    /// <summary>The content we believe the user actually meant to have.</summary>
    private string? _trustedContent;
    private DateTimeOffset _lastChangeAt = DateTimeOffset.MinValue;
    private ProcessIdentity _lastWriter = ProcessIdentity.Unknown;

    private DateTimeOffset? _pausedUntil;
    private int _eventCounter;

    /// <summary>
    /// Full text of content we withheld - blocked swaps and defused commands -
    /// keyed by event id, so "give it back" restores it verbatim. Held in memory
    /// only: this is content we just judged hostile, and it has no business
    /// being written to disk.
    /// </summary>
    private readonly Dictionary<string, string> _withheldContent = [];
    private readonly Queue<string> _withheldOrder = new();
    private const int MaxRememberedWithholds = 20;

    public ProtectionService()
    {
        Settings = Storage.Load(SettingsFile, () => new AppSettings());

        var storedRules = Storage.Load(RulesFile, () => new List<AppRule>());
        Rules = new ObservableCollection<AppRule>(storedRules.OrderBy(r => r.ProcessName));

        TrustedSources = new ObservableCollection<string>(
            Storage.Load(TrustedSourcesFile, () => new List<string>()));

        var storedEvents = Storage.Load(EventsFile, () => new List<ClipboardEvent>(), encrypted: true);
        Events = new ObservableCollection<ClipboardEvent>(
            storedEvents.Where(WithinRetention).OrderByDescending(e => e.Timestamp));

        _eventCounter = Events.Count;

        _autoClearTimer = new DispatcherTimer();
        _autoClearTimer.Tick += OnAutoClearElapsed;

        _pauseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pauseTimer.Tick += OnPauseTick;

        _monitor.ClipboardChanged += OnClipboardChanged;
    }

    // ── State exposed to the UI ───────────────────────────────────────────

    public AppSettings Settings { get; }

    /// <summary>
    /// Running from a location that won't survive, so protection works now but
    /// cannot be registered to start with Windows. Set once at startup.
    /// </summary>
    public bool IsTestMode { get; init; }
    public ObservableCollection<AppRule> Rules { get; }
    public ObservableCollection<ClipboardEvent> Events { get; }

    /// <summary>
    /// Download hosts the user has personally vouched for by overriding a
    /// warning. Visible and removable - unlike the internal ignore list, this
    /// one weakens protection, so it must never be invisible.
    /// </summary>
    public ObservableCollection<string> TrustedSources { get; }

    /// <summary>How often we flagged something the user then said was fine.</summary>
    public int FalsePositiveCount => Events.Count(e => e.UserOverrode);

    /// <summary>Everything we acted on: blocked, defused, or warned about.</summary>
    public int FlaggedCount => Events.Count(e => e.Blocked || e.Defused || e.Warned);

    public event EventHandler<NotificationRequest>? NotificationRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsPaused => _pausedUntil is not null && _pausedUntil > DateTimeOffset.Now;

    public string StatusTitle => !IsHealthy ? "Not protecting"
                               : IsPaused ? "Taking a nap"
                               : "Standing guard";

    /// <summary>Second line of the status chip. Names test mode, since it changes what happens tomorrow.</summary>
    public string StatusDetail => IsTestMode
        ? "Test mode · won't start with Windows"
        // UpdateService.Current, not the assembly version: that one cannot
        // carry a pre-release suffix, so a candidate build sat here calling
        // itself v1.0.0 while the update check knew better.
        : $"v{UpdateService.Current} · SandSlide";

    public string StatusSub => !IsHealthy
        ? "ClipGuard couldn't attach to the clipboard. Restart it: you are not protected."
        : IsPaused
            ? $"Protection paused, back on duty in {RemainingPauseText}"
            : $"Watching clipboard access from apps · {EventsToday} events today";

    public string PauseCta => IsPaused ? "Resume now" : "Pause 15 min";

    private string RemainingPauseText
    {
        get
        {
            if (_pausedUntil is null) return "a moment";
            var left = _pausedUntil.Value - DateTimeOffset.Now;
            var mins = Math.Max(0, (int)Math.Ceiling(left.TotalMinutes));
            return mins == 1 ? "1 minute" : $"{mins} minutes";
        }
    }

    public int EventsToday => Events.Count(e => e.Timestamp.Date == DateTimeOffset.Now.Date);
    public int BlockedToday => Events.Count(e => e.Blocked && e.Timestamp.Date == DateTimeOffset.Now.Date);
    public int AppsBlocked => Rules.Count(r => r.Rule == RuleKind.Blocked);

    // ── Lifecycle ─────────────────────────────────────────────────────────

    public void Start()
    {
        _monitor.Start();
        _trustedContent = ClipboardMonitor.ReadClipboardText();
        _lastChangeAt = DateTimeOffset.Now;
        RestartAutoClearTimer();
        RaiseStatusChanged();
    }

    /// <summary>
    /// False when the clipboard listener failed to attach, or the app faulted.
    /// Distinct from paused: paused is the user's choice, this is a failure.
    /// </summary>
    public bool IsHealthy => _monitor.IsListening && !_faulted;

    private bool _faulted;

    /// <summary>Called by the crash handler so the UI stops claiming to protect.</summary>
    public void MarkFaulted()
    {
        _faulted = true;
        RaiseStatusChanged();
    }

    public void TogglePause()
    {
        if (IsPaused)
        {
            _pausedUntil = null;
            _pauseTimer.Stop();
        }
        else
        {
            _pausedUntil = DateTimeOffset.Now.AddMinutes(15);
            _pauseTimer.Start();
        }
        RaiseStatusChanged();
    }

    private void OnPauseTick(object? sender, EventArgs e)
    {
        if (IsPaused)
        {
            // Keep the countdown copy fresh while paused.
            Raise(nameof(StatusSub));
            return;
        }

        _pausedUntil = null;
        _pauseTimer.Stop();
        RaiseStatusChanged();
    }

    public void ClearClipboardNow()
    {
        _monitor.WriteClipboardText(null);
        _trustedContent = null;
        _autoClearTimer.Stop();
    }

    // ── Detection ─────────────────────────────────────────────────────────

    private void OnClipboardChanged(object? sender, ClipboardChangedEventArgs e)
    {
        var now = DateTimeOffset.Now;
        var writer = _resolver.Resolve(e.OwnerWindow);
        var previous = _trustedContent;

        var rapid = now - _lastChangeAt < SwapWindow;
        var foregroundPid = Win32.GetProcessIdForWindow(e.ForegroundWindow);
        var ownerPid = Win32.GetProcessIdForWindow(e.OwnerWindow);

        // Our own writes - restores, defused stand-ins, Clear clipboard - are
        // never events. ClipboardMonitor already swallows the echo by content;
        // this catches the cases it can't, like an echo arriving late or the
        // content not matching what we wrote.
        if (IsIgnoredWriter(writer, ownerPid))
        {
            // Still a real state change, so keep the baseline current for the
            // next write. _lastWriter is deliberately left alone: paste warnings
            // name it, and "ClipGuard put this here" would be a lie.
            _trustedContent = e.Text;
            _lastChangeAt = now;
            return;
        }

        // A write from a process that isn't the one the user is looking at is
        // the strongest signal available without a keyboard hook.
        var background = ownerPid != 0 && foregroundPid != 0 && ownerPid != foregroundPid;

        // The decision itself is a pure function - see DetectionPolicy. This
        // method's job is gathering the inputs and applying the consequences.
        var outcome = DetectionPolicy.Evaluate(new DetectionInputs
        {
            Text = e.Text,
            Previous = previous,
            WriterName = writer.Name,
            ArrivedRapidly = rapid,
            FromBackgroundApp = background,
            Rule = FindRule(writer.Name)?.Rule,
            IsPaused = IsPaused,
            WarnRiskyContentEnabled = Settings.WarnRiskyContent,
            DefuseEnabled = Settings.DefuseRiskyCommands,
            TrustedSources = TrustedSources
        });

        var shouldBlock = outcome.Block;

        if (shouldBlock)
        {
            // We can't stop the write landing, but we can put the original back
            // before anything gets pasted. See README for why this is "blocked".
            _monitor.WriteClipboardText(previous);
        }
        else
        {
            _trustedContent = e.Text;
        }

        _lastChangeAt = now;
        _lastWriter = writer;

        var warn = outcome.Warn;
        var defuse = outcome.Defuse;

        if (defuse)
        {
            // Shaped like the original so a victim mid-attack sees a plausible
            // command rather than an obvious interception, and doesn't simply
            // copy the real one again.
            var standIn = RiskAnalyzer.DefusedReplacementFor(e.Text);
            _monitor.WriteClipboardText(standIn);
            _trustedContent = standIn;
        }

        var record = new ClipboardEvent
        {
            Id = NextEventId(),
            ProcessName = writer.Name,
            ProcessPath = writer.Path,
            FriendlyName = writer.FriendlyName,
            Action = ClipboardAction.Write,
            Category = outcome.Category,
            // Blank rows read as broken in History; say what actually happened.
            ContentPreview = string.IsNullOrEmpty(e.Text)
                ? "(image or file, not text)"
                : RiskAnalyzer.Preview(e.Text),
            ReplacedPreview = shouldBlock ? RiskAnalyzer.Preview(previous) : null,
            Blocked = shouldBlock,
            Warned = warn,
            Defused = defuse,
            HighRisk = outcome.HighRisk,
            Timestamp = now
        };

        // Keep the original for either kind of withholding, so "give it back"
        // works whether we blocked a swap or defused a command.
        if ((shouldBlock || defuse) && !string.IsNullOrEmpty(e.Text))
            RememberWithheldContent(record.Id, e.Text);

        RecordEvent(record);
        TouchRule(writer.Name);
        RestartAutoClearTimer();

        if (Settings.ShowNotifications && !IsPaused)
        {
            NotificationRequested?.Invoke(this, new NotificationRequest
            {
                Kind = outcome.HighRiskSwap ? NotificationKind.HighRiskSwap
                     : shouldBlock ? NotificationKind.Blocked
                     : warn ? NotificationKind.ContentWarning
                     : NotificationKind.Allowed,
                Event = record
            });
        }
    }

    /// <summary>
    /// Whether newly-arrived content is worth warning about on its own merits.
    /// Shell commands always are. Wallet addresses only when a browser put them
    /// there - you copying an address from your own wallet is routine, a web
    /// page placing one on your clipboard is not.
    /// </summary>
    /// <summary>
    /// Writers whose clipboard activity is never surfaced - no notification, no
    /// history entry. Hardcoded and deliberately not user-editable: this is
    /// plumbing, not a rule someone should be able to add their own entries to.
    /// </summary>
    private static readonly string[] IgnoredProcesses = ["ClipGuard.exe"];

    private static readonly string? OwnExecutablePath =
        System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;

    /// <summary>
    /// True when a write came from ClipGuard itself.
    ///
    /// Identity is checked by process id, which nothing can spoof. The name list
    /// is only a fallback for when the owner window won't resolve to a pid, and
    /// even then the image path has to match ours - otherwise malware could
    /// rename itself to ClipGuard.exe and buy permanent silence.
    /// </summary>
    private static bool IsIgnoredWriter(ProcessIdentity writer, uint ownerPid)
    {
        if (ownerPid != 0 && ownerPid == (uint)Environment.ProcessId) return true;

        if (!IgnoredProcesses.Contains(writer.Name, StringComparer.OrdinalIgnoreCase))
            return false;

        return OwnExecutablePath is not null
               && string.Equals(writer.Path, OwnExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Kept here because the notification views already call it.</summary>
    public static bool IsBrowser(string processName) => DetectionPolicy.IsBrowser(processName);

    /// <summary>Current clipboard text, for the paste guard's pre-paste check.</summary>
    public string? CurrentClipboardText => _trustedContent;

    /// <summary>The app that last wrote to the clipboard.</summary>
    public string LastWriterName => _lastWriter.Name;

    /// <summary>Raises a paste warning. Called by the paste guard, not by monitoring.</summary>
    public void RaisePasteWarning(string targetApp, string clipboardText)
    {
        if (IsPaused) return;

        var record = new ClipboardEvent
        {
            Id = NextEventId(),
            ProcessName = _lastWriter.Name,
            Action = ClipboardAction.Write,
            Category = EventCategory.SuspiciousContent,
            ContentPreview = RiskAnalyzer.Preview(clipboardText),
            PasteTarget = targetApp,
            Warned = true,
            HighRisk = true,
            Timestamp = DateTimeOffset.Now
        };

        // Logged even when notifications are off - a near-miss on a terminal
        // paste is exactly the thing you want to find in the history later.
        RecordEvent(record);

        if (!Settings.ShowNotifications) return;

        NotificationRequested?.Invoke(this, new NotificationRequest
        {
            Kind = NotificationKind.PasteWarning,
            TargetApp = targetApp,
            Event = record
        });
    }

    /// <summary>
    /// Persists the event log, always encrypted, and stripped of clipboard
    /// content entirely when the user has turned that off.
    /// </summary>
    private void SaveEvents()
    {
        var payload = Settings.RememberCopiedContent
            ? Events.ToList()
            : Events.Select(e => e.WithoutContent()).ToList();

        Storage.Save(EventsFile, payload, encrypted: true);
    }

    private void RememberWithheldContent(string eventId, string text)
    {
        _withheldContent[eventId] = text;
        _withheldOrder.Enqueue(eventId);

        while (_withheldOrder.Count > MaxRememberedWithholds)
        {
            _withheldContent.Remove(_withheldOrder.Dequeue());
        }
    }

    /// <summary>Whether the full withheld text is still available to restore.</summary>
    public bool CanRestore(ClipboardEvent record) =>
        (record.Blocked || record.Defused) && _withheldContent.ContainsKey(record.Id);

    /// <summary>
    /// The user said we got it wrong. This is the only labelled data we have:
    /// every other signal is inference, but this is someone telling us directly.
    /// Record it, and learn the download host so we stop asking about it.
    /// </summary>
    public void RecordOverride(ClipboardEvent record)
    {
        if (record.UserOverrode) return;

        record.UserOverrode = true;
        SaveEvents();

        // Learn from the full original, not the truncated preview.
        if (_withheldContent.TryGetValue(record.Id, out var full))
            TrustSourcesIn(full);

        Raise(nameof(FalsePositiveCount));
    }

    private void TrustSourcesIn(string commandText)
    {
        // A lure is malicious regardless of who hosts it - never learn from one.
        if (RiskAnalyzer.IsVerificationLure(commandText)) return;

        var added = false;

        foreach (var host in RiskAnalyzer.ExtractHosts(commandText))
        {
            if (TrustedSources.Contains(host, StringComparer.OrdinalIgnoreCase)) continue;
            TrustedSources.Add(host);
            added = true;
        }

        if (added) Storage.Save(TrustedSourcesFile, TrustedSources.ToList());
    }

    public void RemoveTrustedSource(string host)
    {
        if (!TrustedSources.Remove(host)) return;
        Storage.Save(TrustedSourcesFile, TrustedSources.ToList());
    }

    private string NextEventId() => $"CG-{DateTime.Now:yyyy-MMdd}-{++_eventCounter:D4}";

    private void RecordEvent(ClipboardEvent record)
    {
        Events.Insert(0, record);
        PruneRetention();
        SaveEvents();
        RaiseStatsChanged();
    }

    private bool WithinRetention(ClipboardEvent e) =>
        DateTimeOffset.Now - e.Timestamp <= Settings.RetentionWindow;

    /// <summary>Applies a shortened retention window immediately.</summary>
    public void ApplyRetentionNow()
    {
        PruneRetention();
        SaveEvents();
        RaiseStatsChanged();
    }

    private void PruneRetention()
    {
        for (var i = Events.Count - 1; i >= 0; i--)
        {
            if (!WithinRetention(Events[i])) Events.RemoveAt(i);
        }
    }

    // ── Rules ─────────────────────────────────────────────────────────────

    public AppRule? FindRule(string processName) =>
        Rules.FirstOrDefault(r => string.Equals(r.ProcessName, processName, StringComparison.OrdinalIgnoreCase));

    public void SetRule(string processName, RuleKind kind)
    {
        var existing = FindRule(processName);
        if (existing is not null)
        {
            existing.Rule = kind;
            existing.LastEvent = DateTimeOffset.Now;
            // Reset the collection view so the tag re-renders.
            var index = Rules.IndexOf(existing);
            Rules[index] = existing;
        }
        else
        {
            Rules.Add(new AppRule
            {
                ProcessName = processName,
                Rule = kind,
                LastEvent = DateTimeOffset.Now
            });
        }

        SaveRules();
        RaiseStatsChanged();
    }

    public void RemoveRule(AppRule rule)
    {
        Rules.Remove(rule);
        SaveRules();
        RaiseStatsChanged();
    }

    private void TouchRule(string processName)
    {
        var rule = FindRule(processName);
        if (rule is null) return;
        rule.LastEvent = DateTimeOffset.Now;
        SaveRules();
    }

    private void SaveRules() => Storage.Save(RulesFile, Rules.ToList());

    /// <summary>
    /// Hand back content we withheld - a blocked swap or a defused command.
    /// Uses the full text held in memory: ContentPreview is truncated for
    /// display, and restoring that would return mangled content.
    /// </summary>
    public void RestoreWithheldWrite(ClipboardEvent record)
    {
        if (!record.Blocked && !record.Defused) return;

        if (!_withheldContent.TryGetValue(record.Id, out var full) || string.IsNullOrEmpty(full))
            return;

        _monitor.WriteClipboardText(full);
        _trustedContent = full;
    }

    // ── Settings ──────────────────────────────────────────────────────────

    public void SaveSettings()
    {
        Storage.Save(SettingsFile, Settings);
        RestartAutoClearTimer();
    }

    private void RestartAutoClearTimer()
    {
        _autoClearTimer.Stop();

        var delay = Settings.AutoClearDelay;
        if (delay is null) return;

        _autoClearTimer.Interval = delay.Value;
        _autoClearTimer.Start();
    }

    private void OnAutoClearElapsed(object? sender, EventArgs e)
    {
        _autoClearTimer.Stop();
        ClearClipboardNow();
    }

    // ── Change notification ───────────────────────────────────────────────

    private void RaiseStatusChanged()
    {
        Raise(nameof(IsHealthy));
        Raise(nameof(IsPaused));
        Raise(nameof(StatusTitle));
        Raise(nameof(StatusSub));
        Raise(nameof(PauseCta));
    }

    private void RaiseStatsChanged()
    {
        Raise(nameof(EventsToday));
        Raise(nameof(BlockedToday));
        Raise(nameof(AppsBlocked));
        Raise(nameof(StatusSub));
    }

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        _monitor.ClipboardChanged -= OnClipboardChanged;
        _monitor.Dispose();
        _autoClearTimer.Stop();
        _pauseTimer.Stop();
    }
}
