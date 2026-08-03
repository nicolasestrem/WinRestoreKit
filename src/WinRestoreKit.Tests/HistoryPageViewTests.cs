using System;
using System.Collections.Generic;
using System.IO;
using Views;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class HistoryPageViewTests
    {
        [Fact]
        public void TryPruneCandidates_LeavesEveryCandidateWhenARunIsActive()
        {
            string root = Path.Combine(Path.GetTempPath(), "WinRestoreKitTests", Guid.NewGuid().ToString("N"));
            string firstCandidate = Path.Combine(root, "2025-01-01 - 00.00");
            string secondCandidate = Path.Combine(root, "2025-01-02 - 00.00");
            Directory.CreateDirectory(firstCandidate);
            Directory.CreateDirectory(secondCandidate);

            RunCoordinator.SetRunning(false);
            try
            {
                RunCoordinator.SetRunning(true);

                bool pruned = HistoryPageView.TryPruneCandidates(
                    new[] { firstCandidate, secondCandidate }, out List<string> failures);

                Assert.False(pruned);
                Assert.Empty(failures);
                Assert.True(Directory.Exists(firstCandidate));
                Assert.True(Directory.Exists(secondCandidate));
            }
            finally
            {
                RunCoordinator.SetRunning(false);
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }
    }
}
