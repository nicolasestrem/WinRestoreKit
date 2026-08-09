using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;

namespace WinRestoreKit
{
    public interface ISnapshotEventReader
    {
        IReadOnlyList<SnapshotEvent> Read();
    }

    /// <summary>Classifies discovered backup evidence once into deterministic, immutable events.</summary>
    public sealed class SnapshotEventCatalog : ISnapshotEventReader
    {
        private readonly List<SnapshotEvent> sessionFailures = new List<SnapshotEvent>();
        private readonly object sessionFailuresGate = new object();
        private int nextSessionFailureOrdinal;

        public IReadOnlyList<SnapshotEvent> Read()
        {
            BackupFolders folders = BackupFolders.Read();
            List<SnapshotEvent> events = new List<SnapshotEvent>(
                folders.Backups.Count + folders.Snapshots.Count + folders.UnreadableRoots.Count);

            AddFolderEvents(events, folders.Backups);
            AddFolderEvents(events, folders.Snapshots);

            foreach (UnreadableBackupRoot root in folders.UnreadableRoots)
                events.Add(CreateUnreadableRootEvent(root));

            lock (sessionFailuresGate)
                events.AddRange(sessionFailures);

            events.Sort(CompareEvents);
            return events.AsReadOnly();
        }

        /// <summary>Records a process-local failure that never reached durable backup storage.</summary>
        public void RecordSessionFailure(DateTime created, string displayName, string diagnosticReason)
        {
            if (string.IsNullOrWhiteSpace(diagnosticReason))
                throw new ArgumentException("A session failure must have a diagnostic reason.", nameof(diagnosticReason));

            lock (sessionFailuresGate)
            {
                int ordinal = ++nextSessionFailureOrdinal;
                sessionFailures.Add(new SnapshotEvent(
                    SnapshotEventKind.Failed,
                    created,
                    displayName,
                    "session://failure/" + ordinal.ToString(CultureInfo.InvariantCulture),
                    diagnosticReason,
                    string.Empty,
                    0,
                    false,
                    null));
            }
        }

        private static void AddFolderEvents(List<SnapshotEvent> events, IReadOnlyList<BackupFolder> folders)
        {
            foreach (BackupFolder folder in folders)
                events.Add(CreateFolderEvent(folder));
        }

        private static SnapshotEvent CreateFolderEvent(BackupFolder folder)
        {
            ManifestData manifest = folder.ReadManifest();
            SizeEvidence size = ReadSize(folder.Path);

            return new SnapshotEvent(
                Classify(folder, manifest),
                folder.Created,
                folder.DisplayName,
                CanonicalizePath(folder.Path),
                JoinDiagnostic(folder.ManifestError, folder.CreatedError, size.Error),
                manifest?.MachineName ?? string.Empty,
                size.Bytes,
                size.IsComplete,
                manifest);
        }

        private static SnapshotEvent CreateUnreadableRootEvent(UnreadableBackupRoot root)
        {
            string canonicalPath = CanonicalizePath(root.CanonicalPath);

            return new SnapshotEvent(
                SnapshotEventKind.Unreadable,
                DateTime.MinValue,
                DisplayNameForRoot(canonicalPath),
                canonicalPath,
                root.Reason,
                string.Empty,
                0,
                false,
                null);
        }

        private static SnapshotEventKind Classify(BackupFolder folder, ManifestData manifest)
        {
            if (folder.ManifestError != null)
                return SnapshotEventKind.Unreadable;

            if (manifest == null)
                return SnapshotEventKind.Partial;

            if (manifest.Modules.Count == 0 || manifest.Modules.All(module => module.State == BackupManifest.StateFailed))
                return SnapshotEventKind.Failed;

            if (manifest.Modules.Any(module => module.State != BackupManifest.StateSucceeded))
                return SnapshotEventKind.Partial;

            return SnapshotEventKind.Verified;
        }

        private static int CompareEvents(SnapshotEvent left, SnapshotEvent right)
        {
            int timestamp = right.Created.CompareTo(left.Created);
            return timestamp != 0
                ? timestamp
                : StringComparer.Ordinal.Compare(left.CanonicalPath, right.CanonicalPath);
        }

        private static SizeEvidence ReadSize(string path)
        {
            long bytes = 0;

            try
            {
                foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    long length = new FileInfo(file).Length;
                    checked
                    {
                        bytes += length;
                    }
                }

                return new SizeEvidence(bytes, true, null);
            }
            catch (Exception ex)
            {
                return new SizeEvidence(bytes, false, ex.Message);
            }
        }

        private static string CanonicalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return path ?? string.Empty;
            }
        }

        private static string DisplayNameForRoot(string path)
        {
            string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? path : name;
        }

        private static string JoinDiagnostic(params string[] reasons)
        {
            string diagnostic = null;

            foreach (string reason in reasons)
            {
                if (string.IsNullOrWhiteSpace(reason))
                    continue;

                diagnostic = diagnostic == null ? reason : diagnostic + Environment.NewLine + reason;
            }

            return diagnostic ?? string.Empty;
        }

        private readonly struct SizeEvidence
        {
            internal SizeEvidence(long bytes, bool isComplete, string error)
            {
                Bytes = bytes;
                IsComplete = isComplete;
                Error = error;
            }

            internal long Bytes { get; }

            internal bool IsComplete { get; }

            internal string Error { get; }
        }
    }
}
