using DroneLogger.Model;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.Json;
using System.Windows.Controls;  // Add this line to resolve Canvas namespace
using System.Windows.Media;
using System.Windows.Shapes;

namespace DroneLogger.Classes
{
    public class Logger
    {
        public MainWindow context;
        private SerialPort LogPort;
        private StringBuilder LogPackage = new StringBuilder();
        private bool isSerialMode = false;
        private PidPlotter pidPlotter;

        private const double MIN_CIRCLE_SIZE = 20;
        private const double MAX_CIRCLE_SIZE = 70;

        public Logger(MainWindow context)
        {
            this.context = context;
            this.pidPlotter = new PidPlotter(context, context.rollPidPlot, context.pitchPidPlot, context.rollValuePlot, context.pitchValuePlot);
        }

        // Method to fill the log ports list in the UI
        public void FillLogPortsList()
        {
            // Use Dispatcher to update UI elements from a non-UI thread if this method is called from one
            context.Dispatcher.BeginInvoke(() =>
            {
                context.cmb_LogPort.Items.Clear();
                string[] ports = SerialPort.GetPortNames();

                foreach (string port in ports)
                {
                    if (port != context.cmb_TransmitPort.SelectedItem?.ToString())
                    {
                        context.cmb_LogPort.Items.Add(port);
                    }
                }

                if (context.cmb_LogPort.Items.Count > 0) context.cmb_LogPort.SelectedIndex = 0;
            });
        }

        // Method to connect the log serial port
        public void LogPortConnect()
        {
            context.Dispatcher.BeginInvoke(() =>
            {
                if (context.cmb_LogPort.SelectedItem == null)
                {
                    Tools.Log(context, "Error: No log port selected");
                    return;
                }

                try
                {
                    LogPort = new SerialPort(context.cmb_LogPort.SelectedItem.ToString(), 115200, Parity.None, 8, StopBits.One);
                    LogPort.DataReceived += LogPort_DataReceived;
                    LogPort.ErrorReceived += LogPort_ErrorReceived;
                    LogPort.Open();
                    context.btn_LogPortConnect.Content = "Disconnect";

                    if (isSerialMode)
                    {
                        context.btn_SetConfig.IsEnabled = true;
                        context.btn_Power.IsEnabled = true;
                    }

                    Tools.Log(context, $"Log port {LogPort.PortName} connected.");
                }
                catch (Exception ex)
                {
                    Tools.Log(context, $"Log Port Connection Error: {ex.Message}");
                    LogPortDisconnect();
                }
            });
        }

        // Method to disconnect the log serial port
        public void LogPortDisconnect()
        {
            // Use Dispatcher to update UI elements from a non-UI thread if this method is called from one
            context.Dispatcher.BeginInvoke(() =>
            {
                if (LogPort != null && LogPort.IsOpen)
                {
                    LogPort.Close();
                    LogPort.Dispose();
                    LogPort = null;
                    Tools.Log(context, "Log port disconnected.");
                }
                context.btn_LogPortConnect.Content = "Connect"; // Assuming a btn_LogPortConnect exists in UI
                                                                // Disable log-specific controls here if any
            });
        }

        // Check if the log port is open
        public bool IsLogPortOpen()
        {
            return LogPort != null && LogPort.IsOpen;
        }

        // Event handler for receiving data on the log port
        private void LogPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (LogPort == null || !LogPort.IsOpen)
                return;

            try
            {
                string incoming = LogPort.ReadExisting();
                LogPackage.Append(incoming);

                // Process complete lines (assuming log messages are line-terminated)
                while (LogPackage.ToString().Contains("\n"))
                {
                    string fullLine = LogPackage.ToString();
                    int index = fullLine.IndexOf('\n');
                    string line = fullLine.Substring(0, index).Trim();
                    LogPackage.Remove(0, index + 1);

                    // Call the AnalyseLogData method in MainWindow
                    AnalyseData(line);

                    // Clear the buffer after processing a line
                    LogPackage.Clear();
                }
            }
            catch (IOException ioEx)
            {

                Tools.Log(context, "Log serial port disconnected (IO exception).");
                LogPortDisconnect();

            }
            catch (InvalidOperationException invEx)
            {

                Tools.Log(context, "Log serial port disconnected (invalid operation).");
                LogPortDisconnect();

            }
            catch (Exception ex)
            {
                Tools.Log(context, $"Error receiving log data: {ex.Message}");
            }
        }

        private string CleanJsonString(string input)
        {
            int braceIndex = input.IndexOf('{');
            return braceIndex >= 0 ? input.Substring(braceIndex) : input;
        }

        private void AnalyseData(string line)
        {
            context.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    string jsonData = "";

                    if (line.StartsWith("Data="))
                    {
                        jsonData = CleanJsonString(line.Substring(5).Trim());
                        var logData = JsonSerializer.Deserialize<LogData>(jsonData);

                        // Existing updates
                        context.lbl_Roll.Content = logData.roll.ToString("F2");
                        context.lbl_Pitch.Content = logData.pitch.ToString("F2");
                        context.lbl_Motor1_Value.Content = context.txt_Throttle.Text;
                        context.lbl_Motor2_Value.Content = context.txt_Throttle.Text;
                        context.lbl_Motor3_Value.Content = context.txt_Throttle.Text;
                        context.lbl_Motor4_Value.Content = context.txt_Throttle.Text;
                        context.lbl_Motor1_PID.Content = (logData.m1 - Convert.ToDouble(context.txt_Throttle.Text)).ToString("F2");
                        context.lbl_Motor2_PID.Content = (logData.m2 - Convert.ToDouble(context.txt_Throttle.Text)).ToString("F2");
                        context.lbl_Motor3_PID.Content = (logData.m3 - Convert.ToDouble(context.txt_Throttle.Text)).ToString("F2");
                        context.lbl_Motor4_PID.Content = (logData.m4 - Convert.ToDouble(context.txt_Throttle.Text)).ToString("F2");
                        context.lbl_Motor1_ModifiedValue.Content = logData.m1.ToString("F2");
                        context.lbl_Motor2_ModifiedValue.Content = logData.m2.ToString("F2");
                        context.lbl_Motor3_ModifiedValue.Content = logData.m3.ToString("F2");
                        context.lbl_Motor4_ModifiedValue.Content = logData.m4.ToString("F2");
                        context.lbl_Roll_P.Content = logData.roll_p.ToString("F2");
                        context.lbl_Roll_I.Content = logData.roll_i.ToString("F2");
                        context.lbl_Roll_D.Content = logData.roll_d.ToString("F2");
                        context.lbl_Pitch_P.Content = logData.pitch_p.ToString("F2");
                        context.lbl_Pitch_I.Content = logData.pitch_i.ToString("F2");
                        context.lbl_Pitch_D.Content = logData.pitch_d.ToString("F2");

                        UpdateHorizon(logData.roll, logData.pitch);
                        if (logData != null)
                        {
                            UpdateMotorIndicators(logData.m1, logData.m2, logData.m3, logData.m4);
                            pidPlotter.UpdatePlots(logData);
                        }
                    }

                    if (line.StartsWith("Error="))
                    {
                        jsonData = CleanJsonString(line.Substring(6)); // Remove "Error="
                        var errorData = JsonSerializer.Deserialize<Error>(jsonData);
                        if (errorData != null)
                        {
                            Tools.Log(context, $"Error received: Code={errorData.Code}, Message={errorData.Content}");
                        }
                    }
                    else if (line.StartsWith("Information="))
                    {
                        //Tools.Log(context, line);
                        jsonData = CleanJsonString(line.Substring(12)); // Remove "Information="
                        var infoData = JsonSerializer.Deserialize<Information>(jsonData);
                        if (infoData != null)
                        {
                            Tools.Log(context, $"Information received: Code={infoData.Code}, Message={infoData.Content}");
                        }
                    }

                }
                catch (JsonException jsonEx)
                {
                    Tools.Log(context, $"Log data: {line}");
                    Tools.Log(context, $"JSON deserialization error: {jsonEx.Message}");
                }
            });
        }

        // Event handler for log serial port errors
        private void LogPort_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            Tools.Log(context, $"Log serial port error: {e.EventType}");
            LogPortDisconnect();
        }

        private void UpdateHorizon(double roll, double pitch)
        {
            // Update horizon display
            RotateTransform rotate = new RotateTransform(roll, 90, 10);
            TranslateTransform translate = new TranslateTransform(0, -pitch);

            TransformGroup transformGroup = new TransformGroup();
            transformGroup.Children.Add(translate);
            transformGroup.Children.Add(rotate);
            context.Horizon.RenderTransform = transformGroup;
        }

        private void UpdateMotorIndicators(double m1Speed, double m2Speed, double m3Speed, double m4Speed)
        {
            context.Dispatcher.BeginInvoke(() =>
            {
                // Update circle sizes based on motor speeds
                UpdateMotorCircle(context.Motor1Circle, context.Motor1Speed, m1Speed);
                UpdateMotorCircle(context.Motor2Circle, context.Motor2Speed, m2Speed);
                UpdateMotorCircle(context.Motor3Circle, context.Motor3Speed, m3Speed);
                UpdateMotorCircle(context.Motor4Circle, context.Motor4Speed, m4Speed);
            });
        }

        private void UpdateMotorCircle(Ellipse circle, TextBlock textBlock, double speed)
        {
            // Normalize speed value between 0 and 1
            double normalizedSpeed = speed / 100;

            // Calculate new size
            double newSize = MIN_CIRCLE_SIZE + (MAX_CIRCLE_SIZE - MIN_CIRCLE_SIZE) * normalizedSpeed;

            // Update circle size
            circle.Width = newSize;
            circle.Height = newSize;

            if (Canvas.GetLeft(circle) < 90) // Left side motors
                Canvas.SetLeft(circle, 40 - newSize / 2);
            else // Right side motors
                Canvas.SetLeft(circle, 140 - newSize / 2);

            if (Canvas.GetTop(circle) < 90) // Top motors
                Canvas.SetTop(circle, 40 - newSize / 2);
            else // Bottom motors
                Canvas.SetTop(circle, 140 - newSize / 2);

            // Update color based on speed
            byte greenComponent = (byte)(255 * (1 - normalizedSpeed));
            byte redComponent = (byte)(255 * normalizedSpeed);
            circle.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(redComponent, greenComponent, 0));

            //Text Blocks in the canvas must show the speed of the motors in format of mi: Speed_of_motor_i
            if (textBlock != null)
            {
                textBlock.Text = $"{speed:F2}"; // Assuming circle.Name is "Motor1Circle", etc.
            }

        }

        // Add method to set serial mode
        public void SetSerialMode(bool enabled)
        {
            isSerialMode = enabled;
        }

        // Add method to send commands in serial mode
        public void SendCommand(byte[] data)
        {
            if (!isSerialMode || LogPort == null || !LogPort.IsOpen)
            {
                Tools.Log(context, "Error: Cannot send command - Logger not in serial mode or not connected");
                return;
            }

            try
            {
                LogPort.Write(data, 0, data.Length);
                //LogPort.Write(new byte[] { 255 }, 0, 1); // Terminator byte
            }
            catch (Exception ex)
            {
                Tools.Log(context, $"Error sending command: {ex.Message}");
                LogPortDisconnect();
            }
        }
        // Add this method to Logger.cs
        public PidPlotter GetPidPlotter()
        {
            return pidPlotter;
        }
    }
}
