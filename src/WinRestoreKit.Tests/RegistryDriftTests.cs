using Conf;
using System;
using System.IO;
using System.Text;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class RegistryDriftTests : IDisposable
    {
        private const string Header = "Windows Registry Editor Version 5.00";
        private readonly string _directory;

        public RegistryDriftTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "WinRestoreKit.RegistryDriftTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [Fact]
        public void HasSameCanonicalContent_EquivalentRegExports_ReturnsTrue()
        {
            string captured = Write("captured.reg", Header + "\r\n\r\n[HKEY_CURRENT_USER\\Software\\Drift]\r\n\"Enabled\"=dword:00000001\r\n", Encoding.Unicode);
            string current = Write("current.reg", Header + "\n\n[HKEY_CURRENT_USER\\Software\\Drift]\n\"Enabled\"=dword:00000001\n\n", new UTF8Encoding(false));

            Assert.True(Compare(captured, current));
        }

        [Fact]
        public void HasSameCanonicalContent_ChangedRegistryValue_ReturnsFalse()
        {
            string captured = Write("captured.reg", Header + "\r\n\r\n[HKEY_CURRENT_USER\\Software\\Drift]\r\n\"Enabled\"=dword:00000001\r\n", Encoding.Unicode);
            string current = Write("current.reg", Header + "\r\n\r\n[HKEY_CURRENT_USER\\Software\\Drift]\r\n\"Enabled\"=dword:00000002\r\n", Encoding.Unicode);

            Assert.False(Compare(captured, current));
        }

        [Fact]
        public void HasSameCanonicalContent_AbsentArtifact_ReturnsNull()
        {
            string current = Write("current.reg", Header + "\r\n", Encoding.Unicode);

            Assert.Null(Compare(Path.Combine(_directory, "absent.reg"), current));
        }

        [Fact]
        public void HasSameCanonicalContent_UnreadableArtifact_ReturnsNull()
        {
            string captured = Write("captured.reg", Header + "\r\n", Encoding.Unicode);
            string current = Write("current.reg", Header + "\r\n", Encoding.Unicode);

            using (new FileStream(captured, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert.Null(Compare(captured, current));
        }

        [Fact]
        public void Detect_ConfirmedRegistryDifference_EmitsDriftItem()
        {
            var detected = DriftDetector.Detect(_directory, new BackupBase[]
            {
                new ReportedDriftModule(true)
            }, out _);

            Assert.Single(detected);
        }

        [Fact]
        public void Detect_EqualRegistryRepresentation_EmitsNoDriftItem()
        {
            var detected = DriftDetector.Detect(_directory, new BackupBase[]
            {
                new ReportedDriftModule(false)
            }, out _);

            Assert.Empty(detected);
        }

        [Fact]
        public void FilteredRegistryModule_ReportsUnknownDrift()
            => Assert.Null(new EEnvironmentFiltered().HasDriftedFrom(_directory));

        public void Dispose()
        {
            try { Directory.Delete(_directory, true); } catch { }
        }

        private string Write(string name, string content, Encoding encoding)
        {
            string path = Path.Combine(_directory, name);
            File.WriteAllText(path, content, encoding);
            return path;
        }

        private static bool? Compare(string captured, string current)
            => RegFile.HasSameCanonicalContent(captured, current);

        private sealed class ReportedDriftModule : BackupBase
        {
            private readonly bool? _drift;

            public ReportedDriftModule(bool? drift)
            {
                _drift = drift;
                Title = "Registry artifact";
            }

            public override bool? HasDriftedFrom(string backupPath) => _drift;
        }
    }
}
