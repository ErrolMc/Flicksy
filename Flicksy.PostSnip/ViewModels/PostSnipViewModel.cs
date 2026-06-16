using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flicksy.Drawing.Media;
using Flicksy.Drawing.Source;
using Flicksy.Drawing.ViewModels;

namespace Flicksy.PostSnip.ViewModels;

public partial class PostSnipViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMedia))]
    [NotifyPropertyChangedFor(nameof(IsImage))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(IsVideoLoaded))]
    private string? mediaPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImage))]
    [NotifyPropertyChangedFor(nameof(IsVideoLoaded))]
    private bool isVideo;

    [ObservableProperty]
    private ImageSource? imageSource;

    public PostSnipViewModel(IVideoPlayer player, ImageEditToolsViewModel imageEditTools, DrawingViewModel drawing, CropOverlayViewModel cropOverlay)
    {
        Player = player;
        ImageEditTools = imageEditTools;
        Drawing = drawing;
        CropOverlay = cropOverlay;
        CropOverlay.AttachHistory(drawing.History);
        SelectionOverlay = new SelectionOverlayViewModel
        {
            SelectedItem = drawing.SelectedItem,
            IsActive = imageEditTools.IsSelectActive,
        };

        drawing.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DrawingViewModel.SelectedItem))
            {
                // Committing any in-progress text edit before the selection moves elsewhere.
                if (drawing.EditingTextItem is { } editing && !ReferenceEquals(editing, drawing.SelectedItem))
                {
                    drawing.EndEditText(commit: true);
                }

                SelectionOverlay.SelectedItem = drawing.SelectedItem;
            }
            else if (e.PropertyName == nameof(DrawingViewModel.EditingTextItem))
            {
                // Whenever a text edit begins, make sure the Text tool is the active tool so
                // its settings popup (font/size/fill/outline) targets the editing item.
                if (drawing.EditingTextItem is not null && !imageEditTools.IsTextActive)
                {
                    imageEditTools.SelectedTool = ImageEditTool.Text;
                }

                // Show the selection overlay around the text being edited, regardless of tool.
                SelectionOverlay.IsActive = imageEditTools.IsSelectActive || drawing.EditingTextItem is not null;
            }
        };

        imageEditTools.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ImageEditToolsViewModel.SelectedTool))
            {
                // Commit any active text edit when switching away from the text tool.
                if (!imageEditTools.IsTextActive)
                {
                    drawing.EndEditText(commit: true);
                }

                // Hide selection corner/rotate handles whenever the Text, Shapes or Pen tool is
                // active — for Text to keep focus on typing, for Shapes/Pen because corner-resize
                // is a Select-tool gesture and would be non-functional while restyling.
                SelectionOverlay.ShowHandles = !imageEditTools.IsTextActive
                    && !imageEditTools.IsShapesActive
                    && !imageEditTools.IsPenActive;

                // Crop tool transitions: begin/commit an edit session on the crop VM.
                if (imageEditTools.IsCropActive && !cropOverlay.IsActive)
                {
                    cropOverlay.BeginEdit();
                }
                else if (!imageEditTools.IsCropActive && cropOverlay.IsActive)
                {
                    cropOverlay.CommitEdit();
                }
            }

            if (e.PropertyName == nameof(ImageEditToolsViewModel.IsSelectActive))
            {
                bool keepTextSelection = imageEditTools.IsTextActive
                    && drawing.SelectedItem is TextItem;

                // Keep a selected shape alive when switching to the Shapes tool so its style
                // popup restyles the selection (mirrors keepTextSelection for the Text tool).
                bool keepShapeSelection = imageEditTools.IsShapesActive
                    && drawing.SelectedItem is ShapeItem;

                // Likewise keep a selected pen stroke alive when switching to the Pen tool so its
                // settings popup restyles the selection.
                bool keepPenSelection = imageEditTools.IsPenActive
                    && drawing.SelectedItem is PenStrokeItem;

                SelectionOverlay.IsActive = imageEditTools.IsSelectActive
                    || drawing.EditingTextItem is not null
                    || keepTextSelection
                    || keepShapeSelection
                    || keepPenSelection;

                if (!imageEditTools.IsSelectActive
                    && drawing.EditingTextItem is null
                    && !keepTextSelection
                    && !keepShapeSelection
                    && !keepPenSelection)
                {
                    drawing.SelectedItem = null;
                }
            }
        };
    }

    public IVideoPlayer Player { get; }

    public ImageEditToolsViewModel ImageEditTools { get; }

    public DrawingViewModel Drawing { get; }

    public CropOverlayViewModel CropOverlay { get; }

    public SelectionOverlayViewModel SelectionOverlay { get; }

    public bool PreserveMediaFile { get; set; }

    public bool HasMedia => !string.IsNullOrWhiteSpace(MediaPath);

    public bool IsImage => HasMedia && !IsVideo;

    public bool IsEmpty => !HasMedia;

    public bool IsVideoLoaded => HasMedia && IsVideo;

    public event EventHandler<SaveDialogRequest>? SaveDialogRequested;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler? CloseRequested;

    public void LoadImage(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            throw new ArgumentException("Image path is required.", nameof(imagePath));
        }

        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("Image file was not found.", imagePath);
        }

        Player.Close();

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();

        ImageSource = bitmap;
        IsVideo = false;
        MediaPath = imagePath;
        Drawing.Clear();
        CropOverlay.Reset(bitmap.Width, bitmap.Height);
    }

    public async Task LoadVideoAsync(string videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
        {
            throw new ArgumentException("Video path is required.", nameof(videoPath));
        }

        if (!File.Exists(videoPath))
        {
            throw new FileNotFoundException("Video file was not found.", videoPath);
        }

        ImageSource = null;
        IsVideo = true;
        MediaPath = videoPath;
        Drawing.Clear();
        CropOverlay.Reset(0, 0);

        await Player.OpenAsync(videoPath).ConfigureAwait(true);
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(MediaPath) || !File.Exists(MediaPath))
        {
            ErrorOccurred?.Invoke(this, "There is no media to save.");
            return;
        }

        string? sourceExtension = Path.GetExtension(MediaPath);
        if (string.IsNullOrWhiteSpace(sourceExtension))
        {
            sourceExtension = IsVideo ? ".mp4" : ".png";
        }

        var request = new SaveDialogRequest(
            title: IsVideo ? "Save Recording" : "Save Snip",
            suggestedFileName: $"Flicksy_{DateTime.Now:yyyyMMdd_HHmmss}{sourceExtension}",
            defaultExtension: sourceExtension,
            filter: IsVideo
                ? "MP4 Video (*.mp4)|*.mp4|All Files (*.*)|*.*"
                : "PNG Image (*.png)|*.png|All Files (*.*)|*.*");

        SaveDialogRequested?.Invoke(this, request);

        if (string.IsNullOrWhiteSpace(request.SelectedPath))
        {
            return;
        }

        try
        {
            if (IsImage && (Drawing.HasItems || HasEffectiveCrop()) && ImageSource is BitmapSource bitmapSource)
            {
                SaveImageWithDrawing(bitmapSource, request.SelectedPath);
            }
            else if (!string.Equals(Path.GetFullPath(request.SelectedPath), Path.GetFullPath(MediaPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(MediaPath, request.SelectedPath, overwrite: true);
            }

            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to save file:\n{ex.Message}");
        }
    }

    private bool HasEffectiveCrop()
    {
        if (!CropOverlay.HasImage) 
            return false;

        Rect crop = CropOverlay.EffectiveCrop;
        return crop.X > 0
            || crop.Y > 0
            || crop.Width < CropOverlay.ImageWidth
            || crop.Height < CropOverlay.ImageHeight;
    }

    private void SaveImageWithDrawing(BitmapSource source, string destinationPath)
    {
        double width = source.Width;
        double height = source.Height;
        Rect crop = HasEffectiveCrop()
            ? CropOverlay.EffectiveCrop
            : new Rect(0, 0, width, height);

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            // Offset so the crop origin maps to (0,0) in the rendered output.
            dc.PushTransform(new TranslateTransform(-crop.X, -crop.Y));
            dc.DrawImage(source, new Rect(0, 0, width, height));

            foreach (DrawingItem item in Drawing.Items)
            {
                item.Render(dc);
            }
            dc.Pop();
        }

        int pixelW = (int)Math.Max(1, Math.Round(crop.Width * source.DpiX / 96.0));
        int pixelH = (int)Math.Max(1, Math.Round(crop.Height * source.DpiY / 96.0));
        var rtb = new RenderTargetBitmap(pixelW, pixelH, source.DpiX, source.DpiY, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using FileStream stream = File.Create(destinationPath);
        encoder.Save(stream);
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ApplyCrop()
    {
        if (CropOverlay.IsActive)
        {
            CropOverlay.CommitEdit();
        }
        ImageEditTools.SelectedTool = ImageEditTool.Select;
    }

    [RelayCommand]
    private void CancelCrop()
    {
        if (CropOverlay.IsActive)
        {
            CropOverlay.CancelEdit();
        }
        ImageEditTools.SelectedTool = ImageEditTool.Select;
    }

    [RelayCommand]
    private void New()
    {
        string? snipperPath = ResolveSnipperExecutablePath();
        if (string.IsNullOrWhiteSpace(snipperPath))
        {
            ErrorOccurred?.Invoke(this, "Flicksy.Snipper.exe was not found.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = snipperPath,
                WorkingDirectory = Path.GetDirectoryName(snipperPath),
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Unable to start Flicksy.Snipper:\n{ex.Message}");
            return;
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private static string? ResolveSnipperExecutablePath()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string[] candidates = new[]
        {
            Path.Combine(baseDirectory, "Flicksy.Snipper.exe"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "Flicksy.Snipper", "bin", "Debug", "net10.0-windows", "Flicksy.Snipper.exe")),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "Flicksy.Snipper", "bin", "Release", "net10.0-windows", "Flicksy.Snipper.exe")),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    [RelayCommand]
    private void LaunchInVideoEditor()
    {
        if (!IsVideoLoaded || string.IsNullOrWhiteSpace(MediaPath))
        {
            return;
        }

        string? videoEditorPath = ResolveVideoEditorExecutablePath();
        if (string.IsNullOrWhiteSpace(videoEditorPath))
        {
            ErrorOccurred?.Invoke(this, "Flicksy.VideoEditor.exe was not found.");
            return;
        }

        // Hand the temp media file off to the video editor; if we let PostSnip delete
        // it on close the editor would open a missing file.
        PreserveMediaFile = true;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = videoEditorPath,
                Arguments = $"\"{MediaPath}\"",
                WorkingDirectory = Path.GetDirectoryName(videoEditorPath),
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Unable to start Flicksy.VideoEditor:\n{ex.Message}");
            return;
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private static string? ResolveVideoEditorExecutablePath()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string[] candidates = new[]
        {
            Path.Combine(baseDirectory, "Flicksy.VideoEditor.exe"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "Flicksy.VideoEditor", "bin", "Debug", "net10.0-windows", "Flicksy.VideoEditor.exe")),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "Flicksy.VideoEditor", "bin", "Release", "net10.0-windows", "Flicksy.VideoEditor.exe")),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public void DeleteMediaFile()
    {
        if (PreserveMediaFile || string.IsNullOrWhiteSpace(MediaPath))
        {
            return;
        }

        try
        {
            if (File.Exists(MediaPath))
            {
                File.Delete(MediaPath);
            }
        }
        catch
        {
            // Best effort — leave the file if it can't be removed.
        }
    }
}

public sealed class SaveDialogRequest
{
    public SaveDialogRequest(string title, string suggestedFileName, string defaultExtension, string filter)
    {
        Title = title;
        SuggestedFileName = suggestedFileName;
        DefaultExtension = defaultExtension;
        Filter = filter;
    }

    public string Title { get; }
    public string SuggestedFileName { get; }
    public string DefaultExtension { get; }
    public string Filter { get; }
    public string? SelectedPath { get; set; }
}
