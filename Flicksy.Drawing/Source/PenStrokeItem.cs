using System;
using System.Windows;
using System.Windows.Media;

namespace Flicksy.Drawing.Source;

public sealed class PenStrokeItem : DrawingItem
{
    private PointCollection _basePoints = new();
    private Brush _brush;
    private double _thickness;

    public PenStrokeItem(Brush brush, double thickness)
    {
        _brush = brush;
        _thickness = thickness;
    }

    public PointCollection BasePoints
    {
        get => _basePoints;
        private set => SetProperty(ref _basePoints, value);
    }

    public Brush Brush
    {
        get => _brush;
        private set => SetProperty(ref _brush, value);
    }

    public double Thickness
    {
        get => _thickness;
        private set
        {
            if (SetProperty(ref _thickness, value))
            {
                // Thickness widens the rendered stroke and inflates CanonicalBounds (by
                // thickness/2), so re-notify Geometry to refresh the selection overlay's bounds.
                OnPropertyChanged(nameof(Geometry));
            }
        }
    }

    public void SetStyle(Brush brush, double thickness)
    {
        Brush = brush;
        Thickness = Math.Max(0d, thickness);
    }

    public override Rect CanonicalBounds
    {
        get
        {
            if (Geometry.Bounds.IsEmpty)
            {
                return Rect.Empty;
            }

            Rect b = Geometry.Bounds;
            double inflate = Thickness / 2.0;
            b.Inflate(inflate, inflate);
            return b;
        }
    }

    public void AddPoint(Point point)
    {
        var updated = new PointCollection(BasePoints)
        {
            point,
        };

        BasePoints = updated;
        Geometry = BuildGeometry(updated);
    }

    public override bool HitTest(Point localPoint)
    {
        PointCollection points = BasePoints;
        if (points.Count == 0)
        {
            return false;
        }

        double tolerance = Math.Max(1d, Thickness * 0.5d);
        double toleranceSquared = tolerance * tolerance;

        if (points.Count == 1)
        {
            return DistanceSquared(points[0], localPoint) <= toleranceSquared;
        }

        for (var i = 1; i < points.Count; i++)
        {
            if (DistanceSquaredToSegment(localPoint, points[i - 1], points[i]) <= toleranceSquared)
            {
                return true;
            }
        }

        return false;
    }

    public override void Render(DrawingContext dc)
    {
        if (Geometry == Geometry.Empty)
        {
            return;
        }

        var pen = new Pen(Brush, Thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

        dc.PushTransform(Transform);
        dc.DrawGeometry(null, pen, Geometry);
        dc.Pop();
    }

    private static double DistanceSquared(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }

    private static double DistanceSquaredToSegment(Point point, Point start, Point end)
    {
        double vx = end.X - start.X;
        double vy = end.Y - start.Y;
        double lengthSquared = (vx * vx) + (vy * vy);
        if (lengthSquared <= double.Epsilon)
        {
            return DistanceSquared(point, start);
        }

        double t = ((point.X - start.X) * vx + (point.Y - start.Y) * vy) / lengthSquared;
        t = Math.Clamp(t, 0d, 1d);

        var closest = new Point(start.X + (t * vx), start.Y + (t * vy));
        return DistanceSquared(point, closest);
    }

    private static Geometry BuildGeometry(PointCollection points)
    {
        if (points.Count == 0)
        {
            return Geometry.Empty;
        }

        var figure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = false,
            IsFilled = false,
        };

        if (points.Count == 1)
        {
            figure.Segments.Add(new LineSegment(points[0], isStroked: true));
        }
        else if (points.Count == 2)
        {
            figure.Segments.Add(new LineSegment(points[1], isStroked: true));
        }
        else
        {
            const double tension = 1d;

            for (var i = 0; i < points.Count - 1; i++)
            {
                Point p0 = i == 0 ? points[i] : points[i - 1];
                Point p1 = points[i];
                Point p2 = points[i + 1];
                Point p3 = i + 2 < points.Count ? points[i + 2] : points[i + 1];

                var cp1 = new Point(
                    p1.X + ((p2.X - p0.X) * tension / 6d),
                    p1.Y + ((p2.Y - p0.Y) * tension / 6d));

                var cp2 = new Point(
                    p2.X - ((p3.X - p1.X) * tension / 6d),
                    p2.Y - ((p3.Y - p1.Y) * tension / 6d));

                figure.Segments.Add(new BezierSegment(cp1, cp2, p2, isStroked: true));
            }
        }

        var geometry = new PathGeometry(new[] { figure });
        geometry.Freeze();
        return geometry;
    }
}
