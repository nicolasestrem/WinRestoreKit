using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class CloseResultTests
    {
        [Theory]
        [InlineData(CloseResult.AccessDenied, CloseResult.StillRunning)]
        [InlineData(CloseResult.StillRunning, CloseResult.AccessDenied)]
        public void Worse_AccessDeniedBeatsStillRunning_RegardlessOfOrder(CloseResult a, CloseResult b)
            => Assert.Equal(CloseResult.AccessDenied, Utils.Worse(a, b));

        [Theory]
        [InlineData(CloseResult.StillRunning)]
        [InlineData(CloseResult.AccessDenied)]
        public void Worse_ExitedNeverOverwritesMoreSevereValue_MoreSevereFirst(CloseResult moreSevere)
            => Assert.Equal(moreSevere, Utils.Worse(moreSevere, CloseResult.Exited));

        [Theory]
        [InlineData(CloseResult.StillRunning)]
        [InlineData(CloseResult.AccessDenied)]
        public void Worse_ExitedNeverOverwritesMoreSevereValue_ExitedFirst(CloseResult moreSevere)
            => Assert.Equal(moreSevere, Utils.Worse(CloseResult.Exited, moreSevere));
    }
}
