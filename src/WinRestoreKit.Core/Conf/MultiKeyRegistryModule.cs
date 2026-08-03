using WinRestoreKit;
using System.Collections.Generic;
using System.IO;

namespace Conf
{
    /// <summary>
    /// A module that backs up a list of registry keys, one .reg file per key.
    /// </summary>
    /// <remarks>
    /// Five modules carried this template verbatim - the loops, the any-key IsInstalled, the
    /// read-at-access-time RestoreTargets - differing only in their keys and their per-key
    /// absence rule. The subclass supplies data; the decision logic is written once here, so the
    /// Skipped-vs-Failed rule cannot drift between modules that are supposed to behave
    /// identically. The single-key sibling is <see cref="RegistryModule"/>, which exists
    /// separately because it also carries a filename-compatibility promise this base must not
    /// inherit: its files are named {Title}.reg, while multi-key files must be named per key.
    /// </remarks>
    public abstract class MultiKeyRegistryModule : BackupBase
    {
        /// <summary>The registry keys this module captures, one export file per key.</summary>
        /// <remarks>
        /// A public mutable field, not a property, and populated by subclass constructors. Both
        /// halves are load-bearing for the tests: BackupFileNamingTests discovers multi-key
        /// modules via <c>GetField("Keys")</c>, and its mutation test appends a synthetic key
        /// after construction and observes the filename RestoreAsync computes - which only works
        /// because everything below reads this list at access time rather than capturing it.
        /// </remarks>
        public List<string> Keys = new List<string>();

        /// <summary>
        /// Whether <paramref name="key"/> can legitimately be missing on a healthy Windows 11
        /// install.
        /// </summary>
        /// <remarks>
        /// Per key, and it cannot be inferred from IsInstalled(): that returns true as soon as
        /// any one key exists, so "installed" says nothing about the others. Getting this wrong
        /// is the cry-wolf failure in either direction - false on an often-absent key marks
        /// healthy machines red, true on a core key hides a real problem.
        /// </remarks>
        protected abstract bool AbsenceIsNormal(string key);

        public override bool IsInstalled()
        {
            foreach (string k in Keys)
            {
                if (Utils.KeyExists(k))
                    return true;
            }

            return false;
        }

        // Read from Keys on every access rather than captured once: Keys is a mutable public
        // field filled by subclass constructors, so a snapshot taken at construction would
        // silently describe a different set of keys than the one Restore actually imports.
        public override IReadOnlyList<RestoreTarget> RestoreTargets
        {
            get
            {
                List<RestoreTarget> targets = new List<RestoreTarget>();

                foreach (string k in Keys)
                    targets.Add(RestoreTarget.RegistryKey(k));

                return targets;
            }
        }

        public override ModuleResult Backup(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            foreach (string k in Keys)
            {
                string outputFileName = Path.Combine(path, RegFileNameFor(k));
                steps.Add(Utils.ExportRegistryKey(outputFileName, k, AbsenceIsNormal(k)));
            }

            return ModuleResult.Aggregate(steps);
        }

        public override ModuleResult Restore(string path)
        {
            List<StepResult> steps = new List<StepResult>();

            foreach (string k in Keys)
            {
                string inputFileName = Path.Combine(path, RegFileNameFor(k));
                steps.Add(Utils.ImportRegistryKey(inputFileName, k));
            }

            return ModuleResult.Aggregate(steps);
        }

        /// <remarks>
        /// ANY key's file counts. A partial export - some keys absent on the source machine, which
        /// AbsenceIsNormal makes a routine outcome - still leaves a restorable backup, so requiring
        /// all of them would hide it.
        /// </remarks>
        public override bool? HasArtifactIn(string backupPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath))
                return false;

            foreach (string k in Keys)
            {
                if (File.Exists(Path.Combine(backupPath, RegFileNameFor(k))))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Reports confirmed drift from any recorded key, while keeping incomplete comparisons unknown.
        /// </summary>
        public override bool? HasDriftedFrom(string backupPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath))
                return null;

            bool comparedAny = false;
            bool hasUnknown = false;

            foreach (string key in Keys)
            {
                bool? drift = HasDriftedFromRegistryArtifact(
                    Path.Combine(backupPath, RegFileNameFor(key)),
                    key);

                if (!drift.HasValue)
                {
                    hasUnknown = true;
                    continue;
                }

                comparedAny = true;

                if (drift.Value)
                    return true;
            }

            return comparedAny && !hasUnknown ? false : null;
        }
    }
}
