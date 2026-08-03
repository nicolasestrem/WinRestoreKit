using System;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class ProgressMetricsTests
    {
        [Fact]
        public void Create_ZeroDurationAtFinalModule_ReportsCompleteWithoutMeasuredThroughput()
        {
            ProgressMetricValues metrics = ProgressMetrics.Create(1, 1, TimeSpan.Zero, 2048, true);

            Assert.Equal(100, metrics.Percent);
            Assert.Equal("00:00:00", metrics.Elapsed);
            Assert.Equal("00:00:00", metrics.Remaining);
            Assert.Equal("2.0 KB", metrics.Bytes);
            Assert.Equal("N/A", metrics.Throughput);
        }

        [Fact]
        public void Create_PartialMeasuredBackup_UsesLinearRemainingEstimateAndByteRate()
        {
            ProgressMetricValues metrics = ProgressMetrics.Create(2, 5, TimeSpan.FromSeconds(10), 1536, true);

            Assert.Equal(40, metrics.Percent);
            Assert.Equal("00:00:10", metrics.Elapsed);
            Assert.Equal("00:00:15", metrics.Remaining);
            Assert.Equal("1.5 KB", metrics.Bytes);
            Assert.Equal("153.6 B/s", metrics.Throughput);
        }

        [Fact]
        public void Create_RestoreWithoutByteMeasurement_ReportsByteMetricsAsUnavailable()
        {
            ProgressMetricValues metrics = ProgressMetrics.Create(1, 2, TimeSpan.FromSeconds(3), 0, false);

            Assert.Equal(50, metrics.Percent);
            Assert.Equal("00:00:03", metrics.Elapsed);
            Assert.Equal("00:00:03", metrics.Remaining);
            Assert.Equal("N/A", metrics.Bytes);
            Assert.Equal("N/A", metrics.Throughput);
        }

        [Fact]
        public void FormatGroup_UsesExactCompletedGroupText()
        {
            Assert.Equal("Group 3 of 8. Explorer & shell", ProgressMetrics.FormatGroup(3, 8, "Explorer & shell"));
        }
    }
}
