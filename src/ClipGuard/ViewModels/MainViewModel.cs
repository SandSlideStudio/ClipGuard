using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using ClipGuard.Models;
using ClipGuard.Services;

namespace ClipGuard.ViewModels;

public sealed record TabDefinition(string Label, string Subtitle);

/// <summary>
/// Backs the main window: which tab is showing, the filtered history view, and
/// pass-through access to the protection service the views bind against.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private int _selectedTabIndex;
    private string _searchText = string.Empty;

    public MainViewModel(ProtectionService protection)
    {
        Protection = protection;
        FilteredEvents = new ObservableCollection<ClipboardEvent>(protection.Events);

        protection.Events.CollectionChanged += (_, _) => ApplyFilter();
        protection.Rules.CollectionChanged += (_, _) => Raise(nameof(HasRules));
        protection.TrustedSources.CollectionChanged += (_, _) => Raise(nameof(HasTrustedSources));
    }

    public ProtectionService Protection { get; }

    /// <summary>
    /// Rebuilt rather than fixed, because the History subtitle quotes the
    /// retention setting - hardcoding "7 days" there made the two disagree the
    /// moment anyone changed it.
    /// </summary>
    public IReadOnlyList<TabDefinition> Tabs => BuildTabs();

    private IReadOnlyList<TabDefinition> BuildTabs() =>
    [
        new("Dashboard", "What ClipGuard has been up to today"),
        new("History", $"Clipboard writes, kept for {Protection.Settings.RetentionText}"),
        new("App rules", "Who's always allowed, who's always blocked"),
        new("Settings", "Everything applies instantly. No save button, we're not monsters."),
        new("About", "The who and the why")
    ];

    /// <summary>Re-reads tab copy after a setting it quotes has changed.</summary>
    public void RefreshTabCopy()
    {
        Raise(nameof(Tabs));
        Raise(nameof(TabTitle));
        Raise(nameof(TabSubtitle));
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (_selectedTabIndex == value) return;
            _selectedTabIndex = value;
            Raise();
            Raise(nameof(TabTitle));
            Raise(nameof(TabSubtitle));
        }
    }

    public string TabTitle => Tabs[SelectedTabIndex].Label;
    public string TabSubtitle => Tabs[SelectedTabIndex].Subtitle;

    /// <summary>Latest events shown on the dashboard - the mock caps this at four.</summary>
    public IEnumerable<ClipboardEvent> LatestEvents => Protection.Events.Take(4);

    public bool HasEvents => Protection.Events.Count > 0;

    public bool HasRules => Protection.Rules.Count > 0;

    public bool HasTrustedSources => Protection.TrustedSources.Count > 0;

    public ObservableCollection<ClipboardEvent> FilteredEvents { get; }

    /// <summary>Drives the search placeholder's visibility.</summary>
    public bool HasSearchText => !string.IsNullOrEmpty(_searchText);

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value;
            Raise();
            Raise(nameof(HasSearchText));
            ApplyFilter();
        }
    }

    private void ApplyFilter()
    {
        var query = Protection.Events.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var needle = _searchText.Trim();
            query = query.Where(e =>
                e.ProcessName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || e.ContentPreview.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || (e.FriendlyName?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        FilteredEvents.Clear();
        foreach (var e in query) FilteredEvents.Add(e);

        Raise(nameof(LatestEvents));
        Raise(nameof(HasEvents));
        Raise(nameof(HasRules));
    }

    /// <summary>Called after events land so the dashboard's slice re-reads.</summary>
    public void RefreshDerived()
    {
        Raise(nameof(LatestEvents));
        ApplyFilter();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
