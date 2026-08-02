using WinRestoreKit;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class RegistryStepTests : IDisposable
    {
        private readonly string _dir;

        public RegistryStepTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "acstep_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private const string PresentKey = @"HKEY_CURRENT_USER\Control Panel\Mouse";
        private const string AbsentKey = @"HKEY_CURRENT_USER\Software\Appcopier\NoSuchKeyAtAll";

        // Backing up twice in one app session writes into the same folder, so the second run can
        // find the first run's export still sitting at the target path. If the key has since been
        // removed, the early return on Absent used to leave that file behind while the log said the
        // item was skipped - and a later restore would import registry state the user was told had
        // not been captured.
        [Fact]
        public void Export_KeyNowAbsent_RemovesTheEarlierRunsFile()
        {
            string path = Valid("stale.reg");
            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0));

            StepResult s = Utils.ExportRegistryKey(path, AbsentKey, true, tool);

            Assert.Equal(ResultState.Skipped, s.State);
            Assert.False(File.Exists(path));

            // The clear must not be a side effect of running the export - regedit was never invoked.
            Assert.False(tool.ExportCalled);
        }

        // Same stale file, but absence is NOT normal for this key. The file must still go: the
        // reason it is being removed has nothing to do with how the step is classified.
        [Fact]
        public void Export_KeyMissingAndNotNormal_StillRemovesTheEarlierRunsFile()
        {
            string path = Valid("stale2.reg");

            StepResult s = Utils.ExportRegistryKey(path, AbsentKey, false, new FakeTool(ProcessOutcome.Ran(0)));

            Assert.Equal(ResultState.Failed, s.State);
            Assert.False(File.Exists(path));
        }

        private string Valid(string name)
        {
            string p = Path.Combine(_dir, name);
            File.WriteAllText(p, RegFile.Header + "\r\n\r\n[HKEY_CURRENT_USER\\X]\r\n",
                new UnicodeEncoding(false, true));
            return p;
        }

        // A tool that reports whatever the test wants and records what it was asked to do.
        private sealed class FakeTool : IRegistryTool
        {
            private readonly ProcessOutcome _outcome;
            private readonly Action<string> _onExport;

            public bool ImportCalled;
            public bool ExportCalled;

            public FakeTool(ProcessOutcome outcome, Action<string> onExport = null)
            {
                _outcome = outcome;
                _onExport = onExport;
            }

            public ProcessOutcome Export(string filePath, string registryPath)
            {
                ExportCalled = true;
                if (_onExport != null) _onExport(filePath);
                return _outcome;
            }

            public ProcessOutcome Import(string filePath)
            {
                ImportCalled = true;
                return _outcome;
            }
        }

        // A post-import probe that answers whatever the row under test needs and records that it was
        // actually consulted. Injected because the production probe reads the real registry, where
        // HKEY_CURRENT_USER\X is genuinely absent - every import row would otherwise assert the
        // Absent branch by accident.
        private sealed class FakeProbe
        {
            private readonly KeyProbe _answer;

            public bool Called;

            public FakeProbe(KeyProbe answer) => _answer = answer;

            public KeyProbe Probe(string key)
            {
                Called = true;
                return _answer;
            }
        }

        private static Func<string, KeyProbe> Answers(KeyProbe answer)
            => new FakeProbe(answer).Probe;

        // --- Export ---

        [Fact]
        public void Export_AbsentKey_AbsenceNormal_IsSkippedAndNeverLaunchesRegedit()
        {
            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0));
            StepResult s = Utils.ExportRegistryKey(Path.Combine(_dir, "x.reg"), AbsentKey, true, tool);

            Assert.Equal(ResultState.Skipped, s.State);
            Assert.False(tool.ExportCalled);
        }

        [Fact]
        public void Export_AbsentKey_AbsenceNotNormal_IsFailed()
        {
            StepResult s = Utils.ExportRegistryKey(Path.Combine(_dir, "x.reg"), AbsentKey, false,
                new FakeTool(ProcessOutcome.Ran(0)));

            Assert.Equal(ResultState.Failed, s.State);
        }

        // The measured case: regedit exits 0 and writes nothing at all.
        [Fact]
        public void Export_ExitZeroButNoFile_IsFailed()
        {
            StepResult s = Utils.ExportRegistryKey(Path.Combine(_dir, "never.reg"), PresentKey, false,
                new FakeTool(ProcessOutcome.Ran(0)));

            Assert.Equal(ResultState.Failed, s.State);
            Assert.Contains("no file", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Export_ExitZeroAndValidFile_IsSucceeded()
        {
            string path = Path.Combine(_dir, "good.reg");
            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0), p =>
                File.WriteAllText(p, RegFile.Header + "\r\n\r\n[HKEY_CURRENT_USER\\X]\r\n",
                    new UnicodeEncoding(false, true)));

            StepResult s = Utils.ExportRegistryKey(path, PresentKey, false, tool);

            Assert.Equal(ResultState.Succeeded, s.State);
        }

        [Fact]
        public void Export_NonZeroExit_IsFailed()
        {
            StepResult s = Utils.ExportRegistryKey(Path.Combine(_dir, "x.reg"), PresentKey, false,
                new FakeTool(ProcessOutcome.Ran(1)));

            Assert.Equal(ResultState.Failed, s.State);
        }

        [Fact]
        public void Export_Timeout_IsFailedAndSaysSo()
        {
            StepResult s = Utils.ExportRegistryKey(Path.Combine(_dir, "x.reg"), PresentKey, false,
                new FakeTool(ProcessOutcome.Timeout()));

            Assert.Equal(ResultState.Failed, s.State);
            Assert.Contains("did not exit", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        // --- Import ---

        [Fact]
        public void Import_MissingFile_IsSkippedAndNeverLaunchesRegedit()
        {
            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0));
            StepResult s = Utils.ImportRegistryKey(Path.Combine(_dir, "gone.reg"), "HKEY_CURRENT_USER\\X", tool);

            Assert.Equal(ResultState.Skipped, s.State);
            Assert.False(tool.ImportCalled);
        }

        // The registry must not be touched by a file we already know is malformed.
        [Fact]
        public void Import_MalformedFile_IsFailedBeforeTouchingTheRegistry()
        {
            string bad = Path.Combine(_dir, "bad.reg");
            File.WriteAllText(bad, "REGEDIT4\r\n", new UnicodeEncoding(false, true));

            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0));
            StepResult s = Utils.ImportRegistryKey(bad, "HKEY_CURRENT_USER\\X", tool);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.False(tool.ImportCalled);
        }

        [Fact]
        public void Import_EmptyFile_IsFailedBeforeTouchingTheRegistry()
        {
            string empty = Path.Combine(_dir, "empty.reg");
            File.WriteAllBytes(empty, new byte[0]);

            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0));
            StepResult s = Utils.ImportRegistryKey(empty, "HKEY_CURRENT_USER\\X", tool);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.False(tool.ImportCalled);
        }

        [Fact]
        public void Import_ValidFileAndZeroExit_IsSucceeded()
        {
            StepResult s = Utils.ImportRegistryKey(Valid("in.reg"), "HKEY_CURRENT_USER\\X",
                new FakeTool(ProcessOutcome.Ran(0)), Answers(KeyProbe.Present));

            Assert.Equal(ResultState.Succeeded, s.State);
        }

        // The wording rule: regedit /s returns 0 on partially-applied files, so we can only claim
        // to have applied it.
        [Fact]
        public void Import_Success_SaysAppliedAndNeverVerified()
        {
            StepResult s = Utils.ImportRegistryKey(Valid("in2.reg"), "HKEY_CURRENT_USER\\X",
                new FakeTool(ProcessOutcome.Ran(0)), Answers(KeyProbe.Present));

            Assert.Contains("applied", s.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("verified", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Import_NonZeroExit_IsFailed()
        {
            StepResult s = Utils.ImportRegistryKey(Valid("in3.reg"), "HKEY_CURRENT_USER\\X",
                new FakeTool(ProcessOutcome.Ran(1)));

            Assert.Equal(ResultState.Failed, s.State);
        }

        // --- Branches that had no coverage, and cannot be reached until Task 8 without these ---

        [Fact]
        public void Export_NeverStarted_IsFailed()
        {
            StepResult s = Utils.ExportRegistryKey(Path.Combine(_dir, "x.reg"), PresentKey, false,
                new FakeTool(ProcessOutcome.NeverStarted("boom")));

            Assert.Equal(ResultState.Failed, s.State);
            Assert.Contains("could not start", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        // If regedit STARTED, it may already have written to the registry. Reporting that as
        // "could not start" would be a false claim about whether the machine was modified.
        [Fact]
        public void Import_StartedButOutcomeUnknown_DoesNotClaimRegeditNeverRan()
        {
            StepResult s = Utils.ImportRegistryKey(Valid("unknown.reg"), "HKEY_CURRENT_USER\\X",
                new FakeTool(ProcessOutcome.OutcomeUnknown("handle closed")));

            Assert.Equal(ResultState.Failed, s.State);
            Assert.DoesNotContain("could not start", s.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("may have been partly changed", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Export_StartedButOutcomeUnknown_DoesNotClaimRegeditNeverRan()
        {
            StepResult s = Utils.ExportRegistryKey(Path.Combine(_dir, "u.reg"), PresentKey, false,
                new FakeTool(ProcessOutcome.OutcomeUnknown("handle closed")));

            Assert.Equal(ResultState.Failed, s.State);
            Assert.DoesNotContain("could not start", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Export_ExitZeroButEmptyFile_IsFailed()
        {
            string path = Path.Combine(_dir, "empty-out.reg");
            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0), p => File.WriteAllBytes(p, new byte[0]));

            StepResult s = Utils.ExportRegistryKey(path, PresentKey, false, tool);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.Contains("empty", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Export_ExitZeroButWrongHeader_IsFailed()
        {
            string path = Path.Combine(_dir, "bad-out.reg");
            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0),
                p => File.WriteAllText(p, "REGEDIT4\r\n", new UnicodeEncoding(false, true)));

            StepResult s = Utils.ExportRegistryKey(path, PresentKey, false, tool);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.Contains("not a valid", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        // Provenance: a valid .reg already sitting at the target path must NOT be able to satisfy
        // verification for an export that wrote nothing. Without the pre-delete, regedit's measured
        // exit-0-writes-nothing behaviour would be reported as success off a stale artifact.
        [Fact]
        public void Export_StaleFileAtTarget_DoesNotCountAsThisRunsOutput()
        {
            string path = Valid("stale.reg");          // a valid .reg already present
            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0));   // writes nothing

            StepResult s = Utils.ExportRegistryKey(path, PresentKey, false, tool);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.Contains("no file", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        // --- A failed export must not leave a landmine behind ---
        //
        // RegFile.Validate is header-only by design, so a truncated export with an intact header
        // sails through ImportRegistryKey's pre-flight, reaches regedit /s, exits 0 and is reported
        // as applied. The user would be told the backup failed and then told the restore of that
        // same known-bad file worked. These three pin the delete on each abandoning branch.

        // A part-written export: correct header, then cut off mid-key. This is what regedit leaves
        // when it is killed or times out part-way through writing.
        private static void WriteTruncated(string path)
            => File.WriteAllText(path, RegFile.Header + "\r\n\r\n[HKEY_CURRENT_U",
                   new UnicodeEncoding(false, true));

        [Fact]
        public void Export_NonZeroExit_RemovesThePartWrittenFile()
        {
            string path = Path.Combine(_dir, "partial-exit.reg");
            FakeTool tool = new FakeTool(ProcessOutcome.Ran(1), WriteTruncated);

            StepResult s = Utils.ExportRegistryKey(path, PresentKey, false, tool);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void Export_Timeout_RemovesThePartWrittenFile()
        {
            string path = Path.Combine(_dir, "partial-timeout.reg");
            FakeTool tool = new FakeTool(ProcessOutcome.Timeout(), WriteTruncated);

            StepResult s = Utils.ExportRegistryKey(path, PresentKey, false, tool);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void Export_OutcomeUnknown_RemovesThePartWrittenFile()
        {
            string path = Path.Combine(_dir, "partial-unknown.reg");
            FakeTool tool = new FakeTool(ProcessOutcome.OutcomeUnknown("handle closed"), WriteTruncated);

            StepResult s = Utils.ExportRegistryKey(path, PresentKey, false, tool);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.False(File.Exists(path));
        }

        // The end-to-end shape of the bug: without the delete, the truncated file left by a failed
        // export passes the import pre-flight and reaches the registry.
        [Fact]
        public void Export_FailedThenImport_HasNothingToImport()
        {
            string path = Path.Combine(_dir, "landmine.reg");

            Utils.ExportRegistryKey(path, PresentKey, false,
                new FakeTool(ProcessOutcome.Ran(1), WriteTruncated));

            FakeTool importer = new FakeTool(ProcessOutcome.Ran(0));
            StepResult s = Utils.ImportRegistryKey(path, "HKEY_CURRENT_USER\\X", importer);

            Assert.Equal(ResultState.Skipped, s.State);
            Assert.False(importer.ImportCalled);
        }

        [Fact]
        public void Import_UnreadableFile_IsFailedWithoutCallingItInvalid()
        {
            string path = Valid("locked-in.reg");

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                FakeTool tool = new FakeTool(ProcessOutcome.Ran(0));
                StepResult s = Utils.ImportRegistryKey(path, "HKEY_CURRENT_USER\\X", tool);

                Assert.Equal(ResultState.Failed, s.State);
                Assert.False(tool.ImportCalled);
                Assert.Contains("could not read", s.Reason, StringComparison.OrdinalIgnoreCase);
                // We never read it, so we must not assert anything about its contents.
                Assert.DoesNotContain("not a valid", s.Reason, StringComparison.OrdinalIgnoreCase);
            }
        }

        // --- Post-import read-back ---

        [Fact]
        public void Import_ProbeSaysPresent_IsSucceededAndSaysSo()
        {
            FakeProbe probe = new FakeProbe(KeyProbe.Present);

            StepResult s = Utils.ImportRegistryKey(Valid("present.reg"), "HKEY_CURRENT_USER\\X",
                new FakeTool(ProcessOutcome.Ran(0)), probe.Probe);

            Assert.True(probe.Called);
            Assert.Equal(ResultState.Succeeded, s.State);
            Assert.Contains("the key is present after the import", s.Reason);
        }

        // The whole reason the read-back exists: regedit /s exits 0 on imports that did nothing, so
        // exit code 0 with the key still missing is affirmative evidence of a failed restore.
        [Fact]
        public void Import_ProbeSaysAbsent_IsFailedDespiteExitZero()
        {
            StepResult s = Utils.ImportRegistryKey(Valid("absent.reg"), "HKEY_CURRENT_USER\\X",
                new FakeTool(ProcessOutcome.Ran(0)), Answers(KeyProbe.Absent));

            Assert.Equal(ResultState.Failed, s.State);
            Assert.Contains("is not present after the import", s.Reason);
        }

        // The opposite mapping to the export path, and the reason for it: an unelevated probe of an
        // HKLM key regedit has just written under elevation lands here. Failing would report a false
        // failure on an import that worked.
        [Fact]
        public void Import_ProbeIndeterminate_StaysSucceededAndClaimsNoConfirmation()
        {
            StepResult s = Utils.ImportRegistryKey(Valid("unknown-probe.reg"),
                "HKEY_LOCAL_MACHINE\\Software\\Appcopier", new FakeTool(ProcessOutcome.Ran(0)),
                Answers(KeyProbe.Indeterminate));

            Assert.Equal(ResultState.Succeeded, s.State);
            Assert.Contains("could not confirm", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        // ProbeKey answers Absent for a hive it does not parse, which is indistinguishable from a
        // key it looked for and did not find. Treating that as evidence would fail every import of
        // such a key on the strength of a lookup that never happened.
        [Theory]
        [InlineData("HKEY_CLASSES_ROOT\\.txt")]
        [InlineData("HKEY_USERS\\.DEFAULT\\Control Panel")]
        [InlineData("HKCU\\Control Panel\\Mouse")]
        public void Import_HiveTheProbeCannotParse_IsSucceededWithoutProbing(string key)
        {
            FakeProbe probe = new FakeProbe(KeyProbe.Absent);

            StepResult s = Utils.ImportRegistryKey(Valid("hive.reg"), key,
                new FakeTool(ProcessOutcome.Ran(0)), probe.Probe);

            Assert.False(probe.Called);
            Assert.Equal(ResultState.Succeeded, s.State);
            Assert.Contains("applied", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("HKEY_CURRENT_USER\\Control Panel\\Mouse", true)]
        [InlineData("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft", true)]
        [InlineData("HKEY_CLASSES_ROOT\\.txt", false)]
        [InlineData("HKEY_USERS\\.DEFAULT", false)]
        [InlineData("HKEY_CURRENT_USER", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsProbeableKeyPath_MatchesWhatProbeKeyActuallyUnderstands(string key, bool expected)
            => Assert.Equal(expected, Utils.IsProbeableKeyPath(key));

        // The read-back narrows the wording; it must never upgrade it. Presence proves the key
        // exists, not that a single value under it matches the backup.
        [Theory]
        [InlineData(KeyProbe.Present)]
        [InlineData(KeyProbe.Indeterminate)]
        public void Import_NoReadBackOutcomeEverClaimsVerification(KeyProbe answer)
        {
            StepResult s = Utils.ImportRegistryKey(Valid("wording_" + answer + ".reg"),
                "HKEY_CURRENT_USER\\X", new FakeTool(ProcessOutcome.Ran(0)), Answers(answer));

            Assert.DoesNotContain("verified", s.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("restored", s.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("applied", s.Reason, StringComparison.OrdinalIgnoreCase);
        }

        // Regression rows: the read-back sits AFTER the pre-flight and after the exit-code checks,
        // so a probe that would answer Present must not be able to rescue any of them.
        [Fact]
        public void Import_MalformedFile_StillFailsEvenIfTheKeyIsPresent()
        {
            string bad = Path.Combine(_dir, "bad-preflight.reg");
            File.WriteAllText(bad, "REGEDIT4\r\n", new UnicodeEncoding(false, true));

            FakeProbe probe = new FakeProbe(KeyProbe.Present);
            FakeTool tool = new FakeTool(ProcessOutcome.Ran(0));

            StepResult s = Utils.ImportRegistryKey(bad, "HKEY_CURRENT_USER\\X", tool, probe.Probe);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.False(tool.ImportCalled);
            Assert.False(probe.Called);
        }

        [Fact]
        public void Import_MissingFile_StillSkipsEvenIfTheKeyIsPresent()
        {
            FakeProbe probe = new FakeProbe(KeyProbe.Present);

            StepResult s = Utils.ImportRegistryKey(Path.Combine(_dir, "absent-file.reg"),
                "HKEY_CURRENT_USER\\X", new FakeTool(ProcessOutcome.Ran(0)), probe.Probe);

            Assert.Equal(ResultState.Skipped, s.State);
            Assert.False(probe.Called);
        }

        [Fact]
        public void Import_NonZeroExit_StillFailsEvenIfTheKeyIsPresent()
        {
            FakeProbe probe = new FakeProbe(KeyProbe.Present);

            StepResult s = Utils.ImportRegistryKey(Valid("nonzero.reg"), "HKEY_CURRENT_USER\\X",
                new FakeTool(ProcessOutcome.Ran(1)), probe.Probe);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.False(probe.Called);
        }

        [Fact]
        public void Import_Timeout_StillFailsEvenIfTheKeyIsPresent()
        {
            FakeProbe probe = new FakeProbe(KeyProbe.Present);

            StepResult s = Utils.ImportRegistryKey(Valid("timeout.reg"), "HKEY_CURRENT_USER\\X",
                new FakeTool(ProcessOutcome.Timeout()), probe.Probe);

            Assert.Equal(ResultState.Failed, s.State);
            Assert.False(probe.Called);
            Assert.Contains("did not exit", s.Reason, StringComparison.OrdinalIgnoreCase);
        }
    }
}
