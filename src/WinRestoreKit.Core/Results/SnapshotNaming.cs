using System;
using System.Globalization;

namespace WinRestoreKit
{
    /// <summary>
    /// Names the folder a pre-restore snapshot is written into.
    /// </summary>
    /// <remarks>
    /// The timestamp is a PARAMETER, never <c>Data.NowShort</c>. That field is stamped once at
    /// process start (DataHelper.cs:34), so reusing it would write the snapshot into the same folder
    /// as this session's own backup, and two restores in one session would collide with each other.
    ///
    /// Seconds are part of the format for the same reason: two restores inside one minute is the
    /// expected pattern when the first one goes wrong, which is precisely when the folder that could
    /// undo it must not be overwritten.
    ///
    /// InvariantCulture because the folder name is an artifact users read and re-open months later;
    /// a machine whose culture supplies different digits or separators would produce a second naming
    /// scheme in the same directory. Note that Data.Now/Data.NowShort interpolate under the CURRENT
    /// culture - that inconsistency is left alone here, since changing it would rename the folders
    /// existing installs already write into.
    /// </remarks>
    public static class SnapshotNaming
    {
        /// <summary>Marks the folder as a snapshot; BackupFolders.IsSnapshot is the sole reader.</summary>
        public const string Suffix = " (pre-restore)";

        private const string TimestampFormat = "yyyy-MM-dd - HH.mm.ss";

        /// <summary>How many suffixed candidates <see cref="Unique"/> will try before giving up.</summary>
        private const int MaxCandidates = 999;

        public static string NameFor(DateTime now)
            => now.ToString(TimestampFormat, CultureInfo.InvariantCulture) + Suffix;

        /// <summary>
        /// <paramref name="name"/>, or the first " (2)", " (3)", ... variant for which
        /// <paramref name="exists"/> answers false.
        /// </summary>
        /// <remarks>
        /// Bounded rather than looping until it wins: <paramref name="exists"/> reaches the file
        /// system, and a probe that answers true for everything - a denied directory, a path that
        /// has grown too long - would otherwise hang the restore before it had shown its dialog.
        /// Exhausting the range throws so the caller reports a snapshot folder it could not create,
        /// which SnapshotGate already has a row for, rather than silently reusing an occupied name.
        /// </remarks>
        public static string Unique(string name, Func<string, bool> exists)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A snapshot name is required.", nameof(name));

            if (exists == null)
                throw new ArgumentNullException(nameof(exists));

            if (!exists(name))
                return name;

            for (int n = 2; n <= MaxCandidates; n++)
            {
                string candidate = name + " (" + n.ToString(CultureInfo.InvariantCulture) + ")";

                if (!exists(candidate))
                    return candidate;
            }

            throw new InvalidOperationException(
                "Could not find an unused snapshot folder name for \"" + name + "\".");
        }
    }
}
