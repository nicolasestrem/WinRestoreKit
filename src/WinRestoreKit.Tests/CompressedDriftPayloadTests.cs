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
    public sealed class CompressedDriftPayloadTests : IDisposable
    {
        private const string RegistryHeader = "Windows Registry Editor Version 5.00";
        private readonly string _directory;

        public CompressedDriftPayloadTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "WinRestoreKit.CompressedDriftPayloadTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [Fact]
        public void DetectDrift_CompressedPayloadWithLargeIrrelevantFile_ExtractsOnlyRegistryArtifacts()
        {
            string backupPath = Path.Combine(_directory, "backup");
            Directory.CreateDirectory(backupPath);
            File.WriteAllText(Path.Combine(backupPath, "RegistryArtifact.reg"), Registry(1), Encoding.Unicode);
            using (FileStream irrelevant = new FileStream(Path.Combine(backupPath, "irrelevant.bin"), FileMode.CreateNew, FileAccess.Write))
                irrelevant.SetLength(16 * 1024 * 1024);

            string liveArtifact = Path.Combine(_directory, "live.reg");
            File.WriteAllText(liveArtifact, Registry(2), Encoding.Unicode);
            Assert.True(BackupPayload.TryArchive(backupPath, SnapshotCompression.Fast, out string archiveError), archiveError);

            RegistryArtifactModule module = new RegistryArtifactModule(liveArtifact);
            IReadOnlyList<DriftItem> drifted = Detect(new BackupFolder(backupPath), module, out string unavailableReason);

            Assert.Single(drifted);
            Assert.Null(unavailableReason);
            Assert.False(module.SawIrrelevantArtifact);
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

            public bool SawIrrelevantArtifact { get; private set; }

            public override bool? HasDriftedFrom(string backupPath)
            {
                SawIrrelevantArtifact = File.Exists(Path.Combine(backupPath, "irrelevant.bin"));
                bool? same = RegFile.HasSameCanonicalContent(
                    Path.Combine(backupPath, "RegistryArtifact.reg"),
                    _liveArtifact);
                return same.HasValue ? !same.Value : null;
            }
        }
    }
}
