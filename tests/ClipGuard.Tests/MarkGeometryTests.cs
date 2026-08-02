using System.Windows.Media;
using ClipGuard.Controls;

namespace ClipGuard.Tests;

/// <summary>
/// The mark is defined once as path strings and consumed by the WPF control,
/// the tray icon and build/make-icon.ps1. A typo in either string is a runtime
/// crash rather than a compile error, so it gets checked here.
/// </summary>
public class MarkGeometryTests
{
    [Fact]
    public void Shield_and_clip_parse()
    {
        Assert.False(MarkGeometry.Shield.IsEmpty());
        Assert.False(MarkGeometry.Clip.IsEmpty());
    }

    [Fact]
    public void Mark_is_centred_on_the_canvas()
    {
        var bounds = MarkGeometry.Shield.Bounds;
        var centre = bounds.Left + bounds.Width / 2;

        Assert.Equal(MarkGeometry.Extent / 2, centre, precision: 1);
    }

    [Fact]
    public void Mark_fits_inside_the_canvas()
    {
        var bounds = MarkGeometry.Shield.Bounds;

        Assert.True(bounds.Left >= 0);
        Assert.True(bounds.Top >= 0);
        Assert.True(bounds.Right <= MarkGeometry.Extent);
        Assert.True(bounds.Bottom <= MarkGeometry.Extent);
    }

    [Fact]
    public void Clip_sits_inside_the_shield()
    {
        // A clip poking out of the shield would read as a mistake at any size.
        Assert.True(MarkGeometry.Shield.Bounds.Contains(MarkGeometry.Clip.Bounds));
    }

    [Fact]
    public void Subtracting_the_clip_leaves_a_shield_with_holes()
    {
        var combined = Combine();

        Assert.False(combined.IsEmpty());

        // Cutting the clip out must remove area, or the knockout isn't happening.
        Assert.True(GetArea(combined) < GetArea(MarkGeometry.Shield));
    }

    [Fact]
    public void Knockout_gaps_stay_wider_than_the_wire()
    {
        // What makes it read as a paperclip rather than a solid glyph. If the
        // stroke ever creeps past the spacing between the three verticals, the
        // holes close up and the mark turns into a blob well before tray size.
        const double verticalSpacing = 2.6;

        Assert.True(MarkGeometry.ClipStrokeWidth < verticalSpacing / 2,
            $"stroke {MarkGeometry.ClipStrokeWidth} leaves gaps narrower than the wire");
    }

    [Fact]
    public void Simple_clip_keeps_a_wider_gap_than_the_full_one()
    {
        // The whole reason it exists: at 16px the full clip's gaps fall under a
        // pixel. Two wires 4.4 apart survive where three at 2.6 don't.
        const double simpleSpacing = 4.4;

        Assert.True(MarkGeometry.SimpleClipStrokeWidth < simpleSpacing / 2);
    }

    [Fact]
    public void Simple_clip_also_sits_inside_the_shield() =>
        Assert.True(MarkGeometry.Shield.Bounds.Contains(MarkGeometry.SimpleClip.Bounds));

    [Fact]
    public void Both_marks_still_have_holes_cut_in_them()
    {
        foreach (var simplified in new[] { false, true })
        {
            var mark = MarkGeometry.BuildSolidMark(simplified);

            Assert.False(mark.IsEmpty());
            Assert.True(GetArea(mark) < GetArea(MarkGeometry.Shield),
                $"simplified={simplified} produced no knockout");
        }
    }

    [Fact]
    public void Threat_starts_outside_the_shield()
    {
        // It flies in from off to the right; overlapping at rest would put a red
        // block inside the static mark.
        Assert.True(MarkGeometry.ThreatRect.Left > MarkGeometry.Shield.Bounds.Right);
    }

    private static Geometry Combine()
    {
        var pen = new Pen(Brushes.Black, MarkGeometry.ClipStrokeWidth)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        return Geometry.Combine(
            MarkGeometry.Shield,
            MarkGeometry.Clip.GetWidenedPathGeometry(pen),
            GeometryCombineMode.Exclude,
            null);
    }

    private static double GetArea(Geometry geometry) =>
        geometry.GetFlattenedPathGeometry().GetArea();
}
