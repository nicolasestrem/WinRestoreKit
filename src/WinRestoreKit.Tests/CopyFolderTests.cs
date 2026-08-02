using WinRestoreKit;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class CopyFolderTests : IDisposable
    {
        private readonly string _root;

        public CopyFolderTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "accopy_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private string Dir(string name)
        {
            string p = Path.Combine(_root, name);
            Directory.CreateDirectory(p);
            return p;
        }

        [Fact]
        public async Task CopyFolder_MissingSource_ReportsSourceMissing()
        {
            CopyResult r = await Utils.CopyFolder(Path.Combine(_root, "nope"), Dir("dst1"));

            Assert.True(r.SourceMissing);
            Assert.Equal(0, r.FilesCopied);
        }

        [Fact]
        public async Task CopyFolder_MissingSource_MapsToSkippedWhenAbsenceIsNormal()
        {
            CopyResult r = await Utils.CopyFolder(Path.Combine(_root, "nope"), Dir("dst2"));

            Assert.Equal(ResultState.Skipped, r.ToStep("Chrome", true).State);
        }

        [Fact]
        public async Task CopyFolder_MissingSource_MapsToFailedWhenAbsenceIsNotNormal()
        {
            CopyResult r = await Utils.CopyFolder(Path.Combine(_root, "nope"), Dir("dst3"));

            Assert.Equal(ResultState.Failed, r.ToStep("Themes", false).State);
        }

        [Fact]
        public async Task CopyFolder_EmptySource_CopiesNothingAndIsSkipped()
        {
            CopyResult r = await Utils.CopyFolder(Dir("emptysrc"), Dir("dst4"));

            Assert.Equal(0, r.FilesCopied);
            Assert.Equal(0, r.FilesFailed);
            Assert.Equal(ResultState.Skipped, r.ToStep("Empty", true).State);
        }

        [Fact]
        public async Task CopyFolder_NestedTree_CopiesEveryFile()
        {
            string src = Dir("src5");
            Directory.CreateDirectory(Path.Combine(src, "a", "b"));
            File.WriteAllText(Path.Combine(src, "top.txt"), "1");
            File.WriteAllText(Path.Combine(src, "a", "mid.txt"), "22");
            File.WriteAllText(Path.Combine(src, "a", "b", "deep.txt"), "333");

            string dst = Path.Combine(_root, "dst5");
            CopyResult r = await Utils.CopyFolder(src, dst);

            Assert.Equal(3, r.FilesCopied);
            Assert.Equal(0, r.FilesFailed);
            Assert.Equal(6, r.BytesCopied);
            Assert.True(File.Exists(Path.Combine(dst, "a", "b", "deep.txt")));
            Assert.Equal(ResultState.Succeeded, r.ToStep("Tree", false).State);
        }

        // A locked file is the browser-profile case, made deterministic.
        [Fact]
        public async Task CopyFolder_LockedFile_CountsTheFailureAndKeepsGoing()
        {
            string src = Dir("src6");
            File.WriteAllText(Path.Combine(src, "fine.txt"), "ok");
            string locked = Path.Combine(src, "locked.txt");
            File.WriteAllText(locked, "held");

            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                CopyResult r = await Utils.CopyFolder(src, Path.Combine(_root, "dst6"));

                Assert.Equal(1, r.FilesCopied);
                Assert.Equal(1, r.FilesFailed);
                Assert.False(string.IsNullOrWhiteSpace(r.FirstError));
            }
        }

        // Decision 2 of the spec: any file failure is a failed module. No threshold.
        [Fact]
        public async Task CopyFolder_OneLockedFileAmongMany_IsFailedNotPartial()
        {
            string src = Dir("src7");
            for (int i = 0; i < 5; i++)
                File.WriteAllText(Path.Combine(src, "f" + i + ".txt"), "x");

            string locked = Path.Combine(src, "locked.txt");
            File.WriteAllText(locked, "held");

            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                CopyResult r = await Utils.CopyFolder(src, Path.Combine(_root, "dst7"));
                StepResult s = r.ToStep("Chrome", true);

                Assert.Equal(ResultState.Failed, s.State);
                Assert.Contains("1", s.Reason);
            }
        }

        // A subdirectory that disappears mid-copy must NOT erase the result of everything that
        // already copied. Browsers delete cache folders constantly, so this is the ordinary case
        // for the very modules this tally exists to make honest.
        [Fact]
        public async Task CopyFolder_SubdirectoryVanishesMidCopy_DoesNotReportSourceMissing()
        {
            // The branch under test needs the doomed folder to still exist when GetDirectories()
            // enumerates it and be gone when recursion VISITS it. Hitting that window reliably
            // dictates the layout below:
            //
            //  - the root holds no files, so nothing awaits before GetDirectories() runs and the
            //    delete below cannot land early (it would then never be enumerated at all, and the
            //    vanishing branch would never be entered);
            //  - "aa_slow" sorts first and holds a large file, so the copy is still busy inside it
            //    when the test thread resumes at the first incomplete await;
            //  - "zz_doomed" sorts last, so it is visited only after that copy finishes.
            string src = Dir("src8");

            string slow = Path.Combine(src, "aa_slow");
            Directory.CreateDirectory(slow);
            for (int i = 0; i < 3; i++)
                File.WriteAllText(Path.Combine(slow, "f" + i + ".txt"), "x");
            File.WriteAllBytes(Path.Combine(slow, "big.bin"), new byte[8 * 1024 * 1024]);

            string doomed = Path.Combine(src, "zz_doomed");
            Directory.CreateDirectory(doomed);
            File.WriteAllText(Path.Combine(doomed, "c.txt"), "y");

            Task<CopyResult> copy = Utils.CopyFolder(src, Path.Combine(_root, "dst8"));
            Directory.Delete(doomed, true);
            CopyResult r = await copy;

            Assert.False(r.SourceMissing);
            Assert.True(r.FilesCopied >= 3);

            // Without this the test passes even when the vanishing branch is never entered - it
            // would then assert only that nothing went wrong.
            Assert.True(r.FoldersFailed >= 1);
            Assert.Contains("disappeared", r.FirstError, System.StringComparison.OrdinalIgnoreCase);
        }

        // On restore the source IS the backup folder, so the backup-side sentence describes the
        // wrong machine: it reports the item as absent from this system, a claim about the live
        // machine that restore never examined.
        [Fact]
        public void ToStep_MissingSourceOnRestore_TalksAboutTheBackupNotTheMachine()
        {
            CopyResult r = new CopyResult { SourceMissing = true };

            StepResult restore = r.ToStep("Google Chrome", true, "nothing was backed up for this item");
            StepResult backup = r.ToStep("Google Chrome", true);

            Assert.Equal(ResultState.Skipped, restore.State);
            Assert.Equal("nothing was backed up for this item", restore.Reason);

            // The backup side is unchanged - there the sentence is true.
            Assert.Equal("not present on this system", backup.Reason);
        }

        // A directory-level failure must not be described as a file failure.
        [Fact]
        public void ToStep_FolderFailureOnly_DoesNotInventAFileCount()
        {
            CopyResult r = new CopyResult { FoldersFailed = 1, FirstError = "denied" };
            StepResult s = r.ToStep("Themes", false);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.Contains("folder", s.Reason, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("1 of 1 files", s.Reason);
        }

        // Two simultaneous failures. The single-failure test above would still pass under an
        // "tolerate exactly one failure" rule; this one closes that hole, so the strict-failure
        // decision is guarded against both percentage- and count-based leniency.
        [Fact]
        public async Task CopyFolder_TwoLockedFiles_StillFailedAndCountsBoth()
        {
            string src = Dir("src9");
            for (int i = 0; i < 4; i++)
                File.WriteAllText(Path.Combine(src, "ok" + i + ".txt"), "x");

            string a = Path.Combine(src, "a.lock");
            string b = Path.Combine(src, "b.lock");
            File.WriteAllText(a, "1");
            File.WriteAllText(b, "2");

            using (new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.None))
            using (new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                CopyResult r = await Utils.CopyFolder(src, Path.Combine(_root, "dst9"));

                Assert.Equal(2, r.FilesFailed);
                Assert.Equal(4, r.FilesCopied);
                Assert.Equal(ResultState.Failed, r.ToStep("Chrome", true).State);
                Assert.Contains("2 of 6", r.ToStep("Chrome", true).Reason);
            }
        }
    }
}
