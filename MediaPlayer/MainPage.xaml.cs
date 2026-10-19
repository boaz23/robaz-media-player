using MediaPlayer.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Pickers;
using Windows.System;

namespace MediaPlayer;

public sealed partial class MainPage : Page
{
    private readonly PlaylistResolver _playlists = new();
    private readonly IKeyframeIndex _keyframes = new FfprobeKeyframeIndex();
    private readonly DispatcherTimer _positionTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private IReadOnlyList<MediaItem> _items = [];
    private IReadOnlyList<TimeSpan> _keyframeTimes = [];
    private int _index = -1;
    private bool _updatingTimeline;
    private CancellationTokenSource? _openCancellation;

    public MainPage()
    {
        InitializeComponent();
        VideoView.SetMediaPlayer(new Windows.Media.Playback.MediaPlayer());
        VideoView.MediaPlayer.MediaOpened += MediaPlayer_MediaOpened;
        VideoView.MediaPlayer.MediaEnded += (_, _) => DispatcherQueue.TryEnqueue(MoveNext);
        VideoView.MediaPlayer.CurrentStateChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdatePlaybackState);
        _positionTimer.Tick += (_, _) => UpdateTimeline();
        _positionTimer.Start();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e) => Focus(FocusState.Programmatic);

    private async void OpenFiles_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };
        picker.FileTypeFilter.Add("*");
        InitializePicker(picker);
        var files = await picker.PickMultipleFilesAsync();
        if (files.Count == 0) return;
        await UseSnapshotAsync(_playlists.FromLooseFiles(files.Select(file => file.Path), RandomButton.IsChecked == true));
    }

    private async void OpenPlaylist_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".json");
        InitializePicker(picker);
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        try
        {
            StatusText.Text = "Rendering the complete playlist…";
            await UseSnapshotAsync(await _playlists.MaterializeAsync(file.Path, RandomButton.IsChecked == true));
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private static void InitializePicker(object picker)
    {
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
    }

    private async Task UseSnapshotAsync(PlaylistSnapshot snapshot)
    {
        _items = snapshot.Items;
        _index = _items.Count == 0 ? -1 : 0;
        StatusText.Text = snapshot.IsRandomized ? "Full playlist rendered and shuffled" : "Playlist ready";
        await OpenCurrentAsync();
    }

    private async Task OpenCurrentAsync()
    {
        _openCancellation?.Cancel();
        _openCancellation = new CancellationTokenSource();
        _keyframeTimes = [];
        KeyframeCanvas.Children.Clear();
        ResetZoom();

        if (_index < 0 || _index >= _items.Count)
        {
            ImageView.Visibility = VideoView.Visibility = Visibility.Collapsed;
            EmptyText.Visibility = Visibility.Visible;
            CountText.Text = "0 items";
            return;
        }

        var item = _items[_index];
        TitleText.Text = Path.GetFileName(item.Path);
        CountText.Text = $"{_index + 1} / {_items.Count}";
        EmptyText.Visibility = Visibility.Collapsed;

        if (item.Kind == MediaKind.Image)
        {
            VideoView.MediaPlayer.Pause();
            VideoView.Source = null;
            VideoView.Visibility = Visibility.Collapsed;
            ImageView.Source = new BitmapImage(new Uri(item.Path));
            ImageView.Visibility = Visibility.Visible;
            Timeline.IsEnabled = false;
            StatusText.Text = item.SourcePlaylist;
            return;
        }

        ImageView.Visibility = Visibility.Collapsed;
        VideoView.Visibility = Visibility.Visible;
        Timeline.IsEnabled = true;
        VideoView.Source = MediaSource.CreateFromUri(new Uri(item.Path));
        VideoView.MediaPlayer.Play();
        StatusText.Text = "Indexing key frames…";
        try
        {
            _keyframeTimes = await _keyframes.ReadAsync(item.Path, _openCancellation.Token);
            StatusText.Text = $"{_keyframeTimes.Count} key frames";
            DrawKeyframes();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { StatusText.Text = "Playback ready · install FFmpeg to display key frames"; }
    }

    private void Previous_Click(object sender, RoutedEventArgs e) => MovePrevious();
    private void Next_Click(object sender, RoutedEventArgs e) => MoveNext();
    private void MovePrevious() { if (_items.Count > 0) { _index = (_index - 1 + _items.Count) % _items.Count; _ = OpenCurrentAsync(); } }
    private void MoveNext() { if (_items.Count > 0) { _index = (_index + 1) % _items.Count; _ = OpenCurrentAsync(); } }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (VideoView.Visibility != Visibility.Visible) return;
        if (VideoView.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) VideoView.MediaPlayer.Pause();
        else VideoView.MediaPlayer.Play();
    }

    private void StepBack_Click(object sender, RoutedEventArgs e)
    {
        if (VideoView.Visibility == Visibility.Visible) { VideoView.MediaPlayer.Pause(); VideoView.MediaPlayer.StepBackwardOneFrame(); }
    }

    private void StepForward_Click(object sender, RoutedEventArgs e)
    {
        if (VideoView.Visibility == Visibility.Visible) { VideoView.MediaPlayer.Pause(); VideoView.MediaPlayer.StepForwardOneFrame(); }
    }

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Left: MovePrevious(); e.Handled = true; break;
            case VirtualKey.Right: MoveNext(); e.Handled = true; break;
            case VirtualKey.Space: Play_Click(sender, new RoutedEventArgs()); e.Handled = true; break;
            case VirtualKey.OemComma: StepBack_Click(sender, new RoutedEventArgs()); e.Handled = true; break;
            case VirtualKey.OemPeriod: StepForward_Click(sender, new RoutedEventArgs()); e.Handled = true; break;
        }
    }

    private void MediaSurface_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(MediaSurface);
        if (!point.Properties.IsControlKeyPressed) return;
        ZoomSlider.Value = Math.Clamp(ZoomSlider.Value + (point.Properties.MouseWheelDelta > 0 ? 0.25 : -0.25), ZoomSlider.Minimum, ZoomSlider.Maximum);
        e.Handled = true;
    }

    private void ZoomSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (MediaScale is null || ZoomText is null) return;
        MediaScale.ScaleX = MediaScale.ScaleY = e.NewValue;
        ZoomText.Text = $"{e.NewValue:P0}";
    }

    private void ResetZoom() { if (ZoomSlider is not null) ZoomSlider.Value = 1; }

    private void MediaPlayer_MediaOpened(Windows.Media.Playback.MediaPlayer sender, object args) => DispatcherQueue.TryEnqueue(() =>
    {
        Timeline.Maximum = Math.Max(1, sender.PlaybackSession.NaturalDuration.TotalSeconds);
        DrawKeyframes();
        UpdatePlaybackState();
    });

    private void UpdatePlaybackState() => PlayButton.Content = VideoView.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing ? "⏸ Pause" : "▶ Play";

    private void UpdateTimeline()
    {
        if (VideoView.Visibility != Visibility.Visible) return;
        var session = VideoView.MediaPlayer.PlaybackSession;
        _updatingTimeline = true;
        Timeline.Value = session.Position.TotalSeconds;
        TimeText.Text = $"{FormatTime(session.Position)} / {FormatTime(session.NaturalDuration)}";
        _updatingTimeline = false;
    }

    private void Timeline_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_updatingTimeline && VideoView?.MediaPlayer is not null && Timeline.IsEnabled)
            VideoView.MediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(e.NewValue);
    }

    private void KeyframeCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawKeyframes();

    private void DrawKeyframes()
    {
        KeyframeCanvas.Children.Clear();
        var duration = VideoView.MediaPlayer.PlaybackSession.NaturalDuration.TotalSeconds;
        var width = KeyframeCanvas.ActualWidth;
        if (duration <= 0 || width <= 0) return;
        foreach (var timestamp in _keyframeTimes)
        {
            var tick = new Line { X1 = 0, X2 = 0, Y1 = 2, Y2 = 14, Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 184, 77)), StrokeThickness = 2 };
            Canvas.SetLeft(tick, Math.Clamp(timestamp.TotalSeconds / duration * width, 0, width));
            KeyframeCanvas.Children.Add(tick);
        }
    }

    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
}
