using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ClipGuard.Models;

namespace ClipGuard.Views;

/// <summary>Visible when the bound index equals the ConverterParameter.</summary>
public sealed class IndexToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var index = value as int? ?? -1;
        var target = int.TryParse(parameter?.ToString(), out var p) ? p : -1;
        return index == target ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>Set true to show when the value is false.</summary>
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value as bool? ?? false;
        if (Invert) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>Protection state → the red/grey status dot used everywhere.</summary>
public sealed class PausedToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Active = new(Color.FromRgb(0xec, 0x30, 0x13));
    private static readonly SolidColorBrush Paused = new(Color.FromRgb(0xba, 0xb6, 0xb6));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value as bool? == true ? Paused : Active;

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>
/// Health → status dot. An unhealthy app reads hollow rather than solid, so
/// "not protecting" can't be mistaken for the normal on state at a glance.
/// </summary>
public sealed class HealthToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Healthy = new(Color.FromRgb(0xec, 0x30, 0x13));
    private static readonly SolidColorBrush Unhealthy = new(Color.FromRgb(0x7d, 0x79, 0x79));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value as bool? == false ? Unhealthy : Healthy;

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>Non-empty string → Visible. Used for the optional swap diff rows.</summary>
public sealed class NotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>Rule kind → tag fill/ink. Blocked reads red, allow reads neutral.</summary>
public sealed class RuleToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush AccentFill = new(Color.FromRgb(0xff, 0xf2, 0xef));
    private static readonly SolidColorBrush AccentInk = new(Color.FromRgb(0x7c, 0x14, 0x05));
    private static readonly SolidColorBrush NeutralFill = new(Color.FromRgb(0xf8, 0xf4, 0xf4));
    private static readonly SolidColorBrush NeutralInk = new(Color.FromRgb(0x44, 0x41, 0x41));

    /// <summary>Pass "ink" for the text colour; anything else yields the fill.</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var blocked = value is RuleKind.Blocked;
        var wantInk = string.Equals(parameter?.ToString(), "ink", StringComparison.OrdinalIgnoreCase);

        return (blocked, wantInk) switch
        {
            (true, true) => AccentInk,
            (true, false) => AccentFill,
            (false, true) => NeutralInk,
            (false, false) => NeutralFill
        };
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}
