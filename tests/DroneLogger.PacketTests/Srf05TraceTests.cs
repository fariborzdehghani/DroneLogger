using DroneLogger.Classes;
using System.Buffers.Binary;
using System.IO;
using Xunit;

namespace DroneLogger.PacketTests;

public class Srf05TraceTests
{
    internal static byte[] Fixture() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "flight-log-v6.bin"));
    private const int Trace = 128 + 192;

    [Fact]
    public void NativeRecording_DistinguishesEchoesFromAcceptedMeasurementsAndPrediction()
    {
        var session = FlightLogSession.Decode(Fixture());
        Assert.Equal(6, session.FormatVersion);
        Assert.True(session.HasImuTrace);
        Assert.True(session.HasSrf05Trace);
        Assert.True(session.GyroBasedDamping);
        Assert.Equal(208, FlightLogSession.U16(session.RawBlob, 14));
        Assert.All(session.Samples, s =>
        {
            Assert.True(s.srf05_trace_valid);
            Assert.True(s.gyro_based_damping);
            Assert.Equal(995u, s.srf05_last_echo_ms);
            Assert.Equal(700u, s.srf05_last_accepted_ms);
            Assert.Equal(s.timestamp_ms - 700u, s.srf05_age_ms);
        });
        Assert.Equal(new uint?[] { 300, 310, 320 }, session.Samples.Select(s => s.srf05_age_ms));
        Assert.Equal("Rejected by altitude filter", session.Samples[0].srf05_status);
        Assert.Equal("Echo timeout", session.Samples[1].srf05_status);
        Assert.Equal(.25, session.Samples[0].roll);
        Assert.Equal(42u, session.Samples[0].imu_sequence);
    }

    [Theory]
    [InlineData("flight-log-v1.bin")]
    [InlineData("diagnostics-v2.bin")]
    [InlineData("flight-log-v3.bin")]
    [InlineData("flight-log-v4.bin")]
    [InlineData("flight-log-v5.bin")]
    [InlineData("flight-log-v5-8g.bin")]
    public void OlderRecordings_DoNotInventFreshnessOrDampingMetadata(string file)
    {
        var session = FlightLogSession.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", file)));
        Assert.False(session.HasSrf05Trace);
        Assert.Null(session.GyroBasedDamping);
        Assert.All(session.Samples, s =>
        {
            Assert.False(s.srf05_trace_valid);
            Assert.Null(s.srf05_last_echo_ms);
            Assert.Null(s.srf05_last_accepted_ms);
            Assert.Null(s.srf05_age_ms);
            Assert.Equal("Unavailable", s.srf05_status);
        });
    }

    [Fact]
    public void CompactRecording_RetainsDampingModeWithoutInventingSensorTraces()
    {
        byte[] blob = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "flight-log-v4.bin"));
        blob[6] |= 4;
        var session = FlightLogSession.Decode(blob);
        Assert.True(session.GyroBasedDamping);
        Assert.False(session.HasSrf05Trace);
        Assert.All(session.Samples, s => Assert.Null(s.srf05_age_ms));
    }

    [Theory]
    [InlineData(12, 2)]
    [InlineData(13, 4)]
    [InlineData(13, 2)] // Accepted measurement requires an echo.
    [InlineData(14, 7)]
    [InlineData(15, 1)]
    public void Decoder_RejectsMalformedTraceFlags(int offset, byte value)
    {
        byte[] blob = Fixture(); blob[Trace + offset] = value;
        Assert.Throws<InvalidDataException>(() => FlightLogSession.Decode(blob));
    }

    [Fact]
    public void Decoder_ChecksAgeAndSupportsTickZeroAndTickWrap()
    {
        byte[] blob = Fixture();
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(Trace + 8), 299);
        Assert.Throws<InvalidDataException>(() => FlightLogSession.Decode(blob));
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(Trace), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(Trace + 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(Trace + 8), 1000);
        var first = FlightLogSession.Decode(blob).Samples[0];
        Assert.Equal(0u, first.srf05_last_echo_ms);
        Assert.Equal(0u, first.srf05_last_accepted_ms);
        Assert.Equal(1000u, first.srf05_age_ms);
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(Trace + 4), uint.MaxValue - 50);
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(Trace + 8), 1051);
        Assert.Equal(1051u, FlightLogSession.Decode(blob).Samples[0].srf05_age_ms);
    }

    [Fact]
    public void NoAcceptedMeasurement_AndUnavailableTraceRemainExplicit()
    {
        byte[] blob = Fixture(); blob[Trace + 13] = 0; blob[Trace + 14] = 0;
        var first = FlightLogSession.Decode(blob).Samples[0];
        Assert.True(first.srf05_trace_valid);
        Assert.Null(first.srf05_last_echo_ms);
        Assert.Null(first.srf05_last_accepted_ms);
        Assert.Null(first.srf05_age_ms);
        Assert.Equal("Waiting for echo", first.srf05_status);
        blob[Trace + 12] = 0;
        first = FlightLogSession.Decode(blob).Samples[0];
        Assert.False(first.srf05_trace_valid);
        Assert.Equal("Unavailable", first.srf05_status);
    }

    [Fact]
    public void ArchiveAndExports_RetainFreshnessAndDampingMode()
    {
        byte[] blob = Fixture();
        var session = FlightLogSession.Decode(blob);
        string root = Path.Combine(Path.GetTempPath(), "DroneLogger-Srf05-" + Guid.NewGuid().ToString("N"));
        try
        {
            string folder = FlightLogArchive.Save(blob, session, root);
            var reopened = FlightLogArchive.Load(Path.Combine(folder, "flight.flightlog"));
            Assert.True(reopened.GyroBasedDamping);
            Assert.Equal(320u, reopened.Samples[^1].srf05_age_ms);
            string json = File.ReadAllText(Path.Combine(folder, "flight.json"));
            Assert.Contains("\"GyroBasedDamping\": true", json);
            Assert.Contains("\"srf05_last_accepted_ms\": 700", json);
            Assert.Contains("\"srf05_age_ms\": 320", json);
            string csv = File.ReadAllText(Path.Combine(folder, "samples.csv"));
            Assert.Contains("srf05_last_echo_ms", csv);
            Assert.Contains("srf05_age_ms", csv);
            Assert.Contains("gyro_based_damping", csv);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
