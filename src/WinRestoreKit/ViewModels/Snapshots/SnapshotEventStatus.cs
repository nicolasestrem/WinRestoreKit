using System;
using WinRestoreKit;

namespace WinRestoreKit.Wpf.ViewModels.Snapshots
{
    /// <summary>
    /// Visual weight of a snapshot status. Views pick a glyph shape and a palette from this;
    /// meaning is never carried by colour alone, so every surface also renders <see cref="SnapshotEventStatus.Label"/>.
    /// </summary>
    public enum SnapshotStatusTone
    {
        /// <summary>Snapshot is complete and selectable.</summary>
        Positive,

        /// <summary>Snapshot is usable but incomplete.</summary>
        Caution,

        /// <summary>The attempt failed; the entry is diagnostic only.</summary>
        Critical,

        /// <summary>Nothing trustworthy is known about the entry.</summary>
        Neutral
    }

    public sealed class SnapshotEventStatus
    {
        public SnapshotEventStatus(string label, string shortLabel, SnapshotStatusTone tone, bool isDiagnosticOnly)
        {
            Label = label ?? throw new ArgumentNullException(nameof(label));
            ShortLabel = shortLabel ?? throw new ArgumentNullException(nameof(shortLabel));
            Tone = tone;
            IsDiagnosticOnly = isDiagnosticOnly;
        }

        /// <summary>Full status sentence used for accessible text and dense list columns.</summary>
        public string Label { get; }

        /// <summary>Condensed status word used inside status pills.</summary>
        public string ShortLabel { get; }

        public SnapshotStatusTone Tone { get; }

        public bool IsDiagnosticOnly { get; }
    }

    internal static class SnapshotEventStatusProjection
    {
        private static readonly SnapshotEventStatus Verified =
            new SnapshotEventStatus("Verified", "Verified", SnapshotStatusTone.Positive, false);

        private static readonly SnapshotEventStatus Partial =
            new SnapshotEventStatus("Partial snapshot", "Partial", SnapshotStatusTone.Caution, false);

        private static readonly SnapshotEventStatus Failed =
            new SnapshotEventStatus("Backup failed", "Failed", SnapshotStatusTone.Critical, true);

        private static readonly SnapshotEventStatus Unreadable =
            new SnapshotEventStatus("Details unavailable", "Unreadable", SnapshotStatusTone.Neutral, true);

        internal static SnapshotEventStatus For(SnapshotEventKind kind)
        {
            return kind switch
            {
                SnapshotEventKind.Verified => Verified,
                SnapshotEventKind.Partial => Partial,
                SnapshotEventKind.Failed => Failed,
                SnapshotEventKind.Unreadable => Unreadable,
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }
    }
}
