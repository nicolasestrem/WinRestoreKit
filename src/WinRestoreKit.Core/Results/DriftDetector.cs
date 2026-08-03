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

    /// <summary>
    /// Compares modules with a snapshot without collapsing an unavailable comparison into a clean result.
    /// </summary>
    /// <remarks>
    /// The return value contains only modules whose drift is confirmed. <paramref name="unavailable"/>
    /// contains modules whose comparison returned null. A module that returned false is in neither
    /// collection because its state is confirmed unchanged.
    /// </remarks>
    internal static class DriftDetector
    {
        internal static IReadOnlyList<DriftItem> Detect(
            string backupPath,
            IReadOnlyList<BackupBase> modules,
            out IReadOnlyList<DriftItem> unavailable)
        {
            List<DriftItem> drifted = new List<DriftItem>();
            List<DriftItem> unavailableItems = new List<DriftItem>();

            foreach (BackupBase module in modules)
            {
                bool? hasDrifted = module.HasDriftedFrom(backupPath);

                if (hasDrifted == true)
                    drifted.Add(new DriftItem(module.Title, backupPath, null));
                else if (!hasDrifted.HasValue)
                    unavailableItems.Add(new DriftItem(module.Title, backupPath, null));
            }

            unavailable = unavailableItems;
            return drifted;
        }
    }
}
