using DroneLogger.Model;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;  // Add this line to resolve Canvas namespace
using System.Windows.Media;
using System.Windows.Shapes;

namespace DroneLogger.Classes
{
    public class Logger
    {
        public MainWindow context;
        private SerialPort? LogPort;
        private readonly StringBuilder LogPackage = new StringBuilder();
        private readonly SemaphoreSlim commandWriteGate = new SemaphoreSlim(1, 1);
        private PidPlotter pidPlotter;

        public event EventHandler<bool>? PortStateChanged;

        private const double MIN_CIRCLE_SIZE = 20;
        private const double MAX_CIRCLE_SIZE = 70;

        public Logger(MainWindow context)
        {
            this.context = context;
            this.pidPlotter = new PidPlotter(context, context.rollPidPlot, context.pitchPidPlot, context.rollValuePlot, context.pitchValuePlot, context.altitudePlot, context.vzPlot, context.yawPlot);
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
                    //if (port != context.cmb_TransmitPort.SelectedItem?.ToString())
                    //{
                        context.cmb_LogPort.Items.Add(port);
                    //}
                }

                if (context.cmb_LogPort.Items.Count > 0) context.cmb_LogPort.SelectedIndex = 0;
            });
        }

        // Method to connect the log serial port
        public void LogPortConnect()
        {
            if (!context.Dispatcher.CheckAccess())
            {
                context.Dispatcher.BeginInvoke(LogPortConnect);
                return;
            }

            if (context.cmb_LogPort.SelectedItem == null)
            {
                Tools.Log(context, "Error: No log port selected");
                return;
            }

            try
            {
                LogPort = new SerialPort(context.cmb_LogPort.SelectedItem.ToString(),
                    115200, Parity.None, 8, StopBits.One)
                {
                    Handshake = Handshake.None,
                    WriteTimeout = 500
                };
                LogPort.DataReceived += LogPort_DataReceived;
                LogPort.ErrorReceived += LogPort_ErrorReceived;
                LogPort.Open();
                LogPackage.Clear();
                context.btn_LogPortConnect.Content = "Disconnect";
                PortStateChanged?.Invoke(this, true);
                Tools.Log(context, $"Log port {LogPort.PortName} connected.");
            }
            catch (Exception ex)
            {
                Tools.Log(context, $"Log Port Connection Error: {ex.Message}");
                LogPortDisconnect();
            }
        }

        // Method to disconnect the log serial port
        public void LogPortDisconnect()
        {
            if (!context.Dispatcher.CheckAccess())
            {
                context.Dispatcher.BeginInvoke(LogPortDisconnect);
                return;
            }

            bool hadPort = LogPort != null;
            if (LogPort != null)
            {
                LogPort.DataReceived -= LogPort_DataReceived;
                LogPort.ErrorReceived -= LogPort_ErrorReceived;
                try
                {
                    if (LogPort.IsOpen)
                    {
                        LogPort.Close();
                    }
                }
                catch
                {
                }
                LogPort.Dispose();
                LogPort = null;
            }

            LogPackage.Clear();
            context.btn_LogPortConnect.Content = "Connect";
            if (hadPort)
            {
                Tools.Log(context, "Log port disconnected.");
                PortStateChanged?.Invoke(this, false);
            }
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

                    ProcessDiagnosticLine(line);

                }
            }
            catch (IOException)
            {

                Tools.Log(context, "Log serial port disconnected (IO exception).");
                LogPortDisconnect();

            }
            catch (InvalidOperationException)
            {

                Tools.Log(context, "Log serial port disconnected (invalid operation).");
                LogPortDisconnect();

            }
            catch (Exception ex)
            {
                Tools.Log(context, $"Error receiving log data: {ex.Message}");
            }
        }

        internal static bool TryExtractMessagePayload(
            string line,
            string messageType,
            out string jsonData)
        {
            jsonData = string.Empty;
            if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(messageType))
            {
                return false;
            }

            string marker = messageType + "=";
            int markerIndex = line.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return false;
            }

            // A relay may prepend a source label, for example
            // "RemoteController: Data={...}". Do not accept a marker embedded
            // in an unrelated word.
            if (markerIndex > 0 && !char.IsWhiteSpace(line[markerIndex - 1]))
            {
                return false;
            }

            int braceIndex = line.IndexOf('{', markerIndex + marker.Length);
            if (braceIndex < 0)
            {
                return false;
            }

            jsonData = line[braceIndex..].Trim();
            return true;
        }

        /// <summary>
        /// Process one complete diagnostic line received from either the direct
        /// logger port or the RemoteController radio connection.
        /// </summary>
        internal void ProcessDiagnosticLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            context.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    string jsonData;

                    if (TryExtractMessagePayload(line, "Data", out jsonData))
                    {
                        var logData = JsonSerializer.Deserialize<LogData>(jsonData);
                        if (logData == null)
                        {
                            Tools.Log(context, "Received an empty flight-data record.");
                            return;
                        }

                        // Existing updates
                        context.lbl_Roll.Content = logData.roll.ToString("F2");
                        context.lbl_Pitch.Content = logData.pitch.ToString("F2");
                        context.lbl_Yaw.Content = logData.yaw.ToString("F2");
                        // Gz display removed from UI
                        // Vz (vertical velocity) - only show and plot when valid flag set by device
                        if (logData.Vz_valid == 1)
                        {
                            if (context.lbl_Vz != null) context.lbl_Vz.Content = logData.Vz.ToString("F2");
                            if (context.lbl_Canvas_Vz != null) context.lbl_Canvas_Vz.Text = logData.Vz.ToString("F2") + " m/s";
                        }
                        else
                        {
                            if (context.lbl_Vz != null) context.lbl_Vz.Content = "N/A";
                            if (context.lbl_Canvas_Vz != null) context.lbl_Canvas_Vz.Text = "N/A";
                            // mark as NaN so plots will show a gap
                            logData.Vz = double.NaN;
                        }
                        context.lbl_Altitude.Content = logData.altitude.ToString("F2");
                        // Update canvas tab labels
                        context.lbl_Canvas_Roll.Text = logData.roll.ToString("F2") + "°";
                        context.lbl_Canvas_Pitch.Text = logData.pitch.ToString("F2") + "°";
                        context.lbl_Canvas_Yaw.Text = logData.yaw.ToString("F2") + "°";
                        context.lbl_Canvas_Altitude.Text = logData.altitude.ToString("F2") + " cm";
                        if (context.lbl_Canvas_Vz != null) context.lbl_Canvas_Vz.Text = logData.Vz.ToString("F2") + " m/s";
                        // Update new compass UI
                        context.lbl_YawNumeric.Text = logData.yaw.ToString("F2") + "°";
                        // Rotate the needle around the compass center (90,90)
                        // Use negative angle so increasing yaw rotates the needle counter-clockwise
                        RotateTransform yawRotate = new RotateTransform(logData.yaw, 90, 90);
                        context.YawNeedle.RenderTransform = yawRotate;
                        context.lbl_Motor1_Value.Content = logData.base_throttle.ToString("F2");
                        context.lbl_Motor2_Value.Content = logData.base_throttle.ToString("F2");
                        context.lbl_Motor3_Value.Content = logData.base_throttle.ToString("F2");
                        context.lbl_Motor4_Value.Content = logData.base_throttle.ToString("F2");
                        context.lbl_Motor1_PID.Content = (logData.m1 - logData.base_throttle).ToString("F2");
                        context.lbl_Motor2_PID.Content = (logData.m2 - logData.base_throttle).ToString("F2");
                        context.lbl_Motor3_PID.Content = (logData.m3 - logData.base_throttle).ToString("F2");
                        context.lbl_Motor4_PID.Content = (logData.m4 - logData.base_throttle).ToString("F2");
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
                        // Gz PID displays removed from UI
                        context.lbl_Altitude_P.Content = logData.altitude_p.ToString("F2");
                        context.lbl_Altitude_I.Content = logData.altitude_i.ToString("F2");
                        context.lbl_Altitude_D.Content = logData.altitude_d.ToString("F2");

                        UpdateHorizon(logData.roll, logData.pitch);
                        if (logData != null)
                        {
                            UpdateMotorIndicators(logData.m1, logData.m2, logData.m3, logData.m4);
                            pidPlotter.UpdatePlots(logData);
                            // Keep both the full simulator and compact main-tab view in sync.
                            try
                            {
                                context.Drone3D?.Update(logData);
                                context.CompactDrone3D?.Update(logData);
                            }
                            catch { }
                        }
                    }

                    if (TryExtractMessagePayload(line, "Error", out jsonData))
                    {
                        var errorData = JsonSerializer.Deserialize<Error>(jsonData);
                        if (errorData != null)
                        {
                            Tools.Log(context, $"Error received: Code={errorData.Code}, Message={errorData.Content}");
                        }
                    }
                    else if (TryExtractMessagePayload(line, "Information", out jsonData))
                    {
                        //Tools.Log(context, line);
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
                catch (Exception ex)
                {
                    Tools.Log(context, $"Telemetry update error: {ex.Message}");
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

        public async Task SendCommandAsync(byte[] data, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (data.Length != ProtocolPacketBuilder.PacketSize)
            {
                throw new ArgumentException("Packet must be exactly 64 bytes.", nameof(data));
            }

            await commandWriteGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (LogPort?.IsOpen != true)
                {
                    throw new InvalidOperationException("Logger/device serial port is not connected.");
                }

                ct.ThrowIfCancellationRequested();
                LogPort.Write(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                Tools.Log(context, $"Error sending command: {ex.Message}");
                LogPortDisconnect();
                throw;
            }
            finally
            {
                commandWriteGate.Release();
            }
        }
        // Add this method to Logger.cs
        public PidPlotter GetPidPlotter()
        {
            return pidPlotter;
        }
    }
}
