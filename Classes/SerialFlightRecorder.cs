using DroneLogger.Model;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DroneLogger.Classes;

// Called on the UI thread, independently of plotting and playback.
internal sealed class SerialFlightRecorder
{
    private readonly Stopwatch clock = new();
    private long startedAt;
    private FlightLogSession? flight;
    private FlightLogSession? fullMetadata;
    public bool IsRecording => flight != null;
    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public void Start(Config? config, bool configurationSent)
    {
        if (IsRecording) return;
        fullMetadata = null;
        flight = new FlightLogSession
        {
            FormatVersion = 0, IsSerialRecording = true, RecordedAt = DateTime.Now,
            Configuration = config == null ? new() : JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(config))!,
            ConfigurationApplied = configurationSent,
            Messages = [new(0, 0, false, "Desktop ARM requested; serial capture started")]
        };
        startedAt = Stopwatch.GetTimestamp();
        clock.Restart();
    }

    public void Record(LogData sample, bool hasTimestamp, long? receivedAt = null, FlightLogSession? metadata = null)
    {
        if (flight == null) return;
        double elapsed = Stopwatch.GetElapsedTime(startedAt, receivedAt ?? Stopwatch.GetTimestamp()).TotalMilliseconds;
        if (elapsed < 0) return; // A pre-ARM line may still be queued on the UI dispatcher.
        if (metadata != null) fullMetadata = metadata;
        // Copy: chart updates and review selection must not mutate the recording.
        var copy = sample.Snapshot();
        copy.elapsed_ms = Math.Max(elapsed, flight.Samples.LastOrDefault()?.elapsed_ms ?? 0);
        if (!hasTimestamp) copy.timestamp_ms = (uint)copy.elapsed_ms;
        flight.Samples.Add(copy);
    }

    public void RecordMessage(ushort code, bool error, string message, long? receivedAt = null)
    {
        if (flight == null) return;
        double elapsed = Stopwatch.GetElapsedTime(startedAt, receivedAt ?? Stopwatch.GetTimestamp()).TotalMilliseconds;
        if (elapsed >= 0) flight.Messages.Add(new((uint)elapsed, code, error, message));
    }

    public FlightLogSession? Finish(string reason)
    {
        if (flight == null) return null;
        clock.Stop();
        var result = new FlightLogSession
        {
            FormatVersion = fullMetadata?.FormatVersion ?? 0, IsSerialRecording = true, RecordedAt = flight.RecordedAt,
            RecordingEnd = reason, Configuration = fullMetadata?.Configuration ?? flight.Configuration,
            ConfigurationApplied = fullMetadata?.ConfigurationApplied ?? flight.ConfigurationApplied,
            EffectiveHoverThrottle = fullMetadata?.EffectiveHoverThrottle ?? 0,
            AutomaticHoverThrottle = fullMetadata?.AutomaticHoverThrottle ?? false,
            EnabledSensors = fullMetadata?.EnabledSensors ?? 0,
            SampleIntervalMs = fullMetadata?.SampleIntervalMs ?? 0,
            GyroBasedDamping = fullMetadata?.GyroBasedDamping,
            EstimatorSettingsRecorded = fullMetadata?.EstimatorSettingsRecorded ?? false,
            GyroCutoffHz = fullMetadata?.GyroCutoffHz,
            AccelRejectionEnabled = fullMetadata?.AccelRejectionEnabled,
            AccelRangeG = fullMetadata?.AccelRangeG,
            GyroRangeDps = fullMetadata?.GyroRangeDps,
            EstimatorSourceId = fullMetadata?.EstimatorSourceId,
            AverageLoopDtMs = fullMetadata?.AverageLoopDtMs,
            LoopIterationCount = fullMetadata?.LoopIterationCount ?? 0,
            ErrorCode = fullMetadata?.ErrorCode ?? 0,
            HalStatus = fullMetadata?.HalStatus ?? 0,
            Samples = flight.Samples, Messages = flight.Messages,
            StopReason = reason == "Operator DISARM" ? (byte)1 : (byte)0,
            StartTickMs = flight.Samples.FirstOrDefault()?.timestamp_ms ?? 0,
            EndTickMs = flight.Samples.LastOrDefault()?.timestamp_ms ?? 0
        };
        result.Messages.Add(new((uint)clock.ElapsedMilliseconds, 0, false, reason));
        flight = null;
        fullMetadata = null;
        return result;
    }
}
