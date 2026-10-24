using DroneLogger.Classes;
using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace DroneLogger.PacketTests;

public class ImuTraceTests
{
    internal static byte[] Fixture() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "flight-log-v5.bin"));

    [Theory]
    [InlineData("flight-log-v5.bin", 2)]
    [InlineData("flight-log-v5-8g.bin", 8)]
    public void NativeFirmwareTrace_DecodesAndReconstructsTheRecordedAngles(string file, int accelRangeG)
    {
        var session = FlightLogSession.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", file)));
        Assert.Equal(5, session.FormatVersion);
        Assert.True(session.HasImuTrace);
        Assert.False(session.IsCompact);
        Assert.True(session.EstimatorSettingsRecorded);
        Assert.Equal(20, session.GyroCutoffHz);
        Assert.True(session.AccelRejectionEnabled);
        Assert.Equal(accelRangeG, session.AccelRangeG);
        Assert.Equal(250, session.GyroRangeDps);
        var first = session.Samples[0];
        Assert.Equal(42u, first.imu_sequence);
        Assert.Equal(1000u, first.imu_timestamp_ms);
        Assert.Equal(15, first.imu_dt_ms);
        Assert.Equal(1, first.accel_norm_g);
        Assert.Equal(-.40875, first.roll_previous_deg, 6);
        Assert.Equal(.5, first.roll_accel_deg);
        Assert.Equal(.25, first.roll_correction_deg);
        Assert.Equal(.07625, first.pitch_previous_deg, 6);
        Assert.Equal(2, first.pitch_accel_deg);
        Assert.Equal(.75, first.pitch_correction_deg);
        Assert.Equal(0, first.roll_bias_dps);
        Assert.Equal(0, first.pitch_bias_dps);
        Assert.Equal("Correction applied", first.roll_estimator_status);
        Assert.Equal(43u, session.Samples[1].imu_sequence);
        Assert.Equal(43u, session.Samples[^1].imu_sequence); // Forced stop retains the same IMU snapshot.
        Assert.All(session.Samples, s =>
        {
            Assert.True(s.imu_trace_valid);
            Assert.Equal(s.roll, s.roll_predicted_deg + s.roll_correction_deg, 5);
            Assert.Equal(s.pitch, s.pitch_predicted_deg + s.pitch_correction_deg, 5);
            Assert.Equal(s.roll_predicted_deg, s.roll_previous_deg + (s.roll_rate_dps - s.roll_bias_dps) * s.imu_dt_ms / 1000, 5);
            Assert.Equal(s.pitch_predicted_deg, s.pitch_previous_deg + (s.pitch_rate_dps - s.pitch_bias_dps) * s.imu_dt_ms / 1000, 5);
        });
    }

    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    public void CompactRecording_RetainsTheRecordedAccelerometerRange(byte accelRangeG)
    {
        byte[] blob = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "flight-log-v4.bin"));
        byte[] metadata = Fixture();
        metadata.AsSpan(52, 12).CopyTo(blob.AsSpan(52, 12));
        blob[55] = accelRangeG;
        var session = FlightLogSession.Decode(blob);
        Assert.True(session.IsCompact);
        Assert.True(session.EstimatorSettingsRecorded);
        Assert.Equal((int)accelRangeG, session.AccelRangeG);
        Assert.False(session.HasImuTrace);
    }

    [Theory]
    [InlineData("flight-log-v1.bin")]
    [InlineData("diagnostics-v2.bin")]
    [InlineData("flight-log-v3.bin")]
    [InlineData("flight-log-v4.bin")]
    public void OlderRecordings_DoNotInventTraceValues(string file)
    {
        var session = FlightLogSession.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", file)));
        Assert.False(session.HasImuTrace);
        Assert.False(session.EstimatorSettingsRecorded);
        Assert.Null(session.GyroCutoffHz);
        Assert.All(session.Samples, s =>
        {
            Assert.False(s.imu_trace_valid);
            Assert.Null(s.imu_sequence);
            Assert.True(double.IsNaN(s.roll_correction_deg));
            Assert.True(double.IsNaN(s.pitch_correction_deg));
            Assert.True(double.IsNaN(s.accel_norm_g));
            Assert.Equal("Unavailable", s.roll_estimator_status);
        });
    }

    [Theory]
    [InlineData(52, 2)]
    [InlineData(53, 2)]
    [InlineData(54, 99)]
    [InlineData(55, 4)]
    [InlineData(56, 0)]
    [InlineData(58, 64)]
    [InlineData(128 + 124 + 64, 2)]
    [InlineData(128 + 124 + 67, 16)]
    public void Decoder_RejectsUnsupportedTraceMetadataAndFlags(int offset, byte value)
    {
        byte[] blob = Fixture(); blob[offset] = value;
        Assert.Throws<InvalidDataException>(() => FlightLogSession.Decode(blob));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(101f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Decoder_RejectsAnInvalidValidTraceTimestep(float dt)
    {
        byte[] blob = Fixture();
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(128 + 124 + 8), BitConverter.SingleToInt32Bits(dt));
        Assert.Throws<InvalidDataException>(() => FlightLogSession.Decode(blob));
    }

    [Fact]
    public void InvalidTrace_IsExplicitAndExportsRetainTheTraceAndSettings()
    {
        byte[] blob = Fixture(); blob[128 + 124 + 64] = 0;
        var invalid = FlightLogSession.Decode(blob).Samples[0];
        Assert.False(invalid.imu_trace_valid);
        Assert.Equal("Unavailable", invalid.roll_estimator_status);
        Assert.True(double.IsNaN(invalid.accel_norm_g));
        var session = FlightLogSession.Decode(Fixture());
        string json = JsonSerializer.Serialize(session, new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals });
        Assert.Contains("\"GyroCutoffHz\":20", json);
        Assert.Contains("\"imu_trace_valid\":true", json);
        Assert.Contains("\"roll_correction_deg\":0.25", json);
        string csv = FlightLogArchive.CreateCsv(session);
        Assert.Contains("imu_sequence", csv);
        Assert.Contains("pitch_predicted_deg", csv);
        Assert.Contains("roll_estimator_status", csv);
    }
}
