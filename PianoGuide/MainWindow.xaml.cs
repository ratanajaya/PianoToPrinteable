using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PianoGuide;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly List<double> _marks = [];
    private string? _videoPath;
    private VideoInfo? _videoInfo;
    private MidiScore? _score;
    private CropRect _crop = CropRect.Full;
    private Point? _cropStart;
    private bool _playing;
    private bool _ignoreMarkSelection;

    public MainWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => RefreshPlaybackClock();
        _timer.Start();
    }

    private async void OpenVideo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open a piano video",
            Filter = "Video files|*.mp4;*.mov;*.mkv;*.avi;*.webm|All files|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            StatusText.Text = "Reading video...";
            var info = await VideoTools.ProbeAsync(dialog.FileName);
            Player.Close();
            _videoPath = dialog.FileName;
            _videoInfo = info;
            _crop = CropRect.Full;
            _marks.Clear();
            RefreshMarks();
            _playing = false;
            Player.Source = new Uri(dialog.FileName);
            VideoFileText.Text = Path.GetFileName(dialog.FileName);
            VideoFileText.ToolTip = dialog.FileName;
            VideoSlider.Maximum = info.DurationSeconds;
            VideoHint.Visibility = Visibility.Collapsed;
            RefreshCropOutline();
            StatusText.Text = $"Loaded {Path.GetFileName(dialog.FileName)} ({TimeText.Format(info.DurationSeconds)}).";
        }
        catch (Exception ex) { ShowError("Could not open video", ex); }
    }

    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        Player.Pause();
        Player.Position = TimeSpan.Zero;
        _playing = false;
        RefreshPlaybackClock();
        RefreshCropOutline();
    }

    private void Player_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _playing = false;
        ShowError("Windows could not play this video", e.ErrorException ??
            new InvalidDataException("Try a video encoded as H.264/AAC MP4."));
    }

    private void Player_MediaEnded(object sender, RoutedEventArgs e)
    {
        Player.Pause();
        _playing = false;
        RefreshPlaybackClock();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_videoInfo is null) return;
        if (_playing) Player.Pause(); else Player.Play();
        _playing = !_playing;
    }

    private void BackFrame_Click(object sender, RoutedEventArgs e) => SeekBy(-0.1);
    private void ForwardFrame_Click(object sender, RoutedEventArgs e) => SeekBy(0.1);

    private void SeekBy(double seconds)
    {
        if (_videoInfo is null) return;
        Player.Pause();
        _playing = false;
        SeekTo(Player.Position.TotalSeconds + seconds);
    }

    private void SeekTo(double seconds)
    {
        if (_videoInfo is null) return;
        seconds = Math.Clamp(seconds, 0, Math.Max(0, _videoInfo.DurationSeconds - 0.001));
        Player.Position = TimeSpan.FromSeconds(seconds);
        VideoSlider.Value = seconds;
        CurrentTimeText.Text = $"{TimeText.Format(seconds)} / {TimeText.Format(_videoInfo.DurationSeconds)}";
    }

    private void VideoSlider_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_videoInfo is null) return;
        Player.Pause();
        _playing = false;
        Dispatcher.BeginInvoke(new Action(() => SeekTo(VideoSlider.Value)), DispatcherPriority.Background);
    }

    private void RefreshPlaybackClock()
    {
        if (_videoInfo is null) return;
        var position = Math.Clamp(Player.Position.TotalSeconds, 0, _videoInfo.DurationSeconds);
        if (!VideoSlider.IsMouseCaptureWithin) VideoSlider.Value = position;
        CurrentTimeText.Text = $"{TimeText.Format(position)} / {TimeText.Format(_videoInfo.DurationSeconds)}";
    }

    private void AddFrame_Click(object sender, RoutedEventArgs e)
    {
        if (_videoInfo is null) { ShowMessage("Open a video first."); return; }
        Player.Pause();
        _playing = false;
        var seconds = Math.Clamp(Player.Position.TotalSeconds, 0, _videoInfo.DurationSeconds - 0.001);
        if (_marks.Any(t => Math.Abs(t - seconds) < 0.01))
        {
            ShowMessage("That moment is already selected. Seek to another moment or edit its time.");
            return;
        }
        _marks.Add(seconds);
        _marks.Sort();
        RefreshMarks(seconds);
        StatusText.Text = $"Added frame at {TimeText.Format(seconds)}. {_marks.Count} selected.";
    }

    private void MarksList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ignoreMarkSelection || MarksList.SelectedIndex < 0 || MarksList.SelectedIndex >= _marks.Count) return;
        var seconds = _marks[MarksList.SelectedIndex];
        MarkTimeText.Text = TimeText.Format(seconds);
        Player.Pause();
        _playing = false;
        SeekTo(seconds);
    }

    private void UpdateMark_Click(object sender, RoutedEventArgs e)
    {
        if (_videoInfo is null || MarksList.SelectedIndex < 0) return;
        try
        {
            var value = TimeText.Parse(MarkTimeText.Text);
            if (value >= _videoInfo.DurationSeconds) throw new ArgumentOutOfRangeException(nameof(value),
                "Timestamp must be before the video ends.");
            var selected = MarksList.SelectedIndex;
            if (_marks.Where((_, i) => i != selected).Any(t => Math.Abs(t - value) < 0.01))
                throw new ArgumentException("That timestamp is already selected.");
            _marks[selected] = value;
            _marks.Sort();
            RefreshMarks(value);
            SeekTo(value);
            StatusText.Text = "Timestamp updated.";
        }
        catch (Exception ex) { ShowError("Invalid timestamp", ex); }
    }

    private void RemoveMark_Click(object sender, RoutedEventArgs e)
    {
        if (MarksList.SelectedIndex < 0) return;
        _marks.RemoveAt(MarksList.SelectedIndex);
        RefreshMarks();
        StatusText.Text = $"{_marks.Count} frame(s) selected.";
    }

    private void RefreshMarks(double? select = null)
    {
        _ignoreMarkSelection = true;
        MarksList.ItemsSource = null;
        MarksList.ItemsSource = _marks.Select((seconds, i) =>
            $"{i + 1:00}   {TimeText.Format(seconds)}").ToList();
        MarksList.SelectedIndex = select.HasValue
            ? _marks.FindIndex(x => Math.Abs(x - select.Value) < 0.001) : -1;
        if (!select.HasValue) MarkTimeText.Text = "";
        else MarkTimeText.Text = TimeText.Format(select.Value);
        _ignoreMarkSelection = false;
    }

    private void ResetCrop_Click(object sender, RoutedEventArgs e)
    {
        _crop = CropRect.Full;
        RefreshCropOutline();
        StatusText.Text = "The full video frame will be exported.";
    }

    private void CropCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_videoInfo is null) return;
        _cropStart = ToNormalized(e.GetPosition(CropCanvas));
        CropCanvas.CaptureMouse();
    }

    private void CropCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_cropStart is null || e.LeftButton != MouseButtonState.Pressed) return;
        var point = ToNormalized(e.GetPosition(CropCanvas));
        _crop = new CropRect(Math.Min(_cropStart.Value.X, point.X),
            Math.Min(_cropStart.Value.Y, point.Y), Math.Abs(point.X - _cropStart.Value.X),
            Math.Abs(point.Y - _cropStart.Value.Y));
        RefreshCropOutline();
    }

    private void CropCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_cropStart is null) return;
        CropCanvas.ReleaseMouseCapture();
        _cropStart = null;
        if (_crop.Width < 0.02 || _crop.Height < 0.02)
        {
            _crop = CropRect.Full;
            StatusText.Text = "Crop was too small; using the full frame.";
        }
        else StatusText.Text = "Crop selected. It will be applied to every marked frame.";
        RefreshCropOutline();
    }

    private Rect VideoBounds()
    {
        if (_videoInfo is null || VideoSurface.ActualWidth <= 0 || VideoSurface.ActualHeight <= 0)
            return Rect.Empty;
        var scale = Math.Min(VideoSurface.ActualWidth / _videoInfo.Width,
            VideoSurface.ActualHeight / _videoInfo.Height);
        var width = _videoInfo.Width * scale;
        var height = _videoInfo.Height * scale;
        return new Rect((VideoSurface.ActualWidth - width) / 2,
            (VideoSurface.ActualHeight - height) / 2, width, height);
    }

    private Point ToNormalized(Point point)
    {
        var bounds = VideoBounds();
        if (bounds.IsEmpty) return new Point();
        return new Point(Math.Clamp((point.X - bounds.X) / bounds.Width, 0, 1),
            Math.Clamp((point.Y - bounds.Y) / bounds.Height, 0, 1));
    }

    private void RefreshCropOutline()
    {
        var bounds = VideoBounds();
        CropOutline.Visibility = bounds.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        if (bounds.IsEmpty) return;
        Canvas.SetLeft(CropOutline, bounds.X + _crop.X * bounds.Width);
        Canvas.SetTop(CropOutline, bounds.Y + _crop.Y * bounds.Height);
        CropOutline.Width = _crop.Width * bounds.Width;
        CropOutline.Height = _crop.Height * bounds.Height;
    }

    private void VideoSurface_SizeChanged(object sender, SizeChangedEventArgs e) => RefreshCropOutline();

    private async void ExportVideo_Click(object sender, RoutedEventArgs e)
    {
        if (_videoPath is null) { ShowMessage("Open a video first."); return; }
        if (_marks.Count == 0) { ShowMessage("Add at least one frame before exporting."); return; }
        var output = ChoosePdfPath(_videoPath, "-frames");
        if (output is null) return;
        ExportVideoButton.IsEnabled = false;
        try
        {
            StatusText.Text = "Extracting selected frames...";
            var progress = new Progress<(int Done, int Total)>(p =>
                StatusText.Text = $"Extracting frame {p.Done} of {p.Total}...");
            await VideoTools.ExportAsync(_videoPath, _marks.ToList(), _crop, output, progress);
            StatusText.Text = $"Saved video PDF: {output}";
            MessageBox.Show(this, $"Saved {Path.GetFileName(output)} with {_marks.Count} frame(s).",
                "Export complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { ShowError("Video export failed", ex); }
        finally { ExportVideoButton.IsEnabled = true; }
    }

    private void OpenMidi_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open a MIDI file",
            Filter = "MIDI files|*.mid;*.midi|All files|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _score = MidiReader.Read(dialog.FileName);
            MidiFileText.Text = Path.GetFileName(dialog.FileName);
            MidiFileText.ToolTip = dialog.FileName;
            MidiSummaryText.Text = $"{_score.Notes.Count} notes · {_score.StepCount} steps · " +
                $"{_score.Tracks.Count} note track(s) · {TimeText.Format(_score.DurationSeconds)}";
            MidiStartText.Text = "00:00.000";
            MidiEndText.Text = TimeText.Format(_score.DurationSeconds);
            RenderTrackAssignments();
            StatusText.Text = $"Loaded {Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception ex) { ShowError("Could not open MIDI", ex); }
    }

    private void RenderTrackAssignments()
    {
        TrackPanel.Children.Clear();
        if (_score is null) return;
        foreach (var track in _score.Tracks)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            var label = new TextBlock
            {
                Text = $"{track.Name}  ({track.NoteCount} notes)",
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 12, 0)
            };
            var selector = new ComboBox
            {
                ItemsSource = new[] { "Unassigned", "Left hand", "Right hand" },
                SelectedIndex = (int)track.Hand,
                Tag = track,
                Padding = new Thickness(7, 5, 7, 5)
            };
            selector.SelectionChanged += (_, _) =>
            {
                if (selector.Tag is MidiTrackInfo info && selector.SelectedIndex >= 0)
                    info.Hand = (Hand)selector.SelectedIndex;
            };
            Grid.SetColumn(selector, 1);
            row.Children.Add(label);
            row.Children.Add(selector);
            TrackPanel.Children.Add(row);
        }
    }

    private async void ExportMidi_Click(object sender, RoutedEventArgs e)
    {
        if (_score is null) { ShowMessage("Open a MIDI file first."); return; }
        try
        {
            var start = TimeText.Parse(MidiStartText.Text);
            var end = TimeText.Parse(MidiEndText.Text);
            var output = ChoosePdfPath(_score.SourcePath, "-steps");
            if (output is null) return;
            ExportMidiButton.IsEnabled = false;
            TrackPanel.IsEnabled = false;
            MidiStartText.IsEnabled = false;
            MidiEndText.IsEnabled = false;
            try
            {
                StatusText.Text = "Drawing keyboard steps...";
                var score = _score;
                var count = await Task.Run(() => PdfExports.ExportMidi(score, output, start, end));
                StatusText.Text = $"Saved {count} MIDI steps: {output}";
                MessageBox.Show(this, $"Saved {Path.GetFileName(output)} with {count} steps.",
                    "Export complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                ExportMidiButton.IsEnabled = true;
                TrackPanel.IsEnabled = true;
                MidiStartText.IsEnabled = true;
                MidiEndText.IsEnabled = true;
            }
        }
        catch (Exception ex) { ShowError("MIDI export failed", ex); }
    }

    private string? ChoosePdfPath(string source, string suffix)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save printable guide",
            Filter = "PDF document|*.pdf",
            DefaultExt = ".pdf",
            AddExtension = true,
            FileName = Path.GetFileNameWithoutExtension(source) + suffix + ".pdf"
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void ShowMessage(string message)
    {
        StatusText.Text = message;
        MessageBox.Show(this, message, "Printable Piano Guide", MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ShowError(string title, Exception exception)
    {
        StatusText.Text = $"{title}: {exception.Message}";
        MessageBox.Show(this, exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
