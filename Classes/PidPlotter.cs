using ScottPlot;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;

namespace DroneLogger.Classes
{
    public class PidPlotter
    {
        MainWindow context;

        private const int MAX_POINTS = 100;
        private int dataPointIndex = 0;
        private bool isPaused;
    // configurable Y axis ranges for Vz (vertical velocity)
    public double VzYMin { get; set; } = -5;
    public double VzYMax { get; set; } = 5;
        // configurable Y axis ranges for altitude and yaw (settable)
        public double AltitudeYMin { get; set; } = 0;
        public double AltitudeYMax { get; set; } = 100;
        public double YawYMin { get; set; } = 0;
        public double YawYMax { get; set; } = 360;

        private readonly List<double> rollPidX = new();
        private readonly List<double> rollP = new();
        private readonly List<double> rollI = new();
        private readonly List<double> rollD = new();
        private readonly List<double> rollT = new();

        private readonly List<double> rollX = new();
        private readonly List<double> rollActual = new();
        private readonly List<double> rollTarget = new();

        private readonly List<double> pitchPidX = new();
        private readonly List<double> pitchP = new();
        private readonly List<double> pitchI = new();
        private readonly List<double> pitchD = new();
        private readonly List<double> pitchT = new();

        private readonly List<double> pitchX = new();
        private readonly List<double> pitchActual = new();
        private readonly List<double> pitchTarget = new();

        private readonly List<double> altitudeX = new();
        private readonly List<double> altitudeValues = new();
    private readonly List<double> vzX = new();
    private readonly List<double> vzValues = new();

        private readonly List<double> yawX = new();
        private readonly List<double> yawValues = new();

        private ScottPlot.Plottables.Scatter rollPPlot = null!, rollIPlot = null!, rollDPlot = null!, rollTPlot = null!;
        private ScottPlot.Plottables.Scatter rollActualPlot = null!, rollTargetPlot = null!;
        private ScottPlot.Plottables.Scatter pitchPPlot = null!, pitchIPlot = null!, pitchDPlot = null!, pitchTPlot = null!;
        private ScottPlot.Plottables.Scatter pitchActualPlot = null!, pitchTargetPlot = null!;
        private ScottPlot.Plottables.Scatter altitudePlotObj = null!;
        private ScottPlot.Plottables.Scatter yawPlotObj = null!;
    private ScottPlot.Plottables.Scatter vzPlotObj = null!;

        private readonly ScottPlot.WPF.WpfPlot rollPlot;
        private readonly ScottPlot.WPF.WpfPlot pitchPlot;
        private readonly ScottPlot.WPF.WpfPlot rollPIDPlot;
        private readonly ScottPlot.WPF.WpfPlot pitchPIDPlot;
    private readonly ScottPlot.WPF.WpfPlot altitudePlot;
    private readonly ScottPlot.WPF.WpfPlot vzPlot;
    private readonly ScottPlot.WPF.WpfPlot yawPlot;

        public bool IsPaused
        {
            get => isPaused;
            set => isPaused = value;
        }

    public PidPlotter(MainWindow context, ScottPlot.WPF.WpfPlot rollPIDPlot, ScottPlot.WPF.WpfPlot pitchPIDPlot, ScottPlot.WPF.WpfPlot rollPlot, ScottPlot.WPF.WpfPlot pitchPlot, ScottPlot.WPF.WpfPlot altitudePlot, ScottPlot.WPF.WpfPlot vzPlot, ScottPlot.WPF.WpfPlot yawPlot)
        {
            this.context = context;

            this.rollPIDPlot = rollPIDPlot;
            this.pitchPIDPlot = pitchPIDPlot;
            this.rollPlot = rollPlot;
            this.pitchPlot = pitchPlot;
        this.altitudePlot = altitudePlot;
        this.vzPlot = vzPlot;
        this.yawPlot = yawPlot;

            InitializePlots();
        }

        private void InitializePlots()
        {
            // Roll PID plot
            rollPPlot = rollPIDPlot.Plot.Add.Scatter(rollPidX, rollP);
            rollPPlot.LegendText = "P";
            rollPPlot.Color = ScottPlot.Colors.Red;

            rollIPlot = rollPIDPlot.Plot.Add.Scatter(rollPidX, rollI);
            rollIPlot.LegendText = "I";
            rollIPlot.Color = ScottPlot.Colors.Green;

            rollDPlot = rollPIDPlot.Plot.Add.Scatter(rollPidX, rollD);
            rollDPlot.LegendText = "D";
            rollDPlot.Color = ScottPlot.Colors.Blue;

            rollTPlot = rollPIDPlot.Plot.Add.Scatter(rollPidX, rollT);
            rollTPlot.LegendText = "T";
            rollTPlot.Color = ScottPlot.Colors.Black;

            rollActualPlot = rollPlot.Plot.Add.Scatter(rollX, rollActual);
            rollActualPlot.LegendText = "Actual";
            rollActualPlot.Color = ScottPlot.Colors.Purple;

            rollTargetPlot = rollPlot.Plot.Add.Scatter(rollX, rollTarget);
            rollTargetPlot.LegendText = "Target";
            rollTargetPlot.Color = ScottPlot.Colors.Orange;

            rollPlot.Plot.Legend.IsVisible = true;
            rollPlot.Plot.Legend.Alignment = Alignment.UpperLeft;

            rollPIDPlot.Plot.Legend.IsVisible = true;
            rollPIDPlot.Plot.Legend.Alignment = Alignment.UpperLeft;

            // Pitch PID plot
            pitchPPlot = pitchPIDPlot.Plot.Add.Scatter(pitchPidX, pitchP);
            pitchPPlot.LegendText = "P";
            pitchPPlot.Color = ScottPlot.Colors.Red;

            pitchIPlot = pitchPIDPlot.Plot.Add.Scatter(pitchPidX, pitchI);
            pitchIPlot.LegendText = "I";
            pitchIPlot.Color = ScottPlot.Colors.Green;

            pitchDPlot = pitchPIDPlot.Plot.Add.Scatter(pitchPidX, pitchD);
            pitchDPlot.LegendText = "D";
            pitchDPlot.Color = ScottPlot.Colors.Blue;

            pitchTPlot = pitchPIDPlot.Plot.Add.Scatter(pitchPidX, pitchT);
            pitchTPlot.LegendText = "T";
            pitchTPlot.Color = ScottPlot.Colors.Black;

            pitchActualPlot = pitchPlot.Plot.Add.Scatter(pitchX, pitchActual);
            pitchActualPlot.LegendText = "Actual";
            pitchActualPlot.Color = ScottPlot.Colors.Purple;

            pitchTargetPlot = pitchPlot.Plot.Add.Scatter(pitchX, pitchTarget);
            pitchTargetPlot.LegendText = "Target";
            pitchTargetPlot.Color = ScottPlot.Colors.Orange;

            pitchPlot.Plot.Legend.IsVisible = true;
            pitchPlot.Plot.Legend.Alignment = Alignment.UpperLeft;

            pitchPIDPlot.Plot.Legend.IsVisible = true;
            pitchPIDPlot.Plot.Legend.Alignment = Alignment.UpperLeft;

            // Altitude plot
            altitudePlotObj = altitudePlot.Plot.Add.Scatter(altitudeX, altitudeValues);
            altitudePlotObj.LegendText = "Altitude";
            altitudePlotObj.Color = ScottPlot.Colors.CornflowerBlue;
            altitudePlot.Plot.Legend.IsVisible = true;
            altitudePlot.Plot.Legend.Alignment = Alignment.UpperLeft;
            // enforce fixed Y range for altitude
            altitudePlot.Plot.Axes.SetLimits(0, MAX_POINTS, AltitudeYMin, AltitudeYMax);

            // Vz plot (vertical velocity)
            vzPlotObj = vzPlot.Plot.Add.Scatter(vzX, vzValues);
            vzPlotObj.LegendText = "Vz";
            vzPlotObj.Color = ScottPlot.Colors.Teal;
            vzPlot.Plot.Legend.IsVisible = true;
            vzPlot.Plot.Legend.Alignment = Alignment.UpperLeft;
            // enforce fixed Y range for Vz
            vzPlot.Plot.Axes.SetLimits(0, MAX_POINTS, VzYMin, VzYMax);

            // Yaw plot
            yawPlotObj = yawPlot.Plot.Add.Scatter(yawX, yawValues);
            yawPlotObj.LegendText = "Yaw";
            yawPlotObj.Color = ScottPlot.Colors.MediumPurple;
            yawPlot.Plot.Legend.IsVisible = true;
            yawPlot.Plot.Legend.Alignment = Alignment.UpperLeft;
            // enforce fixed Y range for yaw
            yawPlot.Plot.Axes.SetLimits(0, MAX_POINTS, YawYMin, YawYMax);
        }

        public void UpdatePlots(LogData dataPoint)
        {
            if (isPaused)
                return;

            // Batch all plot updates before refreshing
            UpdateRollData(dataPoint);
            UpdatePitchData(dataPoint);

            // Single refresh for all plots
            context.Dispatcher.BeginInvoke(() =>
            {
                rollPIDPlot.Refresh();
                pitchPIDPlot.Refresh();
                rollPlot.Refresh();
                pitchPlot.Refresh();
                altitudePlot?.Refresh();
                vzPlot?.Refresh();
                yawPlot?.Refresh();
            }, DispatcherPriority.Background);

            dataPointIndex++;
        }

        private void UpdateRollData(LogData dataPoint)
        {
            AddPIDPoint(
                dataPointIndex,
                dataPoint.roll_p, dataPoint.roll_i, dataPoint.roll_d, dataPoint.roll_total,
                rollPidX, rollP, rollI, rollD, rollT,
                rollPIDPlot);

            AddAnglePoint(
                dataPointIndex,
                dataPoint.roll,
                double.Parse(context.txt_TargetRoll.Text),
                rollX,
                rollActual,
                rollTarget,
                rollActualPlot,
                rollTargetPlot,
                rollPlot);
        }

        private void UpdatePitchData(LogData dataPoint)
        {
            AddPIDPoint(
                dataPointIndex,
                dataPoint.pitch_p, dataPoint.pitch_i, dataPoint.pitch_d, dataPoint.pitch_total,
                pitchPidX, pitchP, pitchI, pitchD, pitchT,
                pitchPIDPlot);

            AddAnglePoint(
                dataPointIndex,
                dataPoint.pitch,
                double.Parse(context.txt_TargetPitch.Text),
                pitchX,
                pitchActual,
                pitchTarget,
                pitchActualPlot,
                pitchTargetPlot,
                pitchPlot);
            AddAltitudeYawPoint(dataPointIndex, dataPoint.altitude, dataPoint.Vz, dataPoint.yaw, altitudeX, altitudeValues, vzX, vzValues, yawX, yawValues, altitudePlotObj, vzPlotObj, yawPlotObj, altitudePlot, vzPlot, yawPlot);
        }

        private void AddAltitudeYawPoint(
            int index,
            double altitude,
            double vz,
            double yaw,
            List<double> altX, List<double> altList,
            List<double> vzXList, List<double> vzList,
            List<double> yawXList, List<double> yawList,
            ScottPlot.Plottables.Scatter altPlotObj, ScottPlot.Plottables.Scatter vzPlotObj, ScottPlot.Plottables.Scatter yawPlotObj,
            ScottPlot.WPF.WpfPlot altPlotControl, ScottPlot.WPF.WpfPlot vzPlotControl, ScottPlot.WPF.WpfPlot yawPlotControl)
        {
            // Altitude
            altX.Add(index);
            altList.Add(altitude);

            if (altX.Count > MAX_POINTS)
            {
                altX.RemoveAt(0);
                altList.RemoveAt(0);
            }

            double minAltX = altX.Count > 0 ? altX[0] : 0;
            double maxAltX = altX.Count > 0 ? altX[^1] : MAX_POINTS;

            // Use configurable fixed Y range for altitude
            altPlotControl.Plot.Axes.SetLimits(minAltX, maxAltX + 1, AltitudeYMin, AltitudeYMax);

            // Yaw
            yawXList.Add(index);
            yawList.Add(yaw);

            if (yawXList.Count > MAX_POINTS)
            {
                yawXList.RemoveAt(0);
                yawList.RemoveAt(0);
            }

            double minYawX = yawXList.Count > 0 ? yawXList[0] : 0;
            double maxYawX = yawXList.Count > 0 ? yawXList[^1] : MAX_POINTS;

            // Use configurable fixed Y range for yaw
            yawPlotControl.Plot.Axes.SetLimits(minYawX, maxYawX + 1, YawYMin, YawYMax);

            // Vz
            vzXList.Add(index);
            vzList.Add(vz);

            if (vzXList.Count > MAX_POINTS)
            {
                vzXList.RemoveAt(0);
                vzList.RemoveAt(0);
            }

            double minVzX = vzXList.Count > 0 ? vzXList[0] : 0;
            double maxVzX = vzXList.Count > 0 ? vzXList[^1] : MAX_POINTS;

            // Use configurable fixed Y range for Vz
            vzPlotControl.Plot.Axes.SetLimits(minVzX, maxVzX + 1, VzYMin, VzYMax);
        }

        private void AddPIDPoint(
            int index,
            double kp, double ki, double kd, double total, // add total parameter
            List<double> xList,
            List<double> pList, List<double> iList, List<double> dList, List<double> tList,
            ScottPlot.WPF.WpfPlot plotControl)
        {
            xList.Add(index);
            pList.Add(kp);
            iList.Add(ki);
            dList.Add(kd);
            tList.Add(total); // use total from LogData

            // Maintain MAX_POINTS
            if (xList.Count > MAX_POINTS)
            {
                xList.RemoveAt(0);
                pList.RemoveAt(0);
                iList.RemoveAt(0);
                dList.RemoveAt(0);
                tList.RemoveAt(0);
            }

            // Adjust visible axis range (X: scroll with new data, Y: fixed range)
            double minX = xList.Count > 0 ? xList[0] : 0;
            double maxX = xList.Count > 0 ? xList[^1] : MAX_POINTS;
            plotControl.Plot.Axes.SetLimits(minX, maxX + 1, -6, 6);
        }

        private void AddAnglePoint(
            int index,
            double actual,
            double target,
            List<double> xList,
            List<double> actualList,
            List<double> targetList,
            ScottPlot.Plottables.Scatter actualPlot,
            ScottPlot.Plottables.Scatter targetPlot,
            ScottPlot.WPF.WpfPlot plotControl,
            double yMin = -45,
            double yMax = 45)
        {
            xList.Add(index);
            actualList.Add(actual);
            targetList.Add(target);

            // Maintain MAX_POINTS
            if (xList.Count > MAX_POINTS)
            {
                xList.RemoveAt(0);
                actualList.RemoveAt(0);
                targetList.RemoveAt(0);
            }

            double minX = xList.Count > 0 ? xList[0] : 0;
            double maxX = xList.Count > 0 ? xList[^1] : MAX_POINTS;
            plotControl.Plot.Axes.SetLimits(minX, maxX + 1, yMin, yMax);
        }

        public void Reset()
        {
            dataPointIndex = 0;
            ClearData();
            isPaused = false;  // Ensure plot is not paused after reset

            rollPlot.Plot.Clear();
            rollPlot.Refresh();

            rollPIDPlot.Plot.Clear();
            rollPIDPlot.Refresh();

            pitchPlot.Plot.Clear();
            pitchPlot.Refresh();

            pitchPIDPlot.Plot.Clear();
            pitchPIDPlot.Refresh();

            altitudePlot?.Plot.Clear();
            altitudePlot?.Refresh();

            yawPlot?.Plot.Clear();
            yawPlot?.Refresh();

            InitializePlots();
        }

        private void ClearData()
        {
            rollPidX.Clear();
            rollP.Clear();
            rollI.Clear();
            rollD.Clear();
            rollT.Clear();
            rollX.Clear();
            rollActual.Clear();
            rollTarget.Clear();
            pitchPidX.Clear();
            pitchP.Clear();
            pitchI.Clear();
            pitchD.Clear();
            pitchT.Clear();
            pitchX.Clear();
            pitchActual.Clear();
            pitchTarget.Clear();
        }
    }
}
