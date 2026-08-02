using System;
using System.Collections.Generic;
using System.Linq;

namespace WinRestoreKit
{
    /// <summary>What the restore loop does with one module.</summary>
    public enum RestoreAction
    {
        Run,
        Skip,
        Fail
    }

    /// <summary>
    /// One module's dispatch decision: whether to run it, and the close step that has to appear in
    /// its result either way.
    /// </summary>
    public sealed class RestoreDecision
    {
        public RestoreAction Action { get; }

        /// <summary>The close outcome as a visible step, or null when there was nothing to close.</summary>
        public StepResult CloseStep { get; }

        /// <summary>
        /// Set only for a requirement that does not need consent: the caller closes this process
        /// immediately before dispatching the module, never up front.
        /// </summary>
        /// <remarks>
        /// StartMenuExperienceHost respawns within seconds, so a close performed at consent time is
        /// gone again by the time the copy runs. Carrying the requirement here lets the caller close
        /// it just in time without consulting consent for a process the user was never asked about.
        /// </remarks>
        public RestoreCloseRequirement JustInTimeClose { get; }

        private RestoreDecision(RestoreAction action, StepResult closeStep, RestoreCloseRequirement justInTimeClose)
        {
            Action = action;
            CloseStep = closeStep;
            JustInTimeClose = justInTimeClose;
        }

        internal static RestoreDecision Run(StepResult closeStep, RestoreCloseRequirement justInTimeClose)
            => new RestoreDecision(RestoreAction.Run, closeStep, justInTimeClose);

        internal static RestoreDecision Skip(StepResult closeStep)
            => new RestoreDecision(RestoreAction.Skip, closeStep, null);

        internal static RestoreDecision Fail(StepResult closeStep)
            => new RestoreDecision(RestoreAction.Fail, closeStep, null);
    }

    /// <summary>
    /// Decides, per module, what the restore loop should do about the process that owns the files
    /// the module is about to overwrite.
    /// </summary>
    /// <remarks>
    /// Pure: it starts no process, closes none, and shows no dialog. Every observation it needs -
    /// consent, whether the process is running, how a close went - is passed in, so the whole
    /// decision table is exercisable off a real machine.
    ///
    /// There is deliberately no "restore over the live application anyway" outcome. That is the
    /// hazard this removes, not a preference being offered.
    /// </remarks>
    public static class RestoreDispatch
    {
        /// <param name="moduleTitle">Names the step, so the close shows up under the module it belongs to.</param>
        /// <param name="requirement">Null when the module declares nothing to close.</param>
        /// <param name="consentGiven">Whether the user ticked this process in the confirmation dialog.</param>
        /// <param name="isRunning">
        /// Re-checked per module rather than reused from consent time: consent persists for the run,
        /// the process state does not, and the user can reopen a browser mid-run.
        /// </param>
        /// <param name="closeResult">
        /// The outcome of the close the caller attempted. Consulted only when
        /// <paramref name="isRunning"/> is true; with nothing running there was no close to report on,
        /// and reading a stale value here would let two inputs disagree about the same fact.
        /// </param>
        public static RestoreDecision Decide(
            string moduleTitle,
            RestoreCloseRequirement requirement,
            bool consentGiven,
            bool isRunning,
            CloseResult closeResult)
        {
            if (string.IsNullOrWhiteSpace(moduleTitle))
                throw new ArgumentException("A dispatch decision must name its module.", nameof(moduleTitle));

            if (requirement == null)
                return RestoreDecision.Run(null, null);

            if (!requirement.NeedsConsent)
                return RestoreDecision.Run(null, requirement);

            if (!consentGiven)
                return RestoreDecision.Skip(StepResult.Skipped(
                    moduleTitle,
                    "you chose not to close " + requirement.DisplayName + ", so its profile was not restored"));

            if (!isRunning)
                return RestoreDecision.Run(
                    StepResult.Skipped(
                        moduleTitle,
                        requirement.DisplayName + " was not running, so nothing had to be closed"),
                    null);

            switch (closeResult)
            {
                case CloseResult.Exited:
                    return RestoreDecision.Run(
                        StepResult.Succeeded(
                            moduleTitle,
                            "closed " + requirement.DisplayName + " before writing its profile"),
                        null);

                case CloseResult.NotRunning:
                    return RestoreDecision.Run(
                        StepResult.Skipped(
                            moduleTitle,
                            requirement.DisplayName + " was not running, so nothing had to be closed"),
                        null);

                case CloseResult.StillRunning:
                case CloseResult.AccessDenied:
                    return RestoreDecision.Fail(StepResult.Failed(
                        moduleTitle,
                        requirement.DisplayName + " could not be closed, so its profile was not restored"));

                // Fails closed on purpose. Falling through to Run would overwrite a profile whose
                // owning process is in a state this code does not recognise, which is the one
                // outcome that cannot be taken back.
                default:
                    return RestoreDecision.Fail(StepResult.Failed(
                        moduleTitle,
                        "the state of " + requirement.DisplayName +
                        " could not be determined, so its profile was not restored"));
            }
        }

        /// <summary>
        /// Folds a close step into the module's own result.
        /// </summary>
        /// <remarks>
        /// Re-aggregating the module's steps with the close step in front introduces no new rule:
        /// Aggregate is still the single construction path, so a Failed close dominates by its
        /// Rule 2 exactly as any other failed step would.
        /// </remarks>
        public static ModuleResult Fold(StepResult closeStep, ModuleResult moduleResult)
        {
            if (closeStep == null)
                return moduleResult ?? ModuleResult.Aggregate(new StepResult[0]);

            IEnumerable<StepResult> steps = moduleResult == null
                ? Enumerable.Empty<StepResult>()
                : moduleResult.Steps;

            return ModuleResult.Aggregate(new[] { closeStep }.Concat(steps).ToArray());
        }
    }
}
