using System;
using System.Diagnostics;

namespace WinRestoreKit
{
    /// <summary>
    /// What happened when we tried to run an external tool.
    /// </summary>
    internal sealed class ProcessOutcome
    {
        public bool Started { get; private set; }
        public bool TimedOut { get; private set; }
        public int ExitCode { get; private set; }
        public string Error { get; private set; }

        // Private so there is no way to obtain a default-constructed instance. A `new
        // ProcessOutcome()` would read as Started=false with a null Error, which a caller
        // renders as "could not start regedit: " with nothing after the colon.
        private ProcessOutcome() { }

        public static ProcessOutcome Ran(int exitCode)
            => new ProcessOutcome { Started = true, ExitCode = exitCode };

        public static ProcessOutcome Timeout()
            => new ProcessOutcome { Started = true, TimedOut = true };

        /// <summary>The process never started. Nothing was done.</summary>
        public static ProcessOutcome NeverStarted(string error)
            => new ProcessOutcome { Started = false, Error = error };

        /// <summary>
        /// The process started, but we lost track of how it ended.
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="NeverStarted"/> and the distinction matters more here than
        /// almost anywhere else in this phase. If regedit started, it may already have written to
        /// the registry. Reporting that as "could not start regedit" would tell the user nothing
        /// happened when something might have - a false claim about whether their machine was
        /// modified, which is the exact failure this project exists to eliminate.
        /// </remarks>
        public static ProcessOutcome OutcomeUnknown(string error)
            => new ProcessOutcome { Started = true, Error = error };
    }

    /// <summary>
    /// The registry export/import launch, behind an interface purely so module logic can be tested.
    /// </summary>
    /// <remarks>
    /// Nothing in the test suite can assert what regedit.exe returns for a denied key or a
    /// partially-applied file - those need elevation and a real hive. This seam does not fix that;
    /// it confines it, so everything ABOVE the launch is covered and the uncovered surface is one
    /// small class.
    /// </remarks>
    internal interface IRegistryTool
    {
        ProcessOutcome Export(string filePath, string registryPath);

        ProcessOutcome Import(string filePath);
    }

    internal sealed class RegeditTool : IRegistryTool
    {
        // regedit blocking on a modal error dialog used to hang the backup thread forever, because
        // the old WaitForExit() had no timeout and nothing read the exit code afterwards.
        private const int TimeoutMs = 60000;

        public ProcessOutcome Export(string filePath, string registryPath)
            => Run("/e", filePath, registryPath);

        // Note: no registry path argument. The old code appended one to /s, which documented regedit
        // syntax does not define.
        public ProcessOutcome Import(string filePath)
            => Run("/s", filePath, null);

        private static ProcessOutcome Run(string switchArg, string filePath, string registryPath)
        {
            bool started = false;

            try
            {
                using (Process proc = new Process())
                {
                    proc.StartInfo.FileName = "regedit.exe";
                    proc.StartInfo.UseShellExecute = false;

                    // ArgumentList quotes each value properly rather than pasting it into one
                    // command line. Utils.OpenUrl in this same file already uses it for exactly
                    // this reason; a path ending in a backslash breaks manual quoting.
                    proc.StartInfo.ArgumentList.Add(switchArg);
                    proc.StartInfo.ArgumentList.Add(filePath);

                    if (registryPath != null)
                        proc.StartInfo.ArgumentList.Add(registryPath);

                    // Deliberately no StartInfo.Verb = "runas": Verb is ignored while
                    // UseShellExecute is false, so the old line granted nothing and merely implied
                    // an elevation request that was not happening. Elevation comes from app.manifest.

                    proc.Start();
                    started = true;

                    if (!proc.WaitForExit(TimeoutMs))
                    {
                        try
                        {
                            proc.Kill(entireProcessTree: true);
                            // Kill is asynchronous. Without this the using block disposes while the
                            // process may still be terminating.
                            proc.WaitForExit(5000);
                        }
                        catch (Exception)
                        {
                            // A leaked process is the better trade than losing the timeout signal.
                        }

                        return ProcessOutcome.Timeout();
                    }

                    return ProcessOutcome.Ran(proc.ExitCode);
                }
            }
            catch (Exception ex)
            {
                // Which of these two we return is the whole point. Once Start() has returned,
                // regedit may already have modified the registry, so claiming it never started
                // would be a false statement about whether the machine was changed.
                return started
                    ? ProcessOutcome.OutcomeUnknown(ex.Message)
                    : ProcessOutcome.NeverStarted(ex.Message);
            }
        }
    }
}
