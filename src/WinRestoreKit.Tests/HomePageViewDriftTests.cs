using Conf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Views;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class HomePageViewDriftTests : IDisposable
    {
        private const string RegistryHeader = "Windows Registry Editor Version 5.00";
        private readonly string _directory;

        public HomePageViewDriftTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "WinRestoreKit.HomePageViewDriftTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [Fact]
        public void DetectDrift_CompressedRegistryArtifactChanged_ReportsDriftAndDisposesPreparedPayload()
        {
            string backupPath = Path.Combine(_directory, "backup");
            Directory.CreateDirectory(backupPath);
            string capturedArtifact = Path.Combine(backupPath, "RegistryArtifact.reg");
            string liveArtifact = Path.Combine(_directory, "live.reg");
            File.WriteAllText(capturedArtifact, Registry(1), Encoding.Unicode);
            File.WriteAllText(liveArtifact, Registry(2), Encoding.Unicode);

            Assert.True(BackupPayload.TryArchive(backupPath, SnapshotCompression.Fast, out string archiveError), archiveError);
            Assert.True(File.Exists(Path.Combine(backupPath, BackupPayload.FileName)));
            Assert.False(File.Exists(capturedArtifact));

            RegistryArtifactModule module = new RegistryArtifactModule(liveArtifact);
            IReadOnlyList<DriftItem> drifted = Detect(new BackupFolder(backupPath), module, out string unavailableReason);

            DriftItem item = Assert.Single(drifted);
            Assert.Equal("Registry artifact", item.Name);
            Assert.Null(unavailableReason);
            Assert.NotEqual(backupPath, module.ProbePath);
            Assert.False(Directory.Exists(module.ProbePath));
        }

        [Fact]
        public void DetectDrift_UnreadableCompressedPayload_ReportsUnavailable()
        {
            File.WriteAllText(Path.Combine(_directory, BackupPayload.FileName), "not a zip payload");

            IReadOnlyList<DriftItem> drifted = Detect(
                new BackupFolder(_directory),
                new RegistryArtifactModule(Path.Combine(_directory, "live.reg")),
                out string unavailableReason);

            Assert.Null(drifted);
            Assert.False(string.IsNullOrWhiteSpace(unavailableReason));
        }

        public void Dispose()
        {
            try { Directory.Delete(_directory, true); } catch { }
        }

        private static IReadOnlyList<DriftItem> Detect(BackupFolder folder, BackupBase module, out string unavailableReason)
        {
            MethodInfo detectDrift = typeof(HomePageView).GetMethod("DetectDrift", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(detectDrift);

            object[] arguments =
            {
                folder,
                new List<ModuleRegistration> { new ModuleRegistration(module, "Registry") },
                null
            };

            IReadOnlyList<DriftItem> drifted = (IReadOnlyList<DriftItem>)detectDrift.Invoke(null, arguments);
            unavailableReason = (string)arguments[2];
            return drifted;
        }

        private static string Registry(int value)
            => RegistryHeader + "\r\n\r\n[HKEY_CURRENT_USER\\Software\\Drift]\r\n\"Enabled\"=dword:0000000" + value + "\r\n";

        private sealed class RegistryArtifactModule : BackupBase
        {
            private readonly string _liveArtifact;

            public RegistryArtifactModule(string liveArtifact)
            {
                _liveArtifact = liveArtifact;
                Title = "Registry artifact";
            }

            public string ProbePath { get; private set; }

            public override bool? HasDriftedFrom(string backupPath)
            {
                ProbePath = backupPath;
                bool? same = RegFile.HasSameCanonicalContent(
                    Path.Combine(backupPath, "RegistryArtifact.reg"),
                    _liveArtifact);
                return same.HasValue ? !same.Value : null;
            }
        }
    }
}
