using System;

namespace WinRestoreKit
{
    /// <summary>
    /// The outcome of a single sub-operation: one registry key, one folder copy, one shell command.
    /// </summary>
    public sealed class StepResult
    {
        /// <summary>Human-readable label: a registry key path, a folder path, "winget export".</summary>
        public string Target { get; }

        public ResultState State { get; }

        /// <summary>Never null, never empty. States what happened, not merely that it happened.</summary>
        public string Reason { get; }

        private StepResult(string target, ResultState state, string reason)
        {
            if (string.IsNullOrWhiteSpace(target))
                throw new ArgumentException("A step must name its target.", nameof(target));

            // Enforced here rather than by convention: an empty reason on a Skipped or Failed step
            // produces a summary dialog that says something went wrong without saying what, which
            // is the failure mode this whole phase exists to remove.
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("A step must carry a reason.", nameof(reason));

            Target = target;
            State = state;
            Reason = reason;
        }

        public static StepResult Succeeded(string target, string reason)
            => new StepResult(target, ResultState.Succeeded, reason);

        public static StepResult Skipped(string target, string reason)
            => new StepResult(target, ResultState.Skipped, reason);

        public static StepResult Failed(string target, string reason)
            => new StepResult(target, ResultState.Failed, reason);

        /// <summary>
        /// A successful restore-side operation.
        /// </summary>
        /// <remarks>
        /// A separate factory so the wording cannot drift across the 16 modules that restore.
        /// "applied" is not a synonym for "verified" here: regedit /s returns exit code 0 on a file
        /// it only partially applied, so having run it successfully is the strongest claim available
        /// without reading the keys back. Where a read-back is possible it narrows the wording rather
        /// than replacing it - a key that is present afterwards proves the import created the key,
        /// not that its values match the backup.
        /// </remarks>
        public static StepResult Applied(string target, string what)
            => new StepResult(target, ResultState.Succeeded, "applied " + what);
    }
}
