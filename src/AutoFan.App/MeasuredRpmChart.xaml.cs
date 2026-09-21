using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AutoFan.Core;

namespace AutoFan.App;

public partial class MeasuredRpmChart : UserControl
{
    private const double LeftPad = 76;
    private const double RightPad = 16;
    private const double TopPad = 40;
    private const double BottomPad = 44;
    private const double DotSize = 12;

    public static readonly DependencyProperty PlotProperty = DependencyProperty.Register(
        nameof(Plot),
        typeof(MeasuredRpmPlot),
        typeof(MeasuredRpmChart),
        new PropertyMetadata(null, OnPlotChanged));

    public MeasuredRpmChart()
    {
        InitializeComponent();
        Loaded += (_, _) => Redraw();
        SizeChanged += (_, _) => Redraw();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Dispatcher.BeginInvoke(Redraw, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        };
    }

    public MeasuredRpmPlot? Plot
    {
        get => (MeasuredRpmPlot?)GetValue(PlotProperty);
        set => SetValue(PlotProperty, value);
    }

    private static void OnPlotChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((MeasuredRpmChart)sender).Redraw();

    private void Redraw()
    {
        if (PlotCanvas is null || EmptyLabel is null || TitleLabel is null)
        {
            return;
        }

        PlotCanvas.Children.Clear();
        MeasuredRpmPlot? plot = Plot;
        if (plot is null || !plot.HasPoints)
        {
            ShowEmpty(plot?.EmptyReason ?? MeasuredRpmPlot.NeedFanTestsReason);
            return;
        }

        double width = Math.Max(PlotCanvas.ActualWidth, ActualWidth - 40);
        double height = Math.Max(PlotCanvas.ActualHeight, 8);
        if (width < LeftPad + RightPad + 8 || height < TopPad + BottomPad + 8)
        {
            ShowChrome(plot);
            EmptyLabel.Visibility = Visibility.Collapsed;
            return;
        }

        ShowChrome(plot);
        EmptyLabel.Visibility = Visibility.Collapsed;
        double plotLeft = LeftPad;
        double plotRight = Math.Max(plotLeft + 8, width - RightPad);
        double plotTop = TopPad;
        double plotBottom = Math.Max(plotTop + 8, height - BottomPad);
        Brush muted = (Brush)FindResource("Brush.Muted");
        Brush accent = (Brush)FindResource("Brush.Accent");
        Brush ok = (Brush)FindResource("Brush.Ok");
        Brush text = (Brush)FindResource("Brush.Text");
        Brush window = (Brush)FindResource("Brush.Window");
        var grid = new SolidColorBrush(((SolidColorBrush)muted).Color) { Opacity = 0.35 };
        var bandFill = new SolidColorBrush(((SolidColorBrush)ok).Color) { Opacity = 0.12 };
        double? recommendedX = plot.RecommendedRpm is double recommended
            ? MapX(recommended, plot.RpmMin, plot.RpmMax, plotLeft, plotRight)
            : null;
        if (recommendedX is double bandLeft)
        {
            var band = new Rectangle
            {
                Width = Math.Max(0, plotRight - bandLeft),
                Height = Math.Max(0, plotBottom - plotTop),
                Fill = bandFill,
            };
            Canvas.SetLeft(band, bandLeft);
            Canvas.SetTop(band, plotTop);
            PlotCanvas.Children.Add(band);
        }

        DrawGrid(plot, plotLeft, plotRight, plotTop, plotBottom, grid, muted);
        DrawAxis(plotLeft, plotRight, plotTop, plotBottom, muted);
        DrawAxisTitles(plotLeft, plotRight, plotTop, plotBottom, muted);
        if (recommendedX is double lineX)
        {
            PlotCanvas.Children.Add(new Line
            {
                X1 = lineX,
                X2 = lineX,
                Y1 = plotTop,
                Y2 = plotBottom,
                Stroke = ok,
                StrokeThickness = 1.5,
                StrokeDashArray = [4, 3],
            });
            double labelX = lineX + 8;
            if (labelX + 180 > plotRight)
            {
                labelX = Math.Max(plotLeft + 4, lineX - 188);
            }

            AddLabel(labelX, plotTop + 4, plot.RecommendedLabel, ok, 12, FontWeights.SemiBold);
            AddLabel(labelX, plotTop + 22, MeasuredRpmPlot.PlateauCallout, ok, 11, FontWeights.Normal);
        }

        var geometry = new PointCollection();
        foreach (FanSpeedPoint point in plot.Points)
        {
            geometry.Add(new Point(
                MapX(point.Rpm, plot.RpmMin, plot.RpmMax, plotLeft, plotRight),
                MapY(point.TempCelsius, plot.TempMin, plot.TempMax, plotTop, plotBottom)));
        }

        PlotCanvas.Children.Add(new Polyline
        {
            Points = geometry,
            Stroke = accent,
            StrokeThickness = 2.5,
            Fill = Brushes.Transparent,
        });

        foreach (FanSpeedPoint point in plot.Points)
        {
            bool plateau = plot.IsOnPlateau(point);
            double x = MapX(point.Rpm, plot.RpmMin, plot.RpmMax, plotLeft, plotRight);
            double y = MapY(point.TempCelsius, plot.TempMin, plot.TempMax, plotTop, plotBottom);
            var dot = new Ellipse
            {
                Width = DotSize,
                Height = DotSize,
                Fill = plateau ? ok : accent,
                Stroke = window,
                StrokeThickness = 2,
            };
            Canvas.SetLeft(dot, x - (DotSize / 2));
            Canvas.SetTop(dot, y - (DotSize / 2));
            PlotCanvas.Children.Add(dot);
            double labelY = plateau ? y + 10 : y - 22;
            if (labelY < plotTop)
            {
                labelY = y + 10;
            }

            if (labelY + 16 > plotBottom)
            {
                labelY = y - 22;
            }

            AddLabel(x - 22, labelY, $"{point.TempCelsius:0.0} °C", plateau ? ok : text, 11, FontWeights.SemiBold);
        }
    }

    private void ShowEmpty(string reason)
    {
        TitleLabel.Text = Plot?.Title ?? "Measured RPM vs temperature";
        TitleLabel.Visibility = Visibility.Visible;
        HowToLabel.Visibility = Visibility.Collapsed;
        LegendPanel.Visibility = Visibility.Collapsed;
        CaptionLabel.Visibility = Visibility.Collapsed;
        EmptyLabel.Text = reason;
        EmptyLabel.Visibility = Visibility.Visible;
    }

    private void ShowChrome(MeasuredRpmPlot plot)
    {
        TitleLabel.Text = plot.Title;
        HowToLabel.Text = plot.HowTo;
        CaptionLabel.Text = plot.Caption;
        TitleLabel.Visibility = Visibility.Visible;
        HowToLabel.Visibility = Visibility.Visible;
        LegendPanel.Visibility = Visibility.Visible;
        CaptionLabel.Visibility = Visibility.Visible;
    }

    private void DrawGrid(
        MeasuredRpmPlot plot,
        double left,
        double right,
        double top,
        double bottom,
        Brush grid,
        Brush muted)
    {
        foreach (double temp in Ticks(plot.TempMin, plot.TempMax, 5))
        {
            double y = MapY(temp, plot.TempMin, plot.TempMax, top, bottom);
            PlotCanvas.Children.Add(new Line
            {
                X1 = left,
                X2 = right,
                Y1 = y,
                Y2 = y,
                Stroke = grid,
                StrokeThickness = 1,
            });
            AddLabel(22, y - 8, $"{temp:0} °C", muted, 11, FontWeights.Normal);
        }

        foreach (double rpm in Ticks(plot.RpmMin, plot.RpmMax, 4))
        {
            double x = MapX(rpm, plot.RpmMin, plot.RpmMax, left, right);
            PlotCanvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = top,
                Y2 = bottom,
                Stroke = grid,
                StrokeThickness = 1,
            });
            AddLabel(x - 16, bottom + 6, $"{rpm:0}", muted, 11, FontWeights.Normal);
        }
    }

    private void DrawAxis(double left, double right, double top, double bottom, Brush muted)
    {
        PlotCanvas.Children.Add(new Line
        {
            X1 = left,
            X2 = left,
            Y1 = top,
            Y2 = bottom,
            Stroke = muted,
            StrokeThickness = 1.5,
        });
        PlotCanvas.Children.Add(new Line
        {
            X1 = left,
            X2 = right,
            Y1 = bottom,
            Y2 = bottom,
            Stroke = muted,
            StrokeThickness = 1.5,
        });
    }

    private void DrawAxisTitles(double left, double right, double top, double bottom, Brush muted)
    {
        var yTitle = new TextBlock
        {
            Text = "Settled temperature",
            Foreground = muted,
            FontSize = 12,
            LayoutTransform = new RotateTransform(-90),
        };
        yTitle.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(yTitle, 0);
        Canvas.SetTop(yTitle, ((top + bottom) / 2) - (yTitle.DesiredSize.Height / 2));
        PlotCanvas.Children.Add(yTitle);
        AddLabel(((left + right) / 2) - 48, bottom + 24, "Fan speed (RPM)", muted, 12, FontWeights.Normal);
    }

    private void AddLabel(double x, double y, string text, Brush foreground, double fontSize, FontWeight weight)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = foreground,
            FontSize = fontSize,
            FontWeight = weight,
        };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        PlotCanvas.Children.Add(label);
    }

    private static IReadOnlyList<double> Ticks(double min, double max, int count)
    {
        if (count < 2 || max <= min)
        {
            return [min];
        }

        double span = max - min;
        double raw = span / (count - 1);
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double residual = raw / mag;
        double step = residual <= 1.5 ? mag : residual <= 3 ? 2 * mag : residual <= 7 ? 5 * mag : 10 * mag;
        double start = Math.Ceiling(min / step) * step;
        var ticks = new List<double>();
        for (double value = start; value <= max + (step * 0.01); value += step)
        {
            ticks.Add(value);
        }

        if (ticks.Count == 0)
        {
            return [min, max];
        }

        return ticks;
    }

    private static double MapX(double rpm, double min, double max, double left, double right)
    {
        double span = max - min;
        if (span <= 0)
        {
            return (left + right) / 2;
        }

        return left + ((rpm - min) / span * (right - left));
    }

    private static double MapY(double temp, double min, double max, double top, double bottom)
    {
        double span = max - min;
        if (span <= 0)
        {
            return (top + bottom) / 2;
        }

        return bottom - ((temp - min) / span * (bottom - top));
    }
}
