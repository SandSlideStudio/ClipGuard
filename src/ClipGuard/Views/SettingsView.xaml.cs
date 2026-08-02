using System.Windows;
using ClipGuard.Models;
using ClipGuard.Services;
using ClipGuard.ViewModels;

namespace ClipGuard.Views;

public partial class SettingsView : UserControl
{
    private bool _loading;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => LoadSegmentedState();
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>
    /// The segmented control is a radio group, so its state is pushed in rather
    /// than bound — binding three radios to one enum needs a converter per option.
    /// </summary>
    private void LoadSegmentedState()
    {
        if (Vm is null) return;

        _loading = true;

        switch (Vm.Protection.Settings.AutoClear)
        {
            case AutoClearMode.Never: ClearNever.IsChecked = true; break;
            case AutoClearMode.Minutes5: Clear5Min.IsChecked = true; break;
            default: Clear60.IsChecked = true; break;
        }

        switch (Vm.Protection.Settings.Retention)
        {
            case RetentionMode.Hours24: Keep24h.IsChecked = true; break;
            case RetentionMode.Days30: Keep30d.IsChecked = true; break;
            default: Keep7d.IsChecked = true; break;
        }

        _loading = false;
    }

    private void OnRetentionChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || Vm is null) return;

        Vm.Protection.Settings.Retention =
            Keep24h.IsChecked == true ? RetentionMode.Hours24 :
            Keep30d.IsChecked == true ? RetentionMode.Days30 :
            RetentionMode.Days7;

        Vm.Protection.SaveSettings();

        // Shortening retention should take effect now, not at the next event —
        // and the History tab's subtitle quotes this value.
        Vm.Protection.ApplyRetentionNow();
        Vm.RefreshTabCopy();
    }

    private void OnAutoClearChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || Vm is null) return;

        Vm.Protection.Settings.AutoClear =
            ClearNever.IsChecked == true ? AutoClearMode.Never :
            Clear5Min.IsChecked == true ? AutoClearMode.Minutes5 :
            AutoClearMode.Seconds60;

        Vm.Protection.SaveSettings();
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var settings = Vm.Protection.Settings;
        StartupService.Apply(settings.StartWithWindows, settings.LaunchMinimized);
        Vm.Protection.SaveSettings();
    }
}
