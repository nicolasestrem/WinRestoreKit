using System;

namespace WinRestoreKit
{
    /// <summary>
    /// Builds the text shown when the app fails before its window exists.
    /// </summary>
    /// <remarks>
    /// Pure composition with no UI dependency, so it lives in the framework-neutral Application
    /// layer and the shell (WinForms or WPF) wraps its startup construction in a try/catch that
    /// shows this text and rethrows. The rethrow is what leaves the WER / Event Log record with the
    /// real stack; the text is the only artifact the user reads.
    /// </remarks>
    internal static class StartupDiagnostics
    {
        /// <summary>
        /// Plain concatenation and a total result on a null argument: this runs on the way out of a
        /// startup failure, so a NullReferenceException or FormatException raised while DESCRIBING the
        /// first failure would replace the only diagnostic the user is ever going to see. The
        /// exception's own message is included verbatim - it goes to a MessageBox, which (unlike
        /// LogHelper) has no Console.WriteLine fallback, so a brace in the text must not be read as a
        /// format placeholder.
        /// </summary>
        internal static string DescribeStartupFailure(Exception ex)
        {
            if (ex == null)
                return "WinRestoreKit could not start. No exception details are available.";

            return "WinRestoreKit could not start." + Environment.NewLine + Environment.NewLine +
                   ex.GetType().FullName + ": " + (ex.Message ?? string.Empty);
        }
    }
}
