using DroneLogger.Classes;
using ScottPlot.WPF;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace DroneLogger;

public partial class MainWindow
{
    private Drone3DView? recordedDrone3D;
    private ScottPlot.Plottables.VerticalLine? signalTimeCursor, altitudeTimeCursor;
    private bool updatingFlightLogSelection;
    private int selectedFlightSample = -1;

    private void InitializeFlightLogReview()
    {
        flightLogSamples.LoadingRow += (_, e) =>
        {
            if (e.Row.Item is not LogData sample) return;
            e.Row.ClearValue(Control.BackgroundProperty);
            if (sample.state == 5 || sample.error_code != 0) e.Row.Background = Brushes.MistyRose;
            else if (sample.mixer_limited) e.Row.Background = Brushes.LemonChiffon;
            e.Row.ToolTip = FlightLogSession.DescribeError(sample.error_code);
        };
        ConfigureReviewPlot(flightLogSignalPlot, "Attitude", "deg");
        ConfigureReviewPlot(flightLogAltitudePlot, "Altitude", "cm");
        InitializePidReview();
    }

    private void PopulateFlightLogReview(FlightLogSession session)
    {
        flightLogFilePath.Text = "—";
        flightLogMessages.ItemsSource = session.Messages;
        flightLogRecording.Text = $"{session.Samples[^1].elapsed_ms / 1000:F2} s";
        flightLogSampleCount.Text = $"Flight #{session.SessionId} · {session.Samples.Count} samples";
        flightLogSampleCount.Text += session.IsCompact ? " · compact" : " · full";
        flightLogSampleCount.ToolTip = session.IsCompact
            ? "Compact PID log: yaw, yaw-rate PID and altitude PID channels are unavailable. The 3D view shows roll/pitch with a fixed heading."
            : session.CompactFallback ? "Full precision preserved because a sample exceeded the compact numeric range." : null;
        flightLogStopReason.Text = session.StopDescription;
        flightLogError.Text = $"Error {session.ErrorCode} · HAL {session.HalStatus}";
        flightLogStopReason.Foreground = session.StopReason is 1 or 4 ? Brushes.DarkSlateGray : Brushes.Firebrick;
        double[] loopTimes = session.Samples.Select(s => s.loop_dt_ms).Where(double.IsFinite).ToArray();
        flightLogLoopTiming.Text = session.AverageLoopDtMs.HasValue
            ? $"{session.AverageLoopDtMs.Value:F3} ms average"
            : "Average unavailable (older firmware)";
        flightLogLoopTiming.ToolTip = $"Average of {session.LoopIterationCount:N0} main-loop iterations since startup, captured at download. " +
            (loopTimes.Length == 0 ? "" : $"Retained sample peak: {loopTimes.Max():F2} ms.");
        flightLogRetention.Text = session.OverwrittenSamples == 0 ? "No samples overwritten" :
            $"{session.OverwrittenSamples} earlier samples overwritten";
        int altitudeValid = session.Samples.Count(s => s.altitude_valid);
        int velocityValid = session.Samples.Count(s => s.Vz_valid != 0);
        flightLogValidity.Text = $"Altitude {altitudeValid}/{session.Samples.Count} · Vz {velocityValid}/{session.Samples.Count}";
        flightLogLimits.Text = $"Mixer limited in {session.Samples.Count(s => s.mixer_limited)} samples";
        var c = session.Configuration;
        flightLogPidSettings.ItemsSource = new[]
        {
            new { Controller = "Roll", Kp = c.RollKp, Ki = c.RollKi, Kd = c.RollKd },
            new { Controller = "Pitch", Kp = c.PitchKp, Ki = c.PitchKi, Kd = c.PitchKd },
            new { Controller = "Yaw rate", Kp = c.GzKp, Ki = c.GzKi, Kd = c.GzKd },
            new { Controller = "Altitude", Kp = c.AltitudeKp, Ki = c.AltitudeKi, Kd = c.AltitudeKd }
        };
        string[] sensors = ["MPU6050", "QMC5883", "SRF05", "BMP280"];
        flightLogSettings.ItemsSource = new (string Setting, string Value)[]
        {
            ("Arm throttle", $"{c.ArmThrottle} %"),
            ("Hover throttle (configured)", $"{c.HoverThrottle} %"),
            ("Hover throttle (effective)", $"{session.EffectiveHoverThrottle:G} %"),
            ("Throttle limits", $"{c.MinThrottle} – {c.MaxThrottle} %"),
            ("Takeoff ramp", $"{c.TakeoffThrottleRampPerSecond:G} %/s"),
            ("Takeoff altitude", $"{c.TakeoffAltitudeCm} cm"),
            ("Maximum flight angle", $"{c.MaxAngle} deg"),
            ("Target roll / pitch", $"{c.TargetRoll} / {c.TargetPitch} deg"),
            ("Target yaw", $"{c.TargetYaw} deg"),
            ("Target yaw rate", $"{c.TargetGz} deg/s"),
            ("PID integral limit", $"{c.PIDMaxIPart:G} %"),
            ("PID output limit", $"{c.PIDMaxOutput:G} %"),
            ("Enabled sensors", string.Join(", ", sensors.Where((_, i) => (session.EnabledSensors & (1 << i)) != 0))),
            ("Sample interval", $"{session.SampleIntervalMs} ms"),
            ("Configuration applied", session.ConfigurationApplied ? "Yes" : "No"),
            ("Average loop dt (since startup)", session.AverageLoopDtMs is double dt ? $"{dt:F3} ms" : "Unavailable"),
            ("Loop iterations", session.LoopIterationCount.ToString("N0")),
            ("Start / end boot ticks", $"{session.StartTickMs} / {session.EndTickMs} ms"),
            ("Recording format", $"v{session.FormatVersion} · {session.RawBlob.Length:N0} bytes"),
            ("IMU trace", session.HasImuTrace ? "Recorded in full-precision samples" : "Not included; use full precision on updated firmware"),
            ("SRF05 freshness", session.HasSrf05Trace ? "Echo timestamps and accepted-measurement age recorded" : "Not included; use full precision on updated firmware"),
            ("SRF05 echo details", session.HasSrf05Details ? "Raw distance and specific filter result recorded" : "Not included in this recording"),
            ("Roll/pitch damping", session.GyroBasedDamping is bool gyroD ? (gyroD ? "Gyro rate" : "Angle difference") : "Not recorded by this firmware"),
            ("Gyro filter cutoff", session.GyroCutoffHz is int hz ? $"{hz} Hz" : "Unavailable"),
            ("Accelerometer rejection", session.AccelRejectionEnabled is bool rejection ? (rejection ? "Enabled" : "Disabled") : "Unavailable"),
            ("IMU ranges", session.EstimatorSettingsRecorded ? $"Gyro +/-{session.GyroRangeDps} deg/s; accelerometer +/-{session.AccelRangeG} g" : "Unavailable"),
            ("Estimator source signature", session.EstimatorSourceId is uint sourceId && sourceId != 0 ? $"{sourceId:X8}" : "Unavailable"),
            ("Download detail", session.IsCompact ? "Compact roll/pitch PID; yaw and altitude PID unavailable; 3D heading fixed" :
                session.CompactFallback ? "Full precision (compact range exceeded)" : "Full precision"),
            ("Stop error", FlightLogSession.DescribeError(session.ErrorCode)),
            ("HAL status", session.HalStatus.ToString(CultureInfo.InvariantCulture))
        }.Select(row => new { row.Setting, row.Value }).ToArray();
        updatingFlightLogSelection = true;
        try { flightLogTimeline.Maximum = Math.Max(1, session.Samples[^1].elapsed_ms); }
        finally { updatingFlightLogSelection = false; }
        PopulateFlightSampleTable();
        UpdateFlightRawData();
        DrawFlightReviewPlots();
        DrawPidReview();
        InspectFlightSample(session.Samples.Count - 1, session.Samples[^1].elapsed_ms);
    }

    private void UpdateFlightReviewControls()
    {
        bool loaded = recordedFlight != null;
        bool canReplay = loaded && !transmitter.IsArmed && downloadCancellation == null && !takeoffStartPending;
        btn_RestartFlightLog.IsEnabled = canReplay;
        flightLogTimeline.IsEnabled = canReplay;
        flightLogPlaybackSpeed.IsEnabled = canReplay;
        btn_ExportFlightCsv.IsEnabled = loaded;
        btn_CopyFlightRaw.IsEnabled = loaded;
    }

    private void ConfigureReviewPlot(WpfPlot view, string title, string units)
    {
        view.Plot.Title(title);
        view.Plot.XLabel("Time (s)");
        view.Plot.YLabel(units);
        view.Plot.ShowLegend();
        view.Refresh();
    }

    private void AddReviewSeries(WpfPlot view, string label, Func<LogData, double> value)
    {
        if (recordedFlight == null) return;
        double[] x = recordedFlight.Samples.Select(s => s.elapsed_ms / 1000).ToArray();
        double[] y = recordedFlight.Samples.Select(s => value(s)).Select(v => double.IsFinite(v) ? v : double.NaN).ToArray();
        if (!y.Any(double.IsFinite)) return;
        var series = view.Plot.Add.Scatter(x, y);
        series.LegendText = label;
        series.MarkerSize = 0;
    }

    private void DrawFlightReviewPlots()
    {
        if (recordedFlight == null) return;
        var plot = flightLogSignalPlot;
        plot.Plot.Clear();
        string units = "%";
        switch (flightLogSignal.SelectedIndex)
        {
            case 0:
                units = "deg";
                AddReviewSeries(plot, "Roll", s => s.roll);
                AddReviewSeries(plot, "Pitch", s => s.pitch);
                AddReviewSeries(plot, "Yaw", s => s.yaw);
                break;
            case 1:
                AddReviewSeries(plot, "M1", s => s.m1);
                AddReviewSeries(plot, "M2", s => s.m2);
                AddReviewSeries(plot, "M3", s => s.m3);
                AddReviewSeries(plot, "M4", s => s.m4);
                AddReviewSeries(plot, "Base", s => s.base_throttle);
                break;
            case 2:
                AddReviewSeries(plot, "P", s => s.roll_p);
                AddReviewSeries(plot, "I", s => s.roll_i);
                AddReviewSeries(plot, "D", s => s.roll_d);
                AddReviewSeries(plot, "Total", s => s.roll_total);
                break;
            case 3:
                AddReviewSeries(plot, "P", s => s.pitch_p);
                AddReviewSeries(plot, "I", s => s.pitch_i);
                AddReviewSeries(plot, "D", s => s.pitch_d);
                AddReviewSeries(plot, "Total", s => s.pitch_total);
                break;
            case 4:
                AddReviewSeries(plot, "P", s => s.Gz_p);
                AddReviewSeries(plot, "I", s => s.Gz_i);
                AddReviewSeries(plot, "D", s => s.Gz_d);
                break;
            case 5:
                AddReviewSeries(plot, "P", s => s.altitude_p);
                AddReviewSeries(plot, "I", s => s.altitude_i);
                AddReviewSeries(plot, "D", s => s.altitude_d);
                break;
            case 6:
                units = "m/s";
                AddReviewSeries(plot, "Vz", s => s.Vz_valid != 0 ? s.Vz : double.NaN);
                break;
            case 7:
                units = "ms";
                AddReviewSeries(plot, "Loop dt", s => s.loop_dt_ms);
                AddReviewSeries(plot, "IMU dt", s => s.imu_trace_valid ? s.imu_dt_ms : double.NaN);
                break;
            case 8:
                units = "deg";
                AddReviewSeries(plot, "Gyro prediction", s => s.imu_trace_valid ? s.roll_predicted_deg : double.NaN);
                AddReviewSeries(plot, "Final roll", s => s.imu_trace_valid ? s.roll : double.NaN);
                AddReviewSeries(plot, "Accelerometer angle", s => s.imu_trace_valid ? s.roll_accel_deg : double.NaN);
                break;
            case 9:
                units = "deg";
                AddReviewSeries(plot, "Gyro prediction", s => s.imu_trace_valid ? s.pitch_predicted_deg : double.NaN);
                AddReviewSeries(plot, "Final pitch", s => s.imu_trace_valid ? s.pitch : double.NaN);
                AddReviewSeries(plot, "Accelerometer angle", s => s.imu_trace_valid ? s.pitch_accel_deg : double.NaN);
                break;
            case 10:
                units = "deg per IMU update";
                AddReviewSeries(plot, "Roll correction", s => s.imu_trace_valid ? s.roll_correction_deg : double.NaN);
                AddReviewSeries(plot, "Pitch correction", s => s.imu_trace_valid ? s.pitch_correction_deg : double.NaN);
                break;
            case 11:
                units = "g";
                AddReviewSeries(plot, "X", s => s.imu_trace_valid ? s.accel_x_g : double.NaN);
                AddReviewSeries(plot, "Y", s => s.imu_trace_valid ? s.accel_y_g : double.NaN);
                AddReviewSeries(plot, "Z", s => s.imu_trace_valid ? s.accel_z_g : double.NaN);
                AddReviewSeries(plot, "Magnitude", s => s.accel_norm_g);
                break;
            case 12:
                units = "deg/s";
                AddReviewSeries(plot, "Roll bias", s => s.imu_trace_valid ? s.roll_bias_dps : double.NaN);
                AddReviewSeries(plot, "Pitch bias", s => s.imu_trace_valid ? s.pitch_bias_dps : double.NaN);
                break;
            case 13:
                units = "ms";
                AddReviewSeries(plot, "Time since accepted SRF05 measurement",
                    s => s.srf05_age_ms is uint age ? age : double.NaN);
                break;
        }
        ConfigureReviewPlot(plot, ((ComboBoxItem)flightLogSignal.SelectedItem).Content.ToString()!, units);
        flightLogAltitudePlot.Plot.Clear();
        AddReviewSeries(flightLogAltitudePlot, "Altitude", s => s.altitude_valid ? s.altitude : double.NaN);
        AddReviewSeries(flightLogAltitudePlot, "Target", s => s.altitude_target);
        signalTimeCursor = plot.Plot.Add.VerticalLine(0);
        altitudeTimeCursor = flightLogAltitudePlot.Plot.Add.VerticalLine(0);
        signalTimeCursor.Color = altitudeTimeCursor.Color = ScottPlot.Colors.Gray;
        foreach (var view in new[] { plot, flightLogAltitudePlot })
        {
            view.Plot.Axes.AutoScale();
            view.Plot.Axes.SetLimitsX(0, Math.Max(0.1, recordedFlight.Samples[^1].elapsed_ms / 1000));
            view.Refresh();
        }
        if (selectedFlightSample >= 0) UpdateReviewTimeCursors(recordedFlight.Samples[selectedFlightSample].elapsed_ms);
    }

    private void UpdateReviewTimeCursors(double timeMs)
    {
        if (signalTimeCursor != null) signalTimeCursor.X = timeMs / 1000;
        if (altitudeTimeCursor != null) altitudeTimeCursor.X = timeMs / 1000;
        flightLogSignalPlot.Refresh();
        flightLogAltitudePlot.Refresh();
        UpdatePidReviewTimeCursors(timeMs);
    }

    private void InspectFlightSample(int index, double positionMs)
    {
        if (recordedFlight == null) return;
        selectedFlightSample = index;
        LogData sample = recordedFlight.Samples[index];
        flightLogSelectedState.Text = sample.state_name;
        flightLogSelectedState.Foreground = sample.state == 5 || sample.error_code != 0 || sample.landing_active ? Brushes.Firebrick : Brushes.RoyalBlue;
        flightLogSelectedSample.Text = $"Sample {index + 1}/{recordedFlight.Samples.Count} · {sample.elapsed_ms / 1000:F3} s";
        flightLogSelectedValues.Text = $"Roll   {sample.roll,8:F2}°   Pitch {sample.pitch,8:F2}°\n" +
            $"Yaw    {(double.IsFinite(sample.yaw) ? sample.yaw.ToString("F2") + "°" : "N/A"),8}   Gz    {(double.IsFinite(sample.Gz) ? sample.Gz.ToString("F2") + "°/s" : "N/A"),8}\n" +
            $"Alt    {(sample.altitude_valid ? sample.altitude.ToString("F1") : "N/A"),8} cm  Vz {(sample.Vz_valid != 0 ? sample.Vz.ToString("F2") : "N/A"),8} m/s\n" +
            $"M1 {sample.m1:F1}%  M2 {sample.m2:F1}%  M3 {sample.m3:F1}%  M4 {sample.m4:F1}%\n" +
            $"Loop {sample.loop_dt_ms:F2} ms  ·  Target {sample.altitude_target:F1} cm";
        flightLogSelectedFlags.Text = $"Error {sample.error_code}: {FlightLogSession.DescribeError(sample.error_code)}\n" +
            $"Altitude hold {(sample.altitude_control_active ? "active" : "inactive")} · Mixer {(sample.mixer_limited ? "limited" : "within limits")}";
        if (sample.landing_active && sample.state is 3 or 4)
            flightLogSelectedFlags.Text += "\nGradual landing: throttle reducing; attitude stabilization active";
        if (recordedFlight.IsCompact) flightLogSelectedFlags.Text += "\nCompact log · 3D heading fixed; yaw and altitude PID unavailable";
        if (sample.imu_trace_valid)
            flightLogSelectedFlags.Text += $"\nIMU #{sample.imu_sequence} · dt {sample.imu_dt_ms:F2} ms · {sample.imu_timing_status}" +
                $"\nRoll: {sample.roll_estimator_status}\nPitch: {sample.pitch_estimator_status}";
        else flightLogSelectedFlags.Text += recordedFlight.HasImuTrace
            ? "\nIMU trace unavailable for this sensor sample"
            : "\nIMU trace unavailable · select full precision on updated firmware";
        if (sample.srf05_trace_valid)
            flightLogSelectedFlags.Text += sample.srf05_age_ms.HasValue
                ? $"\nSRF05 age {sample.srf05_age_ms} ms · last attempt: {sample.srf05_status}"
                : $"\nSRF05: no accepted measurement · last attempt: {sample.srf05_status}";
        if (sample.srf05_raw_distance_cm is double rawCm)
            flightLogSelectedFlags.Text += $"\nLast SRF05 echo at {sample.srf05_last_echo_ms} ms: {rawCm:F1} cm · {sample.srf05_filter_status}";
        flightLogSelectedFlags.Foreground = sample.error_code != 0 || sample.mixer_limited || sample.landing_active ? Brushes.Firebrick : Brushes.SlateGray;
        updatingFlightLogSelection = true;
        try
        {
            flightLogTimeline.Value = positionMs;
            if (flightLogSamples.Items.Contains(sample))
            {
                flightLogSamples.SelectedItem = sample;
                if (flightLogSamples.IsVisible) flightLogSamples.ScrollIntoView(sample);
            }
            else flightLogSamples.SelectedItem = null;
        }
        finally { updatingFlightLogSelection = false; }
        UpdateFlightPlaybackTime(positionMs);
        UpdateReviewTimeCursors(positionMs);
        UpdatePidReviewSelectedSample(sample);
        if (recordedDrone3D != null && double.IsFinite(sample.roll) && double.IsFinite(sample.pitch))
            recordedDrone3D.Update(new LogData { roll = sample.roll, pitch = sample.pitch,
                yaw = double.IsFinite(sample.yaw) ? sample.yaw : 0,
                altitude = sample.altitude_valid && double.IsFinite(sample.altitude) ? sample.altitude : 0 });
    }

    private void UpdateFlightPlaybackTime(double positionMs)
    {
        flightLogPlaybackTime.Text = $"{positionMs / 1000:F2} / {(recordedFlight?.Samples[^1].elapsed_ms ?? 0) / 1000:F2} s";
    }

    private void FlightLogDroneContainer_Loaded(object sender, RoutedEventArgs e)
    {
        if (recordedDrone3D != null) return;
        try
        {
            recordedDrone3D = new Drone3DView();
            recordedDrone3D.Initialize(FlightLogDroneContainer, showFrame: false, initialCameraDistance: 1.5,
                cameraTargetHeight: 0.3, enableMouseWheelZoom: true, enableMouseDragPan: true);
            if (recordedFlight != null && selectedFlightSample >= 0)
                InspectFlightSample(selectedFlightSample, flightLogTimeline.Value);
        }
        catch (Exception ex) { Tools.Log(this, $"Recorded 3D view: {ex.Message}"); }
    }

    private void PopulateFlightSampleTable()
    {
        if (recordedFlight == null) return;
        // Explicit columns keep the raw recorder fields and their units visible.
        (string Field, string Header, int Group, string? Format)[] columns =
        [
            ("elapsed_ms", "Time (ms)", -1, "0.0"), ("timestamp_ms", "Boot tick (ms)", 1, null),
            ("state_name", "State", -1, null), ("state", "State code", 1, null), ("error_code", "Error", -1, null),
            ("roll", "Roll (deg)", 1, "0.000"), ("pitch", "Pitch (deg)", 1, "0.000"), ("yaw", "Yaw (deg)", 1, "0.000"),
            ("Gz", "Gz (deg/s)", 1, "0.000"), ("altitude", "Altitude (cm)", 1, "0.000"), ("altitude_target", "Target (cm)", 1, "0.000"),
            ("roll_rate_dps", "Roll rate (deg/s)", 1, "0.000"), ("pitch_rate_dps", "Pitch rate (deg/s)", 1, "0.000"),
            ("attitude_rates_valid", "Gyro rates valid", 1, null),
            ("Vz", "Vz (m/s)", 1, "0.000"), ("Vz_valid", "Vz valid", 1, null), ("altitude_valid", "Altitude valid", 1, null),
            ("loop_dt_ms", "Loop dt (ms)", 1, "0.000"), ("altitude_control_active", "Altitude hold", 1, null),
            ("mixer_limited", "Mixer limited", -1, null), ("base_throttle", "Base (%)", 2, "0.000"),
            ("landing_active", "Gradual landing", -1, null),
            ("m1", "M1 (%)", 2, "0.000"), ("m2", "M2 (%)", 2, "0.000"), ("m3", "M3 (%)", 2, "0.000"), ("m4", "M4 (%)", 2, "0.000"),
            ("roll_p", "Roll P", 3, "0.0000"), ("roll_i", "Roll I", 3, "0.0000"), ("roll_d", "Roll D", 3, "0.0000"), ("roll_total", "Roll total", 3, "0.0000"),
            ("pitch_p", "Pitch P", 3, "0.0000"), ("pitch_i", "Pitch I", 3, "0.0000"), ("pitch_d", "Pitch D", 3, "0.0000"), ("pitch_total", "Pitch total", 3, "0.0000"),
            ("Gz_p", "Gz P", 3, "0.0000"), ("Gz_i", "Gz I", 3, "0.0000"), ("Gz_d", "Gz D", 3, "0.0000"),
            ("altitude_p", "Altitude P", 3, "0.0000"), ("altitude_i", "Altitude I", 3, "0.0000"), ("altitude_d", "Altitude D", 3, "0.0000"),
            ("imu_trace_valid", "IMU trace valid", 4, null), ("imu_sequence", "IMU sample", 4, null),
            ("imu_timestamp_ms", "IMU boot tick (ms)", 4, null), ("imu_dt_ms", "IMU dt (ms)", 4, "0.000"),
            ("accel_x_g", "Accel X (g)", 4, "0.0000"), ("accel_y_g", "Accel Y (g)", 4, "0.0000"),
            ("accel_z_g", "Accel Z (g)", 4, "0.0000"), ("accel_norm_g", "Accel magnitude (g)", 4, "0.0000"),
            ("roll_previous_deg", "Previous roll (deg)", 4, "0.0000"), ("roll_predicted_deg", "Roll prediction (deg)", 4, "0.0000"),
            ("roll_accel_deg", "Accel roll (deg)", 4, "0.0000"), ("roll_bias_dps", "Roll bias (deg/s)", 4, "0.0000"),
            ("roll_correction_deg", "Roll correction (deg)", 4, "0.0000"), ("roll_estimator_flags", "Roll flags", 4, null),
            ("roll_estimator_status", "Roll estimator", 4, null),
            ("pitch_previous_deg", "Previous pitch (deg)", 4, "0.0000"), ("pitch_predicted_deg", "Pitch prediction (deg)", 4, "0.0000"),
            ("pitch_accel_deg", "Accel pitch (deg)", 4, "0.0000"), ("pitch_bias_dps", "Pitch bias (deg/s)", 4, "0.0000"),
            ("pitch_correction_deg", "Pitch correction (deg)", 4, "0.0000"), ("pitch_estimator_flags", "Pitch flags", 4, null),
            ("pitch_estimator_status", "Pitch estimator", 4, null), ("imu_timing_flags", "Timing/clipping flags", 4, null),
            ("imu_timing_status", "IMU timing / clipping", 4, null),
            ("srf05_last_echo_ms", "Last SRF05 echo (boot ms)", 4, null),
            ("srf05_last_accepted_ms", "Last accepted SRF05 (boot ms)", 4, null),
            ("srf05_age_ms", "SRF05 measurement age (ms)", 4, null),
            ("srf05_status", "Last SRF05 attempt", 4, null),
            ("srf05_raw_distance_cm", "Last raw SRF05 echo (cm)", 4, "F1"),
            ("srf05_filter_status", "Last SRF05 echo filter result", 4, null),
            ("gyro_based_damping", "Gyro-based damping", 4, null)
        ];
        updatingFlightLogSelection = true;
        try
        {
            flightLogSamples.Columns.Clear();
            foreach (var column in columns.Where(c => c.Group == -1 || flightLogSampleGroup.SelectedIndex == 0 || c.Group == flightLogSampleGroup.SelectedIndex))
                flightLogSamples.Columns.Add(new DataGridTextColumn { Header = column.Header, MinWidth = 95,
                    Binding = new Binding(column.Field) { StringFormat = column.Format } });
            flightLogSamples.ItemsSource = recordedFlight.Samples.Where(s => flightLogFaultsOnly.IsChecked != true || s.state == 5 || s.error_code != 0 || s.mixer_limited || s.landing_active).ToArray();
            if (selectedFlightSample >= 0 && flightLogSamples.Items.Contains(recordedFlight.Samples[selectedFlightSample]))
                flightLogSamples.SelectedItem = recordedFlight.Samples[selectedFlightSample];
        }
        finally { updatingFlightLogSelection = false; }
    }

    private void UpdateFlightRawData()
    {
        if (recordedFlight == null) return;
        if (flightLogRawFormat.SelectedIndex == 1)
        {
            var text = new StringBuilder();
            for (int i = 0; i < recordedFlight.RawBlob.Length; i += 16)
            {
                text.Append($"{i:X6}  ");
                for (int j = i; j < Math.Min(i + 16, recordedFlight.RawBlob.Length); j++)
                    text.Append($"{recordedFlight.RawBlob[j]:X2} ");
                text.AppendLine();
            }
            flightLogRawData.Text = text.ToString();
        }
        else if (flightLogRawFormat.SelectedIndex == 2) flightLogRawData.Text = FlightLogArchive.CreateCsv(recordedFlight);
        else flightLogRawData.Text = JsonSerializer.Serialize(recordedFlight, new JsonSerializerOptions
        {
            WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        });
    }

    private void flightLogSignal_SelectionChanged(object sender, SelectionChangedEventArgs e) => DrawFlightReviewPlots();
    private void flightLogSampleGroup_SelectionChanged(object sender, SelectionChangedEventArgs e) => PopulateFlightSampleTable();
    private void flightLogFaultsOnly_Changed(object sender, RoutedEventArgs e) => PopulateFlightSampleTable();
    private void flightLogRawFormat_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateFlightRawData();

    private void flightLogSamples_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingFlightLogSelection || flightLogSamples.SelectedItem is not LogData sample || !flightLogTimeline.IsEnabled) return;
        PauseFlightPlayback();
        SeekRecordedFlight(sample.elapsed_ms);
    }

    private void flightLogTimeline_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!updatingFlightLogSelection && flightLogTimeline.IsEnabled) SeekRecordedFlight(e.NewValue);
    }

    private void btn_CopyFlightRaw_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(flightLogRawData.Text); lbl_FlightLogStatus.Text = "Raw data copied"; }
        catch (Exception ex) { lbl_FlightLogStatus.Text = $"Copy failed: {ex.Message}"; }
    }

    private void btn_ExportFlightCsv_Click(object sender, RoutedEventArgs e)
    {
        if (recordedFlight == null) return;
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Sample data (*.csv)|*.csv",
                FileName = $"flight_{recordedFlight.SessionId}_samples.csv", Title = "Export recorded samples" };
            if (dialog.ShowDialog(this) == true)
            {
                FlightLogArchive.SaveCsv(dialog.FileName, recordedFlight);
                lbl_FlightLogStatus.Text = "CSV exported";
            }
        }
        catch (Exception ex) { lbl_FlightLogStatus.Text = $"CSV export failed: {ex.Message}"; }
    }
}
