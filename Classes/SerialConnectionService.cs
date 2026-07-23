using System.IO;
using System.Threading.Channels;

namespace DroneLogger.Classes
{
    internal enum ConnectionState
    {
        Disconnected,
        Connected
    }

    internal enum PacketPriority
    {
        Normal,
        Heartbeat,
        Disarm
    }

    internal sealed class OutboundPacket
    {
        public required byte[] Data { get; init; }
        public required PacketPriority Priority { get; init; }
        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal sealed class SerialConnectionService : IDisposable
    {
        private readonly ISerialTransport transport;
        private Channel<OutboundPacket>? normalChannel;
        private Channel<OutboundPacket>? disarmChannel;
        private SemaphoreSlim? packetSignal;
        private CancellationTokenSource? connectionCts;
        private Task? writerLoopTask;
        private int heartbeatPending;

        public event EventHandler<string>? LineReceived;
        public event EventHandler<ConnectionState>? ConnectionStateChanged;

        public bool IsConnected { get; private set; }

        public SerialConnectionService() : this(new SerialPortTransport())
        {
        }

        internal SerialConnectionService(ISerialTransport transport)
        {
            this.transport = transport;
            this.transport.LineReceived += Transport_LineReceived;
            this.transport.ConnectionLost += Transport_ConnectionLost;
        }

        public async Task ConnectAsync(string portName, CancellationToken ct)
        {
            if (IsConnected)
            {
                throw new InvalidOperationException("The transmit port is already connected.");
            }

            if (writerLoopTask != null || transport.IsOpen)
            {
                await DisconnectAsync().ConfigureAwait(false);
            }

            CreateChannels();
            connectionCts = new CancellationTokenSource();

            try
            {
                await transport.OpenAsync(portName, ct).ConfigureAwait(false);
                IsConnected = true;
                Channel<OutboundPacket> normal = normalChannel!;
                Channel<OutboundPacket> disarm = disarmChannel!;
                SemaphoreSlim signal = packetSignal!;
                writerLoopTask = Task.Run(
                    () => WriterLoopAsync(normal, disarm, signal, connectionCts.Token),
                    CancellationToken.None);
                ConnectionStateChanged?.Invoke(this, ConnectionState.Connected);
            }
            catch
            {
                normalChannel?.Writer.TryComplete();
                disarmChannel?.Writer.TryComplete();
                normalChannel = null;
                disarmChannel = null;
                packetSignal = null;
                connectionCts.Dispose();
                connectionCts = null;
                throw;
            }
        }

        public async Task DisconnectAsync()
        {
            bool wasConnected = IsConnected;
            IsConnected = false;

            normalChannel?.Writer.TryComplete();
            disarmChannel?.Writer.TryComplete();
            connectionCts?.Cancel();

            if (writerLoopTask != null)
            {
                try
                {
                    await writerLoopTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            await transport.CloseAsync().ConfigureAwait(false);
            FailQueuedPackets(new OperationCanceledException("Transmit connection closed."));

            writerLoopTask = null;
            connectionCts?.Dispose();
            connectionCts = null;
            packetSignal = null;
            Interlocked.Exchange(ref heartbeatPending, 0);

            if (wasConnected)
            {
                ConnectionStateChanged?.Invoke(this, ConnectionState.Disconnected);
            }
        }

        public async Task EnqueuePacketAsync(
            byte[] data,
            PacketPriority priority,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (data.Length != ProtocolPacketBuilder.PacketSize)
            {
                throw new ArgumentException("Packet must be exactly 64 bytes.", nameof(data));
            }
            Channel<OutboundPacket>? normal = normalChannel;
            Channel<OutboundPacket>? disarm = disarmChannel;
            SemaphoreSlim? signal = packetSignal;
            if (!IsConnected || normal == null || disarm == null || signal == null)
            {
                throw new InvalidOperationException("Transmit port is not connected.");
            }

            if (priority == PacketPriority.Heartbeat &&
                Interlocked.CompareExchange(ref heartbeatPending, 1, 0) != 0)
            {
                return;
            }

            var packet = new OutboundPacket { Data = data, Priority = priority };

            try
            {
                ChannelWriter<OutboundPacket> writer =
                    priority == PacketPriority.Disarm
                        ? disarm.Writer
                        : normal.Writer;

                if (priority == PacketPriority.Heartbeat)
                {
                    if (!writer.TryWrite(packet))
                    {
                        Interlocked.Exchange(ref heartbeatPending, 0);
                        return;
                    }
                }
                else
                {
                    await writer.WriteAsync(packet, ct).ConfigureAwait(false);
                }

                signal.Release();
                await packet.Completion.Task.WaitAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                if (priority == PacketPriority.Heartbeat)
                {
                    Interlocked.Exchange(ref heartbeatPending, 0);
                }
                throw;
            }
        }

        private void CreateChannels()
        {
            packetSignal = new SemaphoreSlim(0);
            normalChannel = Channel.CreateBounded<OutboundPacket>(
                new BoundedChannelOptions(64)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait
                });
            disarmChannel = Channel.CreateUnbounded<OutboundPacket>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
        }

        private async Task WriterLoopAsync(
            Channel<OutboundPacket> normal,
            Channel<OutboundPacket> disarm,
            SemaphoreSlim signal,
            CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await signal.WaitAsync(token).ConfigureAwait(false);

                    if (disarm.Reader.TryRead(out OutboundPacket? disarmPacket))
                    {
                        await WritePacketAsync(disarmPacket, token).ConfigureAwait(false);
                        continue;
                    }

                    if (normal.Reader.TryRead(out OutboundPacket? normalPacket))
                    {
                        await WritePacketAsync(normalPacket, token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ReportConnectionLoss(ex);
            }
        }

        private async Task WritePacketAsync(OutboundPacket packet, CancellationToken token)
        {
            try
            {
                await transport.WriteAsync(packet.Data, token).ConfigureAwait(false);
                packet.Completion.TrySetResult();
            }
            catch (Exception ex)
            {
                packet.Completion.TrySetException(ex);
                throw;
            }
            finally
            {
                if (packet.Priority == PacketPriority.Heartbeat)
                {
                    Interlocked.Exchange(ref heartbeatPending, 0);
                }
            }
        }

        private void Transport_LineReceived(object? sender, string line)
        {
            LineReceived?.Invoke(this, line);
        }

        private void Transport_ConnectionLost(object? sender, EventArgs e)
        {
            ReportConnectionLoss(new IOException("The transmit serial port was disconnected."));
        }

        private void ReportConnectionLoss(Exception error)
        {
            if (!IsConnected)
            {
                return;
            }

            IsConnected = false;
            connectionCts?.Cancel();
            normalChannel?.Writer.TryComplete(error);
            disarmChannel?.Writer.TryComplete(error);
            FailQueuedPackets(error);
            ConnectionStateChanged?.Invoke(this, ConnectionState.Disconnected);
        }

        private void FailQueuedPackets(Exception error)
        {
            if (normalChannel != null)
            {
                while (normalChannel.Reader.TryRead(out OutboundPacket? packet))
                {
                    packet.Completion.TrySetException(error);
                }
            }
            if (disarmChannel != null)
            {
                while (disarmChannel.Reader.TryRead(out OutboundPacket? packet))
                {
                    packet.Completion.TrySetException(error);
                }
            }
        }

        public void Dispose()
        {
            transport.LineReceived -= Transport_LineReceived;
            transport.ConnectionLost -= Transport_ConnectionLost;
            try
            {
                DisconnectAsync().GetAwaiter().GetResult();
            }
            catch
            {
            }
            transport.Dispose();
        }
    }
}
