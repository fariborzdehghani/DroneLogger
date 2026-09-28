using DroneLogger.Classes;
using System.Text.Json;
using Xunit;

namespace DroneLogger.PacketTests;

public class FlightLogContractTests
{
    [Fact]
    public void RemoteControllerPrefix_IsAcceptedAndPayloadDeserializes()
    {
        const string line = "RemoteController: Data={\"roll\":-6.36,\"pitch\":1.93,\"yaw\":224.17,\"Gz\":-3.42,\"Vz\":0.00,\"Vz_valid\":0,\"altitude\":0.00,\"base_throttle\":20.00,\"m1\":20.00,\"m2\":20.00,\"m3\":20.00,\"m4\":20.00,\"roll_p\":0.0000,\"roll_i\":0.0000,\"roll_d\":0.0000,\"pitch_p\":0.0000,\"pitch_i\":0.0000,\"pitch_d\":0.0000,\"Gz_p\":0.0000,\"Gz_i\":0.0000,\"Gz_d\":0.0000,\"altitude_p\":0.0000,\"altitude_i\":0.0000,\"altitude_d\":0.0000}";

        bool found = Logger.TryExtractMessagePayload(line, "Data", out string payload);
        LogData? data = JsonSerializer.Deserialize<LogData>(payload);

        Assert.True(found);
        Assert.NotNull(data);
        Assert.Equal(-6.36, data.roll);
        Assert.Equal(1.93, data.pitch);
        Assert.Equal(224.17, data.yaw);
        Assert.Equal(20.00, data.base_throttle);
    }

    [Theory]
    [InlineData("Data={\"roll\":1}", true)]
    [InlineData("RemoteController: Information={\"Code\":1001}", true)]
    [InlineData("NotData={}", false)]
    [InlineData("RemoteController: Data=missing-json", false)]
    public void MessageEnvelopeExtraction_RequiresACompleteMarkerAndJson(
        string line,
        bool expected)
    {
        string messageType = line.Contains("Information=", StringComparison.Ordinal)
            ? "Information"
            : "Data";

        Assert.Equal(
            expected,
            Logger.TryExtractMessagePayload(line, messageType, out _));
    }

    [Fact]
    public void FirmwareFlightLog_DeserializesEveryDesktopField()
    {
        const string line = "Data={\"roll\":1.25,\"pitch\":-2.50,\"yaw\":90.00,\"Gz\":0.75,\"Vz\":-0.20,\"Vz_valid\":1,\"altitude\":48.50,\"base_throttle\":31.00,\"m1\":32.00,\"m2\":33.00,\"m3\":30.00,\"m4\":29.00,\"roll_p\":0.1000,\"roll_i\":0.2000,\"roll_d\":0.3000,\"roll_total\":0.6000,\"pitch_p\":-0.1000,\"pitch_i\":-0.2000,\"pitch_d\":-0.3000,\"pitch_total\":-0.6000,\"Gz_p\":0.0100,\"Gz_i\":0.0200,\"Gz_d\":0.0300,\"altitude_p\":1.0000,\"altitude_i\":2.0000,\"altitude_d\":3.0000}";

        LogData? data = JsonSerializer.Deserialize<LogData>(line[5..]);

        Assert.NotNull(data);
        Assert.Equal(1.25, data.roll);
        Assert.Equal(-2.50, data.pitch);
        Assert.Equal(90.00, data.yaw);
        Assert.Equal(0.75, data.Gz);
        Assert.Equal(-0.20, data.Vz);
        Assert.Equal((uint)1, data.Vz_valid);
        Assert.Equal(48.50, data.altitude);
        Assert.Equal(31.00, data.base_throttle);
        Assert.Equal(32.00, data.m1);
        Assert.Equal(33.00, data.m2);
        Assert.Equal(30.00, data.m3);
        Assert.Equal(29.00, data.m4);
        Assert.Equal(0.1000, data.roll_p);
        Assert.Equal(0.2000, data.roll_i);
        Assert.Equal(0.3000, data.roll_d);
        Assert.Equal(0.6000, data.roll_total);
        Assert.Equal(-0.1000, data.pitch_p);
        Assert.Equal(-0.2000, data.pitch_i);
        Assert.Equal(-0.3000, data.pitch_d);
        Assert.Equal(-0.6000, data.pitch_total);
        Assert.Equal(0.0100, data.Gz_p);
        Assert.Equal(0.0200, data.Gz_i);
        Assert.Equal(0.0300, data.Gz_d);
        Assert.Equal(1.0000, data.altitude_p);
        Assert.Equal(2.0000, data.altitude_i);
        Assert.Equal(3.0000, data.altitude_d);
    }
}
