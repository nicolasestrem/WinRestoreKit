using Conf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using WinRestoreKit;
using Views;
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
        public void WinFormsCloseRulesRemainPresentationSpecific()
        {
            Assert.True(RestAppsForm.ShouldDeferClose(true, CloseReason.UserClosing));
            Assert.False(RestAppsForm.ShouldDeferClose(true, CloseReason.ApplicationExitCall));
            Assert.False(RestAppsForm.CanOwnADialog(false, false, false));
            Assert.Contains("timeout", RestAppsForm.StoppingText, StringComparison.OrdinalIgnoreCase);
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
    }
}
