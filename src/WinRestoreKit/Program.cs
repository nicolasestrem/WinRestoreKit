using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Views;

namespace WinRestoreKit
{
    internal static class Program
    {

        /// <summary>
        /// Builds the text shown when the app fails before its window exists.
        /// </summary>
        /// <remarks>
        /// Plain concatenation and total on a null argument: this runs on the way out of a startup
        /// failure, so a NullReferenceException or a FormatException raised while DESCRIBING the
        /// first failure would replace the only diagnostic the user is ever going to see. The
        /// exception's own message is included verbatim - it goes to a MessageBox, which unlike
        /// LogHelper has no Console.WriteLine fallback, so a brace in the text must not be
        /// interpreted as a format placeholder.
        /// </remarks>
        internal static string DescribeStartupFailure(Exception ex)
        {
            if (ex == null)
                return "WinRestoreKit could not start. No exception details are available.";

            return "WinRestoreKit could not start." + Environment.NewLine + Environment.NewLine +
                   ex.GetType().FullName + ": " + (ex.Message ?? string.Empty);
        }

        /// <summary>
        /// Hands the engine the UI it cannot reference itself.
        /// </summary>
        /// <remarks>
        /// Engine code does not depend on WinForms; the few places where it genuinely needs to reach
        /// a human do it through a delegate the app fills in here. This runs as the first statement
        /// of Main, before any form, timer or worker thread exists, because an unregistered seam is
        /// silent (a link failure logs but shows nothing) or fails closed (the app restore dialog
        /// reports Failed) - both correct in a headless process, both wrong in this one.
        ///
        /// Neither delegate may throw: the callers are a timer thread and a thread-pool restore.
        /// MessageBox.Show can fail on locked-down machines, so both call sites keep their own
        /// catch-all around this.
        /// </remarks>
        private static void RegisterUiSeams()
        {
            Utils.UrlFailureUi = (url, ex) =>
                MessageBox.Show(
                    $"Could not open this link in your browser:\n\n{url}\n\n{ex.Message}",
                    "Unable to open link",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

            // AppStoreApps' owner-aware RestoreAsync deliberately returns a completed Task so this
            // runs on the caller's STA thread rather than an MTA pool thread - see its remarks.
            //
            // ShowDialog, unlike Show, does NOT dispose the form when it closes - it keeps the
            // instance alive so the caller can still read its state, which is why this one needs
            // disposing by hand. The old call site (AppStoreApps.Restore before Phase 4 PR 2)
            // never did, so every restore of that module leaked the form's window handle and
            // every GDI object on it for the life of the process. Reaching the same dialog again
            // from a later restore in the same session leaked another.
            Conf.AppStoreApps.RestoreDialog = (sourcePath, owner) =>
            {
                using (RestAppsForm restoreApps = new RestAppsForm(sourcePath))
                {
                    restoreApps.ShowDialog((IWin32Window)owner);
                }
            };
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            RegisterUiSeams();
            Theme.Initialize();

            // PerMonitorV2, and it MUST be the first Application call - SetHighDpiMode is ignored
            // once the first window or visual-styles call has fixed the process DPI awareness. The
            // matching <dpiAware> element was removed from app.manifest in the same PR: a manifest
            // DPI setting is authoritative and would silently override this.
            //
            // Phase 4 PRs 6-9 replaced every absolute Location/Size in the app with
            // TableLayoutPanel/Dock/AutoSize first. Absolute positions do not survive a
            // WM_DPICHANGED rescale, so flipping before the containers landed would have produced
            // breakage that looked like a DPI bug and was really the 2023 layout.
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // The MainForm constructor is evaluated as the ARGUMENT to Application.Run, so it runs
            // BEFORE the message pump starts. WinForms' ThreadExceptionDialog only catches
            // exceptions that surface inside the pump, and nothing in this tree registers
            // Application.ThreadException or AppDomain.UnhandledException - so without this catch a
            // throw during construction escapes to the CLR unhandled path and the process is torn
            // down by WER with no dialog, no log and no window. The user just sees nothing happen.
            //
            // Application.SetUnhandledExceptionMode is deliberately NOT called here. Leaving it at
            // the default (CatchException) means in-pump exceptions are absorbed by WinForms before
            // they can reach this frame, which is what keeps this handler scoped to startup rather
            // than silently becoming the app's global exception policy.
            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    DescribeStartupFailure(ex),
                    "WinRestoreKit",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                // Rethrown rather than swallowed or turned into Environment.Exit: the rethrow is
                // what leaves the Windows Error Reporting / Event Log record with the real stack
                // in it, which is the only artifact anyone can diagnose from after the fact.
                throw;
            }
        }
    }
}
