using DroneLogger.Classes;
using DroneLogger.Model;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace DroneLogger
{
    public partial class MainWindow : Window
    {
        private Transmitter transmitter;
        private Logger logger;
        public SessionManager sessionManager;

        public MainWindow()
        {
            InitializeComponent();
            transmitter = new Transmitter(this);
            logger = new Logger(this);
            sessionManager = new SessionManager(logger);

            // Add keyboard support
            this.KeyDown += Window_KeyDown;

            DataContext = this;

            // Ensure default connection type is Serial on startup
            radioControls.Visibility = Visibility.Collapsed;
            logger.SetSerialMode(true);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            txt_Log.Document.LineHeight = 2;

            logger.FillLogPortsList();
            transmitter.FillTransmitPortsList();

            transmitter.SetTransmitControlsEnabled(false);

            // Load configuration if it exists, otherwise keep XAML initial values
            var savedConfig = ConfigurationManager.LoadConfiguration();
            if (savedConfig != null)
            {
                LoadConfigToUI(savedConfig);
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            sessionManager.EndCurrentSession();
            transmitter.TransmitPortDisconnect();
            logger.LogPortDisconnect();
        }

        private void cmb_ConnectionType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = (cmb_ConnectionType.SelectedItem as ComboBoxItem)?.Content?.ToString();

            // Ensure controls are initialized before using them
            if (radioControls == null || logger == null)
                return;

            if (selectedItem == "Radio")
            {
                radioControls.Visibility = Visibility.Visible;
                logger.SetSerialMode(false);
            }
            else if (selectedItem == "Serial")
            {
                radioControls.Visibility = Visibility.Collapsed;
                logger.SetSerialMode(true);
            }
        }

        private void btn_RefreshTransmitPortsList_Click(object sender, RoutedEventArgs e)
        {
            transmitter.FillTransmitPortsList();
        }

        private void btn_TransmitPortConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (transmitter.IsTransmitPortOpen())
                {
                    transmitter.TransmitPortDisconnect();
                }
                else
                {
                    transmitter.TransmitPortConnect();
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

        private void btn_SetConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (txt_DefaultThrottle.Text != "0")
                {
                    txt_Throttle.Text = txt_DefaultThrottle.Text; 
                }

                //clear txt_log
                txt_Log.Document.Blocks.Clear();

                Config config = GetConfigFromUI();

                // Save configuration
                ConfigurationManager.SaveConfiguration(config);

                // Start new session with current config
                sessionManager.StartNewSession(config);

                // Send to device
                byte[] data = transmitter.CreateConfigPacket(config);
                SendData(data);

                Tools.Log(this, "Configuration saved and sent to device");
            }
            catch (Exception ex)
            {
                Tools.Log(this, $"Error setting configuration: {ex.Message}");
            }
        }

        private void btn_Power_Click(object sender, RoutedEventArgs e)
        {
            byte[] data = new byte[32];
            data[0] = 2;
            SendData(data);
        }

        private void btn_ThrottleIncrease_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(txt_Throttle.Text, out int throttle) &&
                int.TryParse(txt_MaxSpeed.Text, out int maxSpeed))
            {
                throttle = Math.Min(throttle + 1, maxSpeed);
                txt_Throttle.Text = throttle.ToString();

                // Update the config and send it to the device
                UpdateAndSendConfig();
            }
        }

        private void btn_ThrottleDecrease_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(txt_Throttle.Text, out int throttle) &&
                int.TryParse(txt_MinSpeed.Text, out int minSpeed))
            {
                throttle = Math.Max(throttle - 1, minSpeed);
                txt_Throttle.Text = throttle.ToString();

                // Update the config and send it to the device
                UpdateAndSendConfig();
            }
        }

        private void UpdateAndSendConfig()
        {
            try
            {
                Config config = GetConfigFromUI();
                byte[] data = transmitter.CreateConfigPacket(config);
                SendData(data);
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
                Throttle = Tools.ParseInt(txt_Throttle.Text, "Base Speed"),
                DefaultThrottle = Tools.ParseInt(txt_DefaultThrottle.Text, "Base Speed"),
                MinSpeed = Tools.ParseInt(txt_MinSpeed.Text, "Min Speed"),
                MaxSpeed = Tools.ParseInt(txt_MaxSpeed.Text, "Max Speed"),
                MaxAngle = Tools.ParseInt(txt_MaxAngle.Text, "Max Angle"),
                TargetPitch = Tools.ParseInt(txt_TargetPitch.Text, "Target Pitch"),
                TargetRoll = Tools.ParseInt(txt_TargetRoll.Text, "Target Roll"),
                PitchKp = Tools.ParseDouble(txt_Pitch_Kp.Text, "Pitch Kp"),
                PitchKi = Tools.ParseDouble(txt_Pitch_Ki.Text, "Pitch Ki"),
                PitchKd = Tools.ParseDouble(txt_Pitch_Kd.Text, "Pitch Kd"),
                RollKp = Tools.ParseDouble(txt_Roll_Kp.Text, "Roll Kp"),
                RollKi = Tools.ParseDouble(txt_Roll_Ki.Text, "Roll Ki"),
                RollKd = Tools.ParseDouble(txt_Roll_Kd.Text, "Roll Kd"),
                PIDStartThreshold = Tools.ParseDouble(txt_PID_StartThreshold.Text, "PID Start Threshold"),
                PIDMaxIPart = Tools.ParseDouble(txt_PID_MaxIPart.Text, "PID Max I Part"),
                PIDMaxOutput = Tools.ParseDouble(txt_PID_MaxOutput.Text, "PID Max Output"),
                PIDStartThrottle = Tools.ParseInt(txt_PID_StartThrottle.Text, "PID Start Throttle")
            };
        }

        private void LoadConfigToUI(Config config)
        {
            txt_Throttle.Text = config.Throttle.ToString();
            txt_DefaultThrottle.Text = config.DefaultThrottle.ToString();
            txt_MinSpeed.Text = config.MinSpeed.ToString();
            txt_MaxSpeed.Text = config.MaxSpeed.ToString();
            txt_MaxAngle.Text = config.MaxAngle.ToString();
            txt_TargetPitch.Text = config.TargetPitch.ToString();
            txt_TargetRoll.Text = config.TargetRoll.ToString();
            txt_Pitch_Kp.Text = config.PitchKp.ToString();
            txt_Pitch_Ki.Text = config.PitchKi.ToString();
            txt_Pitch_Kd.Text = config.PitchKd.ToString();
            txt_Roll_Kp.Text = config.RollKp.ToString();
            txt_Roll_Ki.Text = config.RollKi.ToString();
            txt_Roll_Kd.Text = config.RollKd.ToString();
            txt_PID_StartThreshold.Text = config.PIDStartThreshold.ToString();
            txt_PID_MaxIPart.Text = config.PIDMaxIPart.ToString();
            txt_PID_MaxOutput.Text = config.PIDMaxOutput.ToString();
            txt_PID_StartThrottle.Text = config.PIDStartThrottle.ToString();
        }

        // Helper to determine if Serial mode is selected
        private bool IsSerialMode()
        {
            var selectedItem = (cmb_ConnectionType.SelectedItem as ComboBoxItem)?.Content.ToString();
            return selectedItem == "Serial";
        }

        // Helper to send data according to connection type
        private void SendData(byte[] data)
        {
            if (IsSerialMode())
            {
                logger.SendCommand(data);
            }
            else
            {
                transmitter.SendPackage(data);
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Up:
                    btn_ThrottleIncrease.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    e.Handled = true;
                    break;

                case Key.Down:
                    btn_ThrottleDecrease.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
