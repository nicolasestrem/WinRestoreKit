using Conf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WinRestoreKit.Wpf.ViewModels;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class AppRestoreServiceTests
    {
        [Fact]
        public void BuildSources_SelectedSourcePrecedesDistinctRestorableCatalogEntries()
        {
            string selected = Path.Combine(Path.GetTempPath(), "app-restore-selected-" + Guid.NewGuid().ToString("N"));
            string alternate = Path.Combine(Path.GetTempPath(), "app-restore-other-" + Guid.NewGuid().ToString("N"));
            string duplicate = selected.ToUpperInvariant();
            var snapshots = new SnapshotEvent[]
            {
                new(SnapshotEventKind.Failed, DateTime.UtcNow, "failed", "C:\\failed", "failure", "", 0, false, null),
                new(SnapshotEventKind.Verified, DateTime.UtcNow, "alternate", alternate, "", "", 0, true, null),
                new(SnapshotEventKind.Partial, DateTime.UtcNow, "duplicate", duplicate, "", "", 0, true, null),
                new(SnapshotEventKind.Unreadable, DateTime.UtcNow, "unreadable", "C:\\unreadable", "error", "", 0, false, null)
            };

            IReadOnlyList<AppRestoreSource> sources = AppRestoreService.BuildSources(selected, snapshots);

            Assert.Equal(2, sources.Count);
            Assert.True(sources[0].IsSelectedRestoreSource);
            Assert.True(sources[0].IsPreparedPayload);
            Assert.Equal("Selected restore source", sources[0].ToString());
            Assert.Equal(Path.GetFullPath(selected), sources[0].Path, StringComparer.OrdinalIgnoreCase);
            Assert.False(sources[1].IsPreparedPayload);
            Assert.Equal(Path.GetFullPath(alternate), sources[1].Path, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void ReadFromSource_PreparedPayloadDoesNotTryToExtractItAgain()
        {
            string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
                Guid.NewGuid().ToString("N"))).FullName;
            try
            {
                File.WriteAllText(AppStoreApps.ExportPathIn(root),
                    "{\"Sources\":[{\"Packages\":[{\"PackageIdentifier\":\"A.One\"}]}]}");
                File.WriteAllText(Path.Combine(root, BackupManifest.FileName), BackupManifest.Compose(
                    new BackupBase[] { new AppStoreApps() },
                    new[] { ModuleResult.Aggregate(new[] { StepResult.Succeeded("Apps", "captured") }) },
                    DateTime.UtcNow, "TEST-PC", "tester", "test-os", "0.0.1",
                    compression: SnapshotCompression.Fast, payloadFile: BackupPayload.FileName));

                AppRestoreSource prepared = new AppRestoreSource(
                    root, "Prepared payload", true, isPreparedPayload: true);
                AppRestoreSource archiveRoot = new AppRestoreSource(
                    root, "Archive root", false, isPreparedPayload: false);

                AppExport preparedExport = AppRestoreService.ReadFromSourceEntry(prepared);
                AppExport archiveExport = AppRestoreService.ReadFromSourceEntry(archiveRoot);

                Assert.Equal(AppExportState.Ok, preparedExport.State);
                Assert.Equal(new[] { "A.One" }, preparedExport.PackageIdentifiers);
                Assert.Equal(AppExportState.Unreadable, archiveExport.State);
                Assert.Contains("payload", archiveExport.Message, StringComparison.OrdinalIgnoreCase);

                var viewModel = new AppRestoreDialogViewModel(root, Array.Empty<SnapshotEvent>());
                Assert.Equal("Selected restore source", viewModel.SelectedSource.ToString());
                Assert.Equal("A.One", Assert.Single(viewModel.Packages).Identifier);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void ReadFromSource_MissingExportIsAbsentButMalformedExportIsUnreadable()
        {
            string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).FullName;
            try
            {
                AppExport missing = AppRestoreService.ReadFromSource(root);
                Assert.Equal(AppExportState.Absent, missing.State);

                File.WriteAllText(AppStoreApps.ExportPathIn(root), "{not json");
                AppExport malformed = AppRestoreService.ReadFromSource(root);
                Assert.Equal(AppExportState.Unreadable, malformed.State);
                Assert.True(malformed.IsProblem);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void ReadFromSource_PreservesNonblankPackageIdentifiersInFileOrder()
        {
            string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).FullName;
            try
            {
                File.WriteAllText(AppStoreApps.ExportPathIn(root),
                    "{\"Sources\":[{\"Packages\":[{\"PackageIdentifier\":\"A.One\"},{},{\"PackageIdentifier\":\"\"},{\"PackageIdentifier\":\"B.Two\"}]}]}");

                AppExport export = AppRestoreService.ReadFromSource(root);

                Assert.Equal(AppExportState.Ok, export.State);
                Assert.Equal(new[] { "A.One", "B.Two" }, export.PackageIdentifiers);
                Assert.True(AppRestoreService.ComposeListState(export).InstallEnabled);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task InstallAsync_StopRequestDoesNotStartTheRemainingPackages()
        {
            int started = 0;
            AppRestoreOutcome outcome = await AppRestoreService.InstallAsync(
                new[] { "A.One", "B.Two" },
                _ =>
                {
                    started++;
                    return Task.FromResult(ProcessOutcome.Ran(0));
                },
                () => started == 1);

            Assert.Equal("Stopped", outcome.Caption);
            Assert.Equal("Stopped after 1 of 2 app(s). The remaining 1 were not started.\n\nwinget installed 1 app(s).", outcome.Text);
            Assert.Equal(RunSeverity.Information, outcome.Severity);
            Assert.Equal(1, started);
        }

        [Fact]
        public void Describe_StartedOutcomeUnknownIsNotReportedAsNeverStarted()
        {
            string description = AppRestoreService.Describe(ProcessOutcome.OutcomeUnknown("pipe closed"));

            Assert.Contains("could not be determined", description);
            Assert.DoesNotContain("could not run winget", description);
        }
    }
}
