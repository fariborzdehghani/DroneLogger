using DroneLogger.Model;

namespace DroneLogger.Classes
{
    internal sealed class Transmitter : IDisposable
    {
        private readonly MainWindow context;
        private readonly SerialConnectionService radioConnection;
        private readonly SemaphoreSlim controlStateGate = new(1, 1);
        private Logger? logger;
        private CancellationTokenSource? heartbeatCts;
        private volatile bool heartbeatEnabled = true;
        private volatile bool isArmed;
        private bool serialMode;

        public event EventHandler<ConnectionState>? ConnectionStateChanged;
        public event EventHandler<bool>? ArmedStateChanged;

        public bool IsArmed => isArmed;
        public bool IsConnected => serialMode
            ? logger?.IsLogPortOpen() == true
            : radioConnection.IsConnected;
        public bool IsSerialMode => serialMode;

        public Transmitter(MainWindow context)
        {
            this.context = context;
            radioConnection = new SerialConnectionService();
            radioConnection.LineReceived += (_, line) =>
                Tools.Log(context, $"RemoteController: {line}");
            radioConnection.ConnectionStateChanged += RadioConnection_StateChanged;
        }

        public void AttachLogger(Logger loggerInstance)
        {
            if (logger != null)
            {
                logger.PortStateChanged -= Logger_PortStateChanged;
            }

            logger = loggerInstance ?? throw new ArgumentNullException(nameof(loggerInstance));
            logger.PortStateChanged += Logger_PortStateChanged;
        }

        public void SetSerialMode(bool enabled)
        {
            if (isArmed)
            {
                throw new InvalidOperationException("DISARM before changing connection type.");
            }

            serialMode = enabled;
            RaiseConnectionState();
        }

        public async Task ArmAsync(CancellationToken ct = default)
        {
            await controlStateGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (isArmed)
                {
                    return;
                }

                EnsureConnected();
                await SendPackageCoreAsync(
                    ProtocolPacketBuilder.BuildArmPacket(),
                    PacketPriority.Normal,
                    ct).ConfigureAwait(false);

                isArmed = true;
                ArmedStateChanged?.Invoke(this, true);
                if (heartbeatEnabled)
                {
                    StartHeartbeat();
                }
            }
            finally
            {
                controlStateGate.Release();
            }
        }

        public async Task DisarmAsync(CancellationToken ct = default)
        {
            await controlStateGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                EnsureConnected();
                await SendPackageCoreAsync(
                    ProtocolPacketBuilder.BuildDisarmPacket(),
                    PacketPriority.Disarm,
                    ct).ConfigureAwait(false);

                StopHeartbeat();
                if (isArmed)
                {
                    isArmed = false;
                    ArmedStateChanged?.Invoke(this, false);
                }
            }
            finally
            {
                controlStateGate.Release();
            }
        }

        public async Task TakeoffAsync(CancellationToken ct = default)
        {
            await controlStateGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                EnsureConnected();
                if (!isArmed)
                {
                    throw new InvalidOperationException("ARM the drone before takeoff.");
                }

                await SendPackageCoreAsync(
                    ProtocolPacketBuilder.BuildTakeoffPacket(),
                    PacketPriority.Normal,
                    ct).ConfigureAwait(false);
            }
            finally
            {
                controlStateGate.Release();
            }
        }

        public void SetHeartbeatEnabled(bool enabled)
        {
            if (isArmed && !enabled)
            {
                throw new InvalidOperationException(
                    "Cannot disable desktop heartbeat while armed. DISARM first.");
            }

            heartbeatEnabled = enabled;
            if (!enabled)
            {
                StopHeartbeat();
                Tools.Log(context,
                    "WARNING: Desktop heartbeat transmission is disabled. " +
                    "The embedded failsafe may still be active.");
            }
            else if (isArmed)
            {
                StartHeartbeat();
            }
        }

        public async Task SendPackageAsync(
            byte[] data,
            PacketPriority priority = PacketPriority.Normal,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (data.Length != ProtocolPacketBuilder.PacketSize)
            {
                throw new ArgumentException("Packet must be exactly 64 bytes.", nameof(data));
            }
            if (isArmed && data[0] == 1)
            {
                throw new InvalidOperationException("DISARM before changing configuration.");
            }

            EnsureConnected();
            await SendPackageCoreAsync(data, priority, ct).ConfigureAwait(false);
        }

        private async Task SendPackageCoreAsync(
            byte[] data,
            PacketPriority priority,
            CancellationToken ct)
        {
            if (serialMode)
            {
                if (logger == null)
                {
                    throw new InvalidOperationException("Logger serial transport is unavailable.");
                }
                await logger.SendCommandAsync(data, ct).ConfigureAwait(false);
                return;
            }

            await radioConnection.EnqueuePacketAsync(data, priority, ct)
                .ConfigureAwait(false);
        }

        private void StartHeartbeat()
        {
            StopHeartbeat();
            var cts = new CancellationTokenSource();
            heartbeatCts = cts;
            _ = Task.Run(async () =>
            {
                using (cts)
                {
                    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
                    try
                    {
                        while (await timer.WaitForNextTickAsync(cts.Token).ConfigureAwait(false))
                        {
                            if (!heartbeatEnabled || !isArmed)
                            {
                                break;
                            }

                            await SendPackageCoreAsync(
                                ProtocolPacketBuilder.BuildHeartbeatPacket(),
                                PacketPriority.Heartbeat,
                                cts.Token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        Tools.Log(context, $"Heartbeat stopped: {ex.Message}");
                        ResetArmedStateAfterConnectionLoss();
                    }
                }
            }, CancellationToken.None);
        }

        private void StopHeartbeat()
        {
            CancellationTokenSource? cts = heartbeatCts;
            heartbeatCts = null;
            if (cts != null)
            {
                try
                {
                    cts.Cancel();
                }
                catch
                {
                }
            }
        }

        public void FillTransmitPortsList()
        {
            context.Dispatcher.BeginInvoke(() =>
            {
                context.cmb_TransmitPort.Items.Clear();
                foreach (string port in System.IO.Ports.SerialPort.GetPortNames())
                {
                    context.cmb_TransmitPort.Items.Add(port);
                }
                if (context.cmb_TransmitPort.Items.Count > 0)
                {
                    context.cmb_TransmitPort.SelectedIndex = 0;
                }
            });
        }

        public async Task ConnectTransmitPortAsync(CancellationToken ct = default)
        {
            if (serialMode)
            {
                throw new InvalidOperationException(
                    "The Transmitter port is used only in Radio mode.");
            }
            if (context.cmb_TransmitPort.SelectedItem == null)
            {
                throw new InvalidOperationException("No transmit port is selected.");
            }

            string portName = context.cmb_TransmitPort.SelectedItem.ToString()!;
            await radioConnection.ConnectAsync(portName, ct).ConfigureAwait(false);
            Tools.Log(context, $"Transmit port {portName} connected.");
        }

        public async Task DisconnectTransmitPortAsync()
        {
            if (!radioConnection.IsConnected)
            {
                return;
            }

            if (!serialMode && isArmed)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                    await DisarmAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Tools.Log(context, $"DISARM before disconnect failed: {ex.Message}");
                }
            }

            await radioConnection.DisconnectAsync().ConfigureAwait(false);
            Tools.Log(context, "Transmit port disconnected.");
        }

        public bool IsTransmitPortOpen() => radioConnection.IsConnected;

        public byte[] CreateConfigPacket(Config config) =>
            ProtocolPacketBuilder.BuildConfigurationPacket(config);

        public async Task ShutdownAsync()
        {
            if (isArmed && IsConnected)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                    await DisarmAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Tools.Log(context, $"Shutdown DISARM failed: {ex.Message}");
                }
            }

            StopHeartbeat();
            await radioConnection.DisconnectAsync().ConfigureAwait(false);
            ResetArmedStateAfterConnectionLoss();
        }

        private void EnsureConnected()
        {
            if (!IsConnected)
            {
                string portDescription = serialMode ? "Logger/device" : "Transmitter";
                throw new InvalidOperationException(
                    $"{portDescription} serial port is not connected.");
            }
        }

        private void RadioConnection_StateChanged(object? sender, ConnectionState state)
        {
            if (state == ConnectionState.Disconnected && !serialMode)
            {
                ResetArmedStateAfterConnectionLoss();
            }
            if (!serialMode)
            {
                ConnectionStateChanged?.Invoke(this, state);
            }
        }

        private void Logger_PortStateChanged(object? sender, bool connected)
        {
            if (!connected && serialMode)
            {
                ResetArmedStateAfterConnectionLoss();
            }
            if (serialMode)
            {
                ConnectionStateChanged?.Invoke(
                    this,
                    connected ? ConnectionState.Connected : ConnectionState.Disconnected);
            }
        }

        private void RaiseConnectionState()
        {
            ConnectionStateChanged?.Invoke(
                this,
                IsConnected ? ConnectionState.Connected : ConnectionState.Disconnected);
        }

        private void ResetArmedStateAfterConnectionLoss()
        {
            StopHeartbeat();
            if (isArmed)
            {
                isArmed = false;
                ArmedStateChanged?.Invoke(this, false);
            }
        }

        public void Dispose()
        {
            StopHeartbeat();
            if (logger != null)
            {
                logger.PortStateChanged -= Logger_PortStateChanged;
            }
            radioConnection.ConnectionStateChanged -= RadioConnection_StateChanged;
            radioConnection.Dispose();
            controlStateGate.Dispose();
        }
    }
}
