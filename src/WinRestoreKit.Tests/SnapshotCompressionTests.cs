using System;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class SnapshotCompressionTests
    {
        [Fact]
        public void SnapshotCompressionExposesTheConfiguredChoices()
        {
            Assert.Equal(new[]
            {
                SnapshotCompression.None,
                SnapshotCompression.Fast,
                SnapshotCompression.Max
            }, Enum.GetValues<SnapshotCompression>());
        }
    }
}
