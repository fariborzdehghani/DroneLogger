using DroneLogger.Classes;
using Xunit;

namespace DroneLogger.PacketTests;

public class DownloadTimingTests
{
    [Fact]
    public void Estimate_UsesPayloadThroughputAndIncludesInitialWaitInTotal()
    {
        var result = new DownloadTimingEstimator().Estimate(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(10), 250, 1000);
        Assert.Equal(TimeSpan.FromSeconds(30), result.Remaining);
        Assert.Equal(TimeSpan.FromSeconds(42), result.Total);
    }

    [Fact]
    public void Estimate_CountsDownWithoutChangingTotalWhenPacketsArriveOrStall()
    {
        var estimator = new DownloadTimingEstimator();
        var initial = estimator.Estimate(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(10), 250, 1000);
        var timerTick = estimator.Estimate(TimeSpan.FromSeconds(12.25), TimeSpan.FromSeconds(10.25), 250, 1000);
        var morePackets = estimator.Estimate(TimeSpan.FromSeconds(12.5), TimeSpan.FromSeconds(10.5), 300, 1000);
        var stalled = estimator.Estimate(TimeSpan.FromSeconds(22), TimeSpan.FromSeconds(20), 300, 1000);
        Assert.Equal(initial.Total, timerTick.Total);
        Assert.Equal(initial.Total, morePackets.Total);
        Assert.Equal(initial.Total, stalled.Total);
        Assert.Equal(TimeSpan.FromSeconds(29.75), timerTick.Remaining);
        Assert.Equal(TimeSpan.FromSeconds(29.5), morePackets.Remaining);
        Assert.Equal(TimeSpan.FromSeconds(20), stalled.Remaining);
        var overdue = estimator.Estimate(TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(48), 300, 1000);
        Assert.Equal(initial.Total, overdue.Total);
        Assert.Equal(TimeSpan.Zero, overdue.Remaining);
    }

    [Fact]
    public void Estimate_WaitsForThreeSecondsOfPayloadBeforeLocking()
    {
        var estimator = new DownloadTimingEstimator();
        var early = estimator.Estimate(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(2.9), 250, 1000);
        Assert.Null(early.Remaining);
        Assert.Null(early.Total);
        var ready = estimator.Estimate(TimeSpan.FromSeconds(12.1), TimeSpan.FromSeconds(3), 300, 1000);
        Assert.Equal(TimeSpan.FromSeconds(7), ready.Remaining);
        Assert.Equal(TimeSpan.FromSeconds(19.1), ready.Total);
    }

    [Fact]
    public void Estimate_ResetMeasuresTheNextDownloadIndependently()
    {
        var estimator = new DownloadTimingEstimator();
        estimator.Estimate(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(10), 250, 1000);
        estimator.Reset();
        Assert.Null(estimator.EstimatedTotal);
        Assert.Null(estimator.Estimate(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 48, 1000).Total);
        var next = estimator.Estimate(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(3), 500, 1000);
        Assert.Equal(TimeSpan.FromSeconds(3), next.Remaining);
        Assert.Equal(TimeSpan.FromSeconds(7), next.Total);
    }

    [Fact]
    public void Estimate_WorksBeforeOnePercentAndAtPayloadCompletion()
    {
        var partial = new DownloadTimingEstimator().Estimate(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3), 48, 100096);
        Assert.NotNull(partial.Remaining);
        Assert.Equal(3 * 100048.0 / 48, partial.Remaining.Value.TotalSeconds, 6);
        Assert.Equal(TimeSpan.FromSeconds(5) + partial.Remaining, partial.Total);
        var done = new DownloadTimingEstimator().Estimate(TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(48), 1000, 1000);
        Assert.Equal(TimeSpan.Zero, done.Remaining);
        Assert.Equal(TimeSpan.FromSeconds(50), done.Total);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(1, 0)]
    [InlineData(101, 100)]
    public void Estimate_DoesNotShowUnsupportedNumbers(int received, int total)
    {
        var result = new DownloadTimingEstimator().Estimate(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), received, total);
        Assert.Null(result.Remaining);
        Assert.Null(result.Total);
    }

    [Fact]
    public void Estimate_RejectsZeroTimeAndOverflow()
    {
        var estimator = new DownloadTimingEstimator();
        Assert.Null(estimator.Estimate(TimeSpan.Zero, TimeSpan.Zero, 1, 100).Total);
        Assert.Null(estimator.Estimate(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(3), 1, 100).Total);
        Assert.Null(estimator.Estimate(TimeSpan.FromSeconds(1), TimeSpan.MaxValue, 1, int.MaxValue).Total);
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(83, "01:23")]
    [InlineData(3665, "1:01:05")]
    [InlineData(93665, "26:01:05")]
    public void Durations_PreserveHoursAndUseReadableClockFormatting(int seconds, string expected)
        => Assert.Equal(expected, MainWindow.FormatDownloadDuration(TimeSpan.FromSeconds(seconds)));
}
