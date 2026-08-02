using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace ClipGuard.Controls;

/// <summary>
/// Letter-spacing for TextBlock. WPF has no tracking property, but the mocks
/// lean on it for every small-caps label (.12em) and the wizard step line
/// (.14em), so it's not optional for matching the design.
///
/// Implemented by rebuilding the text as alternating character/spacer runs,
/// where each spacer is a space scaled to the requested em width. A space is
/// roughly 0.25em in Archivo, so a spacer at (tracking * 4) of the font size
/// lands on the intended gap.
/// </summary>
public static class Typography
{
    private const double SpaceWidthInEm = 0.25;

    /// <summary>Tracking in em, matching the CSS letter-spacing values.</summary>
    public static readonly DependencyProperty TrackingProperty =
        DependencyProperty.RegisterAttached(
            "Tracking", typeof(double), typeof(Typography),
            new PropertyMetadata(0.0, OnTrackingChanged));

    public static double GetTracking(DependencyObject d) => (double)d.GetValue(TrackingProperty);
    public static void SetTracking(DependencyObject d, double value) => d.SetValue(TrackingProperty, value);

    /// <summary>Guards the Inlines rewrite from re-entering via the Text change it causes.</summary>
    private static readonly DependencyProperty IsApplyingProperty =
        DependencyProperty.RegisterAttached(
            "IsApplying", typeof(bool), typeof(Typography), new PropertyMetadata(false));

    private static void OnTrackingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block) return;

        var descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));

        if ((double)e.NewValue == 0)
        {
            descriptor.RemoveValueChanged(block, OnTextChanged);
            return;
        }

        descriptor.AddValueChanged(block, OnTextChanged);
        Apply(block);
    }

    private static void OnTextChanged(object? sender, EventArgs e)
    {
        if (sender is TextBlock block) Apply(block);
    }

    private static void Apply(TextBlock block)
    {
        if ((bool)block.GetValue(IsApplyingProperty)) return;

        var text = block.Text;
        var tracking = GetTracking(block);
        if (string.IsNullOrEmpty(text) || tracking <= 0) return;

        var spacerSize = Math.Max(0.5, block.FontSize * tracking / SpaceWidthInEm);

        block.SetValue(IsApplyingProperty, true);
        try
        {
            block.Inlines.Clear();

            for (var i = 0; i < text.Length; i++)
            {
                block.Inlines.Add(new Run(text[i].ToString()));

                // No trailing spacer - it would offset centred layouts.
                if (i < text.Length - 1)
                    block.Inlines.Add(new Run(" ") { FontSize = spacerSize });
            }
        }
        finally
        {
            block.SetValue(IsApplyingProperty, false);
        }
    }
}
