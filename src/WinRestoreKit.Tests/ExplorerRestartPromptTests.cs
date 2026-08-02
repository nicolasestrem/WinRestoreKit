using WinRestoreKit;
using Conf;
using System.Collections.Generic;
using Xunit;

namespace WinRestoreKit.Tests
{
    /// <summary>
    /// The restart button must appear whenever a restore actually changed Explorer-owned state.
    /// </summary>
    /// <remarks>
    /// Written for a defect found by the Phase 3c safety review. The gate was
    /// <c>result.State == ResultState.Succeeded</c>, which was correct while every module declaring
    /// RequiresExplorerRestart wrote exactly one thing, and became wrong when WThemes (2c) and
    /// WTaskbar (3c) became multi-step hybrids: Aggregate lets one failed step dominate, so a restore
    /// that put back 32 pinned shortcuts and then failed a third step reported Failed and hid the
    /// button. WTaskbar's WarningMessage tells the user to press that button before signing out,
    /// because a live Explorer overwrites the restored pin list when it exits normally. So the bug
    /// removed the control that makes a restore stick, in exactly the case where the user had just
    /// been told to use it.
    ///
    /// These build ModuleResults through Aggregate, the only construction path, so the folded verdicts
    /// below are the real ones rather than assertions about a shape the production code never emits.
    /// </remarks>
    public class ExplorerRestartPromptTests
    {
        private static ModuleResult Folded(params StepResult[] steps)
            => ModuleResult.Aggregate(steps);

        private static bool IsNeeded(BackupBase module, ModuleResult result)
            => ExplorerRestartPrompt.IsNeeded(new List<BackupBase> { module }, new List<ModuleResult> { result });

        // The regression itself, stated as the scenario rather than as a state machine: the folder of
        // shortcuts went back and one key did not.
        [Fact]
        public void APartlyFailedTaskbarRestore_StillOffersTheRestart()
        {
            ModuleResult result = Folded(
                StepResult.Succeeded("Taskbar", "copied 32 file(s)"),
                StepResult.Applied("Taskbar", "the backed-up taskbar settings"),
                StepResult.Failed("Taskbar", "the exported file is empty"));

            // Precondition, so this test cannot quietly stop covering the defect: the module-level
            // verdict really is Failed. If Aggregate ever stopped letting one failure dominate, the
            // assertion below would pass for a reason that has nothing to do with the fix.
            Assert.Equal(ResultState.Failed, result.State);

            Assert.True(IsNeeded(new WTaskbar(), result));
        }

        // WThemes carried the same defect from Phase 2c and is fixed by the same rule. Pinned so the
        // fix cannot later be narrowed to the one module that made it visible.
        [Fact]
        public void APartlyFailedThemesRestore_StillOffersTheRestart()
        {
            ModuleResult result = Folded(
                StepResult.Succeeded("Themes", "copied 4 file(s)"),
                StepResult.Failed("Themes", "regedit exited with code 1"));

            Assert.Equal(ResultState.Failed, result.State);
            Assert.True(IsNeeded(new WThemes(), result));
        }

        [Fact]
        public void AFullySuccessfulRestore_OffersTheRestart()
        {
            ModuleResult result = Folded(StepResult.Applied("Taskbar", "the backed-up taskbar settings"));

            Assert.Equal(ResultState.Succeeded, result.State);
            Assert.True(IsNeeded(new WTaskbar(), result));
        }

        // The other direction, and the original intent this fix had to preserve. A restore where
        // nothing was written has nothing for a restart to reveal, so offering one would be a no-op
        // dressed up as a fix - which is what the old gate got right.
        [Fact]
        public void ARestoreWhereEveryStepFailed_OffersNothing()
        {
            ModuleResult result = Folded(
                StepResult.Failed("Taskbar", "nothing could be copied"),
                StepResult.Failed("Taskbar", "regedit exited with code 1"));

            Assert.False(IsNeeded(new WTaskbar(), result));
        }

        [Fact]
        public void ARestoreWithNothingBackedUp_OffersNothing()
        {
            ModuleResult result = Folded(
                StepResult.Skipped("Taskbar", "nothing was backed up for this item"),
                StepResult.Skipped("Taskbar", "nothing was backed up for this item"));

            Assert.Equal(ResultState.Skipped, result.State);
            Assert.False(IsNeeded(new WTaskbar(), result));
        }

        // A module that writes Explorer state is not the only thing in a restore. One that succeeds
        // completely but declares no restart requirement must not summon the button on its own.
        [Fact]
        public void AModuleThatDoesNotNeedARestart_OffersNothingEvenWhenItSucceeds()
        {
            ModuleResult result = Folded(StepResult.Applied("Mapped network drives", "the backed-up drives"));

            Assert.Equal(ResultState.Succeeded, result.State);
            Assert.False(IsNeeded(new WMappedDrives(), result));
        }

        // The realistic multi-module restore: the button is owned by the run, not by one row, so one
        // qualifying module writing something is enough even when another failed outright.
        [Fact]
        public void OneQualifyingModuleWritingSomething_IsEnoughForTheWholeRun()
        {
            List<BackupBase> modules = new List<BackupBase> { new WMappedDrives(), new WTaskbar() };

            List<ModuleResult> results = new List<ModuleResult>
            {
                Folded(StepResult.Failed("Mapped network drives", "regedit exited with code 1")),
                Folded(StepResult.Succeeded("Taskbar", "copied 32 file(s)"),
                       StepResult.Failed("Taskbar", "the exported file is empty"))
            };

            Assert.True(ExplorerRestartPrompt.IsNeeded(modules, results));
        }

        // Ragged input must not throw out of the final UI update of a restore, after the destructive
        // work is already done. Mirrors ModuleOutcome.Pair's tolerance, for the same reason.
        [Fact]
        public void RaggedOrMissingInput_IsAnsweredRatherThanThrown()
        {
            Assert.False(ExplorerRestartPrompt.IsNeeded(null, null));
            Assert.False(ExplorerRestartPrompt.IsNeeded(new List<BackupBase> { new WTaskbar() }, null));

            // More modules than results - the module that threw before producing one.
            Assert.False(ExplorerRestartPrompt.IsNeeded(
                new List<BackupBase> { new WTaskbar() },
                new List<ModuleResult>()));

            Assert.False(ExplorerRestartPrompt.IsNeeded(
                new List<BackupBase> { null },
                new List<ModuleResult> { Folded(StepResult.Succeeded("x", "y")) }));

            Assert.False(ExplorerRestartPrompt.IsNeeded(
                new List<BackupBase> { new WTaskbar() },
                new List<ModuleResult> { null }));
        }
    }
}
