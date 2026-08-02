using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ClipGuard.Controls;

public partial class ClipGuardMark : UserControl
{
    private static readonly Duration Cycle = new(TimeSpan.FromSeconds(3));

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(ClipGuardMark),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x20, 0x1e, 0x1d))));

    /// <summary>
    /// Colour of the clip knocked out of the shield — set this to whatever sits
    /// behind the mark, so the clip reads as a hole rather than a drawn line.
    /// </summary>
    public static readonly DependencyProperty KnockoutProperty =
        DependencyProperty.Register(nameof(Knockout), typeof(Brush), typeof(ClipGuardMark),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xf3, 0xf2, 0xf2))));

    public static readonly DependencyProperty ThreatFillProperty =
        DependencyProperty.Register(nameof(ThreatFill), typeof(Brush), typeof(ClipGuardMark),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xec, 0x30, 0x13))));

    /// <summary>Plays the interception loop. For loading and waiting states.</summary>
    public static readonly DependencyProperty IsAnimatingProperty =
        DependencyProperty.Register(nameof(IsAnimating), typeof(bool), typeof(ClipGuardMark),
            new PropertyMetadata(false, OnIsAnimatingChanged));

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public Brush Knockout
    {
        get => (Brush)GetValue(KnockoutProperty);
        set => SetValue(KnockoutProperty, value);
    }

    public Brush ThreatFill
    {
        get => (Brush)GetValue(ThreatFillProperty);
        set => SetValue(ThreatFillProperty, value);
    }

    public bool IsAnimating
    {
        get => (bool)GetValue(IsAnimatingProperty);
        set => SetValue(IsAnimatingProperty, value);
    }

    public ClipGuardMark()
    {
        InitializeComponent();
        SizeChanged += (_, _) => PickClipForSize();
    }

    /// <summary>
    /// Swaps to the two-wire clip once the mark is small enough that the full
    /// one would close up. Driven by measured size rather than a flag, so every
    /// caller gets the legible version automatically.
    /// </summary>
    private void PickClipForSize()
    {
        var extent = Math.Min(ActualWidth, ActualHeight);
        if (extent <= 0) return;

        var simplify = extent < MarkGeometry.SimplifyBelow;

        FullClip.Visibility = simplify ? Visibility.Collapsed : Visibility.Visible;
        SimpleClip.Visibility = simplify ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void OnIsAnimatingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var mark = (ClipGuardMark)d;

        if ((bool)e.NewValue) mark.StartInterception();
        else mark.StopInterception();
    }

    /// <summary>
    /// The write flies in, the shield takes the hit and gives a little, and the
    /// write tumbles away. Timings match the logo draft.
    /// </summary>
    private void StartInterception()
    {
        var threatShift = new DoubleAnimationUsingKeyFrames { Duration = Cycle, RepeatBehavior = RepeatBehavior.Forever };
        threatShift.KeyFrames.Add(Frame(13, 0));
        threatShift.KeyFrames.Add(Frame(3.4, 0.22));
        threatShift.KeyFrames.Add(Frame(3.4, 0.34));
        threatShift.KeyFrames.Add(Frame(14, 0.48));
        threatShift.KeyFrames.Add(Frame(14, 1));

        var threatFade = new DoubleAnimationUsingKeyFrames { Duration = Cycle, RepeatBehavior = RepeatBehavior.Forever };
        threatFade.KeyFrames.Add(Frame(0, 0));
        threatFade.KeyFrames.Add(Frame(1, 0.20));
        threatFade.KeyFrames.Add(Frame(1, 0.34));
        threatFade.KeyFrames.Add(Frame(0, 0.48));
        threatFade.KeyFrames.Add(Frame(0, 1));

        var threatSpin = new DoubleAnimationUsingKeyFrames { Duration = Cycle, RepeatBehavior = RepeatBehavior.Forever };
        threatSpin.KeyFrames.Add(Frame(0, 0));
        threatSpin.KeyFrames.Add(Frame(0, 0.34));
        threatSpin.KeyFrames.Add(Frame(38, 0.48));
        threatSpin.KeyFrames.Add(Frame(38, 1));

        // The brace is small on purpose: a shield that recoils hard looks like
        // it lost.
        var brace = new DoubleAnimationUsingKeyFrames { Duration = Cycle, RepeatBehavior = RepeatBehavior.Forever };
        brace.KeyFrames.Add(Frame(0, 0));
        brace.KeyFrames.Add(Frame(0, 0.20));
        brace.KeyFrames.Add(Frame(-0.9, 0.28));
        brace.KeyFrames.Add(Frame(0, 0.44));
        brace.KeyFrames.Add(Frame(0, 1));

        ThreatShift.BeginAnimation(TranslateTransform.XProperty, threatShift);
        ThreatSpin.BeginAnimation(RotateTransform.AngleProperty, threatSpin);
        Threat.BeginAnimation(OpacityProperty, threatFade);
        ShieldShift.BeginAnimation(TranslateTransform.XProperty, brace);
    }

    private void StopInterception()
    {
        ThreatShift.BeginAnimation(TranslateTransform.XProperty, null);
        ThreatSpin.BeginAnimation(RotateTransform.AngleProperty, null);
        Threat.BeginAnimation(OpacityProperty, null);
        ShieldShift.BeginAnimation(TranslateTransform.XProperty, null);

        Threat.Opacity = 0;
        ShieldShift.X = 0;
    }

    private static EasingDoubleKeyFrame Frame(double value, double fraction) =>
        new(value, KeyTime.FromPercent(fraction))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
}
