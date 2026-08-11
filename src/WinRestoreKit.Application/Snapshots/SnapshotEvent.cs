using System;

namespace WinRestoreKit
{
    public sealed class SnapshotEvent
    {
        internal SnapshotEvent(SnapshotEventKind kind, DateTime created, string displayName,
                               string canonicalPath, string diagnosticReason, string machineName,
                               long sizeBytes, bool isSizeComplete, ManifestData manifest)
        {
            Kind = kind;
            Created = created;
            DisplayName = displayName;
            CanonicalPath = canonicalPath;
            DiagnosticReason = diagnosticReason;
            MachineName = machineName;
            SizeBytes = sizeBytes;
            IsSizeComplete = isSizeComplete;
            Manifest = manifest;
        }

        public SnapshotEventKind Kind { get; }

        public DateTime Created { get; }

        public string DisplayName { get; }

        public string CanonicalPath { get; }

        public string DiagnosticReason { get; }

        public string MachineName { get; }

        public long SizeBytes { get; }

        public bool IsSizeComplete { get; }

        public bool IsRestorable => Kind == SnapshotEventKind.Verified || Kind == SnapshotEventKind.Partial;

        internal ManifestData Manifest { get; }
    }
}
