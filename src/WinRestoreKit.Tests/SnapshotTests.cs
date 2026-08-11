using WinRestoreKit;
using Conf;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class SnapshotTests
    {
        // --- SnapshotNaming.NameFor ---

        [Fact]
        public void NameFor_HasTheDocumentedShape()
        {
            string name = SnapshotNaming.NameFor(new DateTime(2026, 7, 20, 9, 5, 3));
            Assert.Equal("2026-07-20 - 09.05.03 (pre-restore)", name);
        }

        // Seconds are the whole point: without them a second restore in the same minute names the
        // folder holding the first restore's undo material.
        [Fact]
        public void NameFor_SecondsApartProduceDifferentNames()
        {
            DateTime first = new DateTime(2026, 7, 20, 9, 5, 3);

            Assert.NotEqual(SnapshotNaming.NameFor(first), SnapshotNaming.NameFor(first.AddSeconds(1)));
        }

        [Fact]
        public void NameFor_SameInstantIsStable()
        {
            DateTime when = new DateTime(2026, 7, 20, 23, 59, 59);

            Assert.Equal(SnapshotNaming.NameFor(when), SnapshotNaming.NameFor(when));
        }

        // The folder name is an artifact users re-open months later. A culture whose calendar or
        // digits differ must not start a second naming scheme in the same directory.
        [Fact]
        public void NameFor_DoesNotVaryWithTheCurrentCulture()
        {
            CultureInfo original = System.Threading.Thread.CurrentThread.CurrentCulture;

            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("ar-SA");
                string arabic = SnapshotNaming.NameFor(new DateTime(2026, 7, 20, 9, 5, 3));

                System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
                string english = SnapshotNaming.NameFor(new DateTime(2026, 7, 20, 9, 5, 3));

                Assert.Equal(english, arabic);
                Assert.Equal("2026-07-20 - 09.05.03 (pre-restore)", arabic);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = original;
            }
        }

        // --- SnapshotNaming.Unique ---

        [Fact]
        public void Unique_NoCollision_ReturnsTheNameUntouched()
            => Assert.Equal("snap", SnapshotNaming.Unique("snap", _ => false));

        [Fact]
        public void Unique_OneCollision_AppendsTwo()
        {
            HashSet<string> taken = new HashSet<string> { "snap" };

            Assert.Equal("snap (2)", SnapshotNaming.Unique("snap", taken.Contains));
        }

        [Fact]
        public void Unique_SeveralCollisions_KeepsCounting()
        {
            HashSet<string> taken = new HashSet<string> { "snap", "snap (2)", "snap (3)" };

            Assert.Equal("snap (4)", SnapshotNaming.Unique("snap", taken.Contains));
        }

        [Fact]
        public void Unique_ConsultsTheProbeForEveryCandidate()
        {
            List<string> asked = new List<string>();

            SnapshotNaming.Unique("snap", n =>
            {
                asked.Add(n);
                return asked.Count <= 2;
            });

            Assert.Equal(new[] { "snap", "snap (2)", "snap (3)" }, asked);
        }

        // A probe that answers "taken" forever - a denied directory, an over-long path - must not
        // hang the restore before it has shown its dialog.
        [Fact]
        public void Unique_ProbeAlwaysSaysTaken_GivesUpInsteadOfLooping()
        {
            int calls = 0;

            Assert.Throws<InvalidOperationException>(
                () => SnapshotNaming.Unique("snap", _ => { calls++; return true; }));

            Assert.InRange(calls, 1, 5000);
        }

        [Fact]
        public void Unique_NullProbe_Throws()
            => Assert.Throws<ArgumentNullException>(() => SnapshotNaming.Unique("snap", null));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Unique_BlankName_Throws(string name)
            => Assert.Throws<ArgumentException>(() => SnapshotNaming.Unique(name, _ => false));

        // --- SnapshotGate ---

        private static ModuleResult Ok() => ModuleResult.Aggregate(new[] { StepResult.Succeeded("k", "exported k") });
        private static ModuleResult Skipped() => ModuleResult.Aggregate(new[] { StepResult.Skipped("k", "not present on this system") });
        private static ModuleResult Failed(string reason) => ModuleResult.Aggregate(new[] { StepResult.Failed("k", reason) });

        private static IReadOnlyList<ModuleOutcome> Outcomes(params ModuleOutcome[] outcomes) => outcomes;

        [Fact]
        public void Gate_FolderNotCreated_RequiresOverrideAndNamesTheError()
        {
            SnapshotDecision d = SnapshotGate.FolderNotCreated("Access to the path 'D:\\app' is denied.");

            Assert.True(d.RequiresOverride);
            Assert.Equal(SnapshotVerdict.FolderNotCreated, d.Verdict);
            Assert.Contains("Access to the path 'D:\\app' is denied.", d.Describe());
        }

        // The folder error is the only evidence there is. Losing it would leave the user a prompt
        // that says something failed without saying what.
        [Fact]
        public void Gate_FolderNotCreated_WithoutAnError_StillSaysSomething()
        {
            SnapshotDecision d = SnapshotGate.FolderNotCreated(null);

            Assert.True(d.RequiresOverride);
            Assert.False(string.IsNullOrWhiteSpace(d.Describe()));
        }

        [Fact]
        public void Gate_AnyFailedModule_RequiresOverrideAndNamesIt()
        {
            SnapshotDecision d = SnapshotGate.Evaluate(Outcomes(
                new ModuleOutcome("Mouse", Ok()),
                new ModuleOutcome("Wi-Fi networks & passwords", Failed("netsh wrote no files"))));

            Assert.True(d.RequiresOverride);
            Assert.Equal(SnapshotVerdict.ModulesFailed, d.Verdict);

            string text = d.Describe();
            Assert.Contains("Wi-Fi networks & passwords", text);
            Assert.Contains("netsh wrote no files", text);
        }

        [Fact]
        public void Gate_SeveralFailedModules_NamesEveryOne()
        {
            SnapshotDecision d = SnapshotGate.Evaluate(Outcomes(
                new ModuleOutcome("Mouse", Failed("access denied")),
                new ModuleOutcome("Touchpad", Failed("regedit exited with code 1")),
                new ModuleOutcome("Themes", Ok())));

            Assert.Equal(2, d.Failures.Count);
            Assert.Contains(d.Failures, f => f.Contains("Mouse") && f.Contains("access denied"));
            Assert.Contains(d.Failures, f => f.Contains("Touchpad") && f.Contains("regedit exited with code 1"));
            Assert.DoesNotContain(d.Failures, f => f.Contains("Themes"));
        }

        // A module that ran and reported nothing is not a module that succeeded. Treating a missing
        // result as fine is the "I could not tell" -> success conversion 2a exists to prevent.
        [Fact]
        public void Gate_ModuleWithNoResult_CountsAsAFailure()
        {
            SnapshotDecision d = SnapshotGate.Evaluate(Outcomes(new ModuleOutcome("Mouse", null)));

            Assert.True(d.RequiresOverride);
            Assert.Equal(SnapshotVerdict.ModulesFailed, d.Verdict);
            Assert.Contains(d.Failures, f => f.Contains("Mouse"));
        }

        [Fact]
        public void Gate_AllSkipped_RequiresOverrideAndSaysNothingWasCaptured()
        {
            SnapshotDecision d = SnapshotGate.Evaluate(Outcomes(
                new ModuleOutcome("Mouse", Skipped()),
                new ModuleOutcome("Touchpad", Skipped())));

            Assert.True(d.RequiresOverride);
            Assert.Equal(SnapshotVerdict.NothingCaptured, d.Verdict);
            Assert.Contains("captured nothing", d.Summary);

            // Listed rather than dropped. Nothing FAILED here - these are legitimate absences - but
            // every one of them is an item the restore will overwrite with no fallback, and the
            // record has to name them.
            Assert.Contains(d.Failures, f => f.Contains("Mouse"));
            Assert.Contains(d.Failures, f => f.Contains("Touchpad"));
        }

        // Every selected module had RestoreMakesChanges false, so nothing was snapshotted. This is
        // fine, and the log must not read as though a snapshot exists to roll back to.
        [Fact]
        public void Gate_EmptySnapshotSet_ProceedsAndSaysNoSnapshotWasTaken()
        {
            SnapshotDecision d = SnapshotGate.Evaluate(Outcomes());

            Assert.False(d.RequiresOverride);
            Assert.Equal(SnapshotVerdict.NothingCaptured, d.Verdict);
            Assert.Contains("No pre-restore snapshot was taken", d.Summary);
        }

        [Fact]
        public void Gate_NullOutcomes_IsTreatedAsAnEmptySet()
        {
            SnapshotDecision d = SnapshotGate.Evaluate(null);

            Assert.False(d.RequiresOverride);
            Assert.Equal(SnapshotVerdict.NothingCaptured, d.Verdict);
        }

        [Fact]
        public void Gate_AllSucceeded_Proceeds()
        {
            SnapshotDecision d = SnapshotGate.Evaluate(Outcomes(
                new ModuleOutcome("Mouse", Ok()),
                new ModuleOutcome("Touchpad", Ok())));

            Assert.False(d.RequiresOverride);
            Assert.Equal(SnapshotVerdict.Complete, d.Verdict);
            Assert.Empty(d.Failures);
            Assert.Contains("2", d.Summary);
        }

        // Rule 4's shape on the snapshot side: some captured, some legitimately absent. The
        // absence means a restore would write without a fallback, so the user must explicitly
        // accept that gap. It is not Complete and it must name what it did not capture.
        [Fact]
        public void Gate_SucceededPlusSkipped_RequiresOverrideButDoesNotClaimCompleteness()
        {
            SnapshotDecision d = SnapshotGate.Evaluate(Outcomes(
                new ModuleOutcome("Mouse", Ok()),
                new ModuleOutcome("Gaming", Skipped())));

            Assert.True(d.RequiresOverride);
            Assert.Equal(SnapshotVerdict.PartiallyCaptured, d.Verdict);

            // Named, not merely counted: the user has to know WHICH item has no fallback.
            Assert.Contains(d.Failures, f => f.Contains("Gaming"));
            Assert.Contains("Gaming", d.Describe());
        }

        // The whole point of separating the two verdicts. A run that captured everything and a run
        // that captured all but one must not read the same.
        [Fact]
        public void Gate_CompleteAndPartial_DoNotReadTheSame()
        {
            SnapshotDecision complete = SnapshotGate.Evaluate(Outcomes(
                new ModuleOutcome("Mouse", Ok())));

            SnapshotDecision partial = SnapshotGate.Evaluate(Outcomes(
                new ModuleOutcome("Mouse", Ok()),
                new ModuleOutcome("Gaming", Skipped())));

            Assert.NotEqual(complete.Verdict, partial.Verdict);
            Assert.NotEqual(complete.Summary, partial.Summary);
        }

        // Empty because everything was refused is not the same fact as empty because nothing writes,
        // and saying the second when the first is true tells the user their selection changes
        // nothing - which is false, and is the class of claim this phase exists to remove.
        [Fact]
        public void Gate_EmptyBecauseEverythingWasBlocked_DoesNotClaimNothingChanges()
        {
            SnapshotDecision blocked = SnapshotGate.Evaluate(new List<ModuleOutcome>(), blockedCount: 2);
            SnapshotDecision inert = SnapshotGate.Evaluate(new List<ModuleOutcome>());

            Assert.Equal(SnapshotVerdict.NothingCaptured, blocked.Verdict);
            Assert.DoesNotContain("change anything", blocked.Summary);
            Assert.Contains("change anything", inert.Summary);
            Assert.NotEqual(inert.Summary, blocked.Summary);
        }

        // A null entry is not a module that succeeded, and it is not a module that had nothing to
        // save either - it is an entry nothing was recorded for, which is the same "I could not
        // tell" the missing-result branch already refuses to convert into success.
        [Fact]
        public void Gate_NullOutcomeEntry_CountsAsAFailureRatherThanBeingSkipped()
        {
            SnapshotDecision d = SnapshotGate.Evaluate(Outcomes(new ModuleOutcome[] { null }));

            Assert.True(d.RequiresOverride);
            Assert.Equal(SnapshotVerdict.ModulesFailed, d.Verdict);
            Assert.Single(d.Failures);
        }

        // The defect this replaced: skipping a null before counting it left considered == 0, so an
        // all-null list fell through to the sentence claiming the selection changes nothing. That
        // is asserted against the LIE rather than against whichever branch happens to answer, so
        // rewording the branches cannot quietly reintroduce it.
        [Fact]
        public void Gate_AllNullOutcomes_NeverClaimTheSelectionChangesNothing()
        {
            SnapshotDecision d = SnapshotGate.Evaluate(Outcomes(new ModuleOutcome[] { null, null }));

            Assert.DoesNotContain("change anything", d.Summary);
            Assert.DoesNotContain("change anything", d.Describe());
        }

        // The denominator is what the user reads as "how much of my restore has a fallback". A null
        // that vanished from it would make a half-recorded snapshot read as a whole one.
        [Fact]
        public void Gate_NullAmongRealEntries_IsCountedInTheDenominator()
        {
            SnapshotDecision d = SnapshotGate.Evaluate(Outcomes(new ModuleOutcome("Mouse", Ok()), null));

            Assert.Contains("1 of 2", d.Summary);
        }

        // A null contributes a line of its own; it must not stand in for, or swallow, the failure
        // of a module the user can actually act on.
        [Fact]
        public void Gate_NullEntry_DoesNotDisplaceANamedFailure()
        {
            SnapshotDecision d = SnapshotGate.Evaluate(Outcomes(
                new ModuleOutcome("Wi-Fi networks & passwords", Failed("netsh wrote no files")),
                null));

            Assert.Equal(2, d.Failures.Count);
            Assert.Contains(d.Failures, f => f.Contains("Wi-Fi networks & passwords") && f.Contains("netsh wrote no files"));
            Assert.Contains("Wi-Fi networks & passwords", d.Describe());
        }

        // No outcomes at all is a legitimate state - nothing needed capturing. A list of nulls is a
        // list of things that were meant to be recorded and were not, and the two must not report
        // the same verdict.
        [Fact]
        public void Gate_ANullListIsNotTheSameAsAListOfNulls()
        {
            SnapshotDecision empty = SnapshotGate.Evaluate(null);
            SnapshotDecision nulls = SnapshotGate.Evaluate(Outcomes(new ModuleOutcome[] { null }));

            Assert.Equal(SnapshotVerdict.NothingCaptured, empty.Verdict);
            Assert.False(empty.RequiresOverride);

            Assert.Equal(SnapshotVerdict.ModulesFailed, nulls.Verdict);
            Assert.True(nulls.RequiresOverride);
            Assert.NotEqual(empty.Summary, nulls.Summary);
        }

        // Whichever way it went, restore_log.txt gets a line. A branch with an empty summary would
        // leave the record silent about the one thing it exists to say.
        [Theory]
        [InlineData(SnapshotVerdict.Complete)]
        [InlineData(SnapshotVerdict.NothingCaptured)]
        [InlineData(SnapshotVerdict.ModulesFailed)]
        [InlineData(SnapshotVerdict.FolderNotCreated)]
        public void Gate_EveryVerdictCarriesALoggableSummary(SnapshotVerdict verdict)
        {
            SnapshotDecision d = DecisionFor(verdict);

            Assert.Equal(verdict, d.Verdict);
            Assert.False(string.IsNullOrWhiteSpace(d.Summary));
            Assert.Contains(d.Summary.Trim(), d.Describe());
        }

        private static SnapshotDecision DecisionFor(SnapshotVerdict verdict)
        {
            switch (verdict)
            {
                case SnapshotVerdict.Complete:
                    return SnapshotGate.Evaluate(Outcomes(new ModuleOutcome("Mouse", Ok())));
                case SnapshotVerdict.NothingCaptured:
                    return SnapshotGate.Evaluate(Outcomes(new ModuleOutcome("Mouse", Skipped())));
                case SnapshotVerdict.ModulesFailed:
                    return SnapshotGate.Evaluate(Outcomes(new ModuleOutcome("Mouse", Failed("access denied"))));
                default:
                    return SnapshotGate.FolderNotCreated("denied");
            }
        }

        // --- BackupLog.Compose extra header lines ---

        private static List<BackupBase> LogModules() => new List<BackupBase> { new DMouse(), new DTouchpad() };

        private static List<ModuleResult> LogResults() => new List<ModuleResult> { Ok(), Skipped() };

        private const string When = "2026-07-20";

        // The exact text a call with no header lines produced before the parameter existed. Spelled
        // out rather than recomputed, so a change to the format fails here instead of being mirrored
        // into the expectation.
        private static string ExpectedV2Log()
        {
            string nl = Environment.NewLine;

            return BackupLog.VersionHeader + nl +
                   "# " + When + nl +
                   nl +
                   "Mouse (DMouse)  OK  exported k" + nl +
                   "Touchpad (DTouchpad)  SKIPPED  nothing to do: not present on this system" + nl;
        }

        [Fact]
        public void Compose_WithoutExtraHeaderLines_IsByteIdenticalToTheExistingFormat()
            => Assert.Equal(ExpectedV2Log(), BackupLog.Compose(LogModules(), LogResults(), When));

        [Fact]
        public void Compose_ExplicitNullExtraHeaderLines_IsTheSameText()
            => Assert.Equal(ExpectedV2Log(), BackupLog.Compose(LogModules(), LogResults(), When, null));

        [Fact]
        public void Compose_EmptyExtraHeaderLines_IsTheSameText()
            => Assert.Equal(ExpectedV2Log(),
                   BackupLog.Compose(LogModules(), LogResults(), When, new string[0]));

        [Fact]
        public void Compose_ExtraHeaderLines_SitBetweenTheVersionHeaderAndTheTimestamp()
        {
            string[] lines = BackupLog.Compose(LogModules(), LogResults(), When,
                    new[] { "# restored from: C:\\app\\2026-07-19 - 10.00", "# gate: complete" })
                .Split(new[] { Environment.NewLine }, StringSplitOptions.None);

            Assert.Equal(BackupLog.VersionHeader, lines[0]);
            Assert.Equal("# restored from: C:\\app\\2026-07-19 - 10.00", lines[1]);
            Assert.Equal("# gate: complete", lines[2]);
            Assert.Equal("# " + When, lines[3]);
        }

        [Fact]
        public void Compose_ExtraHeaderLines_DoNotDisturbTheModuleLines()
        {
            string text = BackupLog.Compose(LogModules(), LogResults(), When, new[] { "# extra" });

            Assert.Contains("Mouse (DMouse)  OK  exported k", text);
            Assert.Contains("Touchpad (DTouchpad)  SKIPPED", text);
        }

        // Verbatim, because the caller composing them owns how they read. A "# " added here would
        // silently double up on lines that already carry one.
        [Fact]
        public void Compose_ExtraHeaderLines_AreWrittenVerbatim()
        {
            string[] lines = BackupLog.Compose(LogModules(), LogResults(), When,
                    new[] { "the snapshot cannot remove values this restore adds" })
                .Split(new[] { Environment.NewLine }, StringSplitOptions.None);

            Assert.Equal("the snapshot cannot remove values this restore adds", lines[1]);
        }

        [Fact]
        public void Compose_NullEntryAmongExtraHeaderLines_IsDropped()
        {
            string[] lines = BackupLog.Compose(LogModules(), LogResults(), When,
                    new[] { "# a", null, "# b" })
                .Split(new[] { Environment.NewLine }, StringSplitOptions.None);

            Assert.Equal(new[] { BackupLog.VersionHeader, "# a", "# b", "# " + When },
                lines.Take(4).ToArray());
        }
    }
}
