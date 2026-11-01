namespace DroneLogger.Classes;

internal sealed class DownloadTimingEstimator
{
    public TimeSpan? EstimatedTotal { get; private set; }
    private TimeSpan? estimatedRemaining, lastEstimateElapsed;

    public void Reset()
    {
        EstimatedTotal = estimatedRemaining = lastEstimateElapsed = null;
    }

    public (TimeSpan? Remaining, TimeSpan? Total) Estimate(
        TimeSpan elapsed, TimeSpan payloadElapsed, int receivedBytes, int totalBytes)
    {
        if (elapsed < TimeSpan.Zero || payloadElapsed <= TimeSpan.Zero ||
            receivedBytes <= 0 || totalBytes <= 0 || receivedBytes > totalBytes)
            return (null, null);

        if (receivedBytes == totalBytes)
        {
            EstimatedTotal = elapsed;
            estimatedRemaining = TimeSpan.Zero;
            lastEstimateElapsed = elapsed;
        }
        else if (lastEstimateElapsed == null ||
                 elapsed - lastEstimateElapsed.Value >= TimeSpan.FromSeconds(3))
        {
            // Average accepted payload throughput includes retries and stalls.
            // Refresh both estimates together every three seconds.
            if (payloadElapsed < TimeSpan.FromSeconds(3)) return (null, null);
            double remainingTicks = payloadElapsed.Ticks * ((double)(totalBytes - receivedBytes) / receivedBytes);
            if (!double.IsFinite(remainingTicks) || remainingTicks >= TimeSpan.MaxValue.Ticks - elapsed.Ticks)
                return (null, null);
            estimatedRemaining = TimeSpan.FromTicks((long)remainingTicks);
            EstimatedTotal = elapsed + estimatedRemaining;
            lastEstimateElapsed = elapsed;
        }

        return (estimatedRemaining, EstimatedTotal);
    }
}
