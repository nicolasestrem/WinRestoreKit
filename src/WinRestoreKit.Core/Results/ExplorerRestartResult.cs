namespace WinRestoreKit
{
    /// <summary>
    /// What happened to the Windows shell after an attempt to restart it.
    /// </summary>
    public enum ShellOutcome
    {
        /// <summary>Windows' own AutoRestartShell brought the shell back; nothing was started.</summary>
        RestartedByWindows,

        /// <summary>WinRestoreKit started exactly one explorer.exe.</summary>
        Restarted,

        /// <summary>A start was attempted and failed. <see cref="ExplorerRestartResult.Error"/> says why.</summary>
        FailedToStart,

        /// <summary>No start was attempted, because starting one would have added a second shell.</summary>
        NotAttempted
    }

    /// <summary>
    /// The outcome of <see cref="Utils.RestartExplorer"/>: how the old shell closed, what happened to
    /// the new one, and one sentence a user can act on.
    /// </summary>
    public sealed class ExplorerRestartResult
    {
        public CloseResult Close { get; }

        public ShellOutcome Shell { get; }

        /// <summary>Why the start failed, or null. Only meaningful with <see cref="ShellOutcome.FailedToStart"/>.</summary>
        public string Error { get; }

        public ExplorerRestartResult(CloseResult close, ShellOutcome shell, string error = null)
        {
            Close = close;
            Shell = shell;
            Error = string.IsNullOrWhiteSpace(error) ? null : error;
        }

        /// <summary>
        /// Whether a new shell should be started after a close that produced <paramref name="close"/>.
        /// </summary>
        /// <remarks>
        /// Both conditions guard the same hazard from different sides: explorer.exe launched while a
        /// shell is already running does not become the shell, it opens a stray file-browser window.
        /// A close that did not clear the old shell (StillRunning, AccessDenied) leaves one running
        /// just as surely as an auto-restart does.
        /// </remarks>
        internal static bool ShouldStartShell(CloseResult close, bool shellAlreadyRunning)
            => ClosedCleanly(close) && !shellAlreadyRunning;

        private static bool ClosedCleanly(CloseResult close)
            => close == CloseResult.Exited || close == CloseResult.NotRunning;

        /// <summary>
        /// The pure decision this class exists to make testable: what became of the shell.
        /// </summary>
        /// <param name="startError">Why the start failed, or null when it succeeded or was never tried.</param>
        public static ShellOutcome DecideShellOutcome(CloseResult close, bool shellAlreadyRunning, string startError)
        {
            if (!ClosedCleanly(close))
                return ShellOutcome.NotAttempted;

            if (shellAlreadyRunning)
                return ShellOutcome.RestartedByWindows;

            return string.IsNullOrWhiteSpace(startError)
                ? ShellOutcome.Restarted
                : ShellOutcome.FailedToStart;
        }

        /// <summary>
        /// One user-facing sentence covering this close/shell combination.
        /// </summary>
        /// <remarks>
        /// Every branch has a default that claims nothing. This is the string shown in a dialog when
        /// the restart went wrong, so an enum member added later must produce "I do not know" rather
        /// than inheriting the wording of whichever case happened to be listed first.
        /// </remarks>
        public string Describe()
        {
            switch (Shell)
            {
                case ShellOutcome.Restarted: return DescribeRestarted();
                case ShellOutcome.RestartedByWindows: return DescribeRestartedByWindows();
                case ShellOutcome.FailedToStart: return DescribeFailedToStart();
                case ShellOutcome.NotAttempted: return DescribeNotAttempted();
                default: return "File Explorer's state after the restart attempt could not be determined.";
            }
        }

        private string DescribeRestarted()
        {
            switch (Close)
            {
                case CloseResult.NotRunning:
                    return "File Explorer was not running, so a new one was started.";
                case CloseResult.Exited:
                    return "File Explorer was closed and restarted.";
                case CloseResult.StillRunning:
                    return "File Explorer did not confirm it had closed, and a new one was started anyway.";
                case CloseResult.AccessDenied:
                    return "Windows would not let WinRestoreKit close every File Explorer process, and a new one was started anyway.";
                default:
                    return "A new File Explorer was started, but how the previous one closed could not be determined.";
            }
        }

        private string DescribeRestartedByWindows()
        {
            switch (Close)
            {
                case CloseResult.NotRunning:
                    return "File Explorer was not running, and Windows already had a shell running, so nothing was started.";
                case CloseResult.Exited:
                    return "File Explorer was closed and Windows restarted it automatically.";
                case CloseResult.StillRunning:
                    return "File Explorer did not confirm it had closed, and a shell was running afterwards, so nothing was started.";
                case CloseResult.AccessDenied:
                    return "Windows would not let WinRestoreKit close every File Explorer process, and a shell was running afterwards, so nothing was started.";
                default:
                    return "A shell was running after the close attempt, so nothing was started, but how File Explorer closed could not be determined.";
            }
        }

        private string DescribeFailedToStart()
        {
            string sentence;

            switch (Close)
            {
                case CloseResult.NotRunning:
                    sentence = "File Explorer was not running and could not be started.";
                    break;
                case CloseResult.Exited:
                    sentence = "File Explorer was closed but could not be started again.";
                    break;
                case CloseResult.StillRunning:
                    sentence = "File Explorer did not confirm it had closed and could not be started again.";
                    break;
                case CloseResult.AccessDenied:
                    sentence = "Windows would not let WinRestoreKit close every File Explorer process, and File Explorer could not be started again.";
                    break;
                default:
                    sentence = "File Explorer could not be started again.";
                    break;
            }

            return Error == null ? sentence : sentence + " " + Error;
        }

        private string DescribeNotAttempted()
        {
            switch (Close)
            {
                case CloseResult.NotRunning:
                    return "File Explorer was not running and was not started.";
                case CloseResult.Exited:
                    return "File Explorer was closed but no restart was attempted.";
                case CloseResult.StillRunning:
                    return "File Explorer did not close, so it was not restarted.";
                case CloseResult.AccessDenied:
                    return "Windows would not let WinRestoreKit close File Explorer, so it was not restarted.";
                default:
                    return "File Explorer was not restarted.";
            }
        }
    }
}
