using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Flicksy.VideoEditor.Project;

/// <summary>
/// Per-clip placement of the clip's visual output inside the composition frame.
/// Stored as decomposed fields (position / scale / rotation / crop) rather than a
/// <c>MatrixTransform</c> so the model serializes cleanly to JSON and an inspector
/// UI can bind to each field independently. The compositor builds the actual render
/// matrix from these values at draw time.
/// </summary>
public partial class Transform2D : ObservableObject
{
    [ObservableProperty]
    private Point position = new(0, 0);

    [ObservableProperty]
    private Vector scale = new(1, 1);

    [ObservableProperty]
    private double rotationDegrees;

    [ObservableProperty]
    private Rect? cropRect;

    /// <summary>
    /// Copies all fields from <paramref name="other"/> into this instance. Used when a clip is
    /// split so each half gets an independent transform rather than sharing the original's
    /// mutable instance.
    /// </summary>
    public void CopyFrom(Transform2D other)
    {
        Position = other.Position;
        Scale = other.Scale;
        RotationDegrees = other.RotationDegrees;
        CropRect = other.CropRect;
    }
}
