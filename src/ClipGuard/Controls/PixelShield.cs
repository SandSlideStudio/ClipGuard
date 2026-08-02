using System.Windows;
using System.Windows.Media;

namespace ClipGuard.Controls;

public partial class PixelShield : UserControl
{
    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(PixelShield),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xec, 0x30, 0x13))));

    /// <summary>Colour of the 2x2 notch — set this to whatever sits behind the mark.</summary>
    public static readonly DependencyProperty NotchFillProperty =
        DependencyProperty.Register(nameof(NotchFill), typeof(Brush), typeof(PixelShield),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xf3, 0xf2, 0xf2))));

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public Brush NotchFill
    {
        get => (Brush)GetValue(NotchFillProperty);
        set => SetValue(NotchFillProperty, value);
    }

    public PixelShield() => InitializeComponent();
}
