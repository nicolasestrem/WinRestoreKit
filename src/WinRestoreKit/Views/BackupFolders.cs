using WinRestoreKit;
using DataHelper;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Views
{
    /// <summary>
    /// The backup directory, split into the user's own backups and the pre-restore snapshots.
    /// </summary>
    /// <remarks>
    /// The retired picker listed these folder names verbatim in one flat list, which was fine for picking a
    /// folder to restore from but wrong for a dashboard: "last backup: 4 minutes ago" naming a
    /// snapshot the app took by itself, immediately before overwriting something, answers a question
    /// nobody asked. SnapshotNaming is the sole authority on which is which.
    /// </remarks>
    internal sealed class BackupFolders
    {
        private BackupFolders(IReadOnlyList<BackupFolder> backups, IReadOnlyList<BackupFolder> snapshots,
                              string unreadableReason)
        {
            Backups = backups;
            Snapshots = snapshots;
            UnreadableReason = unreadableReason;
        }

        /// <summary>User backups, newest first.</summary>
        internal IReadOnlyList<BackupFolder> Backups { get; }

        /// <summary>Pre-restore snapshots, newest first.</summary>
        internal IReadOnlyList<BackupFolder> Snapshots { get; }

        /// <summary>
        /// Why the backup folder could not be listed, or null when it was listed successfully.
        /// </summary>
        /// <remarks>
        /// "Could not look" and "looked, found nothing" are different answers and the screen must not
        /// merge them. An empty list means nothing has ever been backed up; a denied or vanished
        /// folder means this app does not know, and rendering that as "No backups yet" tells the user
        /// their backups are gone when they may be sitting there intact.
        /// </remarks>
        internal string UnreadableReason { get; }

        /// <summary>
        /// Reads the default root and every remembered custom root without letting one removable
        /// destination erase the results already obtained from another.
        /// </summary>
        /// <remarks>
        /// The default root is owned by this application, so every child remains eligible for legacy
        /// restore compatibility. A custom root can be Documents, a drive root or a network share,
        /// where treating every child as a backup would make unrelated user data restorable and make
        /// Home measure it as application storage. Its children therefore need evidence that this
        /// application wrote them.
        ///
        /// A remembered removable destination can disappear after a successful backup. That failure
        /// says nothing about a readable root, so it is retained only when no root could be inspected.
        /// The default root remains fatal because no fallback can distinguish an access failure there
        /// from an empty root the application has successfully listed.
        /// </remarks>
        internal static BackupFolders Read()
        {
            List<BackupFolder> backups = new List<BackupFolder>();
            List<BackupFolder> snapshots = new List<BackupFolder>();
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
            string unreadableCustomRootReason = null;

            for (int rootIndex = 0; rootIndex < roots.Count; rootIndex++)
            {
                string root = roots[rootIndex];
                bool isDefaultRoot = rootIndex == 0;
                string[] paths;

                try
                {
                    // Enumerate directly instead of guarding with Directory.Exists. Exists SWALLOWS the
                    // access error and answers false, so a folder whose ACL denies this user is
                    // indistinguishable from one that was never created - which is how an unreadable
                    // root came to be reported as "no backups yet".
                    paths = Directory.GetDirectories(root);
                    inspectedRoots++;
                }
                catch (DirectoryNotFoundException)
                {
                    // An absent root is ordinary before its first backup. Continue because a known
                    // custom root may still hold snapshots created in an earlier session.
                    continue;
                }
                catch (Exception ex)
                {
                    if (isDefaultRoot)
                        return new BackupFolders(backups, snapshots, ex.Message);

                    unreadableCustomRootReason ??= ex.Message;
                    continue;
                }

                foreach (string path in paths)
                {
                    // A custom destination can live below the default root. Its parent is not a
                    // legacy backup, it is a container whose children the custom-root pass owns.
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

            if (inspectedRoots == 0 && unreadableCustomRootReason != null)
                return new BackupFolders(backups, snapshots, unreadableCustomRootReason);

            backups.Sort(NewestFirst);
            snapshots.Sort(NewestFirst);

            return new BackupFolders(backups, snapshots, null);
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
        {
            return value == Path.DirectorySeparatorChar || value == Path.AltDirectorySeparatorChar;
        }

        /// <summary>
        /// Whether a child of a custom root carries metadata or a frozen name shape that this
        /// application writes.
        /// </summary>
        /// <remarks>
        /// A custom root is often chosen for free space rather than dedicated to backups. Metadata
        /// admits complete and interrupted backups, while the timestamp shape preserves old loose
        /// folders whose manifest was never written. Both paths are needed because excluding an
        /// interrupted backup is safer than presenting every unrelated folder as a restore source.
        /// </remarks>
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

        /// <summary>
        /// Whether a folder name is a pre-restore snapshot.
        /// </summary>
        /// <remarks>
        /// Contains, not EndsWith. SnapshotNaming.Unique appends " (2)", " (3)" AFTER the suffix when
        /// two restores land in the same second, so the marker is not guaranteed to be terminal - and
        /// those collision folders are exactly the ones taken when a restore went wrong, which is
        /// when misfiling one as a user backup would matter most.
        /// </remarks>
        internal static bool IsSnapshot(string folderName)
            => folderName != null
                && folderName.IndexOf(SnapshotNaming.Suffix, StringComparison.OrdinalIgnoreCase) >= 0;

        private static int NewestFirst(BackupFolder a, BackupFolder b)
            => b.Created.CompareTo(a.Created);
    }

    /// <summary>One folder under the backup root.</summary>
    internal sealed class BackupFolder
    {
        private readonly ManifestData manifest;

        internal BackupFolder(string path)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(path);

            // Read once here rather than on demand: Created depends on it, and the folders are
            // sorted by Created, so a lazy read would parse the same small file repeatedly during
            // an ordinary sort.
            manifest = ReadManifest(path);
            Created = ReadCreated(path, manifest);
        }

        internal string Path { get; }

        internal string Name { get; }

        /// <summary>
        /// User-facing backup name from the manifest, or the physical folder name for every legacy
        /// backup and manifest that does not supply one.
        /// </summary>
        internal string DisplayName
            => manifest == null || string.IsNullOrEmpty(manifest.SnapshotName)
                ? Name
                : manifest.SnapshotName;

        /// <summary>
        /// When the run this folder holds actually finished.
        /// </summary>
        /// <remarks>
        /// From the manifest when there is one, and only otherwise from the directory timestamp.
        /// The filesystem answer is the weaker of the two in both directions: a second backup in one
        /// session reuses the folder, so the directory still carries the FIRST run's creation time
        /// and "12 days ago" would be said of a run made minutes ago; and a backup folder copied
        /// from another machine is stamped with the moment it was copied, so an old run sorts first
        /// and reads as today's.
        /// </remarks>
        internal DateTime Created { get; }

        /// <summary>
        /// The folder's manifest, or null when there is not a trustworthy one.
        /// </summary>
        /// <remarks>
        /// Absent file, unreadable file and a document TryParse refuses all collapse to the same
        /// null, because they are the same answer to the caller: this app does not know what is in
        /// here. Callers render that as "details unavailable".
        /// </remarks>
        internal ManifestData ReadManifest() => manifest;

        private static ManifestData ReadManifest(string path)
        {
            try
            {
                string file = System.IO.Path.Combine(path, BackupManifest.FileName);

                if (!File.Exists(file))
                    return null;

                return BackupManifest.TryParse(File.ReadAllText(file));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static DateTime ReadCreated(string path, ManifestData manifest)
        {
            // "o" as written by Compose. Parsed with RoundtripKind and converted to local time, so a
            // backup carried over from a machine in another time zone is described in the time zone
            // of whoever is reading the screen.
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
            catch (Exception)
            {
                // MinValue sorts the folder last rather than pretending it is the newest thing on
                // disk, which is what DateTime.Now here would have done.
                return DateTime.MinValue;
            }
        }
    }
}
