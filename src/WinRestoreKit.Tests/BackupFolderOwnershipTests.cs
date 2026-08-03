using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    /// <summary>
    /// Regression coverage for the PR #4 bot finding at BackupRestoreOrchestrator.cs:185: two app
    /// instances racing to the same minute-granularity backup path could both observe the folder as
    /// absent, both create it, and then whichever one canceled first would delete the other's
    /// output. The fix rests on a single atomic primitive, TryClaimExclusiveFolderOwnership, which
    /// must elect exactly one winner no matter how many callers race. These tests pin that primitive
    /// directly, because the full two-process interleaving cannot be forced deterministically in
    /// a single-process test without reintroducing the very timing gap being closed.
    /// </summary>
    public sealed class BackupFolderOwnershipTests
    {
        [Fact]
        public void Claim_FirstCallWins_SecondCallOnSameFolderLoses()
        {
            string folder = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), "WinRestoreKitTests", Guid.NewGuid().ToString("N"))).FullName;

            try
            {
                Assert.True(BackupRestoreOrchestrator.TryClaimExclusiveFolderOwnership(folder));
                Assert.False(BackupRestoreOrchestrator.TryClaimExclusiveFolderOwnership(folder));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void Claim_UnwritableFolder_LosesRatherThanThrows()
        {
            // A path with no folder behind it stands in for any claim that cannot be established.
            // The safe direction is to lose the race, never to throw into the caller and never to
            // report ownership this run cannot back up with an actual marker on disk.
            string missing = Path.Combine(Path.GetTempPath(), "WinRestoreKitTests",
                Guid.NewGuid().ToString("N"), "does-not-exist");

            Assert.False(BackupRestoreOrchestrator.TryClaimExclusiveFolderOwnership(missing));
        }

        [Fact]
        public async Task Claim_ManyConcurrentCallers_ElectExactlyOneWinner()
        {
            string folder = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), "WinRestoreKitTests", Guid.NewGuid().ToString("N"))).FullName;

            try
            {
                const int racers = 32;
                using ManualResetEventSlim gate = new ManualResetEventSlim(false);
                Task<bool>[] attempts = new Task<bool>[racers];

                for (int i = 0; i < racers; i++)
                {
                    attempts[i] = Task.Run(() =>
                    {
                        // Every racer blocks here, so the claims land as close to simultaneously as
                        // the scheduler allows rather than serializing by construction.
                        gate.Wait();
                        return BackupRestoreOrchestrator.TryClaimExclusiveFolderOwnership(folder);
                    });
                }

                gate.Set();
                bool[] results = await Task.WhenAll(attempts);

                Assert.Equal(1, results.Count(won => won));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
