using DroneLogger.Classes;
using ScottPlot.WPF;
using System.Windows;
using System.Windows.Controls;

namespace DroneLogger;

public partial class MainWindow
{
    private readonly List<ScottPlot.Plottables.VerticalLine> pidReviewTimeCursors = [];

    private WpfPlot[] PidReviewPlots =>
        [flightLogPidAttitudePlot, flightLogPidRatePlot, flightLogPidTermsPlot, flightLogPidMotorsPlot];

    private void InitializePidReview()
    {
        ConfigureReviewPlot(flightLogPidAttitudePlot, "Target, measured angle and error", "deg");
        ConfigureReviewPlot(flightLogPidRatePlot, "Gyro angular rate", "deg/s");
        ConfigureReviewPlot(flightLogPidTermsPlot, "PID contributions and applied total", "%");
        ConfigureReviewPlot(flightLogPidMotorsPlot, "Motor commands", "%");
        // Link X only: angle, rate and motor/PID outputs use different units.
        foreach (var source in PidReviewPlots)
            foreach (var target in PidReviewPlots)
                if (source != target) source.Plot.Axes.Link(target, true, false);
    }

    private void DrawPidReview()
    {
        if (recordedFlight == null) return;
        bool roll = flightLogPidAxis.SelectedIndex != 1;
        string axis = roll ? "Roll" : "Pitch";
        var c = recordedFlight.Configuration;
        double target = roll ? c.TargetRoll : c.TargetPitch;
        double kp = roll ? c.RollKp : c.PitchKp;
        double ki = roll ? c.RollKi : c.PitchKi;
        double kd = roll ? c.RollKd : c.PitchKd;
        flightLogPidGains.Text = $"{axis} · recorded gains: P {kp:G}, I {ki:G}, D {kd:G} · target {target:G}°";
        flightLogPidRateStatus.Text = recordedFlight.Samples.Any(s => s.attitude_rates_valid)
            ? "Gyro rates follow the same sensor axes as the recorded roll and pitch."
            : recordedFlight.FormatVersion < 3
                ? "Gyro rates were not recorded by this older firmware. Attitude, PID and motor traces are available."
                : "Gyro rates are unavailable in this recording.";

        pidReviewTimeCursors.Clear();
        foreach (var plot in PidReviewPlots) plot.Plot.Clear();
        AddReviewSeries(flightLogPidAttitudePlot, "Measured", s => roll ? s.roll : s.pitch);
        AddReviewSeries(flightLogPidAttitudePlot, "Target", _ => target);
        AddReviewSeries(flightLogPidAttitudePlot, "Error", s => target - (roll ? s.roll : s.pitch));
        if (recordedFlight.Samples.Any(s => s.attitude_rates_valid))
            AddReviewSeries(flightLogPidRatePlot, axis + " rate", s => !s.attitude_rates_valid
                ? double.NaN : roll ? s.roll_rate_dps : s.pitch_rate_dps);
        AddReviewSeries(flightLogPidTermsPlot, "P", s => roll ? s.roll_p : s.pitch_p);
        AddReviewSeries(flightLogPidTermsPlot, "I", s => roll ? s.roll_i : s.pitch_i);
        AddReviewSeries(flightLogPidTermsPlot, "D", s => roll ? s.roll_d : s.pitch_d);
        AddReviewSeries(flightLogPidTermsPlot, "Applied total", s => roll ? s.roll_total : s.pitch_total);
        AddReviewSeries(flightLogPidMotorsPlot, "M1 · front left", s => s.m1);
        AddReviewSeries(flightLogPidMotorsPlot, "M2 · front right", s => s.m2);
        AddReviewSeries(flightLogPidMotorsPlot, "M3 · back right", s => s.m3);
        AddReviewSeries(flightLogPidMotorsPlot, "M4 · back left", s => s.m4);
        AddReviewSeries(flightLogPidMotorsPlot, "Base", s => s.base_throttle);
        foreach (var plot in PidReviewPlots)
        {
            var cursor = plot.Plot.Add.VerticalLine(0);
            cursor.Color = ScottPlot.Colors.Gray;
            pidReviewTimeCursors.Add(cursor);
        }
        ResetPidReviewView();
        if (selectedFlightSample >= 0)
        {
            var sample = recordedFlight.Samples[selectedFlightSample];
            UpdatePidReviewTimeCursors(flightLogTimeline.Value);
            UpdatePidReviewSelectedSample(sample);
        }
    }

    private void ResetPidReviewView()
    {
        if (recordedFlight == null) return;
        double end = Math.Max(0.1, recordedFlight.Samples[^1].elapsed_ms / 1000);
        foreach (var plot in PidReviewPlots)
        {
            plot.Plot.Axes.AutoScale();
            plot.Plot.Axes.SetLimitsX(0, end);
            plot.Refresh();
        }
    }

    private void UpdatePidReviewTimeCursors(double timeMs)
    {
        foreach (var cursor in pidReviewTimeCursors) cursor.X = timeMs / 1000;
        foreach (var plot in PidReviewPlots) plot.Refresh();
    }

    private void UpdatePidReviewSelectedSample(LogData sample)
    {
        if (recordedFlight == null) return;
        bool roll = flightLogPidAxis.SelectedIndex != 1;
        double angle = roll ? sample.roll : sample.pitch;
        double target = roll ? recordedFlight.Configuration.TargetRoll : recordedFlight.Configuration.TargetPitch;
        double rate = roll ? sample.roll_rate_dps : sample.pitch_rate_dps;
        double p = roll ? sample.roll_p : sample.pitch_p;
        double i = roll ? sample.roll_i : sample.pitch_i;
        double d = roll ? sample.roll_d : sample.pitch_d;
        double total = roll ? sample.roll_total : sample.pitch_total;
        string rateText = sample.attitude_rates_valid ? $"{rate:F2}°/s" : "unavailable";
        flightLogPidSelected.Text = $"{sample.elapsed_ms / 1000:F3} s · {sample.state_name} · " +
            $"angle {angle:F2}° · error {target - angle:F2}° · rate {rateText}\n" +
            $"P {p:F3} + I {i:F3} + D {d:F3} · applied total {total:F3}% · " +
            $"mixer {(sample.mixer_limited ? "limited" : "within limits")}" +
            (sample.state is 1 or 5 ? "\nMotors stopped; PID terms are from the last control update." : "");
    }

    private void flightLogPidAxis_SelectionChanged(object sender, SelectionChangedEventArgs e) => DrawPidReview();
    private void btn_ResetPidReview_Click(object sender, RoutedEventArgs e) => ResetPidReviewView();
}
