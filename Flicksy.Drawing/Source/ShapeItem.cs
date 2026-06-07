using System;
using System.Windows;
using System.Windows.Media;

namespace Flicksy.Drawing.Source;

public sealed class ShapeItem : DrawingItem
{
    private Point _p0;
    private Point _p1;

    public ShapeItem(ShapeKind kind, Point start, Brush? fill, Brush? outline, double outlineThickness)
    {
        Kind = kind;
        Fill = fill;
        Outline = outline;
        OutlineThickness = Math.Max(0d, outlineThickness);
        _p0 = start;
        _p1 = start;
        RebuildGeometry();
    }

    public ShapeKind Kind { get; }

    public Brush? Fill { get; }

    public Brush? Outline { get; }

    public double OutlineThickness { get; }

    /// <summary>
    /// Brush used by the WPF Path's Stroke. Always the user's outline brush.
    /// </summary>
    public Brush? EffectiveStroke => Outline;

    /// <summary>
    /// Brush used by the WPF Path's Fill. For arrows we use the outline brush so the
    /// arrowhead triangle (a closed sub-figure of the geometry) is filled solidly.
    /// </summary>
    public Brush? EffectiveFill => Kind switch
    {
        ShapeKind.Line => null,
        ShapeKind.Arrow => Outline,
        _ => Fill,
    };

    public Point P0
    {
        get => _p0;
        private set => SetProperty(ref _p0, value);
    }

    public Point P1
    {
        get => _p1;
        private set => SetProperty(ref _p1, value);
    }

    public void UpdateEndPoint(Point p1)
    {
        if (_p1 == p1)
        {
            return;
        }

        _p1 = p1;
        OnPropertyChanged(nameof(P1));
        RebuildGeometry();
    }

    public bool IsDegenerate
    {
        get
        {
            const double minDimension = 3.0;
            return Kind switch
            {
                ShapeKind.Line or ShapeKind.Arrow =>
                    (P1 - P0).LengthSquared < minDimension * minDimension,
                _ => Math.Abs(P1.X - P0.X) < minDimension || Math.Abs(P1.Y - P0.Y) < minDimension,
            };
        }
    }

    public override Rect CanonicalBounds
    {
        get
        {
            if (Geometry == Geometry.Empty || Geometry.Bounds.IsEmpty)
            {
                return Rect.Empty;
            }

            Rect b = Geometry.Bounds;
            double inflate = Outline is not null ? OutlineThickness / 2.0 : 0d;
            if (inflate > 0)
            {
                b.Inflate(inflate, inflate);
            }
            return b;
        }
    }

    public override bool HitTest(Point localPoint)
    {
        if (Geometry == Geometry.Empty)
        {
            return false;
        }

        if (EffectiveFill is not null && Geometry.FillContains(localPoint))
        {
            return true;
        }

        if (EffectiveStroke is not null)
        {
            // Use a minimum hit-test thickness so very thin outlines remain pickable.
            var pen = new Pen(EffectiveStroke, Math.Max(OutlineThickness, 6d));
            if (Geometry.StrokeContains(pen, localPoint))
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

        Pen? pen = null;
        if (EffectiveStroke is not null && OutlineThickness > 0)
        {
            pen = new Pen(EffectiveStroke, OutlineThickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
        }

        dc.PushTransform(Transform);
        dc.DrawGeometry(EffectiveFill, pen, Geometry);
        dc.Pop();
    }

    private void RebuildGeometry()
    {
        Geometry = BuildGeometry(Kind, P0, P1, OutlineThickness);
    }

    private static Geometry BuildGeometry(ShapeKind kind, Point p0, Point p1, double outlineThickness)
    {
        switch (kind)
        {
            case ShapeKind.Square:
            {
                var rect = new Rect(p0, p1);
                var g = new RectangleGeometry(rect);
                g.Freeze();
                return g;
            }
            case ShapeKind.Circle:
            {
                var rect = new Rect(p0, p1);
                var center = new Point(rect.X + rect.Width / 2.0, rect.Y + rect.Height / 2.0);
                var g = new EllipseGeometry(center, rect.Width / 2.0, rect.Height / 2.0);
                g.Freeze();
                return g;
            }
            case ShapeKind.Line:
            {
                var g = new LineGeometry(p0, p1);
                g.Freeze();
                return g;
            }
            case ShapeKind.Arrow:
            {
                Geometry g = BuildArrowGeometry(p0, p1, outlineThickness);
                g.Freeze();
                return g;
            }
            default:
                return Geometry.Empty;
        }
    }

    private static Geometry BuildArrowGeometry(Point start, Point end, double outlineThickness)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= double.Epsilon)
        {
            // Degenerate; render as a tiny line so geometry isn't empty (IsDegenerate prevents it being kept).
            var g = new LineGeometry(start, end);
            g.Freeze();
            return g;
        }

        double headLength = Math.Clamp(outlineThickness * 4.5, 12.0, Math.Max(12.0, length * 0.5));
        double headWidth = headLength * 0.7;

        double ux = dx / length;
        double uy = dy / length;
        double px = -uy;
        double py = ux;

        var shaftEnd = new Point(end.X - ux * headLength * 0.6, end.Y - uy * headLength * 0.6);
        var baseCenter = new Point(end.X - ux * headLength, end.Y - uy * headLength);
        var baseLeft = new Point(baseCenter.X + px * headWidth / 2.0, baseCenter.Y + py * headWidth / 2.0);
        var baseRight = new Point(baseCenter.X - px * headWidth / 2.0, baseCenter.Y - py * headWidth / 2.0);

        var shaftFigure = new PathFigure
        {
            StartPoint = start,
            IsClosed = false,
            IsFilled = false,
        };
        shaftFigure.Segments.Add(new LineSegment(shaftEnd, isStroked: true));

        var headFigure = new PathFigure
        {
            StartPoint = end,
            IsClosed = true,
            IsFilled = true,
        };
        headFigure.Segments.Add(new LineSegment(baseLeft, isStroked: true));
        headFigure.Segments.Add(new LineSegment(baseRight, isStroked: true));

        return new PathGeometry(new[] { shaftFigure, headFigure });
    }
}
