using DroneLogger.Classes;
using Xunit;

namespace DroneLogger.PacketTests;

public sealed class SerialConnectionServiceTests
{
    [Fact]
    public async Task ConnectionCanDisconnectAndReconnect()
    {
        var transport = new FakeSerialTransport();
        using var service = new SerialConnectionService(transport);

        await service.ConnectAsync("COM1", CancellationToken.None);
        await service.EnqueuePacketAsync(
            ProtocolPacketBuilder.BuildArmPacket(), PacketPriority.Normal);
        await service.DisconnectAsync();

        await service.ConnectAsync("COM1", CancellationToken.None);
        await service.EnqueuePacketAsync(
            ProtocolPacketBuilder.BuildHeartbeatPacket(), PacketPriority.Heartbeat);
        await service.DisconnectAsync();

        Assert.Equal(2, transport.OpenCount);
        Assert.Equal(2, transport.Writes.Count);
    }

    [Fact]
    public async Task DisarmIsSentBeforeQueuedNormalPacket()
    {
        var transport = new FakeSerialTransport { BlockFirstWrite = true };
        using var service = new SerialConnectionService(transport);
        await service.ConnectAsync("COM1", CancellationToken.None);

        Task first = service.EnqueuePacketAsync(
            ProtocolPacketBuilder.BuildArmPacket(), PacketPriority.Normal);
        await transport.FirstWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        byte[] configuration = new byte[64];
        configuration[0] = 1;
        Task second = service.EnqueuePacketAsync(configuration, PacketPriority.Normal);
        Task disarm = service.EnqueuePacketAsync(
            ProtocolPacketBuilder.BuildDisarmPacket(), PacketPriority.Disarm);

        transport.ReleaseFirstWrite();
        await Task.WhenAll(first, second, disarm).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal((byte)2, transport.Writes[0][0]);
        Assert.Equal((byte)1, transport.Writes[0][1]);
        Assert.Equal((byte)2, transport.Writes[1][0]);
        Assert.Equal((byte)0, transport.Writes[1][1]);
        Assert.Equal((byte)1, transport.Writes[2][0]);
    }

    [Fact]
    public async Task RedundantPendingHeartbeatIsCoalesced()
    {
        var transport = new FakeSerialTransport { BlockFirstWrite = true };
        using var service = new SerialConnectionService(transport);
        await service.ConnectAsync("COM1", CancellationToken.None);

        Task first = service.EnqueuePacketAsync(
            ProtocolPacketBuilder.BuildHeartbeatPacket(), PacketPriority.Heartbeat);
        await transport.FirstWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.EnqueuePacketAsync(
            ProtocolPacketBuilder.BuildHeartbeatPacket(), PacketPriority.Heartbeat);

        transport.ReleaseFirstWrite();
        await first.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Single(transport.Writes);
    }

    private sealed class FakeSerialTransport : ISerialTransport
    {
        private readonly TaskCompletionSource firstWriteRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int writeCount;

        public event EventHandler<string>? LineReceived { add { } remove { } }
        public event EventHandler? ConnectionLost { add { } remove { } }
        public bool IsOpen { get; private set; }
        public bool BlockFirstWrite { get; init; }
        public int OpenCount { get; private set; }
        public List<byte[]> Writes { get; } = new();
        public TaskCompletionSource FirstWriteStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task OpenAsync(string portName, CancellationToken ct)
        {
            IsOpen = true;
            OpenCount++;
            return Task.CompletedTask;
        }

        public Task CloseAsync()
        {
            IsOpen = false;
            return Task.CompletedTask;
        }

        public async Task WriteAsync(byte[] buffer, CancellationToken ct)
        {
            if (!IsOpen)
            {
                throw new InvalidOperationException("Port not open");
            }

            int currentWrite = Interlocked.Increment(ref writeCount);
            lock (Writes)
            {
                Writes.Add(buffer.ToArray());
            }

            if (currentWrite == 1)
            {
                FirstWriteStarted.TrySetResult();
                if (BlockFirstWrite)
                {
                    await firstWriteRelease.Task.WaitAsync(ct);
                }
            }
        }

        public void ReleaseFirstWrite() => firstWriteRelease.TrySetResult();

        public void Dispose()
        {
            IsOpen = false;
        }
    }
}
