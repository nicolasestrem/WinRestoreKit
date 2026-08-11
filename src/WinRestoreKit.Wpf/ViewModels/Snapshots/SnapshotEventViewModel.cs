using System;
using System.Globalization;
using WinRestoreKit;

namespace WinRestoreKit.Wpf.ViewModels.Snapshots
{
    public sealed class SnapshotEventViewModel
    {
        internal SnapshotEventViewModel(SnapshotEvent snapshot)
        {
            Event = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Status = SnapshotEventStatusProjection.For(snapshot.Kind);
            DisplayName = snapshot.DisplayName;
            CreatedDisplay = FormatCreated(snapshot.Created);
            MachineName = snapshot.MachineName;
            CanonicalPath = snapshot.CanonicalPath;
            DiagnosticReason = snapshot.DiagnosticReason ?? string.Empty;
            HasDiagnostic = !string.IsNullOrWhiteSpace(DiagnosticReason);
            SizeDisplay = snapshot.IsSizeComplete ? FormatBytes(snapshot.SizeBytes) : "Unavailable";
            MetaDisplay = BuildMetaDisplay(snapshot);
            HasMeta = MetaDisplay.Length > 0;
            AutomationName = BuildAutomationName();
        }

        public SnapshotEvent Event { get; }

        public SnapshotEventStatus Status { get; }

        public string DisplayName { get; }

        public DateTime Created => Event.Created;

        public string CreatedDisplay { get; }

        public string MachineName { get; }

        public string CanonicalPath { get; }

        public string SizeDisplay { get; }

        /// <summary>Machine and size joined for the timeline card's secondary line; empty when neither is known.</summary>
        public string MetaDisplay { get; }

        public bool HasMeta { get; }

        public string DiagnosticReason { get; }

        public bool HasDiagnostic { get; }

        public bool IsRestorable => Event.IsRestorable;

        public string AutomationName { get; }

        private string BuildAutomationName()
        {
            string name = DisplayName + ", " + CreatedDisplay + ", " + Status.Label;
            return HasDiagnostic ? name + ", " + DiagnosticReason : name;
        }

        // An unreadable entry carries no discoverable creation time. Rendering DateTime.MinValue
        // as "Monday, January 1, 0001" reads as corrupted data rather than as missing data.
        private static string FormatCreated(DateTime created)
            => created == DateTime.MinValue
                ? "Time unknown"
                : created.ToString("f", CultureInfo.CurrentCulture);

        private static string BuildMetaDisplay(SnapshotEvent snapshot)
        {
            bool hasMachine = !string.IsNullOrWhiteSpace(snapshot.MachineName);
            if (hasMachine && snapshot.IsSizeComplete)
                return snapshot.MachineName + "  ·  " + FormatBytes(snapshot.SizeBytes);
            if (hasMachine)
                return snapshot.MachineName;
            return snapshot.IsSizeComplete ? FormatBytes(snapshot.SizeBytes) : string.Empty;
        }

        private static string FormatBytes(long bytes)
        {
            const long Gigabyte = 1024L * 1024L * 1024L;
            const long Megabyte = 1024L * 1024L;
            const long Kilobyte = 1024L;

            if (bytes >= Gigabyte)
                return (bytes / (double)Gigabyte).ToString("0.0", CultureInfo.CurrentCulture) + " GB";
            if (bytes >= Megabyte)
                return (bytes / (double)Megabyte).ToString("0.0", CultureInfo.CurrentCulture) + " MB";
            if (bytes >= Kilobyte)
                return (bytes / (double)Kilobyte).ToString("0.0", CultureInfo.CurrentCulture) + " KB";
            return bytes + " B";
        }
    }
}
