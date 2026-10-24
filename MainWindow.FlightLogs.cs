using DroneLogger.Classes;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DroneLogger;

public partial class MainWindow
{
    private FlightLogDownloader flightLogDownloader = null!;
    private CancellationTokenSource? downloadCancellation;
    private FlightLogSession? recordedFlight;
    private readonly DispatcherTimer playbackTimer = new() { Interval = TimeSpan.FromMilliseconds(20) };
    private readonly Stopwatch playbackClock = new();
    private int playbackIndex;
    private double playbackOffsetMs, playbackSpeed = 1;
    private bool playbackStarted, takeoffStartPending, takeoffStopRequested;
    private readonly DispatcherTimer downloadTimingTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Stopwatch downloadClock = new();
    private readonly DownloadTimingEstimator downloadTimingEstimator = new();
    private TimeSpan payloadStartedAt;
    private int receivedDownloadBytes, totalDownloadBytes;
    private bool downloadSucceeded;

    private void InitializeFlightLogs()
    {
        flightLogDownloader = new FlightLogDownloader((packet, ct) => transmitter.SendPackageAsync(packet, ct: ct));
        logger.FlightLogLineHandler = flightLogDownloader.AcceptLine;
        playbackTimer.Tick += PlaybackTimer_Tick;
        downloadTimingTimer.Tick += (_, _) => UpdateDownloadTiming();
        InitializeFlightLogReview();
        // Preview handles Space even when a textbox, combo or button has focus.
        AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(FlightShortcut_KeyDown), true);
        AddHandler(Keyboard.PreviewKeyUpEvent, new KeyEventHandler(FlightShortcut_KeyUp), true);
        AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(TextBox_KeyboardFocus), true);
    }

    private async void FlightShortcut_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        e.Handled = true;
        if (!e.IsRepeat && !shutdownInProgress) await ToggleTakeoffAsync();
    }

    private void FlightShortcut_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space) e.Handled = true;
    }

    private void TextBox_KeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is TextBox textBox && Keyboard.IsKeyDown(Key.Tab))
            textBox.Dispatcher.BeginInvoke(new Action(textBox.SelectAll), DispatcherPriority.Input);
    }

    internal async Task ToggleTakeoffAsync()
    {
        if (transmitter.IsTakeoffActive || takeoffStartPending)
        {
            takeoffStopRequested = true;
            try
            {
                await transmitter.DisarmAsync();
                Tools.Log(this, "Space/STOP: DISARM requested");
            }
            catch (Exception ex) { Tools.Log(this, $"Stop error: {ex.Message}"); }
            UpdateCommandControls();
            return;
        }
        if (downloadCancellation != null)
        {
            Tools.Log(this, "Finish or cancel the log download before takeoff.");
            return;
        }
        if (!transmitter.IsConnected) return;
        takeoffStartPending = true;
        takeoffStopRequested = false;
        try
        {
            PrepareLiveFlight();
            UpdateCommandControls();
            // Space starts a test from either armed idle or configured/disarmed.
            if (!transmitter.IsArmed) await transmitter.ArmAsync();
            if (takeoffStopRequested) return;
            await transmitter.TakeoffAsync();
            if (takeoffStopRequested) await transmitter.DisarmAsync();
            else Tools.Log(this, "TAKEOFF requested. Press Space again to DISARM.");
        }
        catch (Exception ex) { Tools.Log(this, $"Takeoff/stop error: {ex.Message}"); }
        finally
        {
            takeoffStartPending = false;
            UpdateCommandControls();
        }
    }

    private void PrepareLiveFlight()
    {
        StopPlayback();
        logger.ShowLiveTelemetry = true;
        logger.GetPidPlotter().Reset();
        btnPausePlots.IsChecked = false;
        btnPausePlots.Content = "Pause";
    }

    private async void btn_DownloadFlightLog_Click(object sender, RoutedEventArgs e)
    {
        if (downloadCancellation != null)
        {
            downloadCancellation.Cancel();
            lbl_FlightLogStatus.Text = "Cancelling download…";
            UpdateDownloadTiming();
            return;
        }
        if (transmitter.IsArmed || takeoffStartPending)
        {
            Tools.Log(this, "DISARM before downloading diagnostics.");
            return;
        }
        StopPlayback();
        using var cancellation = new CancellationTokenSource();
        downloadCancellation = cancellation;
        bool fullPrecision = flightLogDownloadProfile.SelectedIndex == 1;
        flightLogDownloadProfile.IsEnabled = false;
        UpdateCommandControls();
        bool receiving = true;
        downloadSucceeded = false;
        receivedDownloadBytes = totalDownloadBytes = 0;
        payloadStartedAt = TimeSpan.Zero;
        downloadTimingEstimator.Reset();
        downloadClock.Restart();
        flightLogDownloadTiming.Visibility = Visibility.Visible;
        UpdateDownloadTiming();
        downloadTimingTimer.Start();
        try
        {
            lbl_FlightLogStatus.Text = "Downloading…";
            lbl_FlightLogStatus.ToolTip = null;
            flightLogProgress.Value = 0;
            var progress = new Progress<FlightLogDownloadProgress>(value =>
            {
                // Ignore progress callbacks queued by an operation that has
                // completed, failed or been replaced by another download.
                if (!receiving || downloadCancellation != cancellation || cancellation.IsCancellationRequested) return;
                if (totalDownloadBytes == 0) payloadStartedAt = downloadClock.Elapsed;
                receivedDownloadBytes = value.ReceivedBytes;
                totalDownloadBytes = value.TotalBytes;
                flightLogProgress.Value = value.ReceivedBytes * 100.0 / value.TotalBytes;
                lbl_FlightLogStatus.Text = $"Receiving {(int)flightLogProgress.Value}%";
                UpdateDownloadTiming();
            });
            byte[] blob = await flightLogDownloader.DownloadAsync(progress, cancellation.Token, fullPrecision);
            receiving = false;
            receivedDownloadBytes = totalDownloadBytes = blob.Length;
            flightLogProgress.Value = 100;
            UpdateDownloadTiming();
            FlightLogSession session = FlightLogSession.Decode(blob);
            ShowRecordedFlight(session);
            lbl_FlightLogStatus.Text = "Saving recording…";
            foreach (var message in session.Messages)
                Tools.Log(this, $"[{message.TimestampMs} ms] {(message.IsError ? "Error" : "Information")} {message.Code}: {message.Message}");
            if (session.AverageLoopDtMs is double averageDt)
                Tools.Log(this, $"Average controller loop dt: {averageDt:F3} ms ({session.LoopIterationCount:N0} iterations since startup).");
            string folder = await Task.Run(() => FlightLogArchive.Save(blob, session));
            Tools.Log(this, $"Flight log saved: {folder}. {session.StopDescription}");
            flightLogFilePath.Text = Path.Combine(folder, "flight.flightlog");
            lbl_FlightLogStatus.Text = session.CompactFallback ? "Saved in full precision (compact range exceeded)" :
                session.IsCompact ? "Compact PID log received and saved" : "Full log received and saved";
            lbl_FlightLogStatus.ToolTip = folder;
            downloadSucceeded = true;
        }
        catch (OperationCanceledException) { lbl_FlightLogStatus.Text = "Download cancelled; onboard log is retained."; }
        catch (Exception ex)
        {
            lbl_FlightLogStatus.Text = $"Flight log: {ex.Message}";
            lbl_FlightLogStatus.ToolTip = lbl_FlightLogStatus.Text;
            Tools.Log(this, $"Flight-log download/save failed: {ex.Message}");
        }
        finally
        {
            receiving = false;
            downloadTimingTimer.Stop();
            downloadClock.Stop();
            UpdateDownloadTiming();
            downloadCancellation = null;
            flightLogDownloadProfile.IsEnabled = true;
            UpdateCommandControls();
        }
    }

    internal static string FormatDownloadDuration(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        return time.TotalHours >= 1
            ? FormattableString.Invariant($"{(long)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}")
            : time.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private void UpdateDownloadTiming()
    {
        TimeSpan elapsed = downloadClock.Elapsed;
        string remaining = "Estimating…", total = "Estimating…";
        if (!downloadClock.IsRunning)
        {
            remaining = downloadSucceeded ? "00:00" : "—";
            total = downloadSucceeded ? FormatDownloadDuration(elapsed) : "—";
        }
        else if (downloadCancellation?.IsCancellationRequested == true)
        {
            remaining = total = "—";
        }
        else if (totalDownloadBytes > 0 && receivedDownloadBytes == totalDownloadBytes)
        {
            // Verification/release and saving can still take time after the
            // last byte. Do not present that phase as already complete.
            remaining = "Finishing…";
            total = downloadTimingEstimator.EstimatedTotal is TimeSpan fixedTotal
                ? FormatDownloadDuration(fixedTotal)
                : FormatDownloadDuration(elapsed) + "+";
        }
        else
        {
            var estimate = downloadTimingEstimator.Estimate(elapsed, elapsed - payloadStartedAt,
                receivedDownloadBytes, totalDownloadBytes);
            if (estimate.Remaining is TimeSpan left && estimate.Total is TimeSpan duration)
            {
                remaining = left > TimeSpan.Zero ? FormatDownloadDuration(left) : "Taking longer…";
                total = FormatDownloadDuration(duration);
            }
        }
        flightLogDownloadTiming.Text = $"Elapsed: {FormatDownloadDuration(elapsed)}  ·  Remaining: {remaining}  ·  Est. total: {total}";
    }

    internal void ShowRecordedFlight(FlightLogSession session)
    {
        StopPlayback();
        recordedFlight = session;
        selectedFlightSample = -1;
        logger.ShowLiveTelemetry = false;
        logger.GetPidPlotter().LoadRecordedLog(session);
        logger.DisplayTelemetry(session.Samples[^1], updatePlots: false);
        btnPausePlots.IsChecked = false;
        btnPausePlots.Content = "Pause";
        PopulateFlightLogReview(session);
        lbl_FlightLogStatus.Text = "Ready";
        UpdateCommandControls();
    }

    private void btn_PlayFlightLog_Click(object sender, RoutedEventArgs e)
    {
        if (recordedFlight == null || transmitter.IsArmed || downloadCancellation != null) return;
        if (playbackTimer.IsEnabled) { PauseFlightPlayback(); return; }
        if (!playbackStarted || playbackIndex >= recordedFlight.Samples.Count)
        {
            SeekRecordedFlight(0);
            btnPausePlots.IsChecked = false;
            btnPausePlots.Content = "Pause";
        }
        playbackClock.Start();
        logger.ShowLiveTelemetry = false;
        btn_PlayFlightLog.Content = "Pause playback";
        lbl_FlightLogStatus.Text = "Playing";
        playbackTimer.Start();
        PlaybackTimer_Tick(this, EventArgs.Empty);
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs e)
    {
        if (recordedFlight == null) return;
        var samples = recordedFlight.Samples;
        double position = Math.Min(PlaybackPositionMs, samples[^1].elapsed_ms);
        while (playbackIndex < samples.Count && samples[playbackIndex].elapsed_ms <= position)
            logger.DisplayTelemetry(samples[playbackIndex++]);
        InspectFlightSample(Math.Max(0, playbackIndex - 1), position);
        if (playbackIndex == samples.Count)
        {
            StopPlayback();
            lbl_FlightLogStatus.Text = "Playback finished";
        }
    }

    internal double PlaybackPositionMs => playbackOffsetMs + playbackClock.Elapsed.TotalMilliseconds * playbackSpeed;

    private void PauseFlightPlayback()
    {
        if (!playbackTimer.IsEnabled) return;
        playbackOffsetMs = PlaybackPositionMs;
        playbackTimer.Stop();
        playbackClock.Reset();
        btn_PlayFlightLog.Content = "Resume playback";
        lbl_FlightLogStatus.Text = "Paused";
    }

    internal void SeekRecordedFlight(double positionMs)
    {
        if (recordedFlight == null || !flightLogTimeline.IsEnabled) return;
        bool resume = playbackTimer.IsEnabled;
        playbackClock.Reset();
        playbackOffsetMs = Math.Clamp(positionMs, 0, recordedFlight.Samples[^1].elapsed_ms);
        int index = recordedFlight.Samples.FindLastIndex(s => s.elapsed_ms <= playbackOffsetMs);
        index = Math.Max(0, index);
        playbackIndex = index + 1;
        playbackStarted = true;
        logger.ShowLiveTelemetry = false;
        logger.GetPidPlotter().LoadRecordedLog(recordedFlight, playbackIndex);
        logger.DisplayTelemetry(recordedFlight.Samples[index], updatePlots: false);
        InspectFlightSample(index, playbackOffsetMs);
        if (resume) playbackClock.Start();
        else
        {
            btn_PlayFlightLog.Content = "Play flight";
            lbl_FlightLogStatus.Text = "Paused";
        }
    }

    private void btn_RestartFlightLog_Click(object sender, RoutedEventArgs e) => SeekRecordedFlight(0);

    private void flightLogPlaybackSpeed_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.SelectedItem is not ComboBoxItem item ||
            !double.TryParse(item.Tag?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double speed)) return;
        playbackOffsetMs = PlaybackPositionMs;
        bool running = playbackClock.IsRunning;
        playbackClock.Reset();
        playbackSpeed = speed;
        if (running) playbackClock.Start();
    }

    private void StopPlayback()
    {
        playbackTimer.Stop();
        playbackClock.Reset();
        playbackOffsetMs = 0;
        playbackStarted = false;
        if (btn_PlayFlightLog != null) btn_PlayFlightLog.Content = "Play flight";
    }

    private void btn_OpenFlightLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Recorded flight (*.flightlog)|*.flightlog", Title = "Open recorded flight",
                InitialDirectory = Directory.Exists(FlightLogArchive.RootFolder) ? FlightLogArchive.RootFolder : ""
            };
            if (dialog.ShowDialog(this) == true)
            {
                ShowRecordedFlight(FlightLogArchive.Load(dialog.FileName));
                flightLogFilePath.Text = dialog.FileName;
                flightLogProgress.Value = 0;
                flightLogDownloadTiming.Visibility = Visibility.Collapsed;
                lbl_FlightLogStatus.Text = "File opened";
                lbl_FlightLogStatus.ToolTip = dialog.FileName;
            }
        }
        catch (Exception ex)
        {
            lbl_FlightLogStatus.Text = $"Open failed: {ex.Message}";
            lbl_FlightLogStatus.ToolTip = ex.Message;
            Tools.Log(this, $"Cannot open flight log: {ex.Message}");
        }
    }

    private void btn_FlightLogFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(FlightLogArchive.RootFolder);
            Process.Start(new ProcessStartInfo(FlightLogArchive.RootFolder) { UseShellExecute = true });
        }
        catch (Exception ex) { Tools.Log(this, $"Cannot open flight-log folder: {ex.Message}"); }
    }
}
