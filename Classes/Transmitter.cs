using DroneLogger.Model;
using System.IO;
using System.IO.Ports;
using System.Text;

namespace DroneLogger.Classes
{
    internal class Transmitter
    {
        private MainWindow context;
        private SerialPort TransmitPort;
        private StringBuilder TransmitResultPackage = new StringBuilder();

        public Transmitter(MainWindow context)
        {
            this.context = context;
        }

        // Method to fill the transmit ports list in the UI
        public void FillTransmitPortsList()
        {
            // Use Dispatcher to update UI elements from a non-UI thread if this method is called from one
            context.Dispatcher.BeginInvoke(() =>
            {
                context.cmb_TransmitPort.Items.Clear();
                string[] ports = SerialPort.GetPortNames();

                foreach (string port in ports)
                {
                    //if (port != context.cmb_LogPort.SelectedItem?.ToString())
                    //{
                        context.cmb_TransmitPort.Items.Add(port);
                    //}
                }

                if (context.cmb_TransmitPort.Items.Count > 0) context.cmb_TransmitPort.SelectedIndex = 0;
            });
        }

        // Method to connect the transmit serial port
        public void TransmitPortConnect()
        {
            // Use Dispatcher to update UI elements from a non-UI thread if this method is called from one
            context.Dispatcher.BeginInvoke(() =>
            {
                if (context.cmb_TransmitPort.SelectedItem == null)
                {
                    Tools.Log(context, "Error: No transmit port selected");
                    return;
                }
                try
                {
                    TransmitPort = new SerialPort(context.cmb_TransmitPort.SelectedItem.ToString(), 115200, Parity.None, 8, StopBits.One);
                    TransmitPort.DataReceived += TransmitPort_DataReceived;
                    TransmitPort.ErrorReceived += TransmitPort_ErrorReceived;
                    TransmitPort.Open();
                    context.btn_TransmitPortConnect.Content = "Disconnect";
                    SetTransmitControlsEnabled(true);
                    Tools.Log(context, $"Transmit port {TransmitPort.PortName} connected.");
                }
                catch (Exception ex)
                {
                    Tools.Log(context, $"Transmit Port Connection Error: {ex.Message}");
                    TransmitPortDisconnect(); // Ensure state is clean on error
                }
            });
        }

        // Method to disconnect the transmit serial port
        public void TransmitPortDisconnect()
        {
            // Use Dispatcher to update UI elements from a non-UI thread if this method is called from one
            context.Dispatcher.BeginInvoke(() =>
            {
                if (TransmitPort != null && TransmitPort.IsOpen)
                {
                    TransmitPort.Close();
                    TransmitPort.Dispose();
                    TransmitPort = null;
                    Tools.Log(context, "Transmit port disconnected.");
                }
                context.btn_TransmitPortConnect.Content = "Connect";
                SetTransmitControlsEnabled(false);
            });
        }

        // Check if the transmit port is open
        public bool IsTransmitPortOpen()
        {
            return TransmitPort != null && TransmitPort.IsOpen;
        }

        // Helper method to enable/disable transmit related UI controls
        public void SetTransmitControlsEnabled(bool enabled)
        {
            // Use Dispatcher to update UI elements from a non-UI thread if this method is called from one
            context.Dispatcher.BeginInvoke(() =>
            {
                context.btn_SetConfig.IsEnabled = enabled;
                context.btn_Power.IsEnabled = enabled;
            });
        }

        // Event handler for receiving data on the transmit port
        private void TransmitPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (TransmitPort == null || !TransmitPort.IsOpen)
                return;

            try
            {
                string incoming = TransmitPort.ReadExisting();
                TransmitResultPackage.Append(incoming);

                // Process complete lines
                while (TransmitResultPackage.ToString().Contains("\n"))
                {
                    string fullLine = TransmitResultPackage.ToString();
                    int index = fullLine.IndexOf('\n');
                    string line = fullLine.Substring(0, index).Trim();
                    TransmitResultPackage.Remove(0, index + 1);

                    // Call the AnalyseTransmitData method in MainWindow
                    AnalysData(line);

                    // Clear the buffer after processing a line (or keep appending if lines can be very long)
                    // Clearing here assumes each '\n' signifies a complete, independent package.
                    TransmitResultPackage.Clear();
                }
            }
            catch (IOException ioEx)
            {
                context.Dispatcher.BeginInvoke(() =>
                {
                    Tools.Log(context, "Transmit serial port disconnected (IO exception).");
                    TransmitPortDisconnect();
                });
            }
            catch (InvalidOperationException invEx)
            {
                context.Dispatcher.BeginInvoke(() =>
                {
                    Tools.Log(context, "Transmit serial port disconnected (invalid operation).");
                    TransmitPortDisconnect();
                });
            }
            catch (Exception ex)
            {
                Tools.Log(context, $"Error receiving transmit data: {ex.Message}");
            }
        }

        private void AnalysData(string data)
        {
            Tools.Log(context, data);
        }

        // Event handler for transmit serial port errors
        private void TransmitPort_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            Tools.Log(context, $"Transmit serial port error: {e.EventType}");
            TransmitPortDisconnect();
        }

        // Method to send a package over the transmit serial port
        public void SendPackage(byte[] data)
        {
            if (TransmitPort == null || !TransmitPort.IsOpen)
            {
                Tools.Log(context, "Error: Not connected to transmit serial port");
                // No need to call TransmitPortDisconnect here, IsTransmitPortOpen check handles it
                return;
            }

            try
            {
                // Write the data bytes
                TransmitPort.Write(data, 0, data.Length);
                // Write the terminator byte
                //TransmitPort.Write([255], 0, 1);
            }
            catch (Exception ex)
            {
                Tools.Log(context, $"Error sending data: {ex.Message}");
                TransmitPortDisconnect(); // Disconnect on send error
            }
        }

        // Method to create the configuration packet byte array
        public byte[] CreateConfigPacket(Config config)
        {
            byte[] data = new byte[32];
            data[0] = 1; // Packet type for Config

            // Basic configuration values
            data[1] = (byte)config.Throttle;
            data[2] = (byte)config.MinSpeed;
            data[3] = (byte)config.MaxSpeed;
            data[4] = (byte)config.MaxAngle;

            // TargetPitch, TargetRoll, TargetYaw as signed 16-bit
            Buffer.BlockCopy(BitConverter.GetBytes((short)config.TargetPitch), 0, data, 5, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((short)config.TargetRoll), 0, data, 7, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((short)config.TargetYaw), 0, data, 9, 2);

            // PID values - scaling and using ushort for Ki/Kd
            data[11] = (byte)(config.PitchKp * 100);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)(config.PitchKi * 10000)), 0, data, 12, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)(config.PitchKd * 1000)), 0, data, 14, 2);

            data[16] = (byte)(config.RollKp * 100);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)(config.RollKi * 10000)), 0, data, 17, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)(config.RollKd * 1000)), 0, data, 19, 2);

            data[21] = (byte)(config.YawKp * 100);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)(config.YawKi * 1000)), 0, data, 22, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)(config.YawKd * 10000)), 0, data, 24, 2);

            data[26] = (byte)config.PIDStartThreshold;

            Buffer.BlockCopy(BitConverter.GetBytes((ushort)(config.PIDMaxIPart * 10)), 0, data, 27, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)(config.PIDMaxOutput * 10)), 0, data, 29, 2);

            // Add new PID Start Throttle value at the next available byte position (31)
            data[31] = (byte)config.PIDStartThrottle;

            return data;
        }
    }
}
