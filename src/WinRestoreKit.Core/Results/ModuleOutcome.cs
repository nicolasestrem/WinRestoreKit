using System.Collections.Generic;

namespace WinRestoreKit
{
    /// <summary>
    /// One module's verdict, paired with the title that names it to the user.
    /// </summary>
    /// <remarks>
    /// A separate pair type rather than a Title property on ModuleResult, deliberately. ModuleResult
    /// is immutable and returned by value because modules run on different threads (see its own
    /// remarks), so a settable Title would have to be written by the caller after the fact - on the
    /// one object whose immutability the design depends on.
    ///
    /// It is also the honest place for it: a title is a property of the RUN, not of the verdict.
    /// Only the view knows which module produced which result, and this is where the two meet.
    /// Without it the summary dialog listed bare reasons - "1 of 2 operations failed: regedit exited
    /// with code 1" - leaving the user no way to tell which of six selected items had the problem.
    /// </remarks>
    internal sealed class ModuleOutcome
    {
        public string Title { get; }
        public ModuleResult Result { get; }

        public ModuleOutcome(string title, ModuleResult result)
        {
            Title = title;
            Result = result;
        }

        /// <summary>
        /// Zips the modules that ran with the results they produced, in order.
        /// </summary>
        /// <remarks>
        /// Tolerates a short or ragged results list rather than indexing past the end: a module that
        /// threw before producing a result would otherwise take down the very summary that exists to
        /// report the failure.
        /// </remarks>
        internal static IReadOnlyList<ModuleOutcome> Pair(IReadOnlyList<BackupBase> modules,
                                                          IReadOnlyList<ModuleResult> results)
        {
            List<ModuleOutcome> paired = new List<ModuleOutcome>();

            int count = modules == null ? 0 : modules.Count;

            for (int i = 0; i < count; i++)
            {
                if (modules[i] == null || results == null || i >= results.Count || results[i] == null)
                    continue;

                paired.Add(new ModuleOutcome(modules[i].Title, results[i]));
            }

            return paired;
        }
    }
}
