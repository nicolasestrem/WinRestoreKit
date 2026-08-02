using WinRestoreKit;
using Conf;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class RestoreLogTests
    {
        private const string Source = @"C:\Appcopier\app\2026-07-20 - 09.15";
        private const string SnapshotPath = @"C:\Appcopier\app\2026-07-20 - 10.02.41 (pre-restore)";

        private static List<BackupBase> Modules() => new List<BackupBase> { new DMouse(), new DTouchpad() };

        private static List<ModuleResult> Results() => new List<ModuleResult>
        {
            ModuleResult.Aggregate(new[] { StepResult.Applied("Mouse.reg", "1 key") }),
            ModuleResult.Aggregate(new[] { StepResult.Failed("Touchpad.reg", "regedit exited with code 1") })
        };

        // Built through the real gate rather than hand-constructed, so a change to what the gate
        // calls an override cannot leave these rows testing a state production never produces.
        private static SnapshotDecision Complete()
            => SnapshotGate.Evaluate(new[]
            {
                new ModuleOutcome("Mouse", ModuleResult.Aggregate(new[] { StepResult.Succeeded("k", "exported 1 key") }))
            });

        private static SnapshotDecision Overridden()
            => SnapshotGate.Evaluate(new[]
            {
                new ModuleOutcome("Mouse", ModuleResult.Aggregate(new[] { StepResult.Failed("k", "access denied") }))
            });

        private static SnapshotDecision NothingCaptured()
            => SnapshotGate.Evaluate(new ModuleOutcome[0]);

        // One item saved, one with nothing to save. The restore overwrites both.
        private static SnapshotDecision PartiallyCaptured()
            => SnapshotGate.Evaluate(new[]
            {
                new ModuleOutcome("Mouse", ModuleResult.Aggregate(new[] { StepResult.Succeeded("k", "exported 1 key") })),
                new ModuleOutcome("Gaming", ModuleResult.Aggregate(new[] { StepResult.Skipped("k", "not present on this system") }))
            });

        private static string Compose(SnapshotDecision snapshot, string snapshotPath = SnapshotPath)
            => RestoreLog.Compose(Modules(), Results(), "2026-07-20 - 10.02.41", Source, snapshot, snapshotPath);

        // --- What the log has to carry regardless of which path the gate took ---

        [Fact]
        public void Compose_NamesTheFolderTheRestoreReadFrom()
            => Assert.Contains(Source, Compose(Complete()));

        [Fact]
        public void Compose_CarriesTheTimestamp()
            => Assert.Contains("2026-07-20 - 10.02.41", Compose(Complete()));

        [Fact]
        public void Compose_ReportsEveryModulesOutcome()
        {
            string text = Compose(Complete());

            Assert.Contains("Mouse", text);
            Assert.Contains("Touchpad", text);
            Assert.Contains("regedit exited with code 1", text);
            Assert.Contains("FAILED", text);
        }

        // Against the constant, never a copied literal: a caveat that could be reworded in one place
        // and not the other is the softened-guarantee failure the constant exists to prevent.
        [Fact]
        public void Compose_CarriesTheFidelityCaveatVerbatim()
            => Assert.Contains(RestorePlan.FidelityCaveat, Compose(Complete()));

        // A partial capture must not read as a whole one. This is the line someone reads after a bad
        // restore to find out whether anything can undo it, and PartiallyCaptured inherited the
        // completion headline when it was added to the gate - so a restore that overwrote items with
        // no fallback was recorded as fully protected.
        [Fact]
        public void Compose_PartialSnapshot_DoesNotClaimTheSnapshotCompleted()
        {
            SnapshotDecision partial = PartiallyCaptured();
            string text = Compose(partial);

            Assert.Equal(SnapshotVerdict.PartiallyCaptured, partial.Verdict);
            Assert.DoesNotContain(RestoreLog.SnapshotTakenLine, text);
            Assert.DoesNotContain(RestoreLog.NoSnapshotWarning, text);

            // What it says instead: the gate's own summary, naming the item with no fallback.
            Assert.Contains(partial.Summary, text);
            Assert.Contains("Gaming", text);
        }

        // The allowlist, asserted as one. Only Complete may carry the completion line, so a verdict
        // added later cannot inherit a claim about undoability that nobody checked for it.
        [Theory]
        [InlineData(SnapshotVerdict.Complete, true)]
        [InlineData(SnapshotVerdict.PartiallyCaptured, false)]
        [InlineData(SnapshotVerdict.NothingCaptured, false)]
        [InlineData(SnapshotVerdict.ModulesFailed, false)]
        public void Compose_OnlyACompleteSnapshotSaysItCompleted(SnapshotVerdict verdict, bool expected)
        {
            SnapshotDecision snapshot =
                verdict == SnapshotVerdict.Complete ? Complete()
                : verdict == SnapshotVerdict.PartiallyCaptured ? PartiallyCaptured()
                : verdict == SnapshotVerdict.NothingCaptured ? NothingCaptured()
                : Overridden();

            Assert.Equal(verdict, snapshot.Verdict);
            Assert.Equal(expected, Compose(snapshot).Contains(RestoreLog.SnapshotTakenLine));
        }

        [Fact]
        public void Compose_CarriesTheCaveatOnEveryGatePath()
        {
            Assert.Contains(RestorePlan.FidelityCaveat, Compose(Overridden(), null));
            Assert.Contains(RestorePlan.FidelityCaveat, Compose(NothingCaptured()));
        }

        // --- The two variants, each exclusive of the other ---

        [Fact]
        public void Compose_CompleteSnapshot_SaysSoAndNamesTheFolder()
        {
            string text = Compose(Complete());

            Assert.Contains(RestoreLog.SnapshotTakenLine, text);
            Assert.Contains(SnapshotPath, text);
            Assert.DoesNotContain(RestoreLog.NoSnapshotWarning, text);
        }

        [Fact]
        public void Compose_OverriddenGate_WarnsAndNeverClaimsASnapshotCompleted()
        {
            string text = Compose(Overridden(), null);

            Assert.Contains(RestoreLog.NoSnapshotWarning, text);
            Assert.DoesNotContain(RestoreLog.SnapshotTakenLine, text);
        }

        [Fact]
        public void Compose_OverriddenGate_NamesWhatTheSnapshotCouldNotCapture()
        {
            string text = Compose(Overridden(), null);

            Assert.Contains("Mouse", text);
            Assert.Contains("access denied", text);
        }

        [Fact]
        public void Compose_OverriddenAfterFolderFailure_ReportsTheFolderError()
        {
            string text = Compose(SnapshotGate.FolderNotCreated("access to the path is denied"), null);

            Assert.Contains(RestoreLog.NoSnapshotWarning, text);
            Assert.Contains("access to the path is denied", text);
        }

        // An empty snapshot proceeds without an override, so neither variant applies: claiming one
        // sends a user looking for a rollback to a folder with nothing in it, and claiming the other
        // blames a choice they were never asked to make.
        [Fact]
        public void Compose_NothingCaptured_ClaimsNeitherVariant()
        {
            SnapshotDecision decision = NothingCaptured();
            string text = Compose(decision);

            Assert.DoesNotContain(RestoreLog.SnapshotTakenLine, text);
            Assert.DoesNotContain(RestoreLog.NoSnapshotWarning, text);
            Assert.Contains(decision.Summary, text);
        }

        [Fact]
        public void Compose_NoSnapshotDecision_DoesNotClaimASnapshotCompleted()
        {
            string text = RestoreLog.Compose(Modules(), Results(), "2026-07-20", Source, null, null);

            Assert.DoesNotContain(RestoreLog.SnapshotTakenLine, text);
            Assert.Contains("not recorded", text);
        }

        [Fact]
        public void Compose_NoSnapshotPath_OmitsTheFolderLineRatherThanPrintingABlankOne()
        {
            string text = Compose(Overridden(), null);
            Assert.DoesNotContain("Snapshot folder:", text);
        }

        [Fact]
        public void Compose_MissingSourcePath_SaysSoRatherThanLeavingItBlank()
        {
            string text = RestoreLog.Compose(Modules(), Results(), "2026-07-20", null, Complete(), SnapshotPath);
            Assert.Contains("not recorded", text);
        }

        // --- The fallback filename ---

        [Fact]
        public void FallbackFileName_ContainsNoCharacterWindowsRejects()
        {
            string name = RestoreLog.FallbackFileName(new DateTime(2026, 7, 20, 10, 2, 41));

            // IndexOf(char) rather than a substring assertion: the invalid set includes '\0', which
            // a culture-sensitive substring search reports as present in every string.
            foreach (char invalid in Path.GetInvalidFileNameChars())
                Assert.Equal(-1, name.IndexOf(invalid));
        }

        [Fact]
        public void FallbackFileName_IsATextFileNamedForTheRestoreLog()
        {
            string name = RestoreLog.FallbackFileName(new DateTime(2026, 7, 20, 10, 2, 41));

            Assert.StartsWith("restore_log ", name);
            Assert.EndsWith(".txt", name);
        }

        // The fallback lands in the restored-from folder, which every restore from that backup
        // reuses. Two attempts a few seconds apart must not overwrite each other's record.
        [Fact]
        public void FallbackFileName_DistinguishesTwoRestoresInTheSameMinute()
        {
            string first = RestoreLog.FallbackFileName(new DateTime(2026, 7, 20, 10, 2, 41));
            string second = RestoreLog.FallbackFileName(new DateTime(2026, 7, 20, 10, 2, 58));

            Assert.NotEqual(first, second);
        }
    }
}
