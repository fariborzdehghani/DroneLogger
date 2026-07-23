using DroneLogger.Classes;
using DroneLogger.Model;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace DroneLogger
{
    public partial class MainWindow : Window
    {
        private Transmitter transmitter;
        private Logger logger;
        public SessionManager sessionManager;
        private bool shutdownInProgress;
        private bool shutdownComplete;
        // 3D view helper
        public Drone3DView? Drone3D { get; private set; }

        public MainWindow()
        {
            InitializeComponent();
            transmitter = new Transmitter(this);
            logger = new Logger(this);
            transmitter.AttachLogger(logger);
            sessionManager = new SessionManager(logger);

            // Wire transmitter events to update UI automatically
            transmitter.ConnectionStateChanged += (s, state) =>
            {
                DispatchUiUpdate(() =>
                {
                    if (!transmitter.IsSerialMode)
                    {
                        btn_TransmitPortConnect.Content =
                            state == ConnectionState.Connected ? "Disconnect" : "Connect";
                    }
                    UpdateCommandControls();
                });
            };

            transmitter.ArmedStateChanged += (s, armed) =>
            {
                DispatchUiUpdate(() =>
                {
                    UpdateCommandControls();
                    if (!armed)
                    {
                        lbl_HeartbeatWarning.Visibility = (chk_HeartbeatEnabled.IsChecked == false) ? Visibility.Visible : Visibility.Collapsed;
                    }
                    else
                    {
                        lbl_HeartbeatWarning.Visibility = Visibility.Collapsed;
                    }
                });
            };

            logger.PortStateChanged += (_, _) =>
                DispatchUiUpdate(UpdateCommandControls);

            // Add keyboard support
            this.KeyDown += Window_KeyDown;

            DataContext = this;

            transmitter.SetSerialMode(IsSerialMode());
        }

        private void DispatchUiUpdate(Action update)
        {
            if (shutdownInProgress || shutdownComplete ||
                Dispatcher.HasShutdownStarted)
            {
                return;
            }

            Dispatcher.BeginInvoke(() =>
            {
                if (!shutdownInProgress && !shutdownComplete)
                {
                    update();
                }
            });
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            txt_Log.Document.LineHeight = 2;

            transmitter.FillTransmitPortsList();
            logger.FillLogPortsList();

            UpdateCommandControls();

            // Load configuration if it exists, otherwise keep XAML initial values
            var savedConfig = ConfigurationManager.LoadConfiguration();
            if (savedConfig != null)
            {
                LoadConfigToUI(savedConfig);
            }

            // initialize 3D view (you can pass initial altitude, yaw, pitch, roll)
            Drone3D = new Drone3DView();
            // Example: altitude=0, yaw=0, pitch=0, roll=0. Change values as needed.
            Drone3D.Initialize(CanvasContainer, initialAltitude: 7.0, initialYaw: 0.0, initialPitch: 0.0, initialRoll: 0.0);
        }

        private async void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (shutdownComplete)
            {
                return;
            }

            e.Cancel = true;
            if (shutdownInProgress)
            {
                return;
            }

            shutdownInProgress = true;
            IsEnabled = false;
            sessionManager.EndCurrentSession();
            try
            {
                await transmitter.ShutdownAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Application shutdown encountered an error: {ex}");
            }
            finally
            {
                try
                {
                    logger.LogPortDisconnect();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Logger shutdown encountered an error: {ex}");
                }

                try
                {
                    transmitter.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Transmitter disposal encountered an error: {ex}");
                }

                shutdownComplete = true;

                // Always leave the current Closing event before closing again.
                // ShutdownAsync can complete synchronously when no port is open,
                // and a direct Close() here would recursively close the window.
                _ = Dispatcher.BeginInvoke(new Action(Close));
            }
        }

        // 3D-related code moved to Drone3DView class

        private void cmb_ConnectionType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = (cmb_ConnectionType.SelectedItem as ComboBoxItem)?.Content?.ToString();

            // Ensure controls are initialized before using them
            if (radioControls == null || logger == null)
                return;

            if (selectedItem == "Radio")
            {
                radioControls.Visibility = Visibility.Visible;
                transmitter?.SetSerialMode(false);
            }
            else if (selectedItem == "Serial")
            {
                radioControls.Visibility = Visibility.Collapsed;
                transmitter?.SetSerialMode(true);
            }

            UpdateCommandControls();
        }

        private void btn_RefreshTransmitPortsList_Click(object sender, RoutedEventArgs e)
        {
            transmitter.FillTransmitPortsList();
        }

        private async void btn_TransmitPortConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (transmitter.IsTransmitPortOpen())
                {
                    await transmitter.DisconnectTransmitPortAsync();
                }
                else
                {
                    await transmitter.ConnectTransmitPortAsync();
                }
            }
            catch (Exception ex)
            {
                Tools.Log(this, $"Transmit Connection Error: {ex.Message}");
            }
        }

        private void btn_RefreshLogPortsList_Click(object sender, RoutedEventArgs e)
        {
            logger.FillLogPortsList();
        }

        private void btn_LogPortConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (logger.IsLogPortOpen())
                {
                    logger.LogPortDisconnect();
                }
                else
                {
                    logger.LogPortConnect();
                }
                UpdateCommandControls();
            }
            catch (Exception ex)
            {
                Tools.Log(this, $"Log Connection Error: {ex.Message}");
            }
        }

        private void btn_ClearLog_Click(object sender, RoutedEventArgs e)
        {
            txt_Log.Document.Blocks.Clear();
            txt_Log.ScrollToEnd();
        }

        private async void btn_Arm_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await transmitter.ArmAsync();
                Tools.Log(this, "ARM requested: motors will start at arm throttle");
            }
            catch (Exception ex)
            {
                Tools.Log(this, $"ARM error: {ex.Message}");
            }
        }

        private async void btn_Disarm_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await transmitter.DisarmAsync();
                Tools.Log(this, "DISARM requested");
            }
            catch (Exception ex)
            {
                Tools.Log(this, $"DISARM error: {ex.Message}");
            }
        }

        private async void btn_Takeoff_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await transmitter.TakeoffAsync();
                Tools.Log(this, "TAKEOFF requested: firmware is searching for the 50 cm throttle");
            }
            catch (Exception ex)
            {
                Tools.Log(this, $"TAKEOFF error: {ex.Message}");
            }
        }

        private void chk_Heartbeat_Checked(object sender, RoutedEventArgs e)
        {
            // Guard: handler can be invoked during InitializeComponent before transmitter is assigned
            if (transmitter == null)
            {
                return;
            }

            try
            {
                transmitter.SetHeartbeatEnabled(true);
                lbl_HeartbeatWarning.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Tools.Log(this, ex.Message);
            }
        }

        private void chk_Heartbeat_Unchecked(object sender, RoutedEventArgs e)
        {
            // Guard: handler can be invoked during InitializeComponent before transmitter is assigned
            if (transmitter == null)
            {
                return;
            }

            // Prevent disabling heartbeat while armed
            if (transmitter.IsArmed)
            {
                Tools.Log(this, "Cannot disable heartbeat while armed. DISARM first.");
                // revert checkbox
                chk_HeartbeatEnabled.IsChecked = true;
                return;
            }

            try
            {
                transmitter.SetHeartbeatEnabled(false);
                lbl_HeartbeatWarning.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Tools.Log(this, ex.Message);
                chk_HeartbeatEnabled.IsChecked = true;
            }
        }

        private async void btn_SetConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                //clear txt_log
                //txt_Log.Document.Blocks.Clear();

                Config config = GetConfigFromUI();

                // Send to device
                byte[] data = transmitter.CreateConfigPacket(config);
                await SendDataAsync(data);

                // Persist and start logging only after the packet was sent successfully.
                ConfigurationManager.SaveConfiguration(config);
                sessionManager.StartNewSession(config);

                Tools.Log(this, "Configuration saved and sent to device");
            }
            catch (Exception ex)
            {
                Tools.Log(this, $"Error setting configuration: {ex.Message}");
            }
        }

        private async void btn_ThrottleIncrease_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(txt_ArmThrottle.Text, out int throttle) &&
                int.TryParse(txt_MaxSpeed.Text, out int maxSpeed))
            {
                throttle = Math.Min(throttle + 1, maxSpeed);
                txt_ArmThrottle.Text = throttle.ToString();

                // Update the config and send it to the device
                await UpdateAndSendConfigAsync();
            }
        }

        private async void btn_ThrottleDecrease_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(txt_ArmThrottle.Text, out int throttle) &&
                int.TryParse(txt_MinSpeed.Text, out int minSpeed))
            {
                throttle = Math.Max(throttle - 1, minSpeed);
                txt_ArmThrottle.Text = throttle.ToString();

                // Update the config and send it to the device
                await UpdateAndSendConfigAsync();
            }
        }

        private async Task UpdateAndSendConfigAsync()
        {
            try
            {
                Config config = GetConfigFromUI();
                byte[] data = transmitter.CreateConfigPacket(config);
                await SendDataAsync(data);
            }
            catch (Exception ex)
            {
                Tools.Log(this, $"Error updating configuration: {ex.Message}");
            }
        }

        private Config GetConfigFromUI()
        {
            return new Config
            {
                ArmThrottle = Tools.ParseInt(txt_ArmThrottle.Text, "Arm Throttle"),
                MinSpeed = Tools.ParseInt(txt_MinSpeed.Text, "Min Speed"),
                MaxSpeed = Tools.ParseInt(txt_MaxSpeed.Text, "Max Speed"),
                MaxAngle = Tools.ParseInt(txt_MaxAngle.Text, "Max Angle"),
                TargetPitch = Tools.ParseInt(txt_TargetPitch.Text, "Target Pitch"),
                TargetRoll = Tools.ParseInt(txt_TargetRoll.Text, "Target Roll"),
                TargetYaw = Tools.ParseInt(txt_TargetYaw.Text, "Target Yaw"),
                TargetGz = Tools.ParseInt(txt_TargetGz.Text, "Target Gz"),
                PitchKp = Tools.ParseDouble(txt_Pitch_Kp.Text, "Pitch Kp"),
                PitchKi = Tools.ParseDouble(txt_Pitch_Ki.Text, "Pitch Ki"),
                PitchKd = Tools.ParseDouble(txt_Pitch_Kd.Text, "Pitch Kd"),
                RollKp = Tools.ParseDouble(txt_Roll_Kp.Text, "Roll Kp"),
                RollKi = Tools.ParseDouble(txt_Roll_Ki.Text, "Roll Ki"),
                RollKd = Tools.ParseDouble(txt_Roll_Kd.Text, "Roll Kd"),
                GzKp = Tools.ParseDouble(txt_Gz_Kp.Text, "Gz Kp"),
                GzKi = Tools.ParseDouble(txt_Gz_Ki.Text, "Gz Ki"),
                GzKd = Tools.ParseDouble(txt_Gz_Kd.Text, "Gz Kd"),
                AltitudeKp = Tools.ParseDouble(txt_Altitude_Kp.Text, "Altitude Kp"),
                AltitudeKi = Tools.ParseDouble(txt_Altitude_Ki.Text, "Altitude Ki"),
                AltitudeKd = Tools.ParseDouble(txt_Altitude_Kd.Text, "Altitude Kd"),
                PIDMaxIPart = Tools.ParseDouble(txt_PID_MaxIPart.Text, "PID Max I Part"),
                PIDMaxOutput = Tools.ParseDouble(txt_PID_MaxOutput.Text, "PID Max Output"),
                TakeoffThrottleRampPerSecond = Tools.ParseDouble(
                    txt_TakeoffThrottleRamp.Text,
                    "Takeoff Throttle Ramp")
            };
        }

        private void LoadConfigToUI(Config config)
        {
            txt_ArmThrottle.Text = config.ArmThrottle.ToString();
            txt_MinSpeed.Text = config.MinSpeed.ToString();
            txt_MaxSpeed.Text = config.MaxSpeed.ToString();
            txt_MaxAngle.Text = config.MaxAngle.ToString();
            txt_TargetPitch.Text = config.TargetPitch.ToString();
            txt_TargetRoll.Text = config.TargetRoll.ToString();
            txt_TargetYaw.Text = config.TargetYaw.ToString();
            txt_TargetGz.Text = config.TargetGz.ToString();
            txt_Pitch_Kp.Text = config.PitchKp.ToString();
            txt_Pitch_Ki.Text = config.PitchKi.ToString();
            txt_Pitch_Kd.Text = config.PitchKd.ToString();
            txt_Roll_Kp.Text = config.RollKp.ToString();
            txt_Roll_Ki.Text = config.RollKi.ToString();
            txt_Roll_Kd.Text = config.RollKd.ToString();
            txt_Gz_Kp.Text = config.GzKp.ToString();
            txt_Gz_Ki.Text = config.GzKi.ToString();
            txt_Gz_Kd.Text = config.GzKd.ToString();
            txt_Altitude_Kp.Text = config.AltitudeKp.ToString();
            txt_Altitude_Ki.Text = config.AltitudeKi.ToString();
            txt_Altitude_Kd.Text = config.AltitudeKd.ToString();
            txt_PID_MaxIPart.Text = config.PIDMaxIPart.ToString();
            txt_PID_MaxOutput.Text = config.PIDMaxOutput.ToString();
            txt_TakeoffThrottleRamp.Text =
                config.TakeoffThrottleRampPerSecond > 0
                    ? config.TakeoffThrottleRampPerSecond.ToString("0.0")
                    : ProtocolPacketBuilder.DefaultTakeoffThrottleRampPerSecond.ToString("0.0");
        }

        // Helper to determine if Serial mode is selected
        private bool IsSerialMode()
        {
            var selectedItem = (cmb_ConnectionType.SelectedItem as ComboBoxItem)?.Content.ToString();
            return selectedItem == "Serial";
        }

        private Task SendDataAsync(
            byte[] data,
            PacketPriority priority = PacketPriority.Normal,
            CancellationToken ct = default)
        {
            return transmitter.SendPackageAsync(data, priority, ct);
        }

        private void UpdateCommandControls()
        {
            if (transmitter == null || logger == null || btn_Arm == null)
            {
                return;
            }

            bool connected = transmitter.IsConnected;
            bool armed = transmitter.IsArmed;
            btn_Arm.IsEnabled = connected;
            btn_Takeoff.IsEnabled = connected && armed;
            btn_Disarm.IsEnabled = connected;
            btn_SetConfig.IsEnabled = connected && !armed;
            btn_ThrottleIncrease.IsEnabled = connected && !armed;
            btn_ThrottleDecrease.IsEnabled = connected && !armed;
            chk_HeartbeatEnabled.IsEnabled = !armed;

            cmb_ConnectionType.IsEnabled =
                !transmitter.IsTransmitPortOpen() && !logger.IsLogPortOpen();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Up:
                    if (btn_ThrottleIncrease.IsEnabled)
                    {
                        btn_ThrottleIncrease.RaiseEvent(
                            new RoutedEventArgs(Button.ClickEvent));
                    }
                    e.Handled = true;
                    break;

                case Key.Down:
                    if (btn_ThrottleDecrease.IsEnabled)
                    {
                        btn_ThrottleDecrease.RaiseEvent(
                            new RoutedEventArgs(Button.ClickEvent));
                    }
                    e.Handled = true;
                    break;
            }
        }

        private void btnPausePlots_Clicked(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton button)
            {
                var pidPlotter = sessionManager?.logger?.GetPidPlotter();
                if (pidPlotter != null)
                {
                    pidPlotter.IsPaused = button.IsChecked ?? false;
                    button.Content = button.IsChecked ?? false ? "Resume" : "Pause";
                }
            }
        }
    }
}
