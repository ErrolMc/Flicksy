using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Flicksy.Drawing.Media;

namespace Flicksy.PostSnip.Controls;

public partial class VideoPlaybackOverlay : UserControl
{
    private enum ScrubSource { None, Slider, Keyboard }

    private bool _isScrubbing;
    private ScrubSource _scrubSource = ScrubSource.None;
    private bool _isInternalUpdate;
    private bool _shouldResumeAfterScrub;
    private IVideoPlayer? _player;

    private Channel<long>? _scrubChannel;
    private CancellationTokenSource? _scrubCts;
    private Task? _scrubTask;

    // Cumulative target for keyboard frame-stepping. Each KeyDown (including OS
    // auto-repeats during hold) advances this by one frame in the pressed direction.
    private long _stepTargetTicks;
    private bool _leftDown;
    private bool _rightDown;
    private Window? _hostWindow;

    public VideoPlaybackOverlay()
    {
        InitializeComponent();
        UpdateButtonText();
        UpdateTimeText();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public static readonly DependencyProperty PlayerProperty =
        DependencyProperty.Register(
            nameof(Player),
            typeof(IVideoPlayer),
            typeof(VideoPlaybackOverlay),
            new PropertyMetadata(null, OnPlayerChanged));

    public static readonly DependencyProperty IsPlayingProperty =
        DependencyProperty.Register(
            nameof(IsPlaying),
            typeof(bool),
            typeof(VideoPlaybackOverlay),
            new PropertyMetadata(false, OnIsPlayingChanged));

    public static readonly DependencyProperty DurationSecondsProperty =
        DependencyProperty.Register(
            nameof(DurationSeconds),
            typeof(double),
            typeof(VideoPlaybackOverlay),
            new PropertyMetadata(0d, OnDurationChanged));

    public static readonly DependencyProperty CurrentSecondsProperty =
        DependencyProperty.Register(
            nameof(CurrentSeconds),
            typeof(double),
            typeof(VideoPlaybackOverlay),
            new PropertyMetadata(0d, OnCurrentChanged));

    public IVideoPlayer? Player
    {
        get => (IVideoPlayer?)GetValue(PlayerProperty);
        set => SetValue(PlayerProperty, value);
    }

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public double DurationSeconds
    {
        get => (double)GetValue(DurationSecondsProperty);
        set => SetValue(DurationSecondsProperty, value);
    }

    public double CurrentSeconds
    {
        get => (double)GetValue(CurrentSecondsProperty);
        set => SetValue(CurrentSecondsProperty, value);
    }

    public void ResetState()
    {
        IsPlaying = false;
        CurrentSeconds = 0;
        DurationSeconds = 0;
    }

    private static void OnPlayerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var overlay = (VideoPlaybackOverlay)d;
        overlay.DetachPlayer(e.OldValue as IVideoPlayer);
        overlay.AttachPlayer(e.NewValue as IVideoPlayer);
    }

    private void AttachPlayer(IVideoPlayer? player)
    {
        _player = player;
        if (player is null)
        {
            ResetState();
            return;
        }

        player.StateChanged += OnPlayerStateChanged;
        player.PositionChanged += OnPlayerPositionChanged;
        player.MediaEnded += OnPlayerMediaEnded;

        SyncFromPlayer();
    }

    private void DetachPlayer(IVideoPlayer? player)
    {
        if (player is null) 
            return;

        player.StateChanged -= OnPlayerStateChanged;
        player.PositionChanged -= OnPlayerPositionChanged;
        player.MediaEnded -= OnPlayerMediaEnded;
    }

    private void SyncFromPlayer()
    {
        if (_player is null) 
            return;

        DurationSeconds = _player.Duration.TotalSeconds;
        CurrentSeconds = _player.Position.TotalSeconds;
        IsPlaying = _player.State == PlaybackState.Playing;
    }

    private void OnPlayerStateChanged(object? sender, EventArgs e)
    {
        DispatchSync(SyncFromPlayer);
    }

    private void OnPlayerPositionChanged(object? sender, EventArgs e)
    {
        DispatchSync(() =>
        {
            if (_player is null || _isScrubbing) 
                return;

            CurrentSeconds = _player.Position.TotalSeconds;
            if (_player.Duration.TotalSeconds > 0 && Math.Abs(DurationSeconds - _player.Duration.TotalSeconds) > 0.001)
            {
                DurationSeconds = _player.Duration.TotalSeconds;
            }
        });
    }

    private void OnPlayerMediaEnded(object? sender, EventArgs e)
    {
        DispatchSync(() =>
        {
            IsPlaying = false;
            if (_player is not null)
            {
                CurrentSeconds = _player.Duration.TotalSeconds;
            }
        });
    }

    private void DispatchSync(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.BeginInvoke(action);
        }
    }

    private static void OnIsPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((VideoPlaybackOverlay)d).UpdateButtonText();
    }

    private static void OnDurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (VideoPlaybackOverlay)d;
        control._isInternalUpdate = true;
        control.TimelineSlider.Maximum = Math.Max(0, control.DurationSeconds);
        control._isInternalUpdate = false;
        control.UpdateTimeText();
    }

    private static void OnCurrentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (VideoPlaybackOverlay)d;
        if (control._isScrubbing) 
            return;

        control._isInternalUpdate = true;
        control.TimelineSlider.Value = Math.Clamp(control.CurrentSeconds, 0, Math.Max(0, control.DurationSeconds));
        control._isInternalUpdate = false;
        control.UpdateTimeText();
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        if (_player is null) 
            return;

        if (IsPlaying)
        {
            _player.Pause();
        }
        else
        {
            _player.Play();
        }
    }

    private void OnSliderMouseDown(object sender, MouseButtonEventArgs e)
    {
        BeginScrubSession(ScrubSource.Slider);
    }

    private async void OnSliderMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_scrubSource != ScrubSource.Slider) 
            return;

        TimeSpan target = TimeSpan.FromSeconds(Math.Max(0, TimelineSlider.Value));
        await EndScrubSession(target);
    }

    private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInternalUpdate) 
            return;

        if (_isScrubbing || TimelineSlider.IsMouseCaptureWithin)
        {
            double current = Math.Clamp(TimelineSlider.Value, 0, Math.Max(0, DurationSeconds));
            CurrentSeconds = current;
            UpdateTimeText();

            if (_scrubSource == ScrubSource.Slider)
            {
                QueueScrubTarget((long)(current * TimeSpan.TicksPerSecond));
            }
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Window window = Window.GetWindow(this);
        if (window == _hostWindow) 
            return;

        UnhookHostWindowKeys();
        _hostWindow = window;
        if (_hostWindow is not null)
        {
            // PreviewKeyDown/Up at window level fires before the focused slider's own
            // arrow-key handling, so setting Handled=true lets us claim Left/Right
            // without the slider's DecreaseLarge/IncreaseLarge eating them first.
            _hostWindow.PreviewKeyDown += OnHostKeyDown;
            _hostWindow.PreviewKeyUp += OnHostKeyUp;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UnhookHostWindowKeys();
        DetachPlayer(_player);
        _player = null;
    }

    private void UnhookHostWindowKeys()
    {
        if (_hostWindow is null) 
            return;

        _hostWindow.PreviewKeyDown -= OnHostKeyDown;
        _hostWindow.PreviewKeyUp -= OnHostKeyUp;
        _hostWindow = null;
    }

    private void OnHostKeyDown(object sender, KeyEventArgs e)
    {
        if (_player is null) 
            return;

        if (_scrubSource == ScrubSource.Slider) 
            return; // slider owns the session

        int delta = e.Key switch
        {
            Key.Left => -1,
            Key.Right => +1,
            _ => 0,
        };
        if (delta == 0) 
            return;

        e.Handled = true;

        if (_scrubSource != ScrubSource.Keyboard)
        {
            BeginScrubSession(ScrubSource.Keyboard);
            _stepTargetTicks = _player.Position.Ticks;
        }

        if (e.Key == Key.Left) 
            _leftDown = true;
        else if (e.Key == Key.Right) 
            _rightDown = true;

        long frameTicks = _player.FrameDuration.Ticks;
        if (frameTicks <= 0) 
            frameTicks = TimeSpan.FromMilliseconds(33).Ticks;

        long maxTicks = Math.Max(0, _player.Duration.Ticks);
        _stepTargetTicks = Math.Clamp(_stepTargetTicks + delta * frameTicks, 0, maxTicks);

        // Update slider thumb + time text immediately for responsive feedback;
        // the video frame catches up when the worker's Seek completes.
        double seconds = _stepTargetTicks / (double)TimeSpan.TicksPerSecond;
        CurrentSeconds = seconds;
        _isInternalUpdate = true;
        TimelineSlider.Value = Math.Clamp(seconds, 0, Math.Max(0, DurationSeconds));
        _isInternalUpdate = false;
        UpdateTimeText();

        QueueScrubTarget(_stepTargetTicks);
    }

    private async void OnHostKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Left) 
            _leftDown = false;
        else if (e.Key == Key.Right) 
            _rightDown = false;
        else 
            return;

        if (_scrubSource != ScrubSource.Keyboard) 
            return;

        if (_leftDown || _rightDown) 
            return; // other arrow still held

        e.Handled = true;
        TimeSpan target = TimeSpan.FromTicks(Math.Max(0, _stepTargetTicks));
        await EndScrubSession(target);
    }

    private void BeginScrubSession(ScrubSource source)
    {
        if (_player is null || _isScrubbing) 
            return;

        _scrubSource = source;
        _shouldResumeAfterScrub = _player.State == PlaybackState.Playing;
        if (_shouldResumeAfterScrub)
        {
            _player.Pause();
        }
        _isScrubbing = true;

        // AllowSynchronousContinuations=false is load-bearing: without it, signaling
        // the worker (e.g. SemaphoreSlim.Release) would run its awaiter continuation
        // INLINE on the UI thread, dragging player.Seek onto the UI thread with it.
        // DropOldest + capacity 1 also gives us free coalescing — latest target wins.
        _scrubChannel = Channel.CreateBounded<long>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _scrubCts = new CancellationTokenSource();

        IVideoPlayer? player = _player;
        Channel<long>? channel = _scrubChannel;
        CancellationToken token = _scrubCts.Token;
        _scrubTask = Task.Run(() => ScrubWorkerAsync(player, channel, token));
    }

    private async Task EndScrubSession(TimeSpan finalTarget)
    {
        if (!_isScrubbing) 
            return;

        _isScrubbing = false;
        _scrubSource = ScrubSource.None;

        Task? pendingTask = _scrubTask;
        CancellationTokenSource? pendingCts = _scrubCts;
        Channel<long>? pendingChannel = _scrubChannel;
        _scrubTask = null;
        _scrubCts = null;
        _scrubChannel = null;

        pendingChannel?.Writer.TryComplete();
        pendingCts?.Cancel();
        if (pendingTask is not null)
        {
            try 
            { 
                await pendingTask.ConfigureAwait(true); 
            }
            catch 
            { 
                // swallow
            }
        }
        pendingCts?.Dispose();

        if (_player is null) 
            return;

        _player.Seek(finalTarget);

        if (_shouldResumeAfterScrub)
        {
            _shouldResumeAfterScrub = false;
            _player.Play();
        }
    }

    private void QueueScrubTarget(long ticks)
    {
        _scrubChannel?.Writer.TryWrite(ticks);
    }

    private async Task ScrubWorkerAsync(IVideoPlayer player, Channel<long> channel, CancellationToken ct)
    {
        try
        {
            await foreach (var ticks in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try 
                { 
                    player.Seek(new TimeSpan(ticks)); 
                }
                catch 
                { 
                    // swallow scrub errors
                }

                // Yield _seekLock for ~one composition tick (60Hz ≈ 16ms) so the
                // player's OnRendering TryEnter has a chance to grab it and present
                // the freshly-seeked frame. Without this pause, back-to-back scrub
                // seeks hold the lock continuously and the render path starves.
                try 
                { 
                    await Task.Delay(16, ct).ConfigureAwait(false); 
                }
                catch (OperationCanceledException) 
                { 
                    return; 
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on scrub stop.
        }
    }

    private void UpdateButtonText()
    {
        PlayPauseButton.Content = IsPlaying ? "Pause" : "Play";
    }

    private void UpdateTimeText()
    {
        TimeSpan current = TimeSpan.FromSeconds(Math.Max(0, CurrentSeconds));
        TimeSpan total = TimeSpan.FromSeconds(Math.Max(0, DurationSeconds));
        TimeText.Text = $"{current:mm\\:ss} / {total:mm\\:ss}";
    }
}
