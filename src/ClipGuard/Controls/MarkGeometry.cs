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

    /// <summary>
    /// Small-size clip: two wires instead of three, and a fatter stroke.
    ///
    /// Below roughly 24px the three-wire clip's gaps fall under a pixel and the
    /// knockout closes into a grey smudge. Dropping the middle wire doubles the
    /// gap to 4.4, which still reads at 16px — the size this mark spends most of
    /// its life at, in the tray and on the one-line toast.
    /// </summary>
    public const string SimpleClipPath =
        "M9.8 8.4 V13.6 C9.8 15.9 14.2 15.9 14.2 13.6 V8.0";

    public const double SimpleClipStrokeWidth = 1.7;

    /// <summary>Below this, use the simplified clip.</summary>
    public const double SimplifyBelow = 26;

    /// <summary>Canvas the paths are authored against.</summary>
    public const double Extent = 24;

    /// <summary>The intercepted write. Animation only — never part of the static mark.</summary>
    public static readonly Rect ThreatRect = new(19.8, 10.4, 3.2, 3.2);
    public const double ThreatRadius = 0.5;

    private static Geometry? _shield;
    private static Geometry? _clip;
    private static Geometry? _simpleClip;

    public static Geometry Shield => _shield ??= Freeze(Geometry.Parse(ShieldPath));
    public static Geometry Clip => _clip ??= Freeze(Geometry.Parse(ClipPath));
    public static Geometry SimpleClip => _simpleClip ??= Freeze(Geometry.Parse(SimpleClipPath));

    /// <summary>
    /// The mark as one fillable shape, with the clip genuinely cut out so the
    /// hole is transparent and the icon works on light and dark shells alike.
    /// </summary>
    public static Geometry BuildSolidMark(bool simplified)
    {
        var clip = simplified ? SimpleClip : Clip;
        var width = simplified ? SimpleClipStrokeWidth : ClipStrokeWidth;

        var pen = new System.Windows.Media.Pen(Brushes.Black, width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        // Widening turns the stroked clip into a fillable outline, which is what
        // Combine needs in order to subtract it.
        var combined = Geometry.Combine(
            Shield, clip.GetWidenedPathGeometry(pen), GeometryCombineMode.Exclude, null);

        combined.Freeze();
        return combined;
    }

    private static Geometry Freeze(Geometry geometry)
    {
        geometry.Freeze();
        return geometry;
    }
}
