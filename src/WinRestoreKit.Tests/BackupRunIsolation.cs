using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;

namespace WinRestoreKit.Tests
{
    internal sealed class BackupRunIsolation : IDisposable
    {
        private const string RegistryPath = @"Software\WinRestoreKit";
        private readonly object originalRoots;
        private readonly RegistryValueKind? originalRootsKind;
        private bool disposed;

        internal BackupRunIsolation()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
            {
                if (key != null && key.GetValueNames().Contains("BackupRoots"))
                {
                    originalRoots = key.GetValue("BackupRoots", null,
                        RegistryValueOptions.DoNotExpandEnvironmentNames);
                    originalRootsKind = key.GetValueKind("BackupRoots");
                }
            }

            DestinationRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "WinRestoreKitTests",
                Guid.NewGuid().ToString("N"))).FullName;
        }

        internal string DestinationRoot { get; }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            try
            {
                if (Directory.Exists(DestinationRoot))
                    Directory.Delete(DestinationRoot, true);
            }
            finally
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    if (originalRoots == null)
                        key.DeleteValue("BackupRoots", throwOnMissingValue: false);
                    else
                        key.SetValue("BackupRoots", originalRoots, originalRootsKind.Value);
                }
            }
        }
    }
}
