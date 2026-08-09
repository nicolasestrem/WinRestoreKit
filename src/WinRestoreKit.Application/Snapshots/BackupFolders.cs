using DataHelper;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace WinRestoreKit
{
    /// <summary>The backup directory, split into user backups and pre-restore snapshots.</summary>
    internal sealed class BackupFolders
    {
        private BackupFolders(IReadOnlyList<BackupFolder> backups, IReadOnlyList<BackupFolder> snapshots,
                              string unreadableReason, IReadOnlyList<UnreadableBackupRoot> unreadableRoots)
        {
            Backups = backups;
            Snapshots = snapshots;
            UnreadableReason = unreadableReason;
            UnreadableRoots = unreadableRoots;
        }

        /// <summary>User backups, newest first.</summary>
        internal IReadOnlyList<BackupFolder> Backups { get; }

        /// <summary>Pre-restore snapshots, newest first.</summary>
        internal IReadOnlyList<BackupFolder> Snapshots { get; }

        /// <summary>Why legacy WinForms discovery could not list its root, when no root was readable.</summary>
        internal string UnreadableReason { get; }

        /// <summary>Every root whose enumeration produced an observable error.</summary>
        internal IReadOnlyList<UnreadableBackupRoot> UnreadableRoots { get; }

        /// <summary>Reads the default root and every remembered custom root without dropping readable roots.</summary>
        internal static BackupFolders Read()
        {
            List<BackupFolder> backups = new List<BackupFolder>();
            List<BackupFolder> snapshots = new List<BackupFolder>();
            List<UnreadableBackupRoot> unreadableRoots = new List<UnreadableBackupRoot>();
            List<string> roots = new List<string> { Data.DataRootDir };

            BackupRootRegistry.TryCanonicalize(Data.DataRootDir, out string defaultRoot);

            foreach (string root in BackupRootRegistry.Read())
            {
                if (defaultRoot != null
                    && string.Equals(root, defaultRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                roots.Add(root);
            }

            int inspectedRoots = 0;
            string unreadableReason = null;

            for (int rootIndex = 0; rootIndex < roots.Count; rootIndex++)
            {
                string root = roots[rootIndex];
                bool isDefaultRoot = rootIndex == 0;
                string[] paths;

                try
                {
                    // Do not guard this with Directory.Exists: Exists suppresses access errors and
                    // makes an unreadable root indistinguishable from a root with no backups.
                    paths = Directory.GetDirectories(root);
                    inspectedRoots++;
                }
                catch (DirectoryNotFoundException)
                {
                    // An absent root is ordinary before the first backup and remains distinct from
                    // a root that exists but cannot be inspected.
                    continue;
                }
                catch (Exception ex)
                {
                    // Record the failure and keep scanning. A bad default root must not abort the
                    // whole scan: backups living on a remembered custom or external root would
                    // vanish from Timeline and become unrestorable. The reason is surfaced only when
                    // NO root was readable; otherwise the readable roots' backups are listed and this
                    // failure is retained in UnreadableRoots for diagnostics.
                    unreadableRoots.Add(new UnreadableBackupRoot(CanonicalizeRoot(root), ex.Message));
                    unreadableReason ??= ex.Message;
                    continue;
                }

                foreach (string path in paths)
                {
                    // A remembered custom root below the default root is a container owned by the
                    // custom-root pass, not a default-root legacy backup.
                    if (isDefaultRoot && IsRememberedCustomRootOrContainer(path, roots))
                        continue;

                    if (!isDefaultRoot && !IsRecognizableCustomFolder(path))
                        continue;

                    BackupFolder folder = new BackupFolder(path);

                    if (IsSnapshot(folder.Name))
                        snapshots.Add(folder);
                    else
                        backups.Add(folder);
                }
            }

            if (inspectedRoots == 0 && unreadableReason != null)
                return new BackupFolders(backups, snapshots, unreadableReason, unreadableRoots);
            backups.Sort(NewestFirst);
            snapshots.Sort(NewestFirst);

            return new BackupFolders(backups, snapshots, null, unreadableRoots);
        }

        private static string CanonicalizeRoot(string root)
        {
            if (BackupRootRegistry.TryCanonicalize(root, out string canonicalRoot))
                return canonicalRoot;

            try
            {
                return Path.GetFullPath(root);
            }
            catch (Exception)
            {
                return root;
            }
        }

        private static bool IsRememberedCustomRootOrContainer(string path, IReadOnlyList<string> roots)
        {
            if (!BackupRootRegistry.TryCanonicalize(path, out string canonicalPath))
                return false;

            for (int rootIndex = 1; rootIndex < roots.Count; rootIndex++)
            {
                string rememberedRoot = roots[rootIndex];

                if (string.Equals(canonicalPath, rememberedRoot, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (rememberedRoot.Length > canonicalPath.Length
                    && rememberedRoot.StartsWith(canonicalPath, StringComparison.OrdinalIgnoreCase)
                    && IsDirectorySeparator(rememberedRoot[canonicalPath.Length]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDirectorySeparator(char value)
            => value == Path.DirectorySeparatorChar || value == Path.AltDirectorySeparatorChar;

        /// <summary>Whether a custom-root child carries application metadata or a legacy timestamp name.</summary>
        private static bool IsRecognizableCustomFolder(string path)
        {
            return File.Exists(Path.Combine(path, BackupManifest.FileName))
                || File.Exists(Path.Combine(path, BackupLog.FileName))
                || File.Exists(Path.Combine(path, BackupPayload.FileName))
                || HasFrozenTimestampShape(Path.GetFileName(path));
        }

        private static bool HasFrozenTimestampShape(string folderName)
        {
            if (string.IsNullOrEmpty(folderName))
                return false;

            int snapshotStart = folderName.IndexOf(SnapshotNaming.Suffix, StringComparison.OrdinalIgnoreCase);
            string timestamp;

            if (snapshotStart >= 0)
            {
                if (!IsCollisionSuffix(folderName.Substring(snapshotStart + SnapshotNaming.Suffix.Length)))
                    return false;

                timestamp = folderName.Substring(0, snapshotStart);
            }
            else
            {
                timestamp = folderName;
                TryRemoveCollisionSuffix(folderName, out timestamp);
            }

            return MatchesDataRootTimestampShape(timestamp);
        }

        private static bool IsCollisionSuffix(string suffix)
        {
            if (suffix.Length == 0)
                return true;

            return TryRemoveCollisionSuffix("x" + suffix, out _);
        }

        private static bool TryRemoveCollisionSuffix(string folderName, out string withoutCollision)
        {
            withoutCollision = folderName;

            int marker = folderName.LastIndexOf(" (", StringComparison.Ordinal);

            if (marker < 0
                || !folderName.EndsWith(")", StringComparison.Ordinal)
                || !int.TryParse(folderName.Substring(marker + 2, folderName.Length - marker - 3),
                    NumberStyles.None, CultureInfo.InvariantCulture, out int collision)
                || collision < 2)
            {
                return false;
            }

            withoutCollision = folderName.Substring(0, marker);
            return true;
        }

        private static bool MatchesDataRootTimestampShape(string timestamp)
        {
            string template = Data.NowShort;

            if (string.IsNullOrEmpty(template)
                || (timestamp.Length != template.Length && timestamp.Length != template.Length + 3))
            {
                return false;
            }

            for (int index = 0; index < template.Length; index++)
            {
                if (char.IsDigit(template[index]))
                {
                    if (!char.IsDigit(timestamp[index]))
                        return false;
                }
                else if (timestamp[index] != template[index])
                {
                    return false;
                }
            }

            return timestamp.Length == template.Length
                || (template.Length >= 3
                    && timestamp[template.Length] == template[template.Length - 3]
                    && char.IsDigit(timestamp[template.Length + 1])
                    && char.IsDigit(timestamp[template.Length + 2]));
        }

        /// <summary>Whether a folder name is a pre-restore snapshot.</summary>
        internal static bool IsSnapshot(string folderName)
            => folderName != null
                && folderName.IndexOf(SnapshotNaming.Suffix, StringComparison.OrdinalIgnoreCase) >= 0;

        private static int NewestFirst(BackupFolder left, BackupFolder right)
            => right.Created.CompareTo(left.Created);
    }

    /// <summary>One folder under a backup root.</summary>
    internal sealed class BackupFolder
    {
        private readonly ManifestData manifest;

        internal BackupFolder(string path)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(path);
            manifest = ReadManifest(path, out string manifestError);
            ManifestError = manifestError;
            Created = ReadCreated(path, manifest, out string createdError);
            CreatedError = createdError;
        }

        internal string Path { get; }

        internal string Name { get; }

        internal string DisplayName
            => manifest == null || string.IsNullOrEmpty(manifest.SnapshotName)
                ? Name
                : manifest.SnapshotName;

        internal DateTime Created { get; }

        /// <summary>Reason the manifest was invalid or unreadable; null for a valid or absent manifest.</summary>
        internal string ManifestError { get; }

        /// <summary>Reason the directory timestamp could not be read; null when a timestamp was available.</summary>
        internal string CreatedError { get; }

        internal ManifestData ReadManifest() => manifest;

        private static ManifestData ReadManifest(string path, out string error)
        {
            error = null;
            string file = System.IO.Path.Combine(path, BackupManifest.FileName);

            try
            {
                if (!File.Exists(file))
                    return null;

                ManifestData parsed = BackupManifest.TryParse(File.ReadAllText(file));

                if (parsed == null)
                    error = "The backup manifest is invalid or uses an unsupported schema.";

                return parsed;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        private static DateTime ReadCreated(string path, ManifestData manifest, out string error)
        {
            error = null;

            if (manifest != null
                && DateTime.TryParse(manifest.Created, CultureInfo.InvariantCulture,
                                     DateTimeStyles.RoundtripKind, out DateTime recorded))
            {
                return recorded.ToLocalTime();
            }

            try
            {
                return Directory.GetCreationTime(path);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return DateTime.MinValue;
            }
        }
    }

    /// <summary>Observed root-level discovery failure retained for diagnostic event projection.</summary>
    internal sealed class UnreadableBackupRoot
    {
        internal UnreadableBackupRoot(string canonicalPath, string reason)
        {
            CanonicalPath = canonicalPath;
            Reason = reason;
        }

        internal string CanonicalPath { get; }

        internal string Reason { get; }
    }
}
