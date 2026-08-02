using WinRestoreKit;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;

namespace WinRestoreKit.Tests
{
    // Utils.CopyFile and the ToFileStep ladder it feeds. Reached directly through
    // InternalsVisibleTo, so the primitive's own promises are held here rather than only through
    // whichever module happens to exercise them.
    public class CopyFileTests
    {
        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "accopyfile_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public async Task MissingSource_ReportsSourceMissingAndCopiesNothing()
        {
            string dir = NewTempDir();

            try
            {
                CopyResult r = await Utils.CopyFile(Path.Combine(dir, "gone"), Path.Combine(dir, "dest"));

                Assert.True(r.SourceMissing);
                Assert.Equal(0, r.FilesCopied);
                Assert.Equal(0, r.FilesFailed);
                Assert.False(File.Exists(Path.Combine(dir, "dest")));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task CopiesContentsAndCountsOneFile()
        {
            string dir = NewTempDir();

            try
            {
                string source = Path.Combine(dir, "config");
                File.WriteAllText(source, "Host example");

                string dest = Path.Combine(dir, "copy", "config");
                CopyResult r = await Utils.CopyFile(source, dest);

                Assert.False(r.SourceMissing);
                Assert.Equal(1, r.FilesCopied);
                Assert.Equal(0, r.FilesFailed);
                Assert.Equal("Host example", File.ReadAllText(dest));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // Load-bearing rather than convenient: the machine being restored onto may never have run
        // ssh, so %USERPROFILE%\.ssh does not exist yet.
        [Fact]
        public async Task CreatesTheDestinationDirectoryTree()
        {
            string dir = NewTempDir();

            try
            {
                string source = Path.Combine(dir, "known_hosts");
                File.WriteAllText(source, "example.com ssh-ed25519 AAAA");

                string dest = Path.Combine(dir, "a", "b", "c", "known_hosts");
                CopyResult r = await Utils.CopyFile(source, dest);

                Assert.Equal(1, r.FilesCopied);
                Assert.True(File.Exists(dest));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // It does not throw. A module that cannot distinguish "copied" from "threw and was caught"
        // is the failure mode Phase 2a exists to remove, so the failure comes back as a count.
        [Fact]
        public async Task LockedDestination_IsAFailureWithAReasonRatherThanAThrow()
        {
            string dir = NewTempDir();

            try
            {
                string source = Path.Combine(dir, "config");
                File.WriteAllText(source, "Host example");

                string dest = Path.Combine(dir, "locked");
                File.WriteAllText(dest, "original");

                CopyResult r;

                using (new FileStream(dest, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    r = await Utils.CopyFile(source, dest);
                }

                Assert.False(r.SourceMissing);
                Assert.Equal(0, r.FilesCopied);
                Assert.Equal(1, r.FilesFailed);
                Assert.False(string.IsNullOrWhiteSpace(r.FirstError));

                // The destination was not truncated on the way to failing.
                Assert.Equal("original", File.ReadAllText(dest));

                // ...and the flag agrees, which is what the step's wording depends on.
                //
                // This pins the ASSIGNMENT'S PLACEMENT through the real CopyFile: the flag is set on
                // the line after the destination FileStream constructor returns, so a constructor
                // that throws must leave it false. Moving that assignment one line earlier would
                // start claiming truncation for copies that never touched the destination - telling
                // a user their hosts file may be damaged when it is untouched - and nothing else in
                // the suite would notice. The true case cannot be reached here (a genuine
                // mid-CopyToAsync failure needs disk-full or a failing device), so this half is
                // where the regression risk actually lives.
                Assert.False(r.DestinationTruncated);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // A locked SOURCE is a failure, not an absence. Absence maps to Skipped for most modules,
        // so reporting a file we could not read as "not present" is the "I could not tell" ->
        // "nothing was there" slide the reporting rules exist to prevent.
        [Fact]
        public async Task UnreadableSource_IsFailedAndNotReportedAsAbsent()
        {
            string dir = NewTempDir();

            try
            {
                string source = Path.Combine(dir, "config");
                File.WriteAllText(source, "Host example");

                CopyResult r;

                using (new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    r = await Utils.CopyFile(source, Path.Combine(dir, "dest"));
                }

                Assert.False(r.SourceMissing);
                Assert.Equal(1, r.FilesFailed);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // Absence is classified from the exception the OPEN raises, not from File.Exists. These pin
        // the two shapes that are genuinely absent, so a future refactor back to an Exists probe
        // keeps these passing but loses the case below.
        [Fact]
        public async Task MissingDirectory_IsAnAbsenceNotAFailure()
        {
            string dir = NewTempDir();

            try
            {
                // The whole folder is missing, which is what "Windows Terminal Preview is not
                // installed" looks like on disk.
                CopyResult r = await Utils.CopyFile(
                    Path.Combine(dir, "nosuchfolder", "settings.json"),
                    Path.Combine(dir, "dest"));

                Assert.True(r.SourceMissing);
                Assert.Equal(0, r.FilesFailed);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // THE test for the exception-based classification, and the rights it denies are the whole
        // point. Measured on Windows 11, 2026-07-21:
        //
        //   deny ListDirectory + ReadData  -> File.Exists TRUE   (an Exists probe looks correct)
        //   deny ReadAndExecute            -> File.Exists FALSE  (an Exists probe reports absent)
        //
        // Only the second discriminates, because only it denies Traverse and ReadAttributes. A
        // version of this test written against the first passes against a broken implementation -
        // which is exactly what happened here before a review caught it. ReadAndExecute is also
        // the realistic shape: `icacls /inheritance:r` is the standard remedy for OpenSSH's
        // "permissions are too open", and it is applied to the very folder ESsh reads.
        //
        // Returns rather than fails when the DACL cannot be edited: a test that cannot establish
        // its own precondition must not return a verdict. The guard is verified by hand - flip it
        // to a throw and confirm it does not fire - rather than trusted.
        [Fact]
        public async Task SourceUnderAnUnreadableParent_IsFailedAndNeverReportedAsAbsent()
        {
            string dir = NewTempDir();
            string lockedDir = Path.Combine(dir, "locked");
            Directory.CreateDirectory(lockedDir);

            string inside = Path.Combine(lockedDir, "settings.json");
            File.WriteAllText(inside, "{\"a\":1}");

            string me = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            System.Security.AccessControl.FileSystemAccessRule deny = null;

            try
            {
                try
                {
                    DirectoryInfo info = new DirectoryInfo(lockedDir);
                    System.Security.AccessControl.DirectorySecurity acl = info.GetAccessControl();

                    // Inheritance off first: an inherited Allow would otherwise keep granting the
                    // traverse right whose absence is what makes Exists answer false.
                    acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                    acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                        me,
                        System.Security.AccessControl.FileSystemRights.FullControl,
                        System.Security.AccessControl.AccessControlType.Allow));
                    info.SetAccessControl(acl);

                    deny = new System.Security.AccessControl.FileSystemAccessRule(
                        me,
                        System.Security.AccessControl.FileSystemRights.ReadAndExecute,
                        System.Security.AccessControl.InheritanceFlags.ContainerInherit
                            | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                        System.Security.AccessControl.PropagationFlags.None,
                        System.Security.AccessControl.AccessControlType.Deny);

                    acl = info.GetAccessControl();
                    acl.AddAccessRule(deny);
                    info.SetAccessControl(acl);
                }
                catch (Exception)
                {
                    return;   // Could not deny ourselves access; nothing to assert.
                }

                // The precondition this test is actually about: the file is there, and the probe
                // an earlier version of CopyFile used answers "absent" about it.
                if (File.Exists(inside))
                    return;   // ACL did not take effect (some filesystems ignore it); assert nothing.

                CopyResult r = await Utils.CopyFile(inside, Path.Combine(dir, "dest"));

                Assert.False(r.SourceMissing);
                Assert.Equal(1, r.FilesFailed);
                Assert.False(string.IsNullOrWhiteSpace(r.FirstError));
            }
            finally
            {
                try
                {
                    DirectoryInfo info = new DirectoryInfo(lockedDir);
                    System.Security.AccessControl.DirectorySecurity restore = info.GetAccessControl();

                    if (deny != null)
                        restore.RemoveAccessRule(deny);

                    restore.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                        me,
                        System.Security.AccessControl.FileSystemRights.FullControl,
                        System.Security.AccessControl.AccessControlType.Allow));

                    info.SetAccessControl(restore);
                }
                catch (Exception)
                {
                    // Best effort - the temp tree is disposable either way.
                }

                try { Directory.Delete(dir, recursive: true); } catch (Exception) { }
            }
        }

        // A path the framework rejects outright. Not an absence - we never got far enough to
        // establish one - so it must be a failure.
        [Fact]
        public async Task MalformedSourcePath_IsFailedRatherThanReportedAbsent()
        {
            string dir = NewTempDir();

            try
            {
                CopyResult r = await Utils.CopyFile("bad\0path", Path.Combine(dir, "dest"));

                Assert.False(r.SourceMissing);
                Assert.Equal(1, r.FilesFailed);
                Assert.False(string.IsNullOrWhiteSpace(r.FirstError));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // A directory where a file was expected is demonstrably PRESENT, so reporting it absent
        // would be a claim about something we can see. Measured: opening a directory as a file
        // throws UnauthorizedAccessException, so the exception-based classification gets this
        // right where an Exists probe (which answers false for a directory) would not.
        [Fact]
        public async Task SourceThatIsADirectory_IsFailedNotReportedAsAbsent()
        {
            string dir = NewTempDir();

            try
            {
                string asDirectory = Path.Combine(dir, "config");
                Directory.CreateDirectory(asDirectory);

                CopyResult r = await Utils.CopyFile(asDirectory, Path.Combine(dir, "dest"));

                Assert.False(r.SourceMissing);
                Assert.Equal(1, r.FilesFailed);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // A cleared hosts or a zero-byte settings.json is real. One file copied, zero bytes - so
        // no future caller starts reading BytesCopied == 0 as "nothing happened".
        [Fact]
        public async Task EmptyFile_IsCopiedAndCountedAsOne()
        {
            string dir = NewTempDir();

            try
            {
                string source = Path.Combine(dir, "hosts");
                File.WriteAllText(source, string.Empty);

                string dest = Path.Combine(dir, "copy", "hosts");
                CopyResult r = await Utils.CopyFile(source, dest);

                Assert.Equal(1, r.FilesCopied);
                Assert.Equal(0, r.BytesCopied);
                Assert.True(File.Exists(dest));
                Assert.Equal(string.Empty, File.ReadAllText(dest));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // The likeliest real restore failure for EHosts. It refuses rather than clearing the
        // attribute - the honest behaviour, since silently stripping a read-only flag the user set
        // is a change they did not ask for - and the original content survives the refusal.
        [Fact]
        public async Task ReadOnlyDestination_IsAFailureAndLeavesTheOriginalIntact()
        {
            string dir = NewTempDir();
            string dest = Path.Combine(dir, "hosts");

            try
            {
                string source = Path.Combine(dir, "source");
                File.WriteAllText(source, "new content");

                File.WriteAllText(dest, "original content");
                File.SetAttributes(dest, FileAttributes.ReadOnly);

                CopyResult r = await Utils.CopyFile(source, dest);

                Assert.False(r.SourceMissing);
                Assert.Equal(1, r.FilesFailed);

                File.SetAttributes(dest, FileAttributes.Normal);
                Assert.Equal("original content", File.ReadAllText(dest));
            }
            finally
            {
                try { File.SetAttributes(dest, FileAttributes.Normal); } catch (Exception) { }
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task DestinationThatIsADirectory_IsAFailureWithAReason()
        {
            string dir = NewTempDir();

            try
            {
                string source = Path.Combine(dir, "config");
                File.WriteAllText(source, "Host example");

                string dest = Path.Combine(dir, "blocked");
                Directory.CreateDirectory(dest);

                CopyResult r = await Utils.CopyFile(source, dest);

                Assert.Equal(1, r.FilesFailed);
                Assert.False(string.IsNullOrWhiteSpace(r.FirstError));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // A copy that fails BEFORE the write begins leaves the live file at its previous contents.
        //
        // HONEST SCOPE, and the scope is narrower than the name suggests. This induces the failure
        // at the SOURCE OPEN, which happens before the destination is touched, so it pins exactly
        // one thing: CopyFile does not truncate the destination on its way to discovering it cannot
        // read the source. That is worth pinning - it is the difference between a failed backup and
        // a destroyed hosts file - but it is not atomicity.
        //
        // A failure DURING the write is NOT covered and, since 2026-07-21, is no longer defended
        // against: CopyFile writes straight into the destination with FileMode.Create, which
        // truncates first, so a disk-full or mid-stream read error leaves the destination empty or
        // half-written. That is a deliberate trade (see CopyFile's remarks - the temp-file swap that
        // prevented it broke hard links and symlinks, silently and unrecoverably). No test here
        // claims otherwise, and inducing a mid-stream failure through a path-based API needs a
        // filesystem this suite cannot arrange in any case.
        [Fact]
        public async Task FailedCopy_LeavesTheDestinationAtItsPreviousContents()
        {
            string dir = NewTempDir();

            try
            {
                string source = Path.Combine(dir, "source");
                File.WriteAllText(source, "new content");

                string dest = Path.Combine(dir, "hosts");
                File.WriteAllText(dest, "127.0.0.1 localhost");

                CopyResult r;

                // Source locked exclusively: CopyFile cannot open it, so the copy fails.
                using (new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    r = await Utils.CopyFile(source, dest);
                }

                Assert.Equal(1, r.FilesFailed);

                // The live file is untouched, not truncated.
                Assert.Equal("127.0.0.1 localhost", File.ReadAllText(dest));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // A successful copy must leave nothing behind but the destination itself.
        //
        // Written when CopyFile staged through a `.WinRestoreKit-tmp` sibling and a leftover would have
        // landed in the backup folder and, on the restore side, in the user's profile. That staging
        // is gone, so today this is a REGRESSION GUARD rather than a live hazard: it fails the
        // moment anyone reintroduces a scratch file next to the destination without cleaning it up.
        // Cheap, and the exact shape of mistake it catches has already been made once here.
        [Fact]
        public async Task SuccessfulCopy_LeavesNoTemporaryFileBehind()
        {
            string dir = NewTempDir();

            try
            {
                string source = Path.Combine(dir, "config");
                File.WriteAllText(source, "Host example");

                string destDir = Path.Combine(dir, "out");
                CopyResult r = await Utils.CopyFile(source, Path.Combine(destDir, "config"));

                Assert.Equal(1, r.FilesCopied);
                Assert.Equal(new[] { "config" },
                    Directory.GetFiles(destDir).Select(Path.GetFileName).ToArray());
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // The restore must not widen who can read the file it overwrites.
        //
        // This is a security boundary on two real modules: .ssh\config, whose recommended
        // permissions are exactly this hardened shape and which names internal hosts, jump-host
        // topology and usernames; and hosts, which can carry an explicit anti-tamper ACE from EDR
        // or GPO and is written elevated and machine-wide.
        //
        // FileMode.Create truncates the existing file rather than replacing it, so the descriptor
        // survives and this passes for free. It is pinned anyway because it did NOT hold for free
        // once: the temp-file-and-rename version had to reach for File.Replace to keep it, and its
        // first draft used File.Move(overwrite), which handed the destination the temp file's
        // freshly inherited descriptor - measured, protected True -> False, 1 explicit rule -> 7
        // inherited, on exactly the two files above. Any future rewrite that stages through another
        // file inherits that trap, and this test is what stands between it and the user.
        [Fact]
        public async Task RestoringOverAHardenedFile_PreservesItsPermissions()
        {
            string dir = NewTempDir();

            try
            {
                string source = Path.Combine(dir, "source");
                File.WriteAllText(source, "RESTORED");

                string dest = Path.Combine(dir, "config");
                File.WriteAllText(dest, "ORIGINAL");

                string me = System.Security.Principal.WindowsIdentity.GetCurrent().Name;

                try
                {
                    FileInfo info = new FileInfo(dest);
                    System.Security.AccessControl.FileSecurity acl = info.GetAccessControl();

                    // Inheritance off, one explicit ACE - the `icacls /inheritance:r` shape.
                    acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                    acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                        me,
                        System.Security.AccessControl.FileSystemRights.FullControl,
                        System.Security.AccessControl.AccessControlType.Allow));
                    info.SetAccessControl(acl);
                }
                catch (Exception)
                {
                    return;   // Cannot arrange the precondition; assert nothing.
                }

                bool protectedBefore = new FileInfo(dest).GetAccessControl()
                    .AreAccessRulesProtected;

                if (!protectedBefore)
                    return;   // The hardening did not take; nothing to assert.

                CopyResult r = await Utils.CopyFile(source, dest);

                Assert.Equal(1, r.FilesCopied);
                Assert.Equal("RESTORED", File.ReadAllText(dest));

                // The content changed; the permissions did not.
                Assert.True(new FileInfo(dest).GetAccessControl().AreAccessRulesProtected,
                    "the restore re-enabled inheritance on a file that had it removed");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLinkW(string linkPath, string existingPath,
                                                   IntPtr securityAttributes);

        // Restoring onto a linked file updates the file behind the link instead of replacing it.
        //
        // The case: a developer keeps .ssh\config or VS Code's settings.json in a git-managed
        // dotfiles repo and links the real path to it. Restoring must write THROUGH that link, the
        // way every editor saving the path already does. Replacing it leaves an ordinary file at
        // the path, the repo behind it silently stale, and their whole arrangement quietly
        // dismantled - with no step reporting anything, because from the copy's point of view it
        // succeeded.
        //
        // THIS TEST DISCRIMINATES, which is the only reason it is worth having. Verified by
        // restoring the temp-file-and-rename that CopyFile used until 2026-07-21: the assertion
        // below fails against it, because the rename replaces the directory entry and the link
        // stops pointing at the same file. It is the regression guard for the trade recorded in
        // CopyFile's remarks.
        //
        // A hard link, not a symbolic one, because CreateHardLink needs no privilege while
        // symlinks need elevation or Developer Mode. Symlinks are therefore still NOT covered here
        // - stated so a green suite is not read as evidence about them. Both are reparse-free
        // directory entries pointing at one file for the purposes of an in-place write, so a
        // direct write follows either; that last step is reasoning, not measurement.
        [Fact]
        public async Task RestoringOverALinkedFile_WritesThroughTheLink()
        {
            string dir = NewTempDir();

            try
            {
                string source = Path.Combine(dir, "source");
                File.WriteAllText(source, "RESTORED");

                // The file the link stands for - "the dotfiles repo copy".
                string target = Path.Combine(dir, "dotfiles-config");
                File.WriteAllText(target, "ORIGINAL");

                // The path a module restores to - "%USERPROFILE%\.ssh\config".
                string link = Path.Combine(dir, "config");

                if (!CreateHardLinkW(link, target, IntPtr.Zero))
                    return;   // Cannot arrange the precondition; assert nothing.

                CopyResult r = await Utils.CopyFile(source, link);

                Assert.Equal(1, r.FilesCopied);
                Assert.Equal("RESTORED", File.ReadAllText(link));

                // The one that matters: the file behind the link moved too, so it is still the
                // same file. Against the temp-file-and-rename this reads "ORIGINAL".
                Assert.Equal("RESTORED", File.ReadAllText(target));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // A failure that never reached the destination must SAY the destination is unchanged.
        //
        // Pairs with the wording test below. This one goes through the real CopyFile, so it also
        // pins that DestinationTruncated stays false when the copy dies at the source open - which
        // is the half of that flag this suite can actually reach.
        [Fact]
        public async Task AFailureBeforeTheWriteBegins_SaysTheDestinationWasLeftUnchanged()
        {
            string dir = NewTempDir();

            try
            {
                string source = Path.Combine(dir, "source");
                File.WriteAllText(source, "new content");

                string dest = Path.Combine(dir, "hosts");
                File.WriteAllText(dest, "127.0.0.1 localhost");

                CopyResult r;

                using (new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    r = await Utils.CopyFile(source, dest);
                }

                Assert.False(r.DestinationTruncated);

                StepResult step = r.ToFileStep(dest, absenceIsNormal: false);

                Assert.Equal(ResultState.Failed, step.State);
                Assert.Contains("left unchanged", step.Reason);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // A failure that began writing must NOT claim the destination was left alone.
        //
        // HONEST SCOPE: this drives ToFileStep directly with a hand-built tally, because inducing a
        // genuine mid-CopyToAsync failure through a path-based API needs a filesystem this suite
        // cannot arrange - the same limit already recorded on
        // FailedCopy_LeavesTheDestinationAtItsPreviousContents. So this pins the WORDING, and
        // AFailureBeforeTheWriteBegins pins the flag's false case through the real code path. The
        // flag's true case is set one line after the FileStream constructor returns and is not
        // covered by a test; that is stated rather than papered over.
        //
        // Worth having anyway: before the atomic write was reverted, every failure here read "could
        // not be copied", which told the user their live file was untouched. That became false the
        // moment CopyFile went back to writing in place, and for EHosts the file in question is
        // machine-wide.
        [Fact]
        public void AFailureAfterWritingBegan_DoesNotClaimTheFileIsUntouched()
        {
            CopyResult r = new CopyResult
            {
                FilesFailed = 1,
                FirstError = "hosts: The disk is full.",
                DestinationTruncated = true
            };

            StepResult step = r.ToFileStep("hosts", absenceIsNormal: false);

            Assert.Equal(ResultState.Failed, step.State);
            Assert.DoesNotContain("left unchanged", step.Reason);
            Assert.Contains("incomplete", step.Reason);
        }

        // --- ToFileStep: the same ladder as ToStep, differing only in the nouns ---

        [Fact]
        public void ToFileStep_AbsentAndNormal_IsSkipped()
        {
            StepResult s = new CopyResult { SourceMissing = true }.ToFileStep("hosts", true);

            Assert.Equal(ResultState.Skipped, s.State);
            Assert.Equal("not present on this system", s.Reason);
        }

        [Fact]
        public void ToFileStep_AbsentAndNotNormal_IsFailedAndNamesAFileNotAFolder()
        {
            StepResult s = new CopyResult { SourceMissing = true }.ToFileStep("hosts", false);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.Contains("expected file", s.Reason);
            Assert.DoesNotContain("folder", s.Reason);
        }

        // Restore callers supply their own reason because their source is the backup folder, and
        // the default wording would describe the live machine, which was never examined.
        [Fact]
        public void ToFileStep_AbsentOnTheRestoreSide_UsesTheCallersReason()
        {
            StepResult s = new CopyResult { SourceMissing = true }
                .ToFileStep("hosts", true, "nothing was backed up for this item");

            Assert.Equal(ResultState.Skipped, s.State);
            Assert.Equal("nothing was backed up for this item", s.Reason);
        }

        // Precedence: a hard absence cannot be softened by a caller-supplied reason. Unreachable
        // from the current call sites (both directions pass absenceIsNormal:true alongside a
        // reason), but it is the rule that keeps the restore-side wording safe, and nothing
        // stated it.
        [Fact]
        public void ToFileStep_AbsentAndNotNormal_IgnoresTheCallersReason()
        {
            StepResult s = new CopyResult { SourceMissing = true }
                .ToFileStep("hosts", false, "nothing was backed up for this item");

            Assert.Equal(ResultState.Failed, s.State);
            Assert.Contains("expected file", s.Reason);
            Assert.DoesNotContain("nothing was backed up", s.Reason);
        }

        [Fact]
        public void ToFileStep_Failure_CarriesTheUnderlyingError()
        {
            StepResult s = new CopyResult { FilesFailed = 1, FirstError = "access denied" }
                .ToFileStep("hosts", true);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.Contains("access denied", s.Reason);
        }

        [Fact]
        public void ToFileStep_Success_ClaimsOnlyThatItCopied()
        {
            StepResult s = new CopyResult { FilesCopied = 1, BytesCopied = 12 }.ToFileStep("hosts", false);

            Assert.Equal(ResultState.Succeeded, s.State);
            Assert.Equal("copied 1 file", s.Reason);
        }

        // Unreachable through CopyFile, which always sets exactly one of the three. Held as a
        // failure so a future caller folding a hand-built tally through here cannot get silence
        // out of a copy that never happened.
        [Fact]
        public void ToFileStep_EmptyTally_IsFailedRatherThanSilent()
        {
            StepResult s = new CopyResult().ToFileStep("hosts", true);

            Assert.Equal(ResultState.Failed, s.State);
        }
    }
}
