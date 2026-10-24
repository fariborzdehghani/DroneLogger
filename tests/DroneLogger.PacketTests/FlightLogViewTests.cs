using DroneLogger.Classes;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace DroneLogger.PacketTests;

public class FlightLogViewTests
{
    [Fact]
    public void PidReview_UsesSavedTargetsRatesAndSharedCursors_AndSupportsOlderLogs()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var transport = new FakeBridgeTransport();
                using var connection = new SerialConnectionService(transport);
                var window = new MainWindow(connection);
                var session = FlightLogSession.Decode(File.ReadAllBytes(
                    Path.Combine(AppContext.BaseDirectory, "Fixtures", "flight-log-v3.bin")));
                session.Configuration.TargetRoll = 5;
                session.Configuration.TargetPitch = -3;
                window.txt_TargetRoll.Text = "unapplied editing text";
                window.ShowRecordedFlight(session);
                var attitude = window.flightLogPidAttitudePlot.Plot;
                var target = attitude.GetPlottables<ScottPlot.Plottables.Scatter>().Single(p => p.LegendText == "Target");
                var error = attitude.GetPlottables<ScottPlot.Plottables.Scatter>().Single(p => p.LegendText == "Error");
                Assert.All(target.Data.GetScatterPoints(), point => Assert.Equal(5, point.Y));
                Assert.Equal(4.75, error.Data.GetScatterPoints().First().Y);
                Assert.Contains("27.25", window.flightLogPidSelected.Text);
                Assert.Single(window.flightLogPidRatePlot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>());
                Assert.Equal(5, window.flightLogPidMotorsPlot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>().Count());
                window.SeekRecordedFlight(15);
                foreach (var plot in new[] { window.flightLogPidAttitudePlot, window.flightLogPidRatePlot,
                    window.flightLogPidTermsPlot, window.flightLogPidMotorsPlot })
                    Assert.Equal(0.015, Assert.Single(plot.Plot.GetPlottables<ScottPlot.Plottables.VerticalLine>()).X);
                Assert.Contains("0.010 s", window.flightLogPidSelected.Text); // Actual preceding sample.
                window.flightLogPidAxis.SelectedIndex = 1;
                Assert.All(attitude.GetPlottables<ScottPlot.Plottables.Scatter>().Single(p => p.LegendText == "Target")
                    .Data.GetScatterPoints(), point => Assert.Equal(-3, point.Y));
                Assert.Contains("28.25", window.flightLogPidSelected.Text);
                Assert.Contains("Pitch", window.flightLogPidGains.Text);
                Assert.Equal(0.015, Assert.Single(window.flightLogPidTermsPlot.Plot
                    .GetPlottables<ScottPlot.Plottables.VerticalLine>()).X);

                // Axis links must move time only, preserving the other plots' units.
                double rateBottom = window.flightLogPidRatePlot.Plot.Axes.GetLimits().Bottom;
                attitude.Axes.SetLimitsX(0.002, 0.018);
                attitude.GetImage(800, 220);
                Assert.Equal(0.002, window.flightLogPidRatePlot.Plot.Axes.GetLimits().Left, 6);
                Assert.Equal(rateBottom, window.flightLogPidRatePlot.Plot.Axes.GetLimits().Bottom);

                var legacy = FlightLogSession.Decode(File.ReadAllBytes(
                    Path.Combine(AppContext.BaseDirectory, "Fixtures", "flight-log-v1.bin")));
                window.ShowRecordedFlight(legacy);
                Assert.Empty(window.flightLogPidRatePlot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>());
                Assert.Contains("older firmware", window.flightLogPidRateStatus.Text);
                Assert.Empty(transport.Writes);

                var compact = FlightLogSession.Decode(File.ReadAllBytes(
                    Path.Combine(AppContext.BaseDirectory, "Fixtures", "flight-log-v4.bin")));
                window.ShowRecordedFlight(compact);
                Assert.Contains("compact", window.flightLogSampleCount.Text);
                Assert.Contains("heading fixed", window.flightLogSelectedFlags.Text);
                Assert.DoesNotContain("NaN", window.flightLogSelectedValues.Text);
                Assert.Equal("N/A", window.lbl_Yaw.Content);
                Assert.Equal("N/A", window.lbl_Altitude_P.Content);
                Assert.Single(window.flightLogPidRatePlot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>());
                Assert.Equal(4, window.flightLogPidTermsPlot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>().Count());
                window.flightLogSignal.SelectedIndex = 4; // Omitted yaw PID is empty, never a false zero.
                Assert.Empty(window.flightLogSignalPlot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>());
                window.flightLogSignal.SelectedIndex = 0;
                Assert.Equal(2, window.flightLogSignalPlot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>().Count());
                Assert.Empty(transport.Writes);

                var trace = FlightLogSession.Decode(ImuTraceTests.Fixture());
                window.ShowRecordedFlight(trace);
                window.flightLogSignal.SelectedIndex = 8;
                var estimatorLines = window.flightLogSignalPlot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>();
                Assert.Equal(3, estimatorLines.Count());
                Assert.Equal(.25, estimatorLines.Single(s => s.LegendText == "Final roll").Data.GetScatterPoints().First().Y);
                window.flightLogSignal.SelectedIndex = 9;
                Assert.Equal(3, window.flightLogSignalPlot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>().Count());
                window.flightLogSignal.SelectedIndex = 10;
                Assert.Equal(2, window.flightLogSignalPlot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>().Count());
                window.flightLogSignal.SelectedIndex = 11;
                Assert.Equal(4, window.flightLogSignalPlot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>().Count());
                window.flightLogSignal.SelectedIndex = 12;
                Assert.Equal(2, window.flightLogSignalPlot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>().Count());
                window.flightLogSampleGroup.SelectedIndex = 4;
                Assert.Contains(window.flightLogSamples.Columns, c => c.Header.ToString() == "Roll prediction (deg)");
                Assert.DoesNotContain(window.flightLogSamples.Columns, c => c.Header.ToString() == "M1 (%)");
                window.SeekRecordedFlight(0);
                Assert.Contains("IMU #42", window.flightLogSelectedFlags.Text);
                Assert.Contains("Correction applied", window.flightLogSelectedFlags.Text);
                trace.Samples.ForEach(s => s.imu_trace_valid = false);
                window.ShowRecordedFlight(trace);
                Assert.Empty(window.flightLogSignalPlot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>());
                Assert.Contains("IMU trace unavailable", window.flightLogSelectedFlags.Text);
                window.flightLogSignal.SelectedIndex = 13;
                Assert.Empty(window.flightLogSignalPlot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>());
                window.ShowRecordedFlight(FlightLogSession.Decode(Srf05TraceTests.Fixture()));
                var freshness = Assert.Single(window.flightLogSignalPlot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>());
                Assert.Equal(new double[] { 300, 310, 320 }, freshness.Data.GetScatterPoints().Select(p => p.Y));
                Assert.Contains(window.flightLogSamples.Columns, c => c.Header.ToString() == "SRF05 measurement age (ms)");
                window.SeekRecordedFlight(20);
                Assert.Contains("SRF05 age 320 ms", window.flightLogSelectedFlags.Text);
                Assert.Contains("last attempt: Echo timeout", window.flightLogSelectedFlags.Text);
                window.ShowRecordedFlight(FlightLogSession.Decode(Srf05DetailsTests.Fixture()));
                window.SeekRecordedFlight(10);
                Assert.Contains("Last SRF05 echo at 995 ms: 300.0 cm", window.flightLogSelectedFlags.Text);
                Assert.Contains("Rejected by median motion gate", window.flightLogSelectedFlags.Text);
                Assert.Contains("last attempt: Echo timeout", window.flightLogSelectedFlags.Text);
                Assert.Contains(window.flightLogSamples.Columns, c => c.Header.ToString() == "Last raw SRF05 echo (cm)");
                byte[] landing = Srf05DetailsTests.Fixture(); landing[128 + 5] |= 32;
                window.ShowRecordedFlight(FlightLogSession.Decode(landing));
                window.SeekRecordedFlight(0);
                Assert.Equal("LANDING", window.flightLogSelectedState.Text);
                Assert.Contains("throttle reducing", window.flightLogSelectedFlags.Text);
                Assert.Contains(window.flightLogSamples.Columns, c => c.Header.ToString() == "Gradual landing");
                window.ShowRecordedFlight(FlightLogSession.Decode(ImuTraceTests.Fixture()));
                window.flightLogSignal.SelectedIndex = 10;
                Assert.Empty(transport.Writes);

                // Optional visual QA renders the real control tree without opening
                // a hardware connection or replacing a user's running application.
                string? preview = Environment.GetEnvironmentVariable("DRONELOGGER_PID_REVIEW_PREVIEW");
                bool overviewPreview = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DRONELOGGER_FLIGHT_LOG_PREVIEW"));
                if (!string.IsNullOrEmpty(preview))
                {
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DRONELOGGER_DOWNLOAD_TIMING_PREVIEW")))
                    {
                        window.flightLogProgress.Value = 35;
                        window.lbl_FlightLogStatus.Text = "Receiving 35%";
                        window.flightLogDownloadTiming.Visibility = Visibility.Visible;
                        var times = new DownloadTimingEstimator().Estimate(TimeSpan.FromSeconds(83), TimeSpan.FromSeconds(80), 3500, 10000);
                        window.flightLogDownloadTiming.Text = $"Elapsed: 01:23  ·  Remaining: {MainWindow.FormatDownloadDuration(times.Remaining!.Value)}  ·  Est. total: {MainWindow.FormatDownloadDuration(times.Total!.Value)}";
                    }
                    string? recording = Environment.GetEnvironmentVariable("DRONELOGGER_PID_REVIEW_LOG");
                    if (!string.IsNullOrEmpty(recording)) window.ShowRecordedFlight(FlightLogArchive.Load(recording));
                    window.flightLogPidAxis.SelectedIndex = 0;
                    var previewTab = overviewPreview ? window.FlightLogsTab : window.FlightLogPidReviewTab;
                    previewTab.IsSelected = true;
                    var page = (FrameworkElement)previewTab.Content;
                    previewTab.Content = null;
                    var previewHost = new Window { Content = page, Width = 1150, Height = 1400,
                        WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false,
                        Left = -20000, Top = -20000, Background = System.Windows.Media.Brushes.White,
                        Resources = window.Resources, FontFamily = window.FontFamily };
                    previewHost.Show();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    page.UpdateLayout();
                    var image = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        (int)Math.Ceiling(page.ActualWidth), (int)Math.Ceiling(page.ActualHeight), 96, 96,
                        System.Windows.Media.PixelFormats.Pbgra32);
                    image.Render(page);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                    using var file = File.Create(preview);
                    encoder.Save(file);
                    previewHost.Content = null;
                    previewHost.Close();
                    previewTab.Content = page;
                }
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "PID review test did not finish.");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void ReviewSeekingAndRawDataPreserveSamplesAndSendNoCommands()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var transport = new FakeBridgeTransport();
                using var connection = new SerialConnectionService(transport);
                var window = new MainWindow(connection);
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                byte[] blob = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "flight-log-v1.bin"));
                var session = FlightLogSession.Decode(blob);
                window.ShowRecordedFlight(session);
                Assert.Equal(3, window.flightLogSamples.Items.Count);
                Assert.Equal(session.StopDescription, window.flightLogStopReason.Text);
                Assert.Equal("FAULT", window.flightLogSelectedState.Text);
                Assert.True(window.btn_PlayFlightLog.IsEnabled); // Offline replay is available.

                // A time between samples selects the preceding sample. Seeking
                // backwards must replace the graph history, not append to it.
                window.SeekRecordedFlight(75);
                Assert.Equal(3.5, double.Parse(window.lbl_Roll.Content.ToString()!));
                Assert.Equal(75, window.flightLogTimeline.Value);
                Assert.Equal(2, window.rollValuePlot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>()
                    .Single(p => p.LegendText == "Actual").Data.GetScatterPoints().Count());
                window.SeekRecordedFlight(0);
                Assert.Single(window.rollValuePlot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>()
                    .Single(p => p.LegendText == "Actual").Data.GetScatterPoints());
                window.flightLogSamples.SelectedItem = session.Samples[2];
                Assert.Equal(100, window.flightLogTimeline.Value);
                Assert.Equal("FAULT", window.flightLogSelectedState.Text);
                window.flightLogRawFormat.SelectedIndex = 1;
                Assert.StartsWith("000000  46 4C 4F 47", window.flightLogRawData.Text);
                window.flightLogRawFormat.SelectedIndex = 2;
                Assert.Equal(FlightLogArchive.CreateCsv(session), window.flightLogRawData.Text);
                window.flightLogRawFormat.SelectedIndex = 0;
                Assert.Contains("\"ErrorCode\": 7", window.flightLogRawData.Text);
                Assert.DoesNotContain("\"RawBlob\"", window.flightLogRawData.Text);
                Assert.Equal(blob, session.RawBlob);
                Assert.Equal(3.5, session.Samples[1].roll);
                Assert.Empty(transport.Writes);

                // Replacing a long log with a shorter one clears a stale selection.
                var shorter = FlightLogSession.Decode(blob);
                shorter.Samples.RemoveRange(1, 2);
                window.ShowRecordedFlight(shorter);
                Assert.Single(window.flightLogSamples.Items.Cast<object>());
                Assert.Equal(0, window.flightLogTimeline.Value);

                // Playback pauses without losing time, and changing speed while
                // paused must not move the selected position. Resume completes
                // through the actual DispatcherTimer and still sends no commands.
                var longer = FlightLogSession.Decode(blob);
                longer.Samples.Clear();
                for (int i = 0; i < 32; i++) longer.Samples.Add(new LogData { elapsed_ms = i * 50, roll = i });
                window.ShowRecordedFlight(longer);
                window.btn_PlayFlightLog.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Assert.Equal("Pause playback", window.btn_PlayFlightLog.Content);
                window.btn_PlayFlightLog.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Assert.Equal("Resume playback", window.btn_PlayFlightLog.Content);
                double pausedAt = window.PlaybackPositionMs;
                window.flightLogPlaybackSpeed.SelectedIndex = 3;
                Assert.Equal(pausedAt, window.PlaybackPositionMs);
                window.SeekRecordedFlight(1520);
                window.btn_PlayFlightLog.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                PumpUntilFinished(Task.Delay(250));
                Assert.Equal("Playback finished", window.lbl_FlightLogStatus.Text);
                Assert.Equal(1550, window.flightLogTimeline.Value);
                Assert.Equal("Play flight", window.btn_PlayFlightLog.Content);
                Assert.Empty(transport.Writes);
                var diagnostic = FlightLogSession.Decode(File.ReadAllBytes(
                    Path.Combine(AppContext.BaseDirectory, "Fixtures", "diagnostics-v2.bin")));
                window.ShowRecordedFlight(diagnostic);
                Assert.Equal(16, window.flightLogMessages.Items.Count);
                Assert.Contains("2.000 ms average", window.flightLogLoopTiming.Text);
                Assert.Contains("TAKEOFF rejected", window.flightLogRawData.Text);
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Flight review test did not finish.");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void RecordedGraphsUseTimeAndSavedTargets_AndResetAllSeries()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var transport = new FakeBridgeTransport();
                using var connection = new SerialConnectionService(transport);
                var window = new MainWindow(connection); // No window is shown and no physical port is opened.
                var session = FlightLogSession.Decode(File.ReadAllBytes(
                    Path.Combine(AppContext.BaseDirectory, "Fixtures", "flight-log-v1.bin")));
                session.Configuration.TargetRoll = 5;
                session.Samples.Clear();
                for (int i = 0; i < 256; i++)
                    session.Samples.Add(new LogData { elapsed_ms = i * 50, roll = i / 10.0,
                        altitude = i, Vz = 2, Vz_valid = 0 });
                window.txt_TargetRoll.Text = "invalid editing text";
                var plotter = window.sessionManager.logger.GetPidPlotter();
                plotter.LoadRecordedLog(session);
                Assert.Equal(12.75, window.rollValuePlot.Plot.Axes.GetLimits().Right, 5);
                Assert.Equal(12.75, window.altitudePlot.Plot.Axes.GetLimits().Right, 5);
                var actual = window.rollValuePlot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>()
                    .Single(plot => plot.LegendText == "Actual");
                var target = window.rollValuePlot.Plot.GetPlottables<ScottPlot.Plottables.Scatter>()
                    .Single(plot => plot.LegendText == "Target");
                Assert.Equal(256, actual.Data.GetScatterPoints().Count());
                Assert.All(target.Data.GetScatterPoints(), point => Assert.Equal(5, point.Y));
                // These checks exercise actual plot loading and resetting, including
                // invalid Vz flags and a full recorder rather than the live 100-point window.
                plotter.Reset();
                plotter.UpdatePlots(new LogData { altitude = 1, Vz_valid = 1 });
                Assert.Equal(1, window.altitudePlot.Plot.Axes.GetLimits().Right, 5);
                Assert.Equal(1, window.vzPlot.Plot.Axes.GetLimits().Right, 5);
                window.sessionManager.logger.DisplayTelemetry(session.Samples[0], updatePlots: false);
                Assert.Equal("N/A", window.lbl_Vz.Content);
                Assert.Equal(2, session.Samples[0].Vz);

                // Exercise the real UI toggle while the ARM write is still
                // outstanding. A second press must stop it before TAKEOFF.
                window.chk_HeartbeatEnabled.IsChecked = false;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                connection.ConnectAsync("TEST", CancellationToken.None).GetAwaiter().GetResult();
                Task start = window.ToggleTakeoffAsync();
                transport.FirstArmStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                Task stop = window.ToggleTakeoffAsync();
                transport.ReleaseArm.TrySetResult();
                PumpUntilFinished(Task.WhenAll(start, stop));
                Assert.Equal(new byte[] { 1, 0 }, transport.Writes.Select(packet => packet[1]).ToArray());

                // A subsequent pair of ordinary toggles sends ARM, TAKEOFF,
                // then DISARM and never replays commands from the log.
                transport.Writes.Clear();
                PumpUntilFinished(StartThenStop());
                Assert.Equal(new byte[] { 1, 2, 0 }, transport.Writes.Select(packet => packet[1]).ToArray());
                async Task StartThenStop()
                {
                    await window.ToggleTakeoffAsync();
                    Assert.False(window.btn_DownloadFlightLog.IsEnabled);
                    int sent = transport.Writes.Count;
                    // Even a programmatically raised click must not disarm or
                    // request a download while armed.
                    window.btn_DownloadFlightLog.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Assert.Equal(sent, transport.Writes.Count);
                    await window.ToggleTakeoffAsync();
                    Assert.True(window.btn_DownloadFlightLog.IsEnabled);
                }
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "WPF view test did not finish.");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void DownloadTiming_UpdatesWhileWaitingAndFreezesAfterCancellation()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var transport = new FakeBridgeTransport();
                using var connection = new SerialConnectionService(transport);
                var window = new MainWindow(connection);
                window.chk_HeartbeatEnabled.IsChecked = false;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                PumpUntilFinished(connection.ConnectAsync("TEST", CancellationToken.None));
                Assert.Equal(Visibility.Collapsed, window.flightLogDownloadTiming.Visibility);
                window.btn_DownloadFlightLog.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Assert.Equal(Visibility.Visible, window.flightLogDownloadTiming.Visibility);
                Assert.Contains("Remaining: Estimating…", window.flightLogDownloadTiming.Text);
                PumpUntilFinished(Task.Delay(1300));
                Assert.DoesNotContain("Elapsed: 00:00", window.flightLogDownloadTiming.Text);
                Assert.Equal(0, window.flightLogProgress.Value);
                window.btn_DownloadFlightLog.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Assert.Contains("Remaining: —", window.flightLogDownloadTiming.Text);
                PumpUntilFinished(WaitForDownloadToStop());
                Assert.Contains("cancelled", window.lbl_FlightLogStatus.Text);
                Assert.Contains("Remaining: —", window.flightLogDownloadTiming.Text);
                Assert.Contains("Est. total: —", window.flightLogDownloadTiming.Text);
                string stopped = window.flightLogDownloadTiming.Text;
                PumpUntilFinished(Task.Delay(300));
                Assert.Equal(stopped, window.flightLogDownloadTiming.Text);
                Dispatcher.CurrentDispatcher.InvokeShutdown();

                async Task WaitForDownloadToStop()
                {
                    while (window.btn_DownloadFlightLog.Content.ToString() == "Cancel download")
                        await Task.Delay(20);
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Download timing test did not finish.");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void PumpUntilFinished(Task task)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) => frame.Continue = false;
        task.ContinueWith(_ => Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => frame.Continue = false)),
            TaskScheduler.FromCurrentSynchronizationContext());
        timer.Start();
        if (!task.IsCompleted) Dispatcher.PushFrame(frame);
        timer.Stop();
        Assert.True(task.IsCompleted, "Command toggle did not finish.");
        task.GetAwaiter().GetResult();
    }

    private sealed class FakeBridgeTransport : ISerialTransport
    {
        private bool firstArm = true;
        public List<byte[]> Writes { get; } = [];
        public TaskCompletionSource FirstArmStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseArm { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsOpen { get; private set; }
        public event EventHandler<string>? LineReceived { add { } remove { } }
        public event EventHandler? ConnectionLost { add { } remove { } }
        public Task OpenAsync(string portName, CancellationToken ct) { IsOpen = true; return Task.CompletedTask; }
        public Task CloseAsync() { IsOpen = false; return Task.CompletedTask; }
        public async Task WriteAsync(byte[] packet, CancellationToken ct)
        {
            Writes.Add(packet.ToArray());
            if (firstArm && packet[0] == 2 && packet[1] == 1)
            {
                firstArm = false;
                FirstArmStarted.TrySetResult();
                await ReleaseArm.Task.WaitAsync(ct);
            }
        }
        public void Dispose() { }
    }
}
