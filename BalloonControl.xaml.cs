using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace KeepyUppy;

public partial class BalloonControl : UserControl
{
    public static readonly DependencyProperty BalloonColorProperty = DependencyProperty.Register(
        nameof(BalloonColor),
        typeof(Color),
        typeof(BalloonControl),
        new PropertyMetadata(Color.FromRgb(78, 180, 224), OnColorChanged));

    public BalloonControl()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyColor(BalloonColor);
    }

    public Color BalloonColor
    {
        get => (Color)GetValue(BalloonColorProperty);
        set => SetValue(BalloonColorProperty, value);
    }

    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BalloonControl)d).ApplyColor((Color)e.NewValue);
    }

    private void ApplyColor(Color color)
    {
        if (Body == null || Knot == null || StringPath == null)
            return;

        Body.Fill = new SolidColorBrush(color);
        Knot.Fill = new SolidColorBrush(Darken(color, 0.72));
        StringPath.Stroke = new SolidColorBrush(Darken(color, 0.58));
    }

    private static Color Darken(Color color, double factor) => Color.FromRgb(
        (byte)(color.R * factor),
        (byte)(color.G * factor),
        (byte)(color.B * factor));
}
