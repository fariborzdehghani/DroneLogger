using DroneLogger.Model;
using System.Buffers.Binary;
using System.IO;
using System.Text.Json.Serialization;
using System.Text;

namespace DroneLogger.Classes;

public sealed class FlightLogSession
{
    private static readonly int[] CompactFields = [0, 1, 27, 28, 11, 12, 13, 14, 15, 16, 17, 18, 7, 8, 9, 10, 6, 5, 26, 4, 25];
    private static readonly double[] CompactScales = [100, 100, 100, 100, 1000, 1000, 1000, 1000, 1000, 1000, 1000, 1000,
        100, 100, 100, 100, 100, 10, 10, 1000, 100];
    public int FormatVersion { get; init; } = 1;
    public bool IsCompact => FormatVersion == 4;
    public bool HasImuTrace => FormatVersion >= 5;
    public bool HasSrf05Trace => FormatVersion >= 6;
    public bool HasSrf05Details => FormatVersion >= 7;
    public bool? GyroBasedDamping { get; init; }
    public bool EstimatorSettingsRecorded { get; init; }
    public int? GyroCutoffHz { get; init; }
    public bool? AccelRejectionEnabled { get; init; }
    public int? AccelRangeG { get; init; }
    public int? GyroRangeDps { get; init; }
    public uint? EstimatorSourceId { get; init; }
    public bool CompactFallback { get; init; }
    public uint SessionId { get; init; }
    public uint StartTickMs { get; init; }
    public uint EndTickMs { get; init; }
    public uint OverwrittenSamples { get; init; }
    public int SampleIntervalMs { get; init; }
    public byte StopReason { get; init; }
    public ushort ErrorCode { get; init; }
    public byte HalStatus { get; init; }
    public double? AverageLoopDtMs { get; init; }
    public uint LoopIterationCount { get; init; }
    public bool ConfigurationApplied { get; init; } = true;
    public List<FlightLogMessage> Messages { get; init; } = [];
    public string StopDescription => StopReason switch
    {
        1 => "Operator DISARM",
        2 => "Control link timeout",
        3 => "Firmware fault: " + DescribeError(ErrorCode),
        4 => "Disarmed diagnostic snapshot",
        _ => "Unknown stop reason"
    };
    public Config Configuration { get; init; } = new();
    public double EffectiveHoverThrottle { get; init; }
    public byte EnabledSensors { get; init; } // bit 0 MPU6050, 1 QMC5883, 2 SRF05, 3 BMP280
    public List<LogData> Samples { get; init; } = [];
    [JsonIgnore]
    public byte[] RawBlob { get; init; } = [];
    public string TimestampMeaning => "Milliseconds since MCU boot; elapsed_ms is relative to the first retained sample.";
    public string Units => "Angles and IMU corrections: deg; gyro rates and IMU bias: deg/s; acceleration: g; IMU/loop dt and SRF05 age: ms; IMU/SRF05 timestamps: ms since boot; Vz: m/s; altitude/target: cm; motor outputs and PID contributions: percent.";

    public static string DescribeError(ushort code) => code switch
    {
        0 => "No application error", 1 => "Invalid flight-loop timing (over 250 ms or invalid)",
        2 => "MPU6050 calibration failed", 3 => "MPU6050 read failed",
        4 => "QMC5883 read failed", 5 => "Yaw stabilization timeout",
        6 => "Arm motor output failed", 7 => "Maximum flight angle exceeded",
        8 => "Motor output failed", 9 => "Altitude estimate unavailable",
        10 => "BMP280 read failed", 11 => "Sensor acquisition failed",
        12 => "No confirmed liftoff before timeout", 13 => "Takeoff target did not settle before timeout",
        _ => $"Application error {code}"
    };

    internal static FlightLogSession Decode(byte[] blob)
    {
        if (blob.Length < 128 || blob.Length > FlightLogProtocol.MaxBlobSize ||
            !blob.AsSpan(0, 4).SequenceEqual("FLOG"u8) || U16(blob, 4) is < 1 or > 7 ||
            U16(blob, 36) != 64 || U16(blob, 38) != 64)
            throw new InvalidDataException("Unsupported or damaged flight-log header.");
        int count = U16(blob, 12);
        int version = U16(blob, 4);
        int recordSize = version >= 6 ? FlightLogProtocol.Srf05TraceRecordSize :
            version == 5 ? FlightLogProtocol.TraceRecordSize : version == 4 ? FlightLogProtocol.CompactRecordSize :
            version == 3 ? FlightLogProtocol.RecordSize : FlightLogProtocol.LegacyRecordSize;
        int capacity = version >= 3 ? FlightLogProtocol.Capacity : FlightLogProtocol.LegacyCapacity;
        int messageCount = version >= 2 ? U16(blob, 48) : 0;
        if (messageCount > FlightLogProtocol.EventCapacity ||
            (version >= 2 && U16(blob, 50) != FlightLogProtocol.EventSize) ||
            count < 1 || count > capacity || U16(blob, 14) != recordSize ||
            blob.Length != 128 + count * recordSize + messageCount * FlightLogProtocol.EventSize ||
            U16(blob, 16) != (version >= 3 ? 10 : 50) || blob[18] < 1 || blob[18] > (version >= 2 ? 4 : 3))
            throw new InvalidDataException("Flight-log length or sample count is invalid.");

        bool estimatorSettings = version >= 4 && blob[52] == 1;
        if ((version >= 5 && !estimatorSettings) || (estimatorSettings &&
            (blob[53] > 1 || blob[54] is not (5 or 10 or 20 or 42) || blob[55] is not (2 or 8) ||
             U16(blob, 56) != 250 || U16(blob, 58) != FlightLogProtocol.AttitudeTraceSize)))
            throw new InvalidDataException("Invalid IMU trace metadata.");

        var config = blob.AsSpan(64, 64).ToArray();
        if (config[0] != 1 && !(version >= 2 && blob[18] == 4 && config[0] == 0))
            throw new InvalidDataException("Flight-log configuration is missing.");
        double? average = version >= 2 ? ReadFloat(blob, 40) : null;
        if (average.HasValue && (!double.IsFinite(average.Value) || average < 0))
            throw new InvalidDataException("Invalid average loop timing.");
        var session = new FlightLogSession
        {
            RawBlob = blob.ToArray(),
            FormatVersion = version,
            GyroBasedDamping = (U16(blob, 6) & 4) != 0 ? true : version >= 6 ? false : null,
            EstimatorSettingsRecorded = estimatorSettings,
            GyroCutoffHz = estimatorSettings ? blob[54] : null,
            AccelRejectionEnabled = estimatorSettings ? blob[53] != 0 : null,
            AccelRangeG = estimatorSettings ? blob[55] : null,
            GyroRangeDps = estimatorSettings ? U16(blob, 56) : null,
            EstimatorSourceId = estimatorSettings ? U32(blob, 60) : null,
            CompactFallback = (U16(blob, 6) & 2) != 0,
            AverageLoopDtMs = average, LoopIterationCount = version >= 2 ? U32(blob, 44) : 0,
            ConfigurationApplied = config[0] == 1,
            SessionId = U32(blob, 8), StartTickMs = U32(blob, 24), EndTickMs = U32(blob, 28),
            OverwrittenSamples = U32(blob, 32), SampleIntervalMs = U16(blob, 16),
            StopReason = blob[18], ErrorCode = U16(blob, 20), HalStatus = blob[19],
            EffectiveHoverThrottle = config[44] / 2.0,
            EnabledSensors = config[45],
            Configuration = DecodeConfiguration(config)
        };
        uint firstTimestamp = U32(blob, 128);
        double previousTime = -1;
        for (int i = 0; i < count; i++)
        {
            int offset = 128 + i * recordSize;
            double[] v = new double[version >= 3 ? 29 : 27];
            if (version == 4)
            {
                // Same order and scale factors as FlightRecorder.c; omitted channels stay unavailable.
                Array.Fill(v, double.NaN);
                for (int j = 0; j < CompactFields.Length; j++)
                    v[CompactFields[j]] = BinaryPrimitives.ReadInt16LittleEndian(blob.AsSpan(offset + 8 + j * 2)) / CompactScales[j];
            }
            else
                for (int j = 0; j < v.Length; j++)
                    v[j] = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(offset + 8 + j * 4)));
            byte flags = blob[offset + 5];
            bool ratesValid = version >= 3 && (flags & 16) != 0;
            if (ratesValid && (!double.IsFinite(v[27]) || !double.IsFinite(v[28])))
                throw new InvalidDataException("Invalid recorded gyro rates.");
            uint timestamp = U32(blob, offset);
            double elapsed = unchecked(timestamp - firstTimestamp);
            if (elapsed < previousTime || elapsed > 24 * 60 * 60 * 1000 || blob[offset + 4] > 5)
                throw new InvalidDataException("Flight-log timestamps or states are invalid.");
            previousTime = elapsed;
            var sample = new LogData
            {
                timestamp_ms = timestamp, elapsed_ms = elapsed, state = blob[offset + 4],
                error_code = U16(blob, offset + 6), Vz_valid = (uint)(flags & 1),
                altitude_valid = (flags & 2) != 0, altitude_control_active = (flags & 4) != 0,
                mixer_limited = (flags & 8) != 0,
                landing_active = (flags & 32) != 0,
                roll = v[0], pitch = v[1], yaw = v[2], Gz = v[3], Vz = v[4], altitude = v[5],
                base_throttle = v[6], m1 = v[7], m2 = v[8], m3 = v[9], m4 = v[10],
                roll_p = v[11], roll_i = v[12], roll_d = v[13], roll_total = v[14],
                pitch_p = v[15], pitch_i = v[16], pitch_d = v[17], pitch_total = v[18],
                Gz_p = v[19], Gz_i = v[20], Gz_d = v[21], altitude_p = v[22],
                altitude_i = v[23], altitude_d = v[24], loop_dt_ms = v[25], altitude_target = v[26],
                roll_rate_dps = version >= 3 ? v[27] : double.NaN,
                pitch_rate_dps = version >= 3 ? v[28] : double.NaN,
                attitude_rates_valid = ratesValid,
                gyro_based_damping = session.GyroBasedDamping
            };
            if (version >= 5) DecodeImuTrace(blob, offset + 124, sample);
            if (version >= 6) DecodeSrf05Trace(blob, offset + 192, sample, version >= 7);
            session.Samples.Add(sample);
        }
        for (int i = 0; i < messageCount; i++)
        {
            int offset = 128 + count * recordSize + i * FlightLogProtocol.EventSize;
            int length = blob[offset + 7];
            if (length > 96 || blob[offset + 6] > 1)
                throw new InvalidDataException("Invalid diagnostic message.");
            session.Messages.Add(new FlightLogMessage(U32(blob, offset), U16(blob, offset + 4),
                blob[offset + 6] == 1, Encoding.UTF8.GetString(blob, offset + 8, length)));
        }
        return session;
    }

    private static void DecodeSrf05Trace(byte[] blob, int offset, LogData sample, bool details)
    {
        byte valid = blob[offset + 12], flags = blob[offset + 13], result = blob[offset + 14];
        if (valid > 1 || (flags & ~3) != 0 || result > 6 ||
            (details ? blob[offset + 15] > 7 : blob[offset + 15] != 0) ||
            ((flags & 2) != 0 && (flags & 1) == 0))
            throw new InvalidDataException("Invalid SRF05 trace flags.");
        if (valid == 0) return;
        sample.srf05_trace_valid = true;
        sample.srf05_result = result;
        sample.srf05_last_echo_ms = (flags & 1) != 0 ? U32(blob, offset) : null;
        if (details && sample.srf05_last_echo_ms.HasValue)
        {
            float rawCm = ReadFloat(blob, offset + 8);
            if (!float.IsFinite(rawCm) || rawCm <= 0)
                throw new InvalidDataException("Invalid raw SRF05 distance.");
            sample.srf05_raw_distance_cm = rawCm;
            sample.srf05_filter_result = blob[offset + 15];
        }
        if ((flags & 2) != 0)
        {
            uint accepted = U32(blob, offset + 4);
            uint age = unchecked(sample.timestamp_ms - accepted);
            // Format 7 reuses the redundant age word for raw distance.
            if (!details && U32(blob, offset + 8) != age)
                throw new InvalidDataException("Invalid SRF05 measurement age.");
            sample.srf05_last_accepted_ms = accepted;
            sample.srf05_age_ms = age;
        }
    }

    private static void DecodeImuTrace(byte[] blob, int offset, LogData sample)
    {
        byte valid = blob[offset + 64];
        if (valid > 1 || (blob[offset + 67] & ~15) != 0)
            throw new InvalidDataException("Invalid IMU trace flags.");
        double[] values = Enumerable.Range(0, 14).Select(i => (double)ReadFloat(blob, offset + 8 + i * 4)).ToArray();
        if (valid != 0 && (values.Any(v => !double.IsFinite(v)) || values[0] <= 0 || values[0] > 100))
            throw new InvalidDataException("Invalid IMU trace values.");
        sample.imu_trace_valid = valid != 0;
        sample.imu_sequence = U32(blob, offset);
        sample.imu_timestamp_ms = U32(blob, offset + 4);
        sample.imu_dt_ms = values[0];
        sample.accel_x_g = values[1]; sample.accel_y_g = values[2]; sample.accel_z_g = values[3];
        sample.roll_previous_deg = values[4]; sample.roll_predicted_deg = values[5];
        sample.roll_accel_deg = values[6]; sample.roll_bias_dps = values[7]; sample.roll_correction_deg = values[8];
        sample.pitch_previous_deg = values[9]; sample.pitch_predicted_deg = values[10];
        sample.pitch_accel_deg = values[11]; sample.pitch_bias_dps = values[12]; sample.pitch_correction_deg = values[13];
        sample.roll_estimator_flags = blob[offset + 65]; sample.pitch_estimator_flags = blob[offset + 66];
        sample.imu_timing_flags = blob[offset + 67];
    }

    private static Config DecodeConfiguration(byte[] p) => new()
    {
        ArmThrottle = p[1], MinThrottle = p[2], MaxThrottle = p[3], MaxAngle = p[4],
        TargetPitch = (short)U16(p, 5), TargetRoll = (short)U16(p, 7), TargetYaw = (short)U16(p, 9),
        TargetGz = (short)U16(p, 11), TakeoffAltitudeCm = U16(p, 13),
        PitchKp = p[15] / 100.0, PitchKi = U16(p, 16) / 10000.0, PitchKd = U16(p, 18) / 1000.0,
        RollKp = p[20] / 100.0, RollKi = U16(p, 21) / 10000.0, RollKd = U16(p, 23) / 1000.0,
        GzKp = p[25] / 100.0, GzKi = U16(p, 26) / 1000.0, GzKd = U16(p, 28) / 10000.0,
        AltitudeKp = p[30] / 100.0, AltitudeKi = U16(p, 31) / 1000.0, AltitudeKd = U16(p, 33) / 10000.0,
        PIDMaxIPart = U16(p, 36) / 10.0, PIDMaxOutput = U16(p, 38) / 10.0,
        TakeoffThrottleRampPerSecond = U16(p, 41) / 10.0, HoverThrottle = p[43]
    };

    internal static ushort U16(byte[] b, int i) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(i));
    internal static uint U32(byte[] b, int i) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(i));
    private static float ReadFloat(byte[] b, int i) => BitConverter.Int32BitsToSingle(
        BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(i)));
}

public sealed record FlightLogMessage(uint TimestampMs, ushort Code, bool IsError, string Message);
