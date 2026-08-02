using WinRestoreKit;
using System.Collections.Generic;
using System.Windows.Forms;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class RunSummaryTests
    {
        private static ModuleOutcome Ok(string title = "Mouse")
            => new ModuleOutcome(title, ModuleResult.Aggregate(new[] { StepResult.Succeeded("k", "exported k") }));

        private static ModuleOutcome Skip(string title = "Gaming settings")
            => new ModuleOutcome(title, ModuleResult.Aggregate(new[] { StepResult.Skipped("k", "not present on this system") }));

        private static ModuleOutcome Bad(string title = "Printers")
            => new ModuleOutcome(title, ModuleResult.Aggregate(new[] { StepResult.Failed("k", "access denied") }));

        [Fact]
        public void AnyFailure_IsProblems()
            => Assert.Equal(RunState.Problems,
                   RunSummary.For(new List<ModuleOutcome> { Ok(), Bad() }, true, RunVerb.Backup).State);

        [Fact]
        public void AllSucceeded_IsDone()
            => Assert.Equal(RunState.Done,
                   RunSummary.For(new List<ModuleOutcome> { Ok(), Ok() }, true, RunVerb.Backup).State);

        [Fact]
        public void SucceededPlusSkipped_IsDoneNotProblems()
            => Assert.Equal(RunState.Done,
                   RunSummary.For(new List<ModuleOutcome> { Ok(), Skip() }, true, RunVerb.Backup).State);

        // The whole point: absences must never be counted as failures.
        [Fact]
        public void SucceededPlusSkipped_HeadlineDoesNotClaimAProblem()
        {
            RunSummary s = RunSummary.For(new List<ModuleOutcome> { Ok(), Skip() }, true, RunVerb.Backup);

            Assert.DoesNotContain("problem", s.Headline, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("fail", s.Headline, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AllSkipped_IsNothingDone()
            => Assert.Equal(RunState.NothingDone,
                   RunSummary.For(new List<ModuleOutcome> { Skip(), Skip() }, true, RunVerb.Backup).State);

        // The old code said "Back up done." here. It must not.
        [Fact]
        public void AllSkipped_NeverSaysDone()
        {
            RunSummary s = RunSummary.For(new List<ModuleOutcome> { Skip(), Skip() }, true, RunVerb.Backup);
            Assert.DoesNotContain("done", s.Headline, System.StringComparison.OrdinalIgnoreCase);
        }

        // The silent no-op at ConfPageView.cs:185.
        [Fact]
        public void NotRun_IsDidNotRun()
            => Assert.Equal(RunState.DidNotRun,
                   RunSummary.For(new List<ModuleOutcome>(), false, RunVerb.Restore).State);

        [Fact]
        public void NotRun_SaysItDidNotRun()
        {
            RunSummary s = RunSummary.For(new List<ModuleOutcome>(), false, RunVerb.Restore);
            Assert.Contains("did not run", s.Detail, System.StringComparison.OrdinalIgnoreCase);
        }

        // The verb must read correctly in BOTH sentences. A single string cannot do it:
        // the past tense that makes "Backed up 3 items" work yields "Restored did not run."
        [Fact]
        public void NotRun_HeadlineReadsAsASentence()
        {
            Assert.Equal("Restore did not run.",
                RunSummary.For(new List<ModuleOutcome>(), false, RunVerb.Restore).Headline);
        }

        [Fact]
        public void Done_HeadlineUsesThePastTense()
        {
            RunSummary s = RunSummary.For(new List<ModuleOutcome> { Ok() }, true, RunVerb.Restore);
            Assert.StartsWith("Restored", s.Headline);
        }

        // Every user-facing sentence runs for BOTH directions. Three separate bugs came from
        // hardcoding a backup verb into one of them, so each is pinned against the restore verb.

        [Fact]
        public void AllSkipped_Restore_DoesNotSayBackedUp()
        {
            RunSummary s = RunSummary.For(new List<ModuleOutcome> { Skip(), Skip() }, true, RunVerb.Restore);

            Assert.DoesNotContain("backed up", s.Headline, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("back up", s.Detail, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("restored", s.Headline, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SucceededPlusSkipped_Restore_FootnoteDoesNotSayBackUp()
        {
            RunSummary s = RunSummary.For(new List<ModuleOutcome> { Ok(), Skip() }, true, RunVerb.Restore);

            Assert.DoesNotContain("back up", s.Detail, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("restore", s.Detail, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DidNotRun_IsAWarningNotInformation()
            => Assert.Equal(MessageBoxIcon.Warning,
                   RunSummary.For(new List<ModuleOutcome>(), false, RunVerb.Restore).Icon);

        [Fact]
        public void Problems_DetailNamesEveryFailedModule()
        {
            RunSummary s = RunSummary.For(
                new List<ModuleOutcome> { Bad("Printers"), Bad("Wi-Fi networks & passwords"), Ok() },
                true, RunVerb.Backup);

            Assert.Contains("access denied", s.Detail);
            Assert.Contains("2", s.Headline);
        }

        // A reason with no title is unactionable: "1 of 2 operations failed: regedit exited with
        // code 1" tells the user something broke but not which of the items they selected it was.
        [Fact]
        public void Problems_DetailLeadsWithTheModuleTitle()
        {
            RunSummary s = RunSummary.For(
                new List<ModuleOutcome> { Ok("Mouse"), Bad("Printers") }, true, RunVerb.Backup);

            Assert.Contains("Printers", s.Detail);
            // Only the failures are listed here, so a succeeded module must not appear.
            Assert.DoesNotContain("Mouse", s.Detail);
        }

        [Fact]
        public void Done_DetailNamesEverySucceededModule()
        {
            RunSummary s = RunSummary.For(
                new List<ModuleOutcome> { Ok("Mouse"), Ok("Keyboard") }, true, RunVerb.Backup);

            Assert.Contains("Mouse", s.Detail);
            Assert.Contains("Keyboard", s.Detail);
        }
    }
}
