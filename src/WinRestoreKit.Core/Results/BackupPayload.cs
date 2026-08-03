using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace WinRestoreKit
{
    /// <summary>
    /// Creates and opens the optional archive payload for a backup folder.
    /// </summary>
    /// <remarks>
    /// The manifest and human-readable log remain at the backup root so existing readers retain
    /// their metadata path. Module artifacts move into the archive only after its entries have been
    /// reopened and checked against the source file list. Legacy folders without this file are read
    /// directly and never require extraction.
    /// </remarks>
    internal static class BackupPayload
    {
        internal const string FileName = "payload.zip";

        private static readonly HashSet<string> RootMetadata = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            BackupManifest.FileName,
            BackupLog.FileName,
            FileName
        };

        internal static bool TryArchive(string backupPath, SnapshotCompression compression, out string error)
        {
            error = null;

            if (compression == SnapshotCompression.None)
                return false;

            if (compression != SnapshotCompression.Fast && compression != SnapshotCompression.Max)
            {
                error = "The requested compression mode is not supported.";
                return false;
            }

            string temporaryPath = null;

            try
            {
                List<SourceFile> sourceFiles = ListPayloadFiles(backupPath);
                List<string> emptyDirectories = ListEmptyPayloadDirectories(backupPath);
                string payloadPath = Path.Combine(backupPath, FileName);
                temporaryPath = Path.Combine(backupPath, ".payload-"
                    + Guid.NewGuid().ToString("N") + ".tmp");

                using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create, false))
                {
                    CompressionLevel level = compression == SnapshotCompression.Fast
                        ? CompressionLevel.Fastest
                        : CompressionLevel.SmallestSize;

                    foreach (SourceFile source in sourceFiles)
                        archive.CreateEntryFromFile(source.FullPath, source.EntryName, level);

                    foreach (string directory in emptyDirectories)
                        archive.CreateEntry(directory + "/", CompressionLevel.NoCompression);
                }

                if (!ArchiveMatches(temporaryPath, sourceFiles, emptyDirectories))
                {
                    error = "The compressed payload could not be verified.";
                    return false;
                }

                File.Move(temporaryPath, payloadPath, true);

                foreach (SourceFile source in sourceFiles)
                    File.Delete(source.FullPath);

                RemoveEmptyDirectories(backupPath);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (!string.IsNullOrEmpty(temporaryPath))
                {
                    try
                    {
                        if (File.Exists(temporaryPath))
                            File.Delete(temporaryPath);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        internal static bool TryPrepareForRead(string backupPath, out ReadScope payload, out string error)
            => TryPrepareForRead(backupPath, null, out payload, out error);

        internal static bool TryPrepareForRead(string backupPath, Func<string, bool> shouldExtract,
                                               out ReadScope payload, out string error)
        {
            payload = null;
            error = null;

            if (string.IsNullOrWhiteSpace(backupPath))
            {
                error = "The backup folder is empty.";
                return false;
            }

            string payloadPath = Path.Combine(backupPath, FileName);

            if (!File.Exists(payloadPath))
            {
                payload = new ReadScope(backupPath, null);
                return true;
            }

            string extractionPath = Path.Combine(Path.GetTempPath(), "WinRestoreKit", "payload-"
                + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(extractionPath);
                string extractionRoot = Path.GetFullPath(extractionPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;

                using (ZipArchive archive = ZipFile.OpenRead(payloadPath))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (shouldExtract != null && !shouldExtract(entry.FullName))
                            continue;

                        string destination = Path.GetFullPath(Path.Combine(extractionPath, entry.FullName));

                        if (!destination.StartsWith(extractionRoot, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("The payload contains an unsafe entry path.");

                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(destination);
                            continue;
                        }

                        string directory = Path.GetDirectoryName(destination);

                        if (!string.IsNullOrEmpty(directory))
                            Directory.CreateDirectory(directory);

                        using (Stream input = entry.Open())
                        using (FileStream output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            input.CopyTo(output);
                    }
                }

                payload = new ReadScope(extractionPath, extractionPath);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;

                try
                {
                    if (Directory.Exists(extractionPath))
                        Directory.Delete(extractionPath, true);
                }
                catch (Exception)
                {
                }

                return false;
            }
        }

        private static List<SourceFile> ListPayloadFiles(string backupPath)
        {
            List<SourceFile> files = new List<SourceFile>();
            string root = Path.GetFullPath(backupPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            foreach (string path in Directory.EnumerateFiles(backupPath, "*", SearchOption.AllDirectories))
            {
                string fullPath = Path.GetFullPath(path);
                string relative = fullPath.Substring(root.Length);

                if (relative.IndexOf(Path.DirectorySeparatorChar) < 0
                    && (RootMetadata.Contains(relative)
                        || relative.StartsWith(".payload-", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                files.Add(new SourceFile(fullPath, relative.Replace(Path.DirectorySeparatorChar, '/')));
            }

            return files;
        }

        private static List<string> ListEmptyPayloadDirectories(string backupPath)
        {
            List<string> directories = new List<string>();
            string root = Path.GetFullPath(backupPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            foreach (string path in Directory.EnumerateDirectories(backupPath, "*", SearchOption.AllDirectories))
            {
                bool isEmpty = true;

                foreach (string ignored in Directory.EnumerateFileSystemEntries(path))
                {
                    isEmpty = false;
                    break;
                }

                if (isEmpty)
                {
                    string relative = Path.GetFullPath(path).Substring(root.Length)
                        .Replace(Path.DirectorySeparatorChar, '/');
                    directories.Add(relative);
                }
            }

            return directories;
        }

        private static bool ArchiveMatches(string archivePath, IReadOnlyList<SourceFile> files,
                                           IReadOnlyList<string> emptyDirectories)
        {
            using (ZipArchive archive = ZipFile.OpenRead(archivePath))
            {
                if (archive.Entries.Count != files.Count + emptyDirectories.Count)
                    return false;

                foreach (SourceFile source in files)
                {
                    ZipArchiveEntry entry = archive.GetEntry(source.EntryName);

                    if (entry == null || entry.Length != new FileInfo(source.FullPath).Length)
                        return false;
                }

                foreach (string directory in emptyDirectories)
                {
                    if (archive.GetEntry(directory + "/") == null)
                        return false;
                }
            }

            return true;
        }

        private static void RemoveEmptyDirectories(string root)
        {
            List<string> directories = new List<string>(
                Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories));

            directories.Sort((left, right) => right.Length.CompareTo(left.Length));

            foreach (string directory in directories)
            {
                if (!Directory.EnumerateFileSystemEntries(directory).GetEnumerator().MoveNext())
                    Directory.Delete(directory);
            }
        }

        private sealed class SourceFile
        {
            internal SourceFile(string fullPath, string entryName)
            {
                FullPath = fullPath;
                EntryName = entryName;
            }

            internal string FullPath { get; }

            internal string EntryName { get; }
        }

        internal sealed class ReadScope : IDisposable
        {
            private readonly string ownedPath;

            internal ReadScope(string path, string ownedPath)
            {
                Path = path;
                this.ownedPath = ownedPath;
            }

            internal string Path { get; }

            public void Dispose()
            {
                if (ownedPath == null)
                    return;

                try
                {
                    if (Directory.Exists(ownedPath))
                        Directory.Delete(ownedPath, true);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
