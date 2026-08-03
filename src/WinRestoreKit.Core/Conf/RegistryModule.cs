using WinRestoreKit;
using System.Collections.Generic;
using System.IO;

namespace Conf
{
    /// <summary>
    /// A module that backs up exactly one registry key to <c>{Title}.reg</c>.
    /// </summary>
    /// <remarks>
    /// Ten modules share this shape. The subclass supplies data - a key, whether that key can
    /// legitimately be absent - and inherits the decision logic, so the Skipped-vs-Failed rule is
    /// written once and cannot drift between modules that are supposed to behave identically.
    /// </remarks>
    public abstract class RegistryModule : BackupBase
    {
        /// <summary>The single registry key this module captures.</summary>
        protected abstract string Key { get; }

        /// <summary>
        /// Whether this key can legitimately be missing on a healthy Windows 11 install.
        /// </summary>
        /// <remarks>
        /// Getting this wrong is the cry-wolf failure in either direction: false on a key that is
        /// often absent marks healthy machines red, true on a core key hides a real problem.
        /// </remarks>
        protected abstract bool AbsenceIsNormal { get; }

        public override bool IsInstalled() => Utils.KeyExists(Key);

        // Written once for all ten subclasses, from the same Key the import uses, so a subclass
        // cannot declare one key and overwrite another.
        public override IReadOnlyList<RestoreTarget> RestoreTargets
            => new[] { RestoreTarget.RegistryKey(Key) };

        public override ModuleResult Backup(string path)
            => ModuleResult.Aggregate(new[]
            {
                Utils.ExportRegistryKey(FileFor(path), Key, AbsenceIsNormal)
            });

        public override ModuleResult Restore(string path)
            => ModuleResult.Aggregate(new[]
            {
                Utils.ImportRegistryKey(FileFor(path), Key)
            });

        /// <remarks>
        /// One key, one .reg file, and its name is fixed - so presence is a straight File.Exists
        /// and this module never has to answer "cannot tell".
        /// </remarks>
        public override bool? HasArtifactIn(string backupPath)
            => !string.IsNullOrWhiteSpace(backupPath) && File.Exists(FileFor(backupPath));

        /// <summary>
        /// Compares the one exported key this module records with its current live representation.
        /// </summary>
        public override bool? HasDriftedFrom(string backupPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath))
                return null;

            return HasDriftedFromRegistryArtifact(FileFor(backupPath), Key);
        }

        // One key, so one file, and the name does not need to encode which key it holds. Overriding
        // rather than inheriting the key-derived default keeps the filenames these ten modules have
        // always written, so existing backups stay restorable.
        protected override string RegFileNameFor(string key) => Title + ".reg";

        // Path.Combine rather than concatenation. Produces byte-identical paths today because
        // Data.DataRootDir and the restore wizard both hand us a trailing separator, but that is a field
        // contract to honour, not a coincidence to depend on.
        private string FileFor(string path) => Path.Combine(path, RegFileNameFor(Key));
    }
}
