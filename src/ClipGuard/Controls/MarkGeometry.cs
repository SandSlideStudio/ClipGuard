using System.Windows;
using System.Windows.Media;

namespace ClipGuard.Controls;

/// <summary>
/// The ClipGuard mark — "Interception". A paperclip held inside a shield.
///
/// This is the single definition. The WPF control, the tray icon and
/// build/make-icon.ps1 all read from here (the script extracts the path strings
/// by regex), so the three can't drift apart the way three hand-drawn copies
/// would.
///
/// Everything is authored on a 24x24 grid and centred on x=12.
/// </summary>
public static class MarkGeometry
{
    /// <summary>Outer shield. Filled.</summary>
    public const string ShieldPath =
        "M12 3.6 L5.6 6.2 V12.2 C5.6 16.6 12 20.4 12 20.4 C12 20.4 18.4 16.6 18.4 12.2 V6.2 Z";

    /// <summary>
    /// The clip, knocked out of the shield. Stroked rather than filled, so it
    /// reads as a bent wire — a paperclip is one folded piece of metal.
    ///
    /// The three verticals sit 2.6 apart against a 1.15 stroke, leaving gaps
    /// slightly wider than the wire itself. That ratio is what makes it read as
    /// a clip rather than a solid glyph; tighten it and the knockout closes up
    /// into a blob well before tray size.
    /// </summary>
    public const string ClipPath =
        "M9.4 8.8 V13.9 C9.4 16.1 14.6 16.1 14.6 13.9 V8.5 C14.6 7.3 12.0 7.3 12.0 8.5 V13.4";

    public const double ClipStrokeWidth = 1.15;

    /// <summary>Canvas the paths are authored against.</summary>
    public const double Extent = 24;

    /// <summary>The intercepted write. Animation only — never part of the static mark.</summary>
    public static readonly Rect ThreatRect = new(19.8, 10.4, 3.2, 3.2);
    public const double ThreatRadius = 0.5;

    private static Geometry? _shield;
    private static Geometry? _clip;

    public static Geometry Shield => _shield ??= Freeze(Geometry.Parse(ShieldPath));
    public static Geometry Clip => _clip ??= Freeze(Geometry.Parse(ClipPath));

    private static Geometry Freeze(Geometry geometry)
    {
        geometry.Freeze();
        return geometry;
    }
}
