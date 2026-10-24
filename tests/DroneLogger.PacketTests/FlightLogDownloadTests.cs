using DroneLogger.Classes;
using System.Buffers.Binary;
using System.IO;
using Xunit;

namespace DroneLogger.PacketTests;

public class FlightLogDownloadTests
{
    [Theory]
    [InlineData(1, 0u)]
    [InlineData(2, 0u)]
    [InlineData(2, 1u)]
    [InlineData(2, 1359u)]
    [InlineData(3, 0u)]
    public void Requests_KeepTransferIdSeparateFromZeroBasedChunkIndex(byte operation, uint chunk)
    {
        byte[] request = FlightLogProtocol.BuildRequest(operation, 0x12345678u, chunk);
        Assert.Equal(64, request.Length);
        Assert.Equal("78563412", Convert.ToHexString(request.AsSpan(4, 4)));
        Assert.Equal(chunk, BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(8, 4)));
        Assert.Equal(FlightLogProtocol.Crc16(request.AsSpan(0, 62)),
            BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(62, 2)));
    }

    private static byte[] DiagnosticFixture() => File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "diagnostics-v2.bin"));

    [Fact]
    public async Task DiagnosticsBeforeConfiguration_DownloadsMessagesAndActualLoopAverage()
    {
        byte[] blob = DiagnosticFixture();
        FlightLogDownloader downloader = null!;
        downloader = new FlightLogDownloader((request, ct) =>
        {
            downloader.AcceptLine(Line(Response(request, blob)));
            return Task.CompletedTask;
        });
        var session = FlightLogSession.Decode(await downloader.DownloadAsync(null, CancellationToken.None));
        Assert.Equal(2, session.FormatVersion);
        Assert.False(session.ConfigurationApplied);
        Assert.Equal("Disarmed diagnostic snapshot", session.StopDescription);
        Assert.Equal(2.0, session.AverageLoopDtMs);
        Assert.Equal(2u, session.LoopIterationCount);
        Assert.Equal(3.0, Assert.Single(session.Samples).loop_dt_ms); // Not the all-loop mean.
        Assert.Equal(16, session.Messages.Count);
        Assert.True(session.Messages[^1].IsError);
        Assert.Equal((ushort)2002, session.Messages[^1].Code);
        Assert.Equal("TAKEOFF rejected: tilt; roll=-18.25 pitch=2.5 limit=15 deg", session.Messages[^1].Message);
        Assert.Equal(50u, session.Messages[0].TimestampMs);
        Assert.Equal(-1.25, session.Samples[0].roll);
        Assert.Equal(12, session.Samples[0].altitude);
    }

    [Fact]
    public void DiagnosticDecoder_RejectsMalformedMessagesAndTiming()
    {
        byte[] blob = DiagnosticFixture();
        blob[48] = 17;
        Assert.Throws<InvalidDataException>(() => FlightLogSession.Decode(blob));
        blob = DiagnosticFixture(); blob[244 + 7] = 97;
        Assert.Throws<InvalidDataException>(() => FlightLogSession.Decode(blob));
        blob = DiagnosticFixture();
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(40), BitConverter.SingleToInt32Bits(float.NaN));
        Assert.Throws<InvalidDataException>(() => FlightLogSession.Decode(blob));
    }

    [Fact]
    public void DiagnosticArchive_PreservesMessagesAndTiming()
    {
        string root = Path.Combine(Path.GetTempPath(), "DroneLogger-diagnostics-" + Guid.NewGuid());
        try
        {
            byte[] blob = DiagnosticFixture();
            var session = FlightLogSession.Decode(blob);
            string folder = FlightLogArchive.Save(blob, session, root);
            var reopened = FlightLogArchive.Load(Path.Combine(folder, "flight.flightlog"));
            Assert.Equal(session.Messages, reopened.Messages);
            Assert.Equal(session.AverageLoopDtMs, reopened.AverageLoopDtMs);
            string text = File.ReadAllText(Path.Combine(folder, "diagnostics.txt"));
            Assert.Contains("2.000 ms", text);
            Assert.Contains("TAKEOFF rejected", text);
            Assert.Contains("AverageLoopDtMs", File.ReadAllText(Path.Combine(folder, "flight.json")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static byte[] FirmwareFixture() => File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "flight-log-v1.bin"));

    private static byte[] PidFixture() => File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "flight-log-v3.bin"));

    private static byte[] CompactFixture() => File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "flight-log-v4.bin"));

    [Fact]
    public void NativeV4Fixture_PreservesPidPrecisionAndMarksOmittedChannelsUnavailable()
    {
        byte[] blob = CompactFixture();
        var session = FlightLogSession.Decode(blob);
        Assert.True(session.IsCompact);
        Assert.False(session.CompactFallback);
        Assert.Equal(382, blob.Length);
        Assert.Equal(10, session.SampleIntervalMs);
        Assert.Equal(new double[] { 0, 10, 20 }, session.Samples.Select(s => s.elapsed_ms));
        Assert.Equal("Flight started", Assert.Single(session.Messages).Message);
        Assert.Equal(15, session.AverageLoopDtMs);
        Assert.Equal(7, session.ErrorCode);
        var s = session.Samples[0];
        Assert.Equal(0.25, s.roll); Assert.Equal(1.25, s.pitch);
        Assert.Equal(27.25, s.roll_rate_dps); Assert.Equal(28.25, s.pitch_rate_dps);
        Assert.True(s.attitude_rates_valid);
        Assert.Equal(11.123, s.roll_p); Assert.Equal(12.25, s.roll_i);
        Assert.Equal(-0.004, s.roll_d); Assert.Equal(14.25, s.roll_total);
        Assert.Equal(15.25, s.pitch_p); Assert.Equal(16.25, s.pitch_i);
        Assert.Equal(17.25, s.pitch_d); Assert.Equal(18.25, s.pitch_total);
        Assert.Equal(new double[] { 7.25, 8.25, 9.25, 10.25, 6.25 }, new double[] { s.m1, s.m2, s.m3, s.m4, s.base_throttle });
        Assert.Equal(5.3, s.altitude); Assert.Equal(26.3, s.altitude_target);
        Assert.Equal(4.25, s.Vz); Assert.Equal(25.25, s.loop_dt_ms);
        Assert.Equal(1u, s.Vz_valid); Assert.True(s.altitude_valid && s.altitude_control_active && s.mixer_limited);
        Assert.All(new[] { s.yaw, s.Gz, s.Gz_p, s.Gz_i, s.Gz_d, s.altitude_p, s.altitude_i, s.altitude_d },
            value => Assert.True(double.IsNaN(value)));
        Assert.Equal((byte)5, session.Samples[^1].state);
        Assert.Equal((ushort)7, session.Samples[^1].error_code);

        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(14), 124);
        Assert.Throws<InvalidDataException>(() => FlightLogSession.Decode(blob));
        blob = PidFixture(); blob[6] |= 2;
        Assert.True(FlightLogSession.Decode(blob).CompactFallback);
        Assert.False(FlightLogSession.Decode(blob).IsCompact);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DownloadProfile_IsSentOnlyOnBegin_AndArchivesBothFormats(bool fullPrecision, bool imuTrace)
    {
        byte[] blob = imuTrace ? ImuTraceTests.Fixture() : fullPrecision ? PidFixture() : CompactFixture();
        var requests = new List<byte[]>();
        FlightLogDownloader downloader = null!;
        downloader = new FlightLogDownloader((request, ct) =>
        {
            requests.Add(request);
            downloader.AcceptLine(Line(Response(request, blob)));
            return Task.CompletedTask;
        });
        var received = await downloader.DownloadAsync(null, CancellationToken.None, fullPrecision);
        Assert.Equal(blob, received);
        Assert.Equal(fullPrecision ? (byte)1 : (byte)0, requests.First()[3]);
        Assert.All(requests.Skip(1), r => Assert.Equal((byte)0, r[3]));
        Assert.All(requests, r => Assert.Equal(64, r.Length));
        string root = Path.Combine(Path.GetTempPath(), "DroneLogger-compact-" + Guid.NewGuid());
        try
        {
            var session = FlightLogSession.Decode(received);
            string folder = FlightLogArchive.Save(received, session, root);
            var reopened = FlightLogArchive.Load(Path.Combine(folder, "flight.flightlog"));
            Assert.Equal(blob, reopened.RawBlob);
            Assert.Equal(!fullPrecision, reopened.IsCompact);
            Assert.Equal(session.Samples[0].roll_p, reopened.Samples[0].roll_p);
            Assert.Equal(session.Messages, reopened.Messages);
            if (!fullPrecision)
            {
                Assert.True(double.IsNaN(reopened.Samples[0].yaw));
                Assert.Contains("NaN", File.ReadAllText(Path.Combine(folder, "samples.csv")));
                Assert.Contains("\"NaN\"", File.ReadAllText(Path.Combine(folder, "flight.json")));
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MaximumCompactRecording_Downloads512SamplesAnd16MessagesIn571Chunks()
    {
        byte[] first = CompactFixture(), diagnostics = DiagnosticFixture();
        byte[] blob = new byte[128 + 512 * 50 + 16 * 104];
        first.AsSpan(0, 128).CopyTo(blob);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(12), 512);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(48), 16);
        for (int i = 0; i < 512; i++)
        {
            int offset = 128 + i * 50;
            first.AsSpan(128, 50).CopyTo(blob.AsSpan(offset));
            BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(offset), 1000u + (uint)i * 10);
        }
        diagnostics.AsSpan(128 + 116, 16 * 104).CopyTo(blob.AsSpan(128 + 512 * 50));
        int chunks = 0;
        FlightLogDownloader downloader = null!;
        downloader = new FlightLogDownloader((request, ct) =>
        {
            if (request[1] == 2) chunks++;
            downloader.AcceptLine(Line(Response(request, blob)));
            return Task.CompletedTask;
        });
        byte[] received = await downloader.DownloadAsync(null, CancellationToken.None);
        Assert.Equal(blob, received);
        Assert.Equal(571, chunks);
        var session = FlightLogSession.Decode(received);
        Assert.Equal(512, session.Samples.Count);
        Assert.Equal(5110, session.Samples[^1].elapsed_ms);
        Assert.Equal(16, session.Messages.Count);
    }

    [Fact]
    public void NativeV3Fixture_DecodesDenseSamplesAndGyroRates_WithoutInventingLegacyRates()
    {
        var session = FlightLogSession.Decode(PidFixture());
        Assert.Equal(3, session.FormatVersion);
        Assert.Equal(10, session.SampleIntervalMs);
        Assert.Equal(new double[] { 0, 10, 20 }, session.Samples.Select(s => s.elapsed_ms));
        Assert.All(session.Samples, s =>
        {
            Assert.True(s.attitude_rates_valid);
            Assert.Equal(27.25, s.roll_rate_dps);
            Assert.Equal(28.25, s.pitch_rate_dps);
        });
        foreach (var blob in new[] { FirmwareFixture(), DiagnosticFixture() })
            Assert.All(FlightLogSession.Decode(blob).Samples, s =>
            {
                Assert.False(s.attitude_rates_valid);
                Assert.True(double.IsNaN(s.roll_rate_dps) && double.IsNaN(s.pitch_rate_dps));
            });
        string csv = FlightLogArchive.CreateCsv(session);
        Assert.Contains("roll_rate_dps", csv);
        Assert.Contains("attitude_rates_valid", csv);
        Assert.Equal(14.25, session.Samples[0].roll_total);
    }

    [Fact]
    public void V3Decoder_RejectsMismatchedLayoutsAndInvalidValidRates()
    {
        byte[] blob = PidFixture();
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(14), 116);
        Assert.Throws<InvalidDataException>(() => FlightLogSession.Decode(blob));
        blob = PidFixture();
        BinaryPrimitives.WriteSingleLittleEndian(blob.AsSpan(128 + 8 + 27 * 4), float.NaN);
        Assert.Throws<InvalidDataException>(() => FlightLogSession.Decode(blob));
        blob[128 + 5] &= unchecked((byte)~16);
        Assert.False(FlightLogSession.Decode(blob).Samples[0].attitude_rates_valid);
    }

    [Fact]
    public async Task MaximumV3Recording_DownloadsAll512SamplesAndMessages()
    {
        byte[] first = PidFixture();
        byte[] diagnostics = DiagnosticFixture();
        byte[] blob = new byte[128 + 512 * FlightLogProtocol.RecordSize + 16 * FlightLogProtocol.EventSize];
        first.AsSpan(0, 128).CopyTo(blob);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(12), 512);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(48), 16);
        for (int i = 0; i < 512; i++)
        {
            int offset = 128 + i * 124;
            first.AsSpan(128, 124).CopyTo(blob.AsSpan(offset));
            BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(offset), 1000u + (uint)i * 10);
        }
        diagnostics.AsSpan(128 + 116, 16 * 104).CopyTo(blob.AsSpan(128 + 512 * 124));
        FlightLogDownloader downloader = null!;
        downloader = new FlightLogDownloader((request, ct) =>
        {
            downloader.AcceptLine(Line(Response(request, blob)));
            return Task.CompletedTask;
        });
        byte[] received = await downloader.DownloadAsync(null, CancellationToken.None);
        Assert.Equal(blob, received);
        var session = FlightLogSession.Decode(received);
        Assert.Equal(512, session.Samples.Count);
        Assert.Equal(5110, session.Samples[^1].elapsed_ms);
        Assert.Equal(16, session.Messages.Count);
        Assert.Equal(27.25, session.Samples[^1].roll_rate_dps);
    }

    [Fact]
    public async Task MaximumTraceRecording_Downloads512SamplesAnd16MessagesIn2086Chunks()
    {
        byte[] first = ImuTraceTests.Fixture(), diagnostics = DiagnosticFixture();
        byte[] blob = new byte[128 + 512 * FlightLogProtocol.TraceRecordSize + 16 * FlightLogProtocol.EventSize];
        first.AsSpan(0, 128).CopyTo(blob);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(12), 512);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(48), 16);
        for (int i = 0; i < 512; i++)
        {
            int offset = 128 + i * FlightLogProtocol.TraceRecordSize;
            first.AsSpan(128, FlightLogProtocol.TraceRecordSize).CopyTo(blob.AsSpan(offset));
            BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(offset), 1000u + (uint)i * 10);
            BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(offset + 124), 42u + (uint)i);
            BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(offset + 128), 1000u + (uint)i * 10);
        }
        diagnostics.AsSpan(128 + 116, 16 * 104).CopyTo(blob.AsSpan(128 + 512 * FlightLogProtocol.TraceRecordSize));
        int readRequests = 0;
        FlightLogDownloader downloader = null!;
        downloader = new FlightLogDownloader((request, ct) =>
        {
            Assert.Equal(64, request.Length);
            if (request[1] == FlightLogProtocol.Read) readRequests++;
            downloader.AcceptLine(Line(Response(request, blob)));
            return Task.CompletedTask;
        });
        byte[] received = await downloader.DownloadAsync(null, CancellationToken.None, fullPrecision: true);
        Assert.Equal(blob, received);
        Assert.Equal(2086, readRequests);
        var session = FlightLogSession.Decode(received);
        Assert.Equal(512, session.Samples.Count);
        Assert.Equal(16, session.Messages.Count);
        Assert.Equal(553u, session.Samples[^1].imu_sequence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MaximumSrf05TraceRecording_Downloads512SamplesAnd16MessagesIn2256Chunks(bool details)
    {
        byte[] first = details ? Srf05DetailsTests.Fixture() : Srf05TraceTests.Fixture();
        byte[] diagnostics = DiagnosticFixture();
        byte[] blob = new byte[FlightLogProtocol.MaxBlobSize];
        first.AsSpan(0, 128).CopyTo(blob);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(12), 512);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(48), 16);
        for (int i = 0; i < 512; i++)
        {
            int offset = 128 + i * FlightLogProtocol.Srf05TraceRecordSize;
            first.AsSpan(128, FlightLogProtocol.Srf05TraceRecordSize).CopyTo(blob.AsSpan(offset));
            BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(offset), 1000u + (uint)i * 10);
            if (!details) BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(offset + 200), 300u + (uint)i * 10);
        }
        diagnostics.AsSpan(128 + 116, 16 * 104).CopyTo(blob.AsSpan(128 + 512 * FlightLogProtocol.Srf05TraceRecordSize));
        int readRequests = 0;
        FlightLogDownloader downloader = null!;
        downloader = new FlightLogDownloader((request, ct) =>
        {
            Assert.Equal(64, request.Length);
            if (request[1] == FlightLogProtocol.Read) readRequests++;
            downloader.AcceptLine(Line(Response(request, blob)));
            return Task.CompletedTask;
        });
        byte[] received = await downloader.DownloadAsync(null, CancellationToken.None, fullPrecision: true);
        Assert.Equal(blob, received);
        Assert.Equal(2256, readRequests);
        var session = FlightLogSession.Decode(received);
        Assert.Equal(512, session.Samples.Count);
        Assert.Equal(16, session.Messages.Count);
        Assert.True(session.GyroBasedDamping);
        Assert.Equal(5410u, session.Samples[^1].srf05_age_ms);
        if (details) Assert.Equal(300.0, session.Samples[^1].srf05_raw_distance_cm);
    }

    private static byte[] Response(byte[] request, byte[] blob, byte status = 0)
    {
        byte[] response = new byte[64];
        response[0] = 5; response[1] = status; response[2] = 1;
        request.AsSpan(4, 4).CopyTo(response.AsSpan(4));
        uint sequence = request[1] switch
        {
            1 => FlightLogProtocol.BeginSequence,
            3 => FlightLogProtocol.EndSequence,
            _ => FlightLogSession.U32(request, 8)
        };
        BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(8), sequence);
        if (status == 0 && request[1] == 1)
        {
            response[3] = 12;
            BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(12), (uint)blob.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(16), FlightLogProtocol.Crc32(blob));
            BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(20), FlightLogSession.U32(blob, 8));
        }
        else if (status == 0 && request[1] == 2)
        {
            int offset = checked((int)sequence * 48);
            response[3] = (byte)Math.Min(48, blob.Length - offset);
            blob.AsSpan(offset, response[3]).CopyTo(response.AsSpan(12));
        }
        FlightLogProtocol.Seal(response);
        return response;
    }

    private static string Line(byte[] packet) => "FlightLog=" + Convert.ToHexString(packet);

    [Fact]
    public void ChecksumsAndRequests_MatchFirmwareContract()
    {
        Assert.Equal((ushort)0x29b1, FlightLogProtocol.Crc16("123456789"u8));
        Assert.Equal(0xcbf43926u, FlightLogProtocol.Crc32("123456789"u8));
        foreach (byte operation in new byte[] { 1, 2, 3 })
        {
            var request = FlightLogProtocol.BuildRequest(operation, 0x12345678);
            Assert.Equal(64, request.Length);
            Assert.Equal((byte)4, request[0]);
            Assert.Equal((byte)1, request[2]);
            Assert.Equal(0x12345678u, FlightLogSession.U32(request, 4));
            Assert.All(request[12..62], b => Assert.Equal((byte)0, b));
            Assert.Equal(FlightLogProtocol.Crc16(request.AsSpan(0, 62)), FlightLogSession.U16(request, 62));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => FlightLogProtocol.BuildRequest(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FlightLogProtocol.BuildRequest(3, 1, 1));
        Assert.Equal((byte)1, FlightLogProtocol.BuildRequest(1, 1, profile: 1)[3]);
        Assert.Throws<ArgumentOutOfRangeException>(() => FlightLogProtocol.BuildRequest(1, 1, profile: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => FlightLogProtocol.BuildRequest(2, 1, profile: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => FlightLogProtocol.BuildRequest(3, 1, profile: 1));
        Assert.Equal((byte)0, ProtocolPacketBuilder.BuildDisarmPacket()[2]);
        Assert.Equal((byte)1, ProtocolPacketBuilder.BuildDisarmPacket(operatorLinkTimeout: true)[2]);
    }

    [Fact]
    public void NativeFirmwareFixture_DecodesAllFieldsAndStopReason()
    {
        var session = FlightLogSession.Decode(FirmwareFixture());
        Assert.Equal(3, session.Samples.Count);
        Assert.Equal("Firmware fault: Maximum flight angle exceeded", session.StopDescription);
        Assert.Equal((byte)1, session.HalStatus);
        Assert.Equal(40.5, session.EffectiveHoverThrottle);
        Assert.Equal(10, session.Configuration.ArmThrottle);
        Assert.Equal(50, session.Configuration.TakeoffAltitudeCm);
        Assert.Equal(10, session.Configuration.TakeoffThrottleRampPerSecond);
        Assert.Equal((byte)5, session.EnabledSensors);
        var point = session.Samples[0];
        Assert.Equal(1000u, point.timestamp_ms);
        Assert.Equal(0, point.elapsed_ms);
        Assert.True(point.altitude_valid && point.altitude_control_active && point.mixer_limited);
        Assert.Equal(1u, point.Vz_valid);
        Assert.Equal(0.25, point.roll);
        Assert.Equal(1.25, point.pitch);
        Assert.Equal(2.25, point.yaw);
        Assert.Equal(3.25, point.Gz);
        Assert.Equal(4.25, point.Vz);
        Assert.Equal(5.25, point.altitude);
        Assert.Equal(6.25, point.base_throttle);
        Assert.Equal(7.25, point.m1); Assert.Equal(8.25, point.m2);
        Assert.Equal(9.25, point.m3); Assert.Equal(10.25, point.m4);
        Assert.Equal(11.25, point.roll_p); Assert.Equal(12.25, point.roll_i);
        Assert.Equal(13.25, point.roll_d); Assert.Equal(14.25, point.roll_total);
        Assert.Equal(15.25, point.pitch_p); Assert.Equal(16.25, point.pitch_i);
        Assert.Equal(17.25, point.pitch_d); Assert.Equal(18.25, point.pitch_total);
        Assert.Equal(19.25, point.Gz_p); Assert.Equal(20.25, point.Gz_i);
        Assert.Equal(21.25, point.Gz_d); Assert.Equal(22.25, point.altitude_p);
        Assert.Equal(23.25, point.altitude_i); Assert.Equal(24.25, point.altitude_d);
        Assert.Equal(25.25, point.loop_dt_ms); Assert.Equal(26.25, point.altitude_target);
        Assert.Equal(3.5, session.Samples[1].roll);
        Assert.Equal(50, session.Samples[1].elapsed_ms);
        Assert.Equal(100, session.Samples[2].elapsed_ms);
        Assert.Equal("FAULT", session.Samples[2].state_name);
        Assert.Equal((ushort)7, session.Samples[2].error_code);
        Assert.Equal(0, session.Samples[2].m1);
    }

    [Fact]
    public async Task Download_RetriesCorruptionAndLoss_AndIgnoresStaleDuplicates()
    {
        byte[] blob = FirmwareFixture();
        FlightLogDownloader downloader = null!;
        int firstReadAttempts = 0, endCount = 0;
        byte[]? previous = null;
        downloader = new FlightLogDownloader((request, ct) =>
        {
            byte[] response = Response(request, blob);
            if (request[1] == 3) endCount++;
            if (request[1] == 2 && FlightLogSession.U32(request, 8) == 0 && ++firstReadAttempts == 1)
            {
                response[12] ^= 1; // Damaged frame is dropped; triggers retry.
                downloader.AcceptLine(Line(response));
                return Task.CompletedTask;
            }
            if (previous != null) downloader.AcceptLine(Line(previous));
            byte[] stale = (byte[])response.Clone();
            stale[4] ^= 0x80; FlightLogProtocol.Seal(stale);
            downloader.AcceptLine(Line(stale));
            downloader.AcceptLine(Line(response));
            previous = response;
            return Task.CompletedTask;
        }) { ResponseTimeout = TimeSpan.FromMilliseconds(20) };
        var progress = new CapturedProgress();
        byte[] downloaded = await downloader.DownloadAsync(progress, CancellationToken.None);
        Assert.Equal(blob, downloaded);
        Assert.Equal(2, firstReadAttempts);
        Assert.Equal(1, endCount);
        Assert.Equal(new FlightLogDownloadProgress(0, blob.Length), progress.Values[0]);
        Assert.Equal(new FlightLogDownloadProgress(blob.Length, blob.Length), progress.Values[^1]);
        Assert.Equal(1 + (blob.Length + 47) / 48, progress.Values.Count);
        Assert.All(progress.Values, p => Assert.Equal(blob.Length, p.TotalBytes));
        Assert.True(progress.Values.Zip(progress.Values.Skip(1), (a, b) => b.ReceivedBytes > a.ReceivedBytes).All(v => v));
    }

    private sealed class CapturedProgress : IProgress<FlightLogDownloadProgress>
    {
        public List<FlightLogDownloadProgress> Values { get; } = [];
        public void Report(FlightLogDownloadProgress value) => Values.Add(value);
    }

    [Fact]
    public async Task WholeLogChecksum_RejectsValidFramesWithWrongData()
    {
        byte[] blob = FirmwareFixture();
        FlightLogDownloader downloader = null!;
        downloader = new FlightLogDownloader((request, ct) =>
        {
            byte[] response = Response(request, blob);
            if (request[1] == 2 && FlightLogSession.U32(request, 8) == 0)
            {
                response[20] ^= 1; FlightLogProtocol.Seal(response);
            }
            downloader.AcceptLine(Line(response));
            return Task.CompletedTask;
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task Cancel_ReleasesTransferWithoutReturningPartialData()
    {
        byte[] blob = FirmwareFixture();
        FlightLogDownloader downloader = null!;
        int endCount = 0;
        downloader = new FlightLogDownloader((request, ct) =>
        {
            if (request[1] != 2) downloader.AcceptLine(Line(Response(request, blob)));
            if (request[1] == 3) endCount++;
            return Task.CompletedTask;
        });
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloader.DownloadAsync(null, cancel.Token));
        Assert.Equal(1, endCount);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task DeviceRejections_AreReported(byte status)
    {
        FlightLogDownloader downloader = null!;
        downloader = new FlightLogDownloader((request, ct) =>
        {
            downloader.AcceptLine(Line(Response(request, FirmwareFixture(), status)));
            return Task.CompletedTask;
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => downloader.DownloadAsync(null, CancellationToken.None));
    }

    [Fact]
    public void Decoder_RejectsTruncatedLogAndInvalidChronology()
    {
        byte[] blob = FirmwareFixture();
        Assert.Throws<InvalidDataException>(() => FlightLogSession.Decode(blob[..^1]));
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(128 + 116), 999);
        Assert.Throws<InvalidDataException>(() => FlightLogSession.Decode(blob));
    }

    [Fact]
    public void Decoder_AcceptsMcuTickRollover()
    {
        byte[] blob = FirmwareFixture();
        uint first = uint.MaxValue - 20;
        for (int i = 0; i < 3; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(128 + i * 116), unchecked(first + (uint)i * 50));
        Assert.Equal(100, FlightLogSession.Decode(blob).Samples[^1].elapsed_ms);
    }

    [Fact]
    public void ResponseParser_RejectsBadLengthVersionAndCrc()
    {
        byte[] request = FlightLogProtocol.BuildRequest(1, 1);
        byte[] response = Response(request, FirmwareFixture());
        Assert.True(FlightLogProtocol.TryDecodeLine(Line(response), out _));
        Assert.True(FlightLogProtocol.TryDecodeLine("RemoteController: " + Line(response), out _));
        Assert.False(FlightLogProtocol.TryDecodeLine("Not" + Line(response), out _));
        Assert.False(FlightLogProtocol.TryDecodeLine(Line(response)[..^1], out _));
        response[2] = 2; FlightLogProtocol.Seal(response);
        Assert.False(FlightLogProtocol.TryDecodeLine(Line(response), out _));
        response[2] = 1; response[3] = 49; FlightLogProtocol.Seal(response);
        Assert.False(FlightLogProtocol.TryDecodeLine(Line(response), out _));
        response[3] = 12; response[62] ^= 1;
        Assert.False(FlightLogProtocol.TryDecodeLine(Line(response), out _));
    }

    [Fact]
    public void Archive_SavesAnalysisFilesAndReopensOriginalSamples()
    {
        string root = Path.Combine(Path.GetTempPath(), "DroneLogger-flight-log-test-" + Guid.NewGuid());
        try
        {
            byte[] blob = FirmwareFixture();
            var session = FlightLogSession.Decode(blob);
            session.Samples[0].Vz = double.NaN; // JSON export supports sensor-invalid numbers.
            string folder = FlightLogArchive.Save(blob, session, root);
            Assert.True(File.Exists(Path.Combine(folder, "flight.json")));
            string csv = File.ReadAllText(Path.Combine(folder, "samples.csv"));
            Assert.Contains("elapsed_ms", csv); Assert.Contains("loop_dt_ms", csv);
            Assert.Contains("mixer_limited", csv); Assert.Contains("roll_total", csv);
            Assert.Contains("EffectiveHoverThrottle", File.ReadAllText(Path.Combine(folder, "flight.json")));
            string raw = Path.Combine(folder, "flight.flightlog");
            var reopened = FlightLogArchive.Load(raw);
            Assert.Equal(3, reopened.Samples.Count);
            Assert.Equal(session.StopDescription, reopened.StopDescription);
            Assert.Equal(4.25, reopened.Samples[0].Vz); // Exporting views never mutates the raw recording.
            byte[] file = File.ReadAllBytes(raw); file[^1] ^= 1; File.WriteAllBytes(raw, file);
            Assert.Throws<InvalidDataException>(() => FlightLogArchive.Load(raw));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
