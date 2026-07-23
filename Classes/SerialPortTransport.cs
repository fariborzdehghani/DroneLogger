using System.IO.Ports;
using System.Text;

namespace DroneLogger.Classes
{
    internal sealed class SerialPortTransport : ISerialTransport
    {
        private readonly object portLock = new();
        private readonly StringBuilder receiveBuffer = new();
        private SerialPort? port;

        public event EventHandler<string>? LineReceived;
        public event EventHandler? ConnectionLost;

        public bool IsOpen
        {
            get
            {
                lock (portLock)
                {
                    return port?.IsOpen == true;
                }
            }
        }

        public Task OpenAsync(string portName, CancellationToken ct)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(portName);
            ct.ThrowIfCancellationRequested();

            lock (portLock)
            {
                if (port?.IsOpen == true)
                {
                    throw new InvalidOperationException("A transmit port is already open.");
                }

                var newPort = new SerialPort(portName, 115200, Parity.None, 8,
                    StopBits.One)
                {
                    Handshake = Handshake.None,
                    WriteTimeout = 500
                };
                newPort.DataReceived += Port_DataReceived;
                newPort.ErrorReceived += Port_ErrorReceived;
                newPort.Open();
                port = newPort;
                receiveBuffer.Clear();
            }

            return Task.CompletedTask;
        }

        private void Port_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            ClosePort();
            ConnectionLost?.Invoke(this, EventArgs.Empty);
        }

        private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string incoming;
                lock (portLock)
                {
                    if (port?.IsOpen != true)
                    {
                        return;
                    }
                    incoming = port.ReadExisting();
                }

                if (string.IsNullOrEmpty(incoming))
                {
                    return;
                }

                receiveBuffer.Append(incoming);
                while (true)
                {
                    string buffered = receiveBuffer.ToString();
                    int newlineIndex = buffered.IndexOf('\n');
                    if (newlineIndex < 0)
                    {
                        break;
                    }

                    string line = buffered[..newlineIndex].TrimEnd('\r');
                    receiveBuffer.Remove(0, newlineIndex + 1);
                    LineReceived?.Invoke(this, line);
                }

                if (receiveBuffer.Length > 8192)
                {
                    receiveBuffer.Clear();
                }
            }
            catch
            {
                ClosePort();
                ConnectionLost?.Invoke(this, EventArgs.Empty);
            }
        }

        public Task CloseAsync()
        {
            ClosePort();
            return Task.CompletedTask;
        }

        private void ClosePort()
        {
            lock (portLock)
            {
                if (port == null)
                {
                    return;
                }

                port.DataReceived -= Port_DataReceived;
                port.ErrorReceived -= Port_ErrorReceived;
                try
                {
                    if (port.IsOpen)
                    {
                        port.Close();
                    }
                }
                catch
                {
                }
                port.Dispose();
                port = null;
                receiveBuffer.Clear();
            }
        }

        public Task WriteAsync(byte[] buffer, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ct.ThrowIfCancellationRequested();

            lock (portLock)
            {
                if (port?.IsOpen != true)
                {
                    throw new InvalidOperationException("Transmit port is not open.");
                }
                port.Write(buffer, 0, buffer.Length);
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            ClosePort();
        }
    }
}
