using ScottPlot;
using System.Collections.Generic;
using System.Windows.Threading;

namespace DroneLogger.Classes
{
    public class PidPlotter
    {
        MainWindow context;

        private const int MAX_POINTS = 100;
        private int dataPointIndex = 0;
        private bool isPaused;

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

        private ScottPlot.Plottables.Scatter rollPPlot, rollIPlot, rollDPlot, rollTPlot;
        private ScottPlot.Plottables.Scatter rollActualPlot, rollTargetPlot;
        private ScottPlot.Plottables.Scatter pitchPPlot, pitchIPlot, pitchDPlot, pitchTPlot;
        private ScottPlot.Plottables.Scatter pitchActualPlot, pitchTargetPlot;

        private readonly ScottPlot.WPF.WpfPlot rollPlot;
        private readonly ScottPlot.WPF.WpfPlot pitchPlot;
        private readonly ScottPlot.WPF.WpfPlot rollPIDPlot;
        private readonly ScottPlot.WPF.WpfPlot pitchPIDPlot;

        public bool IsPaused
        {
            get => isPaused;
            set => isPaused = value;
        }

        public PidPlotter(MainWindow context, ScottPlot.WPF.WpfPlot rollPIDPlot, ScottPlot.WPF.WpfPlot pitchPIDPlot, ScottPlot.WPF.WpfPlot rollPlot, ScottPlot.WPF.WpfPlot pitchPlot)
        {
            this.context = context;

            this.rollPIDPlot = rollPIDPlot;
            this.pitchPIDPlot = pitchPIDPlot;
            this.rollPlot = rollPlot;
            this.pitchPlot = pitchPlot;

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