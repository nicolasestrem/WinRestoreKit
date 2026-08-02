using Conf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
// CloseReason only. Naming an enum member does not create a window or a message loop.
using System.Windows.Forms;
using Views;
using Xunit;

namespace WinRestoreKit.Tests
{
    /// <summary>
    /// Covers the app restore dialog's pure surfaces: the export filename, the reader, the list
    /// state, and the end-of-run summary.
    /// </summary>
    /// <remarks>
    /// No test constructs RestAppsForm. Constructing it runs InitializeComponent and LoadBackups,
    /// which opens a window and can raise a modal MessageBox - in a test run that is a message loop
    /// nobody will ever close. Everything asserted here is deliberately internal static so it can be
    /// reached without one, following the precedent set by RestAppsForm.Describe.
    ///
    /// Everything here also runs unelevated and never invokes winget.
    /// </remarks>
    public class AppRestoreDialogTests
    {
        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "acapps_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string ExportWith(params string[] ids)
        {
            StringBuilder packages = new StringBuilder();

            for (int i = 0; i < ids.Length; i++)
            {
                if (i > 0)
                    packages.Append(',');

                packages.Append("{\"PackageIdentifier\":\"").Append(ids[i]).Append("\"}");
            }

            return "{\"Sources\":[{\"Packages\":[" + packages + "]}]}";
        }

        // ---- the dialog seam -------------------------------------------------------------

        /// <summary>
        /// Swaps AppStoreApps.RestoreDialog for the duration of a test and puts it back.
        /// </summary>
        /// <remarks>
        /// The hook is static, exactly like LogHelper's sink, and the same hazard applies: one left
        /// behind here outlives the test that set it. A leaked hook would make every later test that
        /// restores this module run someone else's lambda.
        /// </remarks>
        private sealed class DialogHook : IDisposable
        {
            private readonly Action previous;

            public DialogHook(Action replacement)
            {
                previous = AppStoreApps.RestoreDialog;
                AppStoreApps.RestoreDialog = replacement;
            }

            public void Dispose() => AppStoreApps.RestoreDialog = previous;
        }

        // The judgement call this module's restore turns on. Skipped is already TRUE here when a
        // dialog ran - "the dialog took it from here" - so reusing it when no dialog exists would
        // make a total no-op read exactly like the ordinary success path.
        [Fact]
        public void Restore_WithNoDialogRegistered_FailsRatherThanClaimingSkipped()
        {
            using (new DialogHook(null))
            {
                ModuleResult result = new AppStoreApps().Restore(NewTempDir());

                Assert.Equal(ResultState.Failed, result.State);
                Assert.Contains("restore dialog", result.Reason, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void Restore_WithADialogRegistered_OpensItOnceAndReportsSkipped()
        {
            int opened = 0;

            using (new DialogHook(() => opened++))
            {
                ModuleResult result = new AppStoreApps().Restore(NewTempDir());

                Assert.Equal(1, opened);
                Assert.Equal(ResultState.Skipped, result.State);
                Assert.Equal("handled interactively in the app restore dialog",
                    result.Steps[0].Reason, StringComparer.Ordinal);
            }
        }

        // RestoreAsync returns a completed Task on purpose so the dialog opens on the caller's STA
        // thread rather than an MTA pool thread. Nothing pinned that until now, and the seam made it
        // observable without constructing a window.
        [Fact]
        public void RestoreAsync_RunsTheDialogOnTheCallersThread()
        {
            int dialogThread = 0;

            using (new DialogHook(() => dialogThread = Environment.CurrentManagedThreadId))
            {
                System.Threading.Tasks.Task<ModuleResult> task =
                    new AppStoreApps().RestoreAsync(NewTempDir());

                Assert.True(task.IsCompleted);
                Assert.Equal(Environment.CurrentManagedThreadId, dialogThread);
            }
        }

        // ---- the filename ----------------------------------------------------------------

        // Ordinal on purpose. This name existed in four spellings at once, three of which differed
        // only in case; a case-insensitive assertion would pass on every one of them and so would
        // reproduce the very bug it is here to prevent.
        [Fact]
        public void ExportFileName_IsTheModuleTitlePlusALowercaseJsonExtension()
        {
            Assert.Equal("Remember installed apps.json", AppStoreApps.ExportFileName, StringComparer.Ordinal);
            Assert.Equal(AppStoreApps.ModuleTitle + ".json", AppStoreApps.ExportFileName, StringComparer.Ordinal);
            Assert.EndsWith(".json", AppStoreApps.ExportFileName, StringComparison.Ordinal);
        }

        [Fact]
        public void ModuleTitle_IsTheTitleTheModuleActuallyCarries()
            => Assert.Equal(AppStoreApps.ModuleTitle, new AppStoreApps().Title, StringComparer.Ordinal);

        // The producer's path and the dialog's path must name ONE file. Written through the
        // producer, found through the reader - which is the round trip that was broken.
        [Fact]
        public void ProducerPathAndDialogPath_ResolveToTheSameFile()
        {
            string backupName = "acapps_" + Guid.NewGuid().ToString("N");
            string backupDir = Path.Combine(DataHelper.Data.DataRootDir, backupName);

            Directory.CreateDirectory(backupDir);

            try
            {
                string written = AppStoreApps.ExportPathIn(backupDir);
                File.WriteAllText(written, ExportWith("Microsoft.PowerToys"));

                string read = RestAppsForm.ExportPathFor(backupName);

                Assert.True(File.Exists(read), "the dialog's path did not find the file the module wrote: " + read);
                Assert.Equal(Path.GetFullPath(written), Path.GetFullPath(read), StringComparer.OrdinalIgnoreCase);

                RestAppsForm.AppExport export = RestAppsForm.AppExport.Read(read);

                Assert.Equal(RestAppsForm.AppExportState.Ok, export.State);
                Assert.Equal(new[] { "Microsoft.PowerToys" }, export.PackageIdentifiers);
            }
            finally
            {
                try { Directory.Delete(backupDir, true); } catch { }
            }
        }

        // ---- Parse -----------------------------------------------------------------------

        [Fact]
        public void Parse_ReturnsIdentifiersInFileOrder()
        {
            RestAppsForm.AppExport export = RestAppsForm.AppExport.Parse(
                ExportWith("A.One", "B.Two", "C.Three"), "x.json");

            Assert.Equal(RestAppsForm.AppExportState.Ok, export.State);
            Assert.Equal(new[] { "A.One", "B.Two", "C.Three" }, export.PackageIdentifiers);
        }

        // A machine with no winget packages is a fact, not a fault.
        [Fact]
        public void Parse_AnEmptyPackagesArray_IsNotAProblem()
        {
            RestAppsForm.AppExport export = RestAppsForm.AppExport.Parse(ExportWith(), "x.json");

            Assert.Equal(RestAppsForm.AppExportState.Ok, export.State);
            Assert.False(export.IsProblem);
            Assert.Empty(export.PackageIdentifiers);
        }

        // A missing Packages array is not the same as an empty one: that file restores nothing.
        [Fact]
        public void Parse_NoPackagesArray_IsAProblemAndSaysSo()
        {
            RestAppsForm.AppExport export = RestAppsForm.AppExport.Parse("{\"Sources\":[{}]}", "x.json");

            Assert.Equal(RestAppsForm.AppExportState.Unreadable, export.State);
            Assert.True(export.IsProblem);
            Assert.Contains("no list of packages", export.Message);
        }

        [Fact]
        public void Parse_InvalidJson_SaysSoAndDoesNotThrow()
        {
            RestAppsForm.AppExport export = RestAppsForm.AppExport.Parse("{not json at all", "x.json");

            Assert.Equal(RestAppsForm.AppExportState.Unreadable, export.State);
            Assert.Contains("not valid JSON", export.Message);
            Assert.Empty(export.PackageIdentifiers);
        }

        // A blank row is tickable, and a ticked blank row reaches winget install --id "".
        [Fact]
        public void Parse_EntriesWithNoPackageIdentifier_AreSkipped()
        {
            RestAppsForm.AppExport export = RestAppsForm.AppExport.Parse(
                "{\"Sources\":[{\"Packages\":[{\"PackageIdentifier\":\"A.One\"},{},{\"PackageIdentifier\":\"\"},{\"PackageIdentifier\":\"B.Two\"}]}]}",
                "x.json");

            Assert.Equal(RestAppsForm.AppExportState.Ok, export.State);
            Assert.Equal(new[] { "A.One", "B.Two" }, export.PackageIdentifiers);
        }

        // ---- Read ------------------------------------------------------------------------

        // Absent is the ORDINARY case: this dialog is reachable standalone, and most backup folders
        // contain no app export at all. It must not raise a problem.
        [Fact]
        public void Read_MissingFile_IsAbsentAndNamesThePath()
        {
            string dir = NewTempDir();

            try
            {
                string path = AppStoreApps.ExportPathIn(dir);
                RestAppsForm.AppExport export = RestAppsForm.AppExport.Read(path);

                Assert.Equal(RestAppsForm.AppExportState.Absent, export.State);
                Assert.False(export.IsProblem);
                Assert.Contains(path, export.Message);
                Assert.Empty(export.PackageIdentifiers);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void Read_AGoodFile_IsOk()
        {
            string dir = NewTempDir();

            try
            {
                string path = AppStoreApps.ExportPathIn(dir);
                File.WriteAllText(path, ExportWith("A.One"));

                RestAppsForm.AppExport export = RestAppsForm.AppExport.Read(path);

                Assert.Equal(RestAppsForm.AppExportState.Ok, export.State);
                Assert.Equal(new[] { "A.One" }, export.PackageIdentifiers);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void Read_AnInvalidFile_IsAProblem()
        {
            string dir = NewTempDir();

            try
            {
                string path = AppStoreApps.ExportPathIn(dir);
                File.WriteAllText(path, "{not json at all");

                RestAppsForm.AppExport export = RestAppsForm.AppExport.Read(path);

                Assert.Equal(RestAppsForm.AppExportState.Unreadable, export.State);
                Assert.True(export.IsProblem);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // Read runs during CONSTRUCTION, before any window exists, where an escaping exception
        // terminates the process. Nothing may escape it - not a directory handed in where a file was
        // expected, not a file another handle holds open, not a nonsense path.
        [Fact]
        public void Read_IsTotal()
        {
            string dir = NewTempDir();

            try
            {
                // A directory where a file was expected.
                Assert.NotNull(RestAppsForm.AppExport.Read(dir));

                // ...and it is Unreadable, not Absent. This is the case that pins the rule: to
                // File.Exists a directory is indistinguishable from nothing at all, and the old
                // Read believed it - answering "this backup contains no app export" about a path it
                // had not been able to look inside. Absent is a claim about the folder's contents
                // and is only earned by a read that came back saying the file is not there.
                Assert.Equal(RestAppsForm.AppExportState.Unreadable,
                             RestAppsForm.AppExport.Read(dir).State);

                // A path whose CONTAINING FOLDER is not there is Unreadable too, and this is the
                // narrower half of the same rule. Only a read that opened the folder and did not
                // find the file has seen enough to call it an absence. The folder vanishing between
                // LoadBackups enumerating it and this read means a pulled drive or a dropped network
                // path - the world changing, not a backup that contains no apps.
                Assert.Equal(
                    RestAppsForm.AppExportState.Unreadable,
                    RestAppsForm.AppExport.Read(Path.Combine(dir, "gone", "apps.json")).State);

                // And the one case that IS an absence: the folder is right there, the file is not.
                Assert.Equal(
                    RestAppsForm.AppExportState.Absent,
                    RestAppsForm.AppExport.Read(Path.Combine(dir, "apps.json")).State);

                // A file locked by another handle.
                string locked = Path.Combine(dir, "locked.json");

                using (FileStream hold = new FileStream(locked, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    hold.WriteByte((byte)'{');
                    hold.Flush();

                    RestAppsForm.AppExport export = RestAppsForm.AppExport.Read(locked);

                    Assert.Equal(RestAppsForm.AppExportState.Unreadable, export.State);
                    Assert.NotNull(export.PackageIdentifiers);
                }
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }

            // Paths that are not paths, and no path at all.
            foreach (string path in new[] { null, "", "   ", "\0::invalid", "Z:\\no\\such\\place\\x.json" })
            {
                RestAppsForm.AppExport export = RestAppsForm.AppExport.Read(path);

                Assert.NotNull(export);
                Assert.NotNull(export.PackageIdentifiers);
            }
        }

        // ---- list state ------------------------------------------------------------------

        [Fact]
        public void ComposeListState_AFailedRead_YieldsAnEmptyListAndNoRestore()
        {
            RestAppsForm.ListState state = RestAppsForm.ComposeListState(
                RestAppsForm.AppExport.Unreadable("broken"));

            Assert.Empty(state.Items);
            Assert.False(state.RestoreEnabled);
        }

        [Fact]
        public void ComposeListState_AnAbsentExport_YieldsAnEmptyListAndNoRestore()
        {
            RestAppsForm.ListState state = RestAppsForm.ComposeListState(
                RestAppsForm.AppExport.Absent("nothing there"));

            Assert.Empty(state.Items);
            Assert.False(state.RestoreEnabled);
        }

        // An export that read perfectly but holds no apps still leaves nothing to install.
        [Fact]
        public void ComposeListState_AnEmptyExport_StillDisablesRestore()
        {
            RestAppsForm.ListState state = RestAppsForm.ComposeListState(
                RestAppsForm.AppExport.Parse(ExportWith(), "x.json"));

            Assert.Empty(state.Items);
            Assert.False(state.RestoreEnabled);
        }

        [Fact]
        public void ComposeListState_AnExportWithApps_EnablesRestore()
        {
            RestAppsForm.ListState state = RestAppsForm.ComposeListState(
                RestAppsForm.AppExport.Parse(ExportWith("A.One", "B.Two"), "x.json"));

            Assert.Equal(new[] { "A.One", "B.Two" }, state.Items);
            Assert.True(state.RestoreEnabled);
        }

        // ---- where a problem gets reported -----------------------------------------------

        // The dialog used to be raised straight out of ApplyExport, which runs during CONSTRUCTION:
        // LoadBackups sets SelectedIndex, the handler fires, and all of that happens before
        // AStoreApps calls ShowDialog. Passing `this` as owner forced handle creation on an unshown
        // form, so the box got an effectively invisible owner and could paint BEHIND the main
        // window - which during an orchestrated restore is disabled. Frozen app, no cause, no exit.
        [Fact]
        public void RouteProblem_BeforeTheWindowIsShown_IsDeferredRatherThanShown()
        {
            Assert.Equal(RestAppsForm.ProblemRouting.Defer,
                RestAppsForm.RouteProblem(RestAppsForm.AppExport.Unreadable("broken"), hasBeenShown: false));
        }

        // Picking a different backup from the dropdown is a live window and must still report at once.
        [Fact]
        public void RouteProblem_AfterTheWindowIsShown_IsReportedImmediately()
        {
            Assert.Equal(RestAppsForm.ProblemRouting.ShowNow,
                RestAppsForm.RouteProblem(RestAppsForm.AppExport.Unreadable("broken"), hasBeenShown: true));
        }

        // Absent must NOT be collapsed into Unreadable by the deferral. It is the ordinary case -
        // most backup folders hold no app export - so it stays log-only, shown or not.
        [Fact]
        public void RouteProblem_AnAbsentExport_IsNeverReportedAtAll()
        {
            Assert.Equal(RestAppsForm.ProblemRouting.None,
                RestAppsForm.RouteProblem(RestAppsForm.AppExport.Absent("nothing there"), hasBeenShown: false));

            Assert.Equal(RestAppsForm.ProblemRouting.None,
                RestAppsForm.RouteProblem(RestAppsForm.AppExport.Absent("nothing there"), hasBeenShown: true));
        }

        [Fact]
        public void RouteProblem_AGoodExport_IsNeverReported()
        {
            RestAppsForm.AppExport ok = RestAppsForm.AppExport.Parse(ExportWith("A.One"), "x.json");

            Assert.Equal(RestAppsForm.ProblemRouting.None, RestAppsForm.RouteProblem(ok, hasBeenShown: false));
            Assert.Equal(RestAppsForm.ProblemRouting.None, RestAppsForm.RouteProblem(ok, hasBeenShown: true));
        }

        [Fact]
        public void RouteProblem_NoExport_IsNotAProblemAndDoesNotThrow()
        {
            Assert.Equal(RestAppsForm.ProblemRouting.None, RestAppsForm.RouteProblem(null, hasBeenShown: false));
            Assert.Equal(RestAppsForm.ProblemRouting.None, RestAppsForm.RouteProblem(null, hasBeenShown: true));
        }

        // ---- which closes this dialog gets a vote on -------------------------------------

        // The handler checked only the installing flag, so it vetoed EVERY CloseReason. A mid-install
        // Application.Exit or a close of the owning MainForm was silently refused and the app looked
        // hung; WindowsShutDown was refused until Windows overrode it, having first shown the user a
        // blocked-shutdown screen naming Appcopier.
        [Fact]
        public void ShouldDeferClose_OnlyAUserClosingThisDialogIsHeldBack()
        {
            Assert.True(RestAppsForm.ShouldDeferClose(installing: true, CloseReason.UserClosing));

            foreach (CloseReason reason in new[]
            {
                CloseReason.ApplicationExitCall,
                CloseReason.FormOwnerClosing,
                CloseReason.MdiFormClosing,
                CloseReason.WindowsShutDown,
                CloseReason.TaskManagerClosing,
                CloseReason.None
            })
            {
                Assert.False(RestAppsForm.ShouldDeferClose(installing: true, reason),
                    "a close this dialog does not get a vote on was vetoed: " + reason);
            }
        }

        // With no install running there is nothing to wait for, whoever is closing.
        [Fact]
        public void ShouldDeferClose_WhenNotInstalling_NothingIsEverHeldBack()
        {
            foreach (CloseReason reason in Enum.GetValues<CloseReason>())
                Assert.False(RestAppsForm.ShouldDeferClose(installing: false, reason));
        }

        /// <summary>
        /// A closed-but-not-disposed form cannot own the install summary.
        /// </summary>
        /// <remarks>
        /// This is the state both callers actually leave the form in. They open it with ShowDialog
        /// and neither disposes it, and ShowDialog does not dispose on its own - Close on a modal
        /// form hides it - so after any close ShouldDeferClose does not hold back, the window is
        /// invisible while IsDisposed and Disposing are both still false. A guard testing only
        /// disposal would pass here and raise a modal owned by an invisible window, which is the
        /// hidden-dialog defect rather than the crash, and just as silent.
        /// </remarks>
        [Fact]
        public void CanOwnADialog_AHiddenFormCannot_EvenWhenItIsNotDisposed()
        {
            Assert.True(RestAppsForm.CanOwnADialog(isDisposed: false, disposing: false, visible: true));

            Assert.False(RestAppsForm.CanOwnADialog(isDisposed: false, disposing: false, visible: false));
            Assert.False(RestAppsForm.CanOwnADialog(isDisposed: true, disposing: false, visible: true));
            Assert.False(RestAppsForm.CanOwnADialog(isDisposed: false, disposing: true, visible: true));
        }

        // stopRequested is only read BETWEEN packages, so a wedged winget holds this dialog until
        // RunWingetAsync's timeout kills it. The caption has to say the wait ends either way.
        [Fact]
        public void StoppingText_SaysTheWaitIsBoundedByATimeout()
        {
            Assert.Contains("timeout", RestAppsForm.StoppingText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("current app", RestAppsForm.StoppingText, StringComparison.OrdinalIgnoreCase);
        }

        // ---- outcome composition ---------------------------------------------------------

        [Fact]
        public void ComposeOutcome_NothingTicked_SaysNothingWasInstalled()
        {
            RestAppsForm.OutcomeMessage m = RestAppsForm.ComposeOutcome(0, 0, new string[0]);

            Assert.Contains("No apps were ticked", m.Text);
            Assert.DoesNotContain("did not install", m.Text);
        }

        [Fact]
        public void ComposeOutcome_AllInstalled_SaysInstalledNotRestored()
        {
            RestAppsForm.OutcomeMessage m = RestAppsForm.ComposeOutcome(3, 3, new string[0]);

            Assert.Contains("winget installed 3 app(s)", m.Text);

            // "restored" would be a claim nothing here verified: an already-present package is
            // upgraded to the current version, and no version is pinned.
            Assert.DoesNotContain("restored", m.Text);
        }

        [Fact]
        public void ComposeOutcome_SomeFailed_NamesEveryFailure()
        {
            List<string> failures = new List<string>
            {
                "A.One: winget exited with code 1",
                "B.Two: could not run winget: not installed"
            };

            RestAppsForm.OutcomeMessage m = RestAppsForm.ComposeOutcome(4, 4, failures);

            Assert.Contains("2 of 4 app(s) did not install", m.Text);

            foreach (string failure in failures)
                Assert.Contains(failure, m.Text);
        }

        // Stopping after 2 of 10 leaves 8 that were never attempted. Calling those failures would be
        // a false alarm; folding them into the attempted count would hide what actually happened.
        [Fact]
        public void ComposeOutcome_StoppedEarly_SeparatesNotStartedFromFailed()
        {
            RestAppsForm.OutcomeMessage m = RestAppsForm.ComposeOutcome(10, 2, new string[0]);

            Assert.Contains("2 of 10", m.Text);
            Assert.Contains("not started", m.Text);
            Assert.DoesNotContain("did not install", m.Text);
        }

        [Fact]
        public void ComposeOutcome_StoppedEarlyWithAFailure_KeepsBothFacts()
        {
            RestAppsForm.OutcomeMessage m = RestAppsForm.ComposeOutcome(
                10, 3, new[] { "A.One: winget exited with code 1" });

            // The stop.
            Assert.Contains("3 of 10", m.Text);
            Assert.Contains("not started", m.Text);

            // And the failure, which must not be swallowed by the stop.
            Assert.Contains("A.One: winget exited with code 1", m.Text);
            Assert.Contains("1 of the 3 attempted failed", m.Text);
        }

        [Fact]
        public void ComposeOutcome_NullFailures_IsTreatedAsNone()
        {
            RestAppsForm.OutcomeMessage m = RestAppsForm.ComposeOutcome(2, 2, null);

            Assert.Contains("winget installed 2 app(s)", m.Text);
        }

        // ---- the producer's verify ladder ------------------------------------------------

        // AStoreApps.Verify had no test at all. This covers the mapping, not winget.
        [Fact]
        public void Verify_NamesEveryWayTheExportCanFail()
        {
            string missing = Path.Combine(NewTempDir(), "nope.json");

            Assert.Equal(ResultState.Failed, AppStoreApps.Verify(null, missing).State);
            Assert.Equal(ResultState.Failed, AppStoreApps.Verify(ProcessOutcome.NeverStarted("no winget"), missing).State);
            Assert.Equal(ResultState.Failed, AppStoreApps.Verify(ProcessOutcome.Timeout(), missing).State);
            Assert.Equal(ResultState.Failed, AppStoreApps.Verify(ProcessOutcome.OutcomeUnknown("pipe closed"), missing).State);
            Assert.Equal(ResultState.Failed, AppStoreApps.Verify(ProcessOutcome.Ran(1), missing).State);

            // Exit code 0 is not evidence: winget exits 0 having written nothing when it has no
            // source configured.
            Assert.Equal(ResultState.Failed, AppStoreApps.Verify(ProcessOutcome.Ran(0), missing).State);
        }

        [Fact]
        public void Verify_AGoodExport_Succeeds()
        {
            string dir = NewTempDir();

            try
            {
                string path = AppStoreApps.ExportPathIn(dir);
                File.WriteAllText(path, ExportWith("A.One", "B.Two"));

                StepResult r = AppStoreApps.Verify(ProcessOutcome.Ran(0), path);

                Assert.Equal(ResultState.Succeeded, r.State);
                Assert.Contains("2", r.Reason);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // A file with no Packages array restores nothing, so a green tick on it would be a lie.
        [Fact]
        public void Verify_AnExportWithNoPackagesArray_Fails()
        {
            string dir = NewTempDir();

            try
            {
                string path = AppStoreApps.ExportPathIn(dir);
                File.WriteAllText(path, "{\"Sources\":[{}]}");

                Assert.Equal(ResultState.Failed, AppStoreApps.Verify(ProcessOutcome.Ran(0), path).State);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
