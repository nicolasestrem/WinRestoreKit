using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Views;

namespace WinRestoreKit
{
    internal static class Program
    {
        /// <summary>
        /// Get app version
        /// </summary>
        /// <remarks>
        /// Reads AssemblyFileVersion directly, which is the exact attribute Data.ParseLatestVersion
        /// scrapes out of the remote Properties/AssemblyInfo.cs. The two are expected to DIFFER, on
        /// any install running an older build: that gap is the update. The point of using the same
        /// attribute on both sides is that a difference is then always a real version difference,
        /// never an artefact of comparing two different fields.
        ///
        /// That holds only for the update check's FALLBACK. Its primary source is the GitHub
        /// release tag, a separate hand-entered value that never reads this attribute, so a
        /// difference there can equally well be a mistyped tag. Only the release process keeps the
        /// tag, this attribute and main in step. See .claude/skills/release/SKILL.md.
        /// Application.ProductVersion is deliberately not used
        /// as the primary source: on .NET 5+ it prefers AssemblyInformationalVersion, which the SDK may
        /// decorate with a "+&lt;commit-sha&gt;" suffix that would make new Version(...) throw.
        /// </remarks>
        internal static string GetCurrentVersionTostring()
            => NormalizeVersion(
                typeof(Program).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
                ?? Application.ProductVersion);

        /// <summary>
        /// Reduces a raw version string to the three-part form used throughout the app.
        /// </summary>
        /// <remarks>
        /// This runs during MainForm construction, so it must not throw: an exception here is a
        /// startup crash with no UI to report it. Every input that cannot be reduced to three
        /// components is therefore passed through unchanged rather than parsed.
        ///
        /// Deliberately NOT substituted with a realistic-looking placeholder like "0.0.0" on
        /// failure. Any unusable version makes the update check offer a phantom update - that much
        /// is unavoidable here, since the comparison in UpdateCheck.CheckForUpdatesAsync is a
        /// string ==. What the
        /// choice buys is diagnosis: "unknown" in the title bar says the app cannot determine its
        /// own version, whereas "0.0.0" reads as a real installed version and sends whoever
        /// investigates the repeating update prompt looking in the wrong place entirely.
        /// </remarks>
        internal static string NormalizeVersion(string rawVersion)
        {
            if (string.IsNullOrWhiteSpace(rawVersion))
                return UnknownVersion;

            string raw = rawVersion.Trim();

            // Strip any SemVer build/prerelease suffix ("1.2.3+sha", "1.2.3-preview") before parsing.
            int suffix = raw.IndexOfAny(new[] { '+', '-' });
            string candidate = suffix >= 0 ? raw.Substring(0, suffix) : raw;

            // Build is -1 when fewer than three components were supplied, and ToString(3) throws on
            // those - so "1.2" has to fail this check, not just parse successfully.
            return Version.TryParse(candidate, out Version parsed) && parsed.Build >= 0
                ? parsed.ToString(3)
                : raw;
        }

        /// <summary>
        /// Shown when the assembly carries no usable version at all. Chosen so it cannot be mistaken
        /// for a real version number by a user reading the title bar.
        /// </summary>
        internal const string UnknownVersion = "unknown";

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

            // AppStoreApps.RestoreAsync deliberately returns a completed Task so this runs on the
            // caller's STA thread rather than an MTA pool thread - see the remarks there.
            //
            // ShowDialog, unlike Show, does NOT dispose the form when it closes - it keeps the
            // instance alive so the caller can still read its state, which is why this one needs
            // disposing by hand. The old call site (AppStoreApps.Restore before Phase 4 PR 2)
            // never did, so every restore of that module leaked the form's window handle and
            // every GDI object on it for the life of the process. Reaching the same dialog again
            // from a later restore in the same session leaked another.
            Conf.AppStoreApps.RestoreDialog = () =>
            {
                using (RestAppsForm restoreApps = new RestAppsForm())
                {
                    restoreApps.ShowDialog();
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
