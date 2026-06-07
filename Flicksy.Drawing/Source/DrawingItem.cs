using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Flicksy.Drawing.Source;

public abstract class DrawingItem : ObservableObject
{
    private Geometry _geometry = Geometry.Empty;

    protected DrawingItem()
    {
        Transform = new MatrixTransform(Matrix.Identity);
    }

    public Geometry Geometry
    {
        get => _geometry;
        protected set => SetProperty(ref _geometry, value);
    }

    public MatrixTransform Transform { get; }

    public abstract Rect CanonicalBounds { get; }

    /// <summary>
    /// Returns true if the given point (in the item's local/canonical space) hits the item.
    /// </summary>
    public abstract bool HitTest(Point localPoint);

    public void TranslateFrom(Matrix baseTransform, Vector totalDelta)
    {
        Matrix m = baseTransform;
        m.Translate(totalDelta.X, totalDelta.Y);
        Transform.Matrix = m;
    }

    public void ScaleFrom(Matrix baseTransform, double factor, Point anchorWorld)
    {
        Matrix m = baseTransform;
        m.ScaleAt(factor, factor, anchorWorld.X, anchorWorld.Y);
        Transform.Matrix = m;
    }

    public void RotateFrom(Matrix baseTransform, double angleDegrees, Point centerWorld)
    {
        Matrix m = baseTransform;
        m.RotateAt(angleDegrees, centerWorld.X, centerWorld.Y);
        Transform.Matrix = m;
    }

    /// <summary>
    /// Renders the item into the given DrawingContext (used for flattening on save).
    /// </summary>
    public abstract void Render(DrawingContext dc);
}
