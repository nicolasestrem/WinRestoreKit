using DataHelper;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Views;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class HomePageViewBaselineTests : IDisposable
    {
        private readonly string parent;
        private readonly string root;
        private readonly string originalDataRoot;
        private readonly object originalRoots;
        private readonly RegistryValueKind? originalRootsKind;

        public HomePageViewBaselineTests()
        {
            parent = Path.Combine(Path.GetTempPath(), "WinRestoreKit.HomePageViewBaselineTests", Guid.NewGuid().ToString("N"));
            root = Directory.CreateDirectory(Path.Combine(parent, "backups")).FullName;
            originalDataRoot = Data.DataRootDir;

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\WinRestoreKit"))
            {
                if (key != null && key.GetValueNames().Contains("BackupRoots"))
                {
                    originalRoots = key.GetValue("BackupRoots", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    originalRootsKind = key.GetValueKind("BackupRoots");
                }
            }

            Data.DataRootDir = root;
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\WinRestoreKit"))
                key.DeleteValue("BackupRoots", throwOnMissingValue: false);
            Theme.Initialize();
        }

        [Fact]
        public void Home_UsesNewestCurrentMachineBackupInsteadOfNewerForeignBackup()
        {
            CreateBackup("local", Environment.MachineName, new[] { BackupManifest.StateSucceeded }, new DateTime(2026, 8, 1));
            CreateBackup("foreign", "OTHER-MACHINE", new[] { BackupManifest.StateSucceeded }, new DateTime(2026, 8, 2));

            using (HomePageView view = CreateView())
            {
                Assert.Contains(AllLabels(view), label => label.Text.Contains("\"local\"", StringComparison.Ordinal));
                Assert.DoesNotContain(AllLabels(view), label => label.Text.Contains("\"foreign\"", StringComparison.Ordinal));
            }
        }

        [Fact]
        public void Home_WhenEveryBackupIsForeign_RendersAwaitingSnapshotState()
        {
            CreateBackup("foreign", "OTHER-MACHINE", new[] { BackupManifest.StateSucceeded }, new DateTime(2026, 8, 2));

            using (HomePageView view = CreateView())
                Assert.Contains(AllLabels(view), label => label.Text == "SYSTEM AWAITS A SNAPSHOT");
        }

        [Fact]
        public void Home_WhenBaselineContainsOnlyFailedModules_RendersFailedHeading()
        {
            AssertFailedBaselineHeading(new[] { BackupManifest.StateFailed });
        }

        [Fact]
        public void Home_WhenBaselineContainsNoModules_RendersFailedHeading()
        {
            AssertFailedBaselineHeading(Array.Empty<string>());
        }

        [Fact]
        public void Home_WhenBaselineHasPartialSuccess_RendersCapturedHeading()
        {
            CreateBackup("partial", Environment.MachineName,
                new[] { BackupManifest.StateSucceeded, BackupManifest.StateFailed }, new DateTime(2026, 8, 1));

            using (HomePageView view = CreateView())
                Assert.Contains(AllLabels(view), label => label.Text == "SYSTEM IS CAPTURED");
        }

        [Fact]
        public void Home_WhenBaselineIsLegacyWithoutManifest_RendersCapturedHeading()
        {
            Directory.CreateDirectory(Path.Combine(root, "legacy"));

            using (HomePageView view = CreateView())
                Assert.Contains(AllLabels(view), label => label.Text == "SYSTEM IS CAPTURED");
        }

        private void AssertFailedBaselineHeading(IReadOnlyList<string> states)
        {
            CreateBackup("failed", Environment.MachineName, states, new DateTime(2026, 8, 1));

            using (HomePageView view = CreateView())
            {
                Label heading = AllLabels(view).Single(label => label.Text == "LAST BACKUP FAILED");
                Assert.Equal(Theme.Current.Accent2_600.ToArgb(), heading.ForeColor.ToArgb());
            }
        }

        public void Dispose()
        {
            Data.DataRootDir = originalDataRoot;
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\WinRestoreKit"))
            {
                if (originalRoots == null)
                    key.DeleteValue("BackupRoots", throwOnMissingValue: false);
                else
                    key.SetValue("BackupRoots", originalRoots, originalRootsKind.Value);
            }

            try { Directory.Delete(parent, true); } catch { }
        }

        private void CreateBackup(string name, string machineName, IReadOnlyList<string> states, DateTime created)
        {
            string folder = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
            string modules = string.Join(",", states.Select((state, index) =>
                "{\"type\":\"Module" + index + "\",\"title\":\"Module\",\"state\":\"" + state + "\",\"reason\":\"test\"}"));
            string json = "{\"manifest_version\":1,\"created\":\"" + created.ToString("o") + "\",\"machine_name\":\"" + machineName + "\",\"modules\":[" + modules + "]}";
            File.WriteAllText(Path.Combine(folder, BackupManifest.FileName), json);
        }

        private static HomePageView CreateView()
            => new HomePageView(_ => { }, _ => { }, () => { });

        private static IEnumerable<Label> AllLabels(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (child is Label label)
                    yield return label;

                foreach (Label nested in AllLabels(child))
                    yield return nested;
            }
        }
    }
}
