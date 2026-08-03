using System;
using System.Collections.Generic;

namespace WinRestoreKit
{
    internal sealed class DriftItem
    {
        internal DriftItem(string name, string path, DateTime? changedAt)
        {
            Name = name;
            Path = path;
            ChangedAt = changedAt;
        }

        internal string Name { get; }

        internal string Path { get; }

        internal DateTime? ChangedAt { get; }
    }

    internal static class DriftDetector
    {
        internal static IReadOnlyList<DriftItem> Detect(string backupPath, IReadOnlyList<BackupBase> modules)
        {
            List<DriftItem> drifted = new List<DriftItem>();

            foreach (BackupBase module in modules)
            {
                if (module.HasDriftedFrom(backupPath) == true)
                    drifted.Add(new DriftItem(module.Title, backupPath, null));
            }

            return drifted;
        }
    }
}
