using DroneLogger.Classes;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DroneLogger;

public partial class MainWindow
{
    private readonly AppViewState viewState = AppViewState.Load();

    private void RestoreViewState()
    {
        if (viewState.Configuration != null) LoadConfigToUI(viewState.Configuration);
        if (double.IsFinite(viewState.Width) && double.IsFinite(viewState.Height) &&
            viewState.Width >= 640 && viewState.Height >= 480)
        {
            Width = viewState.Width;
            Height = viewState.Height;
            var bounds = new Rect(viewState.Left, viewState.Top, Width, Height);
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (double.IsFinite(bounds.Left) && double.IsFinite(bounds.Top) && screen.IntersectsWith(bounds))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = bounds.Left;
                Top = bounds.Top;
            }
        }
        WindowState = viewState.Maximized ? WindowState.Maximized : WindowState.Normal;
        cmb_ConnectionType.SelectedIndex = Math.Clamp(viewState.ConnectionType, 0, cmb_ConnectionType.Items.Count - 1);
        if (viewState.GraphsWidth >= 240 && double.IsFinite(viewState.GraphsWidth))
        {
            MainGraphsColumn.Width = new GridLength(viewState.GraphsWidth);
            initialPanelWidthsSet = true;
        }
        if (!viewState.GraphsVisible) GraphPanelToggleButton_Click(this, new RoutedEventArgs());
        Drone3D?.RestoreCameraState(viewState.Cameras?.GetValueOrDefault("simulator"));
        CompactDrone3D?.RestoreCameraState(viewState.Cameras?.GetValueOrDefault("main"));
        if (File.Exists(viewState.FlightLogPath))
        {
            try
            {
                ShowRecordedFlight(FlightLogArchive.Load(viewState.FlightLogPath));
                flightLogFilePath.Text = viewState.FlightLogPath;
                SeekRecordedFlight(viewState.ReviewPositionMs);
            }
            catch (Exception ex) { Tools.Log(this, $"Cannot restore reviewed flight: {ex.Message}"); }
        }
        FlightReviewTabs.SelectedIndex = Math.Clamp(viewState.ReviewTab, 0, FlightReviewTabs.Items.Count - 1);
        if (MainTab.Parent is TabControl tabs)
            tabs.SelectedIndex = Math.Clamp(viewState.MainTab, 0, tabs.Items.Count - 1);
    }

    private void SaveViewState()
    {
        try
        {
            viewState.Cameras ??= new();
            if (Drone3D != null) viewState.Cameras["simulator"] = Drone3D.GetCameraState();
            if (CompactDrone3D != null) viewState.Cameras["main"] = CompactDrone3D.GetCameraState();
            if (recordedDrone3D != null) viewState.Cameras["review"] = recordedDrone3D.GetCameraState();
            try
            {
                var config = GetConfigFromUI();
                ProtocolPacketBuilder.BuildConfigurationPacket(config);
                viewState.Configuration = config;
            }
            catch (ArgumentException) { /* Keep the last readable configuration. */ }
            Rect bounds = RestoreBounds;
            if (!bounds.IsEmpty)
            {
                viewState.Left = bounds.Left; viewState.Top = bounds.Top;
                viewState.Width = bounds.Width; viewState.Height = bounds.Height;
            }
            viewState.Maximized = WindowState == WindowState.Maximized;
            viewState.MainTab = (MainTab.Parent as TabControl)?.SelectedIndex ?? 0;
            viewState.ReviewTab = FlightReviewTabs.SelectedIndex;
            viewState.ConnectionType = cmb_ConnectionType.SelectedIndex;
            viewState.GraphsVisible = MainGraphsPanel.Visibility == Visibility.Visible;
            viewState.GraphsWidth = viewState.GraphsVisible ? MainGraphsColumn.ActualWidth : expandedGraphsWidth.Value;
            viewState.FlightLogPath = File.Exists(flightLogFilePath.Text) ? flightLogFilePath.Text : null;
            viewState.ReviewPositionMs = flightLogTimeline.Value;
            viewState.Save();
        }
        catch (Exception ex) { Tools.Log(this, $"Cannot save window/camera state: {ex.Message}"); }
    }
}
