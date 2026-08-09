using System;

namespace WinRestoreKit
{
    public sealed class ModuleComparison
    {
        internal ModuleComparison(BackupBase module, ComparisonState state,
                                  bool hasUsableArtifact, string artifactSummary, string reason)
        {
            Module = module ?? throw new ArgumentNullException(nameof(module));
            State = state;
            HasUsableArtifact = hasUsableArtifact;
            ArtifactSummary = artifactSummary ?? string.Empty;
            Reason = reason ?? string.Empty;
        }

        public BackupBase Module { get; }

        public ComparisonState State { get; }

        public bool HasUsableArtifact { get; }

        public string ArtifactSummary { get; }

        public string Reason { get; }
    }
}
