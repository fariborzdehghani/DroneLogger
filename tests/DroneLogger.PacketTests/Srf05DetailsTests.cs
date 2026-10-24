using DroneLogger.Classes;
using System.Buffers.Binary;
using System.IO;
using Xunit;

namespace DroneLogger.PacketTests;

public class Srf05DetailsTests
{
    internal static byte[] Fixture() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "flight-log-v7.bin"));
    private const int Trace = 128 + 192;

    [Theory]
    [InlineData("flight-log-v7.bin")]
    [InlineData("flight-log-v4.bin")]
    public void LandingFlagSurvivesFullAndCompactArchivesWithoutNewRecordBytes(string file)
    {
        byte[] blob = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", file));
        byte[] original = blob.ToArray();
        blob[128 + 5] |= 32;
        var session = FlightLogSession.Decode(blob);
        Assert.True(session.Samples[0].landing_active);
        Assert.Equal("LANDING", session.Samples[0].state_name);
        Assert.False(FlightLogSession.Decode(original).Samples[0].landing_active);
        Assert.Equal(original.Length, blob.Length);
        Assert.Contains("landing_active", FlightLogArchive.CreateCsv(session));
        session.Samples[0].state = 5;
        Assert.Equal("FAULT", session.Samples[0].state_name);
    }

    [Fact]
    public void NativeRecording_AddsRawEchoAndFilterResultWithoutLargerRecords()
    {
        var session = FlightLogSession.Decode(Fixture());
        Assert.Equal(7, session.FormatVersion);
        Assert.True(session.HasSrf05Details);
        Assert.Equal(208, FlightLogSession.U16(session.RawBlob, 14));
        Assert.Equal(Srf05TraceTests.Fixture().Length, session.RawBlob.Length);
        Assert.Equal(new uint?[] { 300, 310, 320 }, session.Samples.Select(s => s.srf05_age_ms));
        Assert.All(session.Samples, s =>
        {
            Assert.Equal(300.0, s.srf05_raw_distance_cm);
            Assert.Equal((byte)5, s.srf05_filter_result);
            Assert.Equal("Rejected by median motion gate", s.srf05_filter_status);
            Assert.Equal(995u, s.srf05_last_echo_ms);
        });
        // A later timeout does not erase the last raw echo or its filter result.
        Assert.Equal("Echo timeout", session.Samples[1].srf05_status);
    }

    [Theory]
    [InlineData(0, "Invalid filter input")]
    [InlineData(1, "Accepted")]
    [InlineData(2, "Rejected due to tilt")]
    [InlineData(3, "Waiting for three echoes")]
    [InlineData(4, "Duplicate or old timestamp")]
    [InlineData(5, "Rejected by median motion gate")]
    [InlineData(6, "Rejected by prediction innovation gate")]
    [InlineData(7, "Invalid filter state")]
    public void Decoder_DescribesEachFilterResult(byte result, string description)
    {
        byte[] blob = Fixture(); blob[Trace + 15] = result;
        Assert.Equal(description, FlightLogSession.Decode(blob).Samples[0].srf05_filter_status);
    }

    [Fact]
    public void DerivedAgeSupportsTickZeroAndWrap()
    {
        byte[] blob = Fixture();
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(Trace), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(Trace + 4), 0);
        var first = FlightLogSession.Decode(blob).Samples[0];
        Assert.Equal(0u, first.srf05_last_echo_ms);
        Assert.Equal(1000u, first.srf05_age_ms);
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(Trace + 4), uint.MaxValue - 50);
        Assert.Equal(1051u, FlightLogSession.Decode(blob).Samples[0].srf05_age_ms);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-1f)]
    [InlineData(0f)]
    public void DecoderRejectsInvalidRawDistance(float distance)
    {
        byte[] blob = Fixture();
        BinaryPrimitives.WriteSingleLittleEndian(blob.AsSpan(Trace + 8), distance);
        Assert.Throws<InvalidDataException>(() => FlightLogSession.Decode(blob));
    }

    [Fact]
    public void DecoderRejectsUnknownFilterResult()
    {
        byte[] blob = Fixture(); blob[Trace + 15] = 8;
        Assert.Throws<InvalidDataException>(() => FlightLogSession.Decode(blob));
    }

    [Fact]
    public void MissingEchoAndOlderLogsDoNotInventDetails()
    {
        byte[] blob = Fixture(); blob[Trace + 13] = 0;
        var first = FlightLogSession.Decode(blob).Samples[0];
        Assert.Null(first.srf05_raw_distance_cm);
        Assert.Null(first.srf05_filter_result);
        Assert.Null(first.srf05_age_ms);
        var older = FlightLogSession.Decode(Srf05TraceTests.Fixture());
        Assert.False(older.HasSrf05Details);
        Assert.All(older.Samples, s =>
        {
            Assert.Null(s.srf05_raw_distance_cm);
            Assert.Equal("Not recorded", s.srf05_filter_status);
        });
    }

    [Fact]
    public void ArchiveAndExportsRetainRawEchoDetails()
    {
        byte[] blob = Fixture();
        string root = Path.Combine(Path.GetTempPath(), "DroneLogger-Srf05Details-" + Guid.NewGuid().ToString("N"));
        try
        {
            string folder = FlightLogArchive.Save(blob, FlightLogSession.Decode(blob), root);
            var reopened = FlightLogArchive.Load(Path.Combine(folder, "flight.flightlog"));
            Assert.Equal(300.0, reopened.Samples[^1].srf05_raw_distance_cm);
            Assert.Equal(320u, reopened.Samples[^1].srf05_age_ms);
            string json = File.ReadAllText(Path.Combine(folder, "flight.json"));
            Assert.Contains("\"srf05_raw_distance_cm\": 300", json);
            Assert.Contains("\"srf05_filter_result\": 5", json);
            string csv = File.ReadAllText(Path.Combine(folder, "samples.csv"));
            Assert.Contains("srf05_raw_distance_cm", csv);
            Assert.Contains("Rejected by median motion gate", csv);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
