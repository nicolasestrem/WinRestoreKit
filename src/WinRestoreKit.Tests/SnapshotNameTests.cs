using System;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class SnapshotNameTests
    {
        [Fact]
        public void TimestampNameFor_UsesFreshSecondPrecisionInvariantShape()
        {
            string name = BackupNaming.TimestampNameFor(
                new DateTime(2026, 8, 10, 7, 8, 9, DateTimeKind.Local));

            Assert.Equal("2026-08-10 - 07.08.09", name);
        }

        [Theory]
        [InlineData("before-driver-update")]
        [InlineData("2026-08-03 baseline")]
        [InlineData("Quarterly.checkpoint")]
        public void TryValidateCustomName_AcceptsSafeSingleDirectorySegments(string value)
        {
            bool valid = BackupNaming.TryValidateCustomName(value, out string name);

            Assert.True(valid);
            Assert.Equal(value, name);
        }

        [Theory]
        [InlineData("nested/name")]
        [InlineData("nested\\name")]
        [InlineData("..")]
        [InlineData(".")]
        [InlineData("name.")]
        [InlineData("name ")]
        [InlineData("CON")]
        [InlineData("COM1.txt")]
        [InlineData("bad:name")]
        public void TryValidateCustomName_RejectsUnsafeDirectorySegments(string value)
        {
            bool valid = BackupNaming.TryValidateCustomName(value, out string name);

            Assert.False(valid);
            Assert.Null(name);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void TryValidateCustomName_TreatsAbsentInputAsNoCustomName(string value)
        {
            bool valid = BackupNaming.TryValidateCustomName(value, out string name);

            Assert.True(valid);
            Assert.Null(name);
        }
    }
}
