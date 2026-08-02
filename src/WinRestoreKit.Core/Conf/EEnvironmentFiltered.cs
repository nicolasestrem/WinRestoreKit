using WinRestoreKit;
using System;
using System.IO;

namespace Conf
{
    /// <summary>
    /// The current user's environment variables with likely credentials held back
    /// (<c>HKCU\Environment</c>, filtered).
    /// </summary>
    /// <remarks>
    /// The opt-in half of a deliberate pair. <see cref="EEnvironment"/> exports the key whole and
    /// says plainly that secrets go with it; this exports the same key and holds back the values
    /// whose NAMES look like credentials, naming every one it held back. Both appear in the tree,
    /// the user ticks whichever they want, and the tree checkbox is the entire opt-in mechanism -
    /// this app has no settings surface, and inventing one for a single choice would be a larger
    /// change than the feature.
    ///
    /// Ticking both is meaningful, not a mistake: two files, one complete and one shareable.
    ///
    /// WHY THIS EXISTS ALONGSIDE, RATHER THAN INSTEAD. EEnvironment's own remarks used to argue
    /// against building a filter at all, on the grounds that name guesswork is wrong in both
    /// directions - it drops real settings and still misses secrets named differently. That
    /// objection was right about the filter and wrong about the conclusion, and the difference is
    /// which failure is SILENT:
    ///
    ///   - a false positive is announced. The held-back name is in the step reason the user reads
    ///     at backup time, and the unfiltered module is one tick away.
    ///   - a false negative is announced too, in the only way it can be: this module never claims
    ///     the export is free of secrets, only that names matching a stated list were removed.
    ///
    /// What would have been indefensible is REPLACING the plain export with a filtered one, or
    /// filtering quietly. Neither happens here. The remaining risk is a user who reads the title as
    /// a guarantee, which is why Info spends its second paragraph saying it is not one.
    ///
    /// The three limitations EEnvironment discloses - additive-merge restore, no WM_SETTINGCHANGE
    /// broadcast, plaintext on disk - all apply here unchanged. Plaintext especially: what is left
    /// after filtering is still unencrypted, and a variable this did not recognise is sitting in it.
    /// </remarks>
    public class EEnvironmentFiltered : RegistryModule
    {
        public EEnvironmentFiltered()
        {
            Title = "Environment variables (excluding likely secrets)";
            Info = "This backs up your account's environment variables the same way as the full version, except that it leaves out any variable whose NAME looks like it holds a credential - names containing TOKEN, SECRET, PASSWORD, API_KEY and similar. Every variable it leaves out is listed in the result, so you can see exactly what was held back.\n\nThis is not a guarantee that the backup holds no secrets. It matches names, so it catches GITHUB_TOKEN and does not catch a token you called something else. Treat the backup folder as sensitive either way. If you want everything, including the variables named like credentials, use \"Environment variables\" instead.\n\nRestoring merges the saved variables back: variables in the backup are overwritten, and anything you have added since - including the credentials this left out - is left alone. Programs already running keep the values they started with until you restart them.";
            WarningMessage = "This leaves out variables whose names look like credentials, which means the backup is deliberately INCOMPLETE. Restoring it will not bring those variables back, on this machine or any other - if you are rebuilding a PC from this backup, the credentials in your environment are not in it and you will need them from wherever else you keep them.\n\nIt also matches on names only, so a secret stored under a name it does not recognise is still written to the backup folder unencrypted.";
        }

        // The same key EEnvironment reads. Two modules over one key is the point: they differ in
        // what they keep, not in what they look at.
        protected override string Key => @"HKEY_CURRENT_USER\Environment";

        // False, for the reason EEnvironment gives: Windows creates this key with the account, so
        // its absence is a broken profile or a failed probe, and both deserve red.
        protected override bool AbsenceIsNormal => false;

        /// <summary>
        /// Exports the key, then rewrites the export without the values that look like credentials.
        /// </summary>
        /// <remarks>
        /// Two steps rather than one because they can fail independently and the user needs to know
        /// which. Restore is inherited unchanged - a filtered .reg is an ordinary .reg with fewer
        /// values in it, and RegistryModule already reads the file this Title names.
        /// </remarks>
        public override ModuleResult Backup(string path)
        {
            string file = Path.Combine(path, RegFileNameFor(Key));

            StepResult export = Utils.ExportRegistryKey(file, Key, AbsenceIsNormal);

            // Nothing on disk to filter. Skipped or Failed, either way the export step carries the
            // reason and a filter step would only repeat it.
            if (export.State != ResultState.Succeeded)
                return ModuleResult.Aggregate(new[] { export });

            RegFilterOutcome outcome = RegSecretFilter.FilterInPlace(file);

            if (!outcome.Ok)
                return ModuleResult.Aggregate(new[] { export, AbandonUnfiltered(file, outcome.Error) });

            return ModuleResult.Aggregate(new[] { export, Describe(outcome) });
        }

        /// <summary>
        /// Deletes the export the filter could not process, and fails the step.
        /// </summary>
        /// <remarks>
        /// The file at this point is a COMPLETE export - every variable, credentials included -
        /// sitting under a filename that says it excludes them. Leaving it there would be the worst
        /// outcome this module can produce: the user reads a red step, believes the backup did not
        /// happen, and their tokens are on disk anyway. It is the same reasoning as
        /// Utils.AbandonExport, applied to a different lie about the same file.
        ///
        /// A delete failure is folded into the reason rather than swallowed, because then the file
        /// really is still there and the user has to be told to remove it themselves.
        /// </remarks>
        /// <remarks>
        /// Internal rather than private so the tests can reach it. Its behaviour - the unfiltered
        /// file is GONE afterwards - is the one thing on this path that protects the user, and the
        /// only other way to reach it is to make regedit fail on demand.
        /// </remarks>
        internal StepResult AbandonUnfiltered(string file, string reason)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                return StepResult.Failed(Key,
                    "could not filter the export (" + reason + "), and the unfiltered copy could " +
                    "not be removed either (" + ex.Message + ") - delete " + file + " yourself, it " +
                    "contains every variable including any credentials");
            }

            return StepResult.Failed(Key,
                "could not filter the export, so it was discarded rather than left holding " +
                "credentials it promised to exclude: " + reason);
        }

        /// <summary>
        /// States what was held back, by name.
        /// </summary>
        /// <remarks>
        /// The names go in the reason rather than only into the log. This is the one moment the
        /// user can act on the information - at restore time the variables are simply not there,
        /// and nothing at that point can tell them whether that was this filter or a machine that
        /// never had them.
        /// </remarks>
        internal StepResult Describe(RegFilterOutcome outcome)
        {
            if (outcome.Removed.Count == 0)
                return StepResult.Succeeded(Key,
                    "exported; no variable name matched the credential list, so nothing was held back");

            return StepResult.Succeeded(Key,
                "exported, holding back " + outcome.Removed.Count + " variable(s) whose names look " +
                "like credentials: " + string.Join(", ", outcome.Removed));
        }
    }
}
