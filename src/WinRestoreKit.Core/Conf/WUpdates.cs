using System;

namespace Conf
{
    /// <summary>
    /// Windows Update servicing state, plus the Delivery Optimization policy key when one exists.
    /// </summary>
    /// <remarks>
    /// Phase 3c retargeted this module. It used to carry
    /// <c>HKEY_LOCAL_MACHINE\Software\Policies\Microsoft\Windows\WindowsUpdate\AU</c>, the Group
    /// Policy key holding DetectionFrequency, AutoInstallMinorUpdates and the rest of the
    /// automatic-update options. That key was measured ABSENT on this machine (2026-07-21, Windows
    /// 11 Pro; reg query answers "unable to find"), which is the normal state on a consumer PC - it
    /// exists only where WSUS or Group Policy put it. It was declared AbsenceIsNormal, so it did not
    /// cry wolf; it simply reported Skipped forever and earned nothing. It has been DROPPED rather
    /// than demoted (user decision, 2026-07-21) and replaced by the Delivery Optimization policy
    /// key, which is the tweak a power user on such a machine actually makes.
    ///
    /// KNOWN CONSEQUENCE, accepted deliberately, and the same shape as WTelemetry's control-set
    /// correction: the .reg filename is derived from the key, so dropping \AU orphans the file
    ///
    ///     Windows Update_HKEY_LOCAL_MACHINE_Software_Policies_Microsoft_Windows_WindowsUpdate_AU.reg
    ///
    /// in every backup taken before this version. It is worse-reported than WTelemetry's orphan,
    /// not better: because the key is gone from Keys entirely, the restore does not even emit a
    /// Skipped("nothing was backed up for this item") row for it. The file is simply never looked
    /// at. It remains a perfectly valid .reg that a user who wants those policy values back can
    /// double-click or pass to regedit by hand - which is why this is disclosed in the changelog
    /// rather than papered over with a filename fallback. (A fallback would also be the wrong tool
    /// here for the reason BackupBase records: the file's contents name the \AU key, so applying it
    /// writes \AU regardless of which key it is handed, and nothing in this module's Backup reads
    /// \AU any more - so that write would land outside the pre-restore snapshot while SnapshotGate
    /// still reported the restore undoable.)
    /// </remarks>
    public class WUpdates : MultiKeyRegistryModule
    {
        // Measured PRESENT on this machine, 2026-07-21. Core servicing state on every install.
        private const string WindowsUpdateKey =
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate";

        // The only key C:\Windows\PolicyDefinitions\DeliveryOptimization.admx declares, and the one
        // holding DODownloadMode. Measured ABSENT on this machine, 2026-07-21, along with both
        // CurrentVersion\DeliveryOptimization paths; a registry-wide search for the value
        // DODownloadMode returned 0 matches. Absent is therefore the stock state, not a fault.
        private const string DeliveryOptimizationPolicyKey =
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";

        public WUpdates()
        {
            Title = "Windows Update";
            Info = "This will back up Windows Update's own settings for this PC, and - if peer-to-peer "
                 + "update sharing (Delivery Optimization) has been configured by policy - that "
                 + "configuration too.";

            // Delivery Optimization is a per-machine policy, and the Windows Update key below
            // carries this PC's Windows Update identity. Both facts are things a user has to know
            // BEFORE consenting to a restore, not after, so they are stated here rather than in a
            // step reason nobody reads until it is too late.
            // The second sentence of this warning is the one the Phase 3c review corrected, and the
            // correction is worth keeping visible. The first draft closed with "restoring it onto the
            // same PC it came from is what this item is for", which reads as reassurance for the
            // app's HEADLINE use case: back up, reinstall Windows, restore. But a reinstalled Windows
            // issues a NEW SusClientId, so putting the old one back re-points the fresh install at an
            // identity already registered with Windows Update - the exact confusion the warning
            // describes for a different PC, arriving on the same physical machine. Naming only the
            // different-PC case would have told users the risky scenario was the safe one.
            WarningMessage = "The Windows Update settings saved here include the ID numbers Windows Update "
                           + "uses to recognise this particular installation of Windows. Restoring them "
                           + "onto a DIFFERENT PC - or onto the same PC after reinstalling Windows, which "
                           + "gives it new ID numbers - puts the old identity back, and that can confuse "
                           + "Windows Update, or a company update server, about which machine is which.\n\n"
                           + "Nothing here is needed to make Windows Update work: a fresh install sorts "
                           + "its own settings out. Restore this item when you want your update "
                           + "preferences back on a machine you have not reinstalled, and leave it "
                           + "unticked after a reinstall.\n\n"
                           + "Delivery Optimization is only saved when someone has configured it. On a PC "
                           + "where nobody changed it, this item reports that it is not present - that is "
                           + "normal and not an error.";

            // Unchanged from the previous version, deliberately: keeping this key spelled exactly
            // as it was keeps its key-derived filename identical, so existing backups still restore
            // it. It is NOT orphaned; only the dropped \AU file is.
            Keys.Add(WindowsUpdateKey);
            Keys.Add(DeliveryOptimizationPolicyKey);
        }

        // False for the servicing key (measured present; every install has it, so missing is a real
        // fault), true for the Delivery Optimization policy key (measured absent on a stock machine,
        // where it materialises only once someone configures peer-to-peer sharing). Matched on the
        // key that may be absent rather than by excluding the one that may not, so adding a third
        // key later has to make a decision instead of inheriting one.
        protected override bool AbsenceIsNormal(string key)
            => string.Equals(key, DeliveryOptimizationPolicyKey, StringComparison.OrdinalIgnoreCase);
    }
}
