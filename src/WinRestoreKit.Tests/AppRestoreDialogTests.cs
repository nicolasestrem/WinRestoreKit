using Conf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class AppRestoreDialogTests
    {
        private static string NewTempDir()
            => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "acapps_" + Guid.NewGuid().ToString("N"))).FullName;

        private static string ExportWith(params string[] ids)
        {
            var packages = new StringBuilder();
            for (int index = 0; index < ids.Length; index++)
            {
                if (index > 0)
                    packages.Append(',');
                packages.Append("{\"PackageIdentifier\":\"").Append(ids[index]).Append("\"}");
            }
            return "{\"Sources\":[{\"Packages\":[" + packages + "]}]}";
        }

        [Fact]
        public void ReadFromSource_UsesTheProducerExportPath()
        {
            string root = NewTempDir();
            try
            {
                File.WriteAllText(AppStoreApps.ExportPathIn(root), ExportWith("Microsoft.PowerToys"));
                AppExport export = AppRestoreService.ReadFromSource(root);
                Assert.Equal(AppExportState.Ok, export.State);
                Assert.Equal(new[] { "Microsoft.PowerToys" }, export.PackageIdentifiers);
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void ReadFromSource_EmptyPackagesIsOkButMissingPackagesIsUnreadable()
        {
            string root = NewTempDir();
            try
            {
                File.WriteAllText(AppStoreApps.ExportPathIn(root), ExportWith());
                AppExport empty = AppRestoreService.ReadFromSource(root);
                Assert.Equal(AppExportState.Ok, empty.State);
                Assert.Empty(empty.PackageIdentifiers);

                File.WriteAllText(AppStoreApps.ExportPathIn(root), "{\"Sources\":[{}]}");
                AppExport missing = AppRestoreService.ReadFromSource(root);
                Assert.Equal(AppExportState.Unreadable, missing.State);
                Assert.Contains("no list of packages", missing.Message);
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void ReadFromSource_MissingExportIsAbsentAndReadFailuresAreUnreadable()
        {
            string root = NewTempDir();
            try
            {
                Assert.Equal(AppExportState.Absent, AppRestoreService.ReadFromSource(root).State);
                Assert.Equal(AppExportState.Unreadable,
                    AppRestoreService.ReadFromSource(Path.Combine(root, "gone")).State);
                Assert.Equal(AppExportState.Unreadable, AppRestoreService.ReadFromSource(null).State);
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void ReadFromSource_OmitsBlankIdentifiersInFileOrder()
        {
            string root = NewTempDir();
            try
            {
                File.WriteAllText(AppStoreApps.ExportPathIn(root),
                    "{\"Sources\":[{\"Packages\":[{\"PackageIdentifier\":\"A.One\"},{},{\"PackageIdentifier\":\"\"},{\"PackageIdentifier\":\"B.Two\"}]}]}");
                Assert.Equal(new[] { "A.One", "B.Two" },
                    AppRestoreService.ReadFromSource(root).PackageIdentifiers);
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void ComposeListState_EnablesOnlyWhenThereAreIdentifiers()
        {
            Assert.False(AppRestoreService.ComposeListState(AppExport.Absent("missing")).InstallEnabled);
            Assert.False(AppRestoreService.ComposeListState(AppExport.Unreadable("broken")).InstallEnabled);
            Assert.True(AppRestoreService.ComposeListState(AppExport.Ok(new[] { "A.One" }, "read")).InstallEnabled);
        }

        [Fact]
        public void RouteProblem_DefersOnlyUnreadableExportsBeforeTheWindowExists()
        {
            Assert.Equal(AppRestoreProblemRouting.Defer,
                AppRestoreService.RouteProblem(AppExport.Unreadable("broken"), false));
            Assert.Equal(AppRestoreProblemRouting.ShowNow,
                AppRestoreService.RouteProblem(AppExport.Unreadable("broken"), true));
            Assert.Equal(AppRestoreProblemRouting.None,
                AppRestoreService.RouteProblem(AppExport.Absent("missing"), false));
        }

        [Fact]
        public void ComposeOutcome_PreservesStoppedAndFailedFacts()
        {
            AppRestoreOutcome stopped = AppRestoreService.ComposeOutcome(10, 2, Array.Empty<string>());
            Assert.Equal(RunSeverity.Information, stopped.Severity);
            Assert.Contains("8 were not started", stopped.Text);

            AppRestoreOutcome failed = AppRestoreService.ComposeOutcome(10, 3,
                new[] { "A.One: winget exited with code 1" });
            Assert.Equal(RunSeverity.Warning, failed.Severity);
            Assert.Contains("3 of 10", failed.Text);
            Assert.Contains("A.One: winget exited with code 1", failed.Text);
        }

        [Fact]
        public void Describe_DistinguishesUnknownStartedProcessFromNeverStarted()
        {
            Assert.Contains("could not run winget", AppRestoreService.Describe(ProcessOutcome.NeverStarted("missing")));
            string unknown = AppRestoreService.Describe(ProcessOutcome.OutcomeUnknown("pipe closed"));
            Assert.Contains("could not be determined", unknown);
            Assert.DoesNotContain("could not run winget", unknown);
        }


        [Fact]
        public void RestoreDialogCoreSeamRemainsInteractive()
        {
            Action<string, object> previous = AppStoreApps.RestoreDialog;
            string root = NewTempDir();
            try
            {
                object owner = new object();
                object receivedOwner = null;
                string receivedPath = null;
                AppStoreApps.RestoreDialog = (path, suppliedOwner) =>
                {
                    receivedPath = path;
                    receivedOwner = suppliedOwner;
                };

                ModuleResult result = new AppStoreApps().Restore(root, owner);
                Assert.Equal(ResultState.Skipped, result.State);
                Assert.Same(owner, receivedOwner);
                Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(receivedPath), StringComparer.OrdinalIgnoreCase);
            }
            finally
            {
                AppStoreApps.RestoreDialog = previous;
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void Verify_NamesEveryWayTheExportCanFail()
        {
            string root = NewTempDir();
            try
            {
                string path = AppStoreApps.ExportPathIn(root);

                // An exit code is not evidence: every rung of the ladder is checked against the
                // artifact, not the exit code, and the exit code is not the only thing checked.
                AssertFailedReason(AppStoreApps.Verify(ProcessOutcome.NeverStarted("no winget"), path), "could not run");
                AssertFailedReason(AppStoreApps.Verify(ProcessOutcome.Timeout(), path), "did not finish");
                AssertFailedReason(AppStoreApps.Verify(ProcessOutcome.OutcomeUnknown("pipe closed"), path), "could not be determined");
                AssertFailedReason(AppStoreApps.Verify(ProcessOutcome.Ran(1), path), "exited with code 1");

                // Exit code 0 with nothing to show for it.
                AssertFailedReason(AppStoreApps.Verify(ProcessOutcome.Ran(0), path), "wrote no file");

                File.WriteAllText(path, "");
                AssertFailedReason(AppStoreApps.Verify(ProcessOutcome.Ran(0), path), "empty file");

                File.WriteAllText(path, "{\"Sources\":[{}]}");
                AssertFailedReason(AppStoreApps.Verify(ProcessOutcome.Ran(0), path), "no list of packages");

                File.WriteAllText(path, "not json");
                AssertFailedReason(AppStoreApps.Verify(ProcessOutcome.Ran(0), path), "not valid JSON");
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void Verify_AGoodExport_Succeeds()
        {
            string root = NewTempDir();
            try
            {
                string path = AppStoreApps.ExportPathIn(root);
                File.WriteAllText(path, ExportWith("A.One", "B.Two"));

                StepResult result = AppStoreApps.Verify(ProcessOutcome.Ran(0), path);
                Assert.Equal(ResultState.Succeeded, result.State);
                Assert.Contains("exported 2", result.Reason);
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void Restore_WithNoDialogRegistered_FailsRatherThanClaimingSkipped()
        {
            Action<string, object> previous = AppStoreApps.RestoreDialog;
            string root = NewTempDir();
            try
            {
                AppStoreApps.RestoreDialog = null;

                ModuleResult result = new AppStoreApps().Restore(root);
                Assert.Equal(ResultState.Failed, result.State);
                Assert.Contains("not available", result.Reason);
            }
            finally
            {
                AppStoreApps.RestoreDialog = previous;
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void Restore_WithNullOwner_FailsRatherThanClaimingSkipped()
        {
            Action<string, object> previous = AppStoreApps.RestoreDialog;
            string root = NewTempDir();
            try
            {
                bool dialogWasInvoked = false;
                AppStoreApps.RestoreDialog = (path, owner) => dialogWasInvoked = true;

                ModuleResult result = new AppStoreApps().Restore(root, null);
                Assert.Equal(ResultState.Failed, result.State);
                Assert.Contains("owning application window", result.Reason);
                Assert.False(dialogWasInvoked);
            }
            finally
            {
                AppStoreApps.RestoreDialog = previous;
                Directory.Delete(root, true);
            }
        }

        private static void AssertFailedReason(StepResult result, string reasonFragment)
        {
            Assert.Equal(ResultState.Failed, result.State);
            Assert.Contains(reasonFragment, result.Reason);
        }
    }
}
