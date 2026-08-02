using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flicksy.Drawing.Helpers;
using Flicksy.Drawing.ViewModels;

namespace Flicksy.PostSnip.ViewModels;

public enum ImageEditTool
{
    Select,
    Pen,
    Erase,
    Shapes,
    Text,
    Crop,
}

public partial class ImageEditToolsViewModel : ObservableObject
{
    private DateTime _lastPopupCloseAt = DateTime.MinValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPenActive))]
    [NotifyPropertyChangedFor(nameof(IsEraseActive))]
    [NotifyPropertyChangedFor(nameof(IsSelectActive))]
    [NotifyPropertyChangedFor(nameof(IsShapesActive))]
    [NotifyPropertyChangedFor(nameof(IsTextActive))]
    [NotifyPropertyChangedFor(nameof(IsCropActive))]
    [NotifyPropertyChangedFor(nameof(IsDrawingToolActive))]
    [NotifyPropertyChangedFor(nameof(IsImageToolActive))]
    private ImageEditTool selectedTool = ImageEditTool.Select;

    [ObservableProperty]
    private bool isPenSettingsOpen;

    [ObservableProperty]
    private bool isShapeSettingsOpen;

    [ObservableProperty]
    private bool isTextSettingsOpen;

    private DateTime _lastShapePopupCloseAt = DateTime.MinValue;
    private DateTime _lastTextPopupCloseAt = DateTime.MinValue;

    public ImageEditToolsViewModel()
    {
        Cursor = Images.cursor.ToImageSource();
        PenBackground = Images.pen_background.ToImageSource();
        PenForeground = Images.pen_foreground.ToImageSource();
        Eraser = Images.eraser.ToImageSource();
        Shapes = Images.shapes.ToImageSource();
        Text = Images.text.ToImageSource();
        Crop = Images.crop.ToImageSource();
        UpArrow = Images.up_arrow.ToImageSource();
        Undo = Images.undo.ToImageSource();
    }

    public ImageSource Cursor { get; }

    public ImageSource PenBackground { get; }

    public ImageSource PenForeground { get; }

    public ImageSource Eraser { get; }

    public ImageSource Shapes { get; }

    public ImageSource Text { get; }

    public ImageSource Crop { get; }

    public ImageSource UpArrow { get; }

    public ImageSource Undo { get; }

    public PenSettingsViewModel PenSettings { get; } = new();

    public ShapeSettingsViewModel ShapeSettings { get; } = new();

    public TextSettingsViewModel TextSettings { get; } = new();

    public bool IsPenActive => SelectedTool == ImageEditTool.Pen;

    public bool IsEraseActive => SelectedTool == ImageEditTool.Erase;

    public bool IsSelectActive => SelectedTool == ImageEditTool.Select;

    public bool IsShapesActive => SelectedTool == ImageEditTool.Shapes;

    public bool IsTextActive => SelectedTool == ImageEditTool.Text;

    public bool IsCropActive => SelectedTool == ImageEditTool.Crop;

    public bool IsDrawingToolActive => SelectedTool is ImageEditTool.Pen or ImageEditTool.Erase or ImageEditTool.Shapes;

    public bool IsImageToolActive => SelectedTool is ImageEditTool.Pen or ImageEditTool.Erase or ImageEditTool.Select or ImageEditTool.Shapes or ImageEditTool.Text;

    [RelayCommand]
    private void Select()
    {
        SelectedTool = ImageEditTool.Select;
    }

    [RelayCommand]
    private void Pen()
    {
        if (SelectedTool == ImageEditTool.Pen)
        {
            if ((DateTime.UtcNow - _lastPopupCloseAt).TotalMilliseconds < 250)
            {
                return;
            }

            IsPenSettingsOpen = !IsPenSettingsOpen;
            return;
        }

        SelectedTool = ImageEditTool.Pen;
    }

    [RelayCommand]
    private void Erase()
    {
        SelectedTool = ImageEditTool.Erase;
    }

    [RelayCommand]
    private void TextTool()
    {
        if (SelectedTool == ImageEditTool.Text)
        {
            if ((DateTime.UtcNow - _lastTextPopupCloseAt).TotalMilliseconds < 250)
            {
                return;
            }

            IsTextSettingsOpen = !IsTextSettingsOpen;
            return;
        }

        SelectedTool = ImageEditTool.Text;
    }

    [RelayCommand]
    private void ShapesTool()
    {
        if (SelectedTool == ImageEditTool.Shapes)
        {
            if ((DateTime.UtcNow - _lastShapePopupCloseAt).TotalMilliseconds < 250)
            {
                return;
            }

            IsShapeSettingsOpen = !IsShapeSettingsOpen;
            return;
        }

        SelectedTool = ImageEditTool.Shapes;
    }

    [RelayCommand]
    private void CropTool()
    {
        SelectedTool = ImageEditTool.Crop;
    }

    partial void OnSelectedToolChanged(ImageEditTool value)
    {
        if (value != ImageEditTool.Pen)
        {
            IsPenSettingsOpen = false;
        }

        if (value != ImageEditTool.Shapes)
        {
            IsShapeSettingsOpen = false;
        }

        if (value != ImageEditTool.Text)
        {
            IsTextSettingsOpen = false;
        }
    }

    partial void OnIsPenSettingsOpenChanged(bool value)
    {
        if (!value)
        {
            _lastPopupCloseAt = DateTime.UtcNow;
        }
    }

    partial void OnIsShapeSettingsOpenChanged(bool value)
    {
        if (!value)
        {
            _lastShapePopupCloseAt = DateTime.UtcNow;
        }
    }

    partial void OnIsTextSettingsOpenChanged(bool value)
    {
        if (!value)
        {
            _lastTextPopupCloseAt = DateTime.UtcNow;
        }
    }

}
