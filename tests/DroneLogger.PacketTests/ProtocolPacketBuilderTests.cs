using DroneLogger.Classes;
using DroneLogger.Model;
using System.Text.Json;
using Xunit;

namespace DroneLogger.PacketTests;

public sealed class ProtocolPacketBuilderTests
{
    [Fact]
    public void ControlAndHeartbeatPacketsMatchFirmwareProtocol()
    {
        byte[] arm = ProtocolPacketBuilder.BuildArmPacket();
        byte[] disarm = ProtocolPacketBuilder.BuildDisarmPacket();
        byte[] takeoff = ProtocolPacketBuilder.BuildTakeoffPacket();
        byte[] heartbeat = ProtocolPacketBuilder.BuildHeartbeatPacket();

        Assert.Equal(64, arm.Length);
        Assert.Equal((byte)2, arm[0]);
        Assert.Equal((byte)1, arm[1]);
        Assert.All(arm.Skip(2), value => Assert.Equal((byte)0, value));

        Assert.Equal(64, disarm.Length);
        Assert.Equal((byte)2, disarm[0]);
        Assert.Equal((byte)0, disarm[1]);
        Assert.All(disarm.Skip(2), value => Assert.Equal((byte)0, value));

        Assert.Equal(64, takeoff.Length);
        Assert.Equal((byte)2, takeoff[0]);
        Assert.Equal((byte)2, takeoff[1]);
        Assert.All(takeoff.Skip(2), value => Assert.Equal((byte)0, value));

        Assert.Equal(64, heartbeat.Length);
        Assert.Equal((byte)3, heartbeat[0]);
        Assert.All(heartbeat.Skip(1), value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public void ConfigurationPacketUsesExpectedOffsetsScalesAndEndianness()
    {
        Config config = CreateValidConfig();

        byte[] packet = ProtocolPacketBuilder.BuildConfigurationPacket(config);

        Assert.Equal(64, packet.Length);
        Assert.Equal((byte)1, packet[0]);
        Assert.Equal((byte)50, packet[1]);
        Assert.Equal((byte)10, packet[2]);
        Assert.Equal((byte)80, packet[3]);
        Assert.Equal((byte)30, packet[4]);
        Assert.Equal((byte)0xD4, packet[5]); // -300 = 0xFED4
        Assert.Equal((byte)0xFE, packet[6]);
        Assert.Equal((byte)(1234 & 0xFF), packet[7]);
        Assert.Equal((byte)(1234 >> 8), packet[8]);
        Assert.Equal((byte)0, packet[13]); // Fixed altitude; reserved
        Assert.Equal((byte)0, packet[14]);
        Assert.Equal((byte)125, packet[15]); // 1.25 * 100
        Assert.Equal((byte)123, packet[16]); // 0.0123 * 10000
        Assert.Equal((byte)0, packet[17]);
        Assert.Equal((byte)0, packet[35]); // Reserved
        Assert.Equal((byte)250, packet[36]); // 25.0 * 10
        Assert.Equal((byte)0, packet[37]);
        Assert.Equal((byte)0, packet[40]); // Reserved
        Assert.Equal((byte)125, packet[41]); // 12.5 %/s * 10
        Assert.Equal((byte)0, packet[42]);
        Assert.All(packet.Skip(43), value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public void InvalidOrNonFiniteConfigurationValuesAreRejected()
    {
        Config invalidRange = CreateValidConfig();
        invalidRange.ArmThrottle = 101;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ProtocolPacketBuilder.BuildConfigurationPacket(invalidRange));

        Config nonFinite = CreateValidConfig();
        nonFinite.PitchKp = double.NaN;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ProtocolPacketBuilder.BuildConfigurationPacket(nonFinite));

        Config targetOverflow = CreateValidConfig();
        targetOverflow.TargetPitch = short.MaxValue + 1;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ProtocolPacketBuilder.BuildConfigurationPacket(targetOverflow));

        Config unsafeTakeoffRamp = CreateValidConfig();
        unsafeTakeoffRamp.TakeoffThrottleRampPerSecond = 31.0;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ProtocolPacketBuilder.BuildConfigurationPacket(unsafeTakeoffRamp));
    }

    [Fact]
    public void ExistingThrottleJsonLoadsAsArmThrottle()
    {
        Config? config = JsonSerializer.Deserialize<Config>(
            "{\"Throttle\":30}");

        Assert.NotNull(config);
        Assert.Equal(30, config.ArmThrottle);
        Assert.Contains(
            "\"Throttle\":30",
            JsonSerializer.Serialize(config));
    }

    private static Config CreateValidConfig() => new()
    {
        ArmThrottle = 50,
        MinSpeed = 10,
        MaxSpeed = 80,
        MaxAngle = 30,
        TargetPitch = -300,
        TargetRoll = 1234,
        TargetYaw = -10,
        TargetGz = 20,
        PitchKp = 1.25,
        PitchKi = 0.0123,
        PitchKd = 0.5,
        RollKp = 1.1,
        RollKi = 0.01,
        RollKd = 0.4,
        GzKp = 1.0,
        GzKi = 0.02,
        GzKd = 0.003,
        AltitudeKp = 0.9,
        AltitudeKi = 0.04,
        AltitudeKd = 0.005,
        PIDMaxIPart = 25,
        PIDMaxOutput = 30,
        TakeoffThrottleRampPerSecond = 12.5
    };
}
