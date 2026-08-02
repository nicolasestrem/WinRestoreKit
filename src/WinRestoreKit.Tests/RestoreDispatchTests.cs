using WinRestoreKit;
using System;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class RestoreDispatchTests
    {
        private const string Title = "Google Chrome";

        private static RestoreCloseRequirement Consented()
            => new RestoreCloseRequirement("chrome", "Google Chrome", true);

        private static RestoreCloseRequirement Unconsented()
            => new RestoreCloseRequirement("StartMenuExperienceHost", "the Start menu", false);

        // --- No close requirement ---

        [Fact]
        public void NoRequirement_Runs_WithNoCloseStep()
        {
            RestoreDecision d = RestoreDispatch.Decide("Mouse", null, false, true, CloseResult.StillRunning);

            Assert.Equal(RestoreAction.Run, d.Action);
            Assert.Null(d.CloseStep);
            Assert.Null(d.JustInTimeClose);
        }

        // --- Consent required and given ---

        [Fact]
        public void ConsentGiven_ProcessExited_RunsAndRecordsTheClose()
        {
            RestoreDecision d = RestoreDispatch.Decide(Title, Consented(), true, true, CloseResult.Exited);

            Assert.Equal(RestoreAction.Run, d.Action);
            Assert.NotNull(d.CloseStep);
            Assert.Equal(ResultState.Succeeded, d.CloseStep.State);
            Assert.Equal(Title, d.CloseStep.Target);
            Assert.Equal("closed Google Chrome before writing its profile", d.CloseStep.Reason);
        }

        [Fact]
        public void ConsentGiven_CloseReportsNotRunning_RunsAndSaysNothingHadToBeClosed()
        {
            RestoreDecision d = RestoreDispatch.Decide(Title, Consented(), true, true, CloseResult.NotRunning);

            Assert.Equal(RestoreAction.Run, d.Action);
            Assert.Equal(ResultState.Skipped, d.CloseStep.State);
            Assert.Equal("Google Chrome was not running, so nothing had to be closed", d.CloseStep.Reason);
        }

        // Consent persists for the whole run; the process state does not. A consented browser that
        // is simply not running must not be reported as having been closed.
        [Fact]
        public void ConsentGiven_ProcessNotRunning_RunsWithoutClaimingAClose()
        {
            RestoreDecision d = RestoreDispatch.Decide(Title, Consented(), true, false, CloseResult.Exited);

            Assert.Equal(RestoreAction.Run, d.Action);
            Assert.Equal(ResultState.Skipped, d.CloseStep.State);
            Assert.Equal("Google Chrome was not running, so nothing had to be closed", d.CloseStep.Reason);
            Assert.DoesNotContain("closed Google Chrome", d.CloseStep.Reason);
        }

        // --- Consent required and withheld ---

        // The wording mirrors the backup side's Skipped case in BGoogleChrome, which is the sentence
        // users have already seen for the same choice in the other direction.
        [Fact]
        public void ConsentWithheld_SkipsWithTheBackupMirrorWording()
        {
            RestoreDecision d = RestoreDispatch.Decide(Title, Consented(), false, true, CloseResult.Exited);

            Assert.Equal(RestoreAction.Skip, d.Action);
            Assert.Equal(ResultState.Skipped, d.CloseStep.State);
            Assert.Equal(
                "you chose not to close Google Chrome, so its profile was not restored",
                d.CloseStep.Reason);
        }

        // Withheld consent is a decision, not an observation of the machine: it holds whether or not
        // the browser happens to be running, and there is no "restore over it anyway" outcome.
        [Fact]
        public void ConsentWithheld_ProcessNotRunning_StillSkips()
        {
            RestoreDecision d = RestoreDispatch.Decide(Title, Consented(), false, false, CloseResult.NotRunning);

            Assert.Equal(RestoreAction.Skip, d.Action);
        }

        // --- Close attempted and failed ---

        [Theory]
        [InlineData(CloseResult.StillRunning)]
        [InlineData(CloseResult.AccessDenied)]
        public void ConsentGiven_CloseFailed_FailsAndSaysTheProfileWasNotRestored(CloseResult outcome)
        {
            RestoreDecision d = RestoreDispatch.Decide(Title, Consented(), true, true, outcome);

            Assert.Equal(RestoreAction.Fail, d.Action);
            Assert.Equal(ResultState.Failed, d.CloseStep.State);
            Assert.Equal(
                "Google Chrome could not be closed, so its profile was not restored",
                d.CloseStep.Reason);
        }

        // --- Non-consent requirements ---

        [Fact]
        public void RequirementWithoutConsent_RunsAndHandsBackTheJustInTimeClose()
        {
            RestoreDecision d = RestoreDispatch.Decide("Pinned apps", Unconsented(), false, true, CloseResult.NotRunning);

            Assert.Equal(RestoreAction.Run, d.Action);
            Assert.NotNull(d.JustInTimeClose);
            Assert.Equal("StartMenuExperienceHost", d.JustInTimeClose.ProcessName);
            Assert.Null(d.CloseStep);
        }

        // The consent flag is not consulted at all for these, so an unticked box - or a consent set
        // that never contained the process - cannot suppress the module.
        [Fact]
        public void RequirementWithoutConsent_IsUnaffectedByConsent()
        {
            RestoreDecision withConsent =
                RestoreDispatch.Decide("Pinned apps", Unconsented(), true, true, CloseResult.Exited);
            RestoreDecision without =
                RestoreDispatch.Decide("Pinned apps", Unconsented(), false, false, CloseResult.StillRunning);

            Assert.Equal(RestoreAction.Run, withConsent.Action);
            Assert.Equal(RestoreAction.Run, without.Action);
        }

        // --- Totality over CloseResult ---

        // Defaulting an unrecognised value to Run would overwrite a profile whose owning process is
        // in an unknown state, which is the one outcome that cannot be taken back.
        [Fact]
        public void UnrecognisedCloseResult_FailsClosed()
        {
            RestoreDecision d = RestoreDispatch.Decide(Title, Consented(), true, true, (CloseResult)999);

            Assert.Equal(RestoreAction.Fail, d.Action);
            Assert.Equal(ResultState.Failed, d.CloseStep.State);
            Assert.Contains("could not be determined", d.CloseStep.Reason);
        }

        [Fact]
        public void EveryDeclaredCloseResult_ProducesADecision()
        {
            foreach (CloseResult outcome in Enum.GetValues(typeof(CloseResult)))
            {
                RestoreDecision d = RestoreDispatch.Decide(Title, Consented(), true, true, outcome);
                Assert.NotNull(d.CloseStep);
            }
        }

        [Fact]
        public void Decide_WithoutAModuleTitle_Throws()
            => Assert.Throws<ArgumentException>(
                () => RestoreDispatch.Decide("", Consented(), true, true, CloseResult.Exited));

        // --- Folding the close step into the module's result ---

        // Rule 2 has to survive the fold: a module that copied every file it was asked to, but was
        // written over a process that would not close, is not a success.
        [Fact]
        public void Fold_FailedCloseStep_DominatesASucceededModuleResult()
        {
            ModuleResult module = ModuleResult.Aggregate(new[]
            {
                StepResult.Succeeded(Title, "copied 1204 file(s)")
            });
            Assert.Equal(ResultState.Succeeded, module.State);

            StepResult close = StepResult.Failed(Title, "Google Chrome could not be closed, so its profile was not restored");

            ModuleResult folded = RestoreDispatch.Fold(close, module);

            Assert.Equal(ResultState.Failed, folded.State);
            Assert.Contains("could not be closed", folded.Reason);
        }

        [Fact]
        public void Fold_PreservesTheModulesOwnSteps()
        {
            ModuleResult module = ModuleResult.Aggregate(new[]
            {
                StepResult.Succeeded("a", "copied 3 file(s)"),
                StepResult.Skipped("b", "nothing was backed up for this item")
            });

            ModuleResult folded = RestoreDispatch.Fold(
                StepResult.Succeeded(Title, "closed Google Chrome before writing its profile"),
                module);

            Assert.Equal(3, folded.Steps.Count);
            Assert.Contains(folded.Steps, s => s.Reason == "copied 3 file(s)");
            Assert.Contains(folded.Steps, s => s.Reason == "nothing was backed up for this item");
            Assert.Equal(ResultState.Succeeded, folded.State);
        }

        [Fact]
        public void Fold_NoCloseStep_ReturnsTheModuleResultUnchanged()
        {
            ModuleResult module = ModuleResult.Aggregate(new[] { StepResult.Succeeded("a", "copied 3 file(s)") });

            Assert.Same(module, RestoreDispatch.Fold(null, module));
        }

        [Fact]
        public void Fold_SkippedCloseStepOverASucceededModule_StaysSucceeded()
        {
            ModuleResult module = ModuleResult.Aggregate(new[] { StepResult.Succeeded("a", "copied 3 file(s)") });

            ModuleResult folded = RestoreDispatch.Fold(
                StepResult.Skipped(Title, "Google Chrome was not running, so nothing had to be closed"),
                module);

            Assert.Equal(ResultState.Succeeded, folded.State);
            Assert.Contains("nothing had to be closed", folded.Reason);
        }
    }
}
