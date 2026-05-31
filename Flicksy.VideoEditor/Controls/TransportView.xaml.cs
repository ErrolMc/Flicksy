using System.Windows.Controls;

namespace Flicksy.VideoEditor.Controls;

/// <summary>
/// Center-column transport bar: prev/play-pause/next buttons flanked by current and total
/// timecode labels. Commands bind to <see cref="ViewModels.TransportViewModel"/>, which
/// delegates them to the playback engine (clock-driven play/pause and frame stepping); the
/// engine writes <c>Playhead</c> / <c>IsPlaying</c> back for the labels to reflect.
/// </summary>
public partial class TransportView : UserControl
{
    public TransportView()
    {
        InitializeComponent();
    }
}
