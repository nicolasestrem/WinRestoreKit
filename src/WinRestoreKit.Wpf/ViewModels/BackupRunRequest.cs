using System;
using System.Collections.Generic;
using DataHelper;
using WinRestoreKit;

namespace WinRestoreKit.Wpf.ViewModels
{
    internal sealed class BackupRunRequest
    {
        internal BackupRunRequest(IReadOnlyList<BackupBase> modules, string snapshotName,
                                  SnapshotCompression compression, string destination)
        {
            Modules = modules ?? throw new ArgumentNullException(nameof(modules));
            SnapshotName = snapshotName ?? string.Empty;
            Compression = compression;
            Destination = destination ?? string.Empty;
        }

        public IReadOnlyList<BackupBase> Modules { get; }
        public string SnapshotName { get; }
        public SnapshotCompression Compression { get; }
        public string Destination { get; }
    }
}
