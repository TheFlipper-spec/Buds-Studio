using System;
using System.Windows;
using System.Windows.Media;

namespace RealmeBudsPopup.Controls
{
    public class CircularGauge : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(CircularGauge),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeThicknessProperty =
            DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(CircularGauge),
                new FrameworkPropertyMetadata(4.5, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TrackBrushProperty =
            DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(CircularGauge),
                new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(229, 231, 235)), FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ProgressBrushProperty =
            DependencyProperty.Register(nameof(ProgressBrush), typeof(Brush), typeof(CircularGauge),
                new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0, 102, 204)), FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty IsAvailableProperty =
            DependencyProperty.Register(nameof(IsAvailable), typeof(bool), typeof(CircularGauge),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public double StrokeThickness
        {
            get => (double)GetValue(StrokeThicknessProperty);
            set => SetValue(StrokeThicknessProperty, value);
        }

        public Brush TrackBrush
        {
            get => (Brush)GetValue(TrackBrushProperty);
            set => SetValue(TrackBrushProperty, value);
        }

        public Brush ProgressBrush
        {
            get => (Brush)GetValue(ProgressBrushProperty);
            set => SetValue(ProgressBrushProperty, value);
        }

        public bool IsAvailable
        {
            get => (bool)GetValue(IsAvailableProperty);
            set => SetValue(IsAvailableProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            double stroke = StrokeThickness;
            double radius = Math.Max(1, (Math.Min(w, h) - stroke) / 2.0);
            Point center = new Point(w / 2.0, h / 2.0);

            // 1. Draw Background Track Circle
            Pen trackPen = new Pen(TrackBrush, stroke)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            dc.DrawEllipse(null, trackPen, center, radius, radius);

            // 2. If available and Value > 0, Draw Progress Arc
            if (!IsAvailable || Value <= 0) return;

            double clampedValue = Math.Clamp(Value, 0.0, 100.0);
            double angle = clampedValue / 100.0 * 360.0;

            Pen progressPen = new Pen(ProgressBrush, stroke)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };

            if (angle >= 359.9)
            {
                // Full circle
                dc.DrawEllipse(null, progressPen, center, radius, radius);
                return;
            }

            // Top start point (12 o'clock)
            Point startPoint = new Point(center.X, center.Y - radius);

            // Calculate end point
            double rad = (angle - 90.0) * Math.PI / 180.0;
            Point endPoint = new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
            bool isLargeArc = angle > 180.0;

            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(startPoint, false, false);
                ctx.ArcTo(endPoint, new Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise, true, false);
            }
            geometry.Freeze();

            dc.DrawGeometry(null, progressPen, geometry);
        }
    }
}
