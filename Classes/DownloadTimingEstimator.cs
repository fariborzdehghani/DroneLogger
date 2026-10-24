namespace DroneLogger.Classes;

internal sealed class DownloadTimingEstimator
{
    public TimeSpan? EstimatedTotal { get; private set; }

    public void Reset() => EstimatedTotal = null;

    public (TimeSpan? Remaining, TimeSpan? Total) Estimate(
        TimeSpan elapsed, TimeSpan payloadElapsed, int receivedBytes, int totalBytes)
    {
        if (elapsed < TimeSpan.Zero || payloadElapsed <= TimeSpan.Zero ||
            receivedBytes <= 0 || totalBytes <= 0 || receivedBytes > totalBytes)
            return (null, null);

        if (EstimatedTotal == null)
        {
            // Measure several seconds of packets before fixing the estimate.
            if (payloadElapsed < TimeSpan.FromSeconds(3)) return (null, null);
            double remainingTicks = payloadElapsed.Ticks * ((double)(totalBytes - receivedBytes) / receivedBytes);
            if (!double.IsFinite(remainingTicks) || remainingTicks >= TimeSpan.MaxValue.Ticks - elapsed.Ticks)
                return (null, null);
            EstimatedTotal = elapsed + TimeSpan.FromTicks((long)remainingTicks);
        }

        TimeSpan remaining = EstimatedTotal.Value - elapsed;
        return (remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, EstimatedTotal);
    }
}
