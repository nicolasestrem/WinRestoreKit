using System;
using System.IO;
using System.Linq;

namespace WinRestoreKit
{
    internal sealed class BackupCompletionPublisher
    {
        private readonly SnapshotEventCatalog catalog;

        internal BackupCompletionPublisher(SnapshotEventCatalog catalog)
            => this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

        internal void Publish(string attemptedBackupPath, string displayName,
            RunSummary summary, DateTime created)
        {
            string canonicalPath = TryCanonicalize(attemptedBackupPath);
            bool retained = canonicalPath != null && catalog.Read().Any(snapshot =>
                string.Equals(snapshot.CanonicalPath, canonicalPath,
                    StringComparison.OrdinalIgnoreCase));

            if (retained)
                return;

            string diagnostic = summary?.Detail;
            if (string.IsNullOrWhiteSpace(diagnostic))
                diagnostic = "The snapshot run ended without a retained recognizable result.";

            catalog.RecordSessionFailure(created, displayName ?? string.Empty, diagnostic);
        }

        private static string TryCanonicalize(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
