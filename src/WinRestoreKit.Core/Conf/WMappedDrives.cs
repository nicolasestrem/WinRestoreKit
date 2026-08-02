using WinRestoreKit;

namespace Conf
{
    /// <summary>
    /// The current user's mapped network drives - the drive-letter-to-share definitions, not the
    /// credentials that open them.
    /// </summary>
    public class WMappedDrives : RegistryModule
    {
        // One subkey per mapped drive letter, each holding RemotePath, ProviderName and friends.
        // Measured 2026-07-21 on this machine: present, with subkeys Y and Z for two mapped drives.
        protected override string Key => @"HKEY_CURRENT_USER\Network";

        // Absent is the normal state for a machine that has never mapped a drive: Windows creates
        // this key when the first mapping is made and removes it when the last one goes, so marking
        // its absence red would cry wolf on every standalone PC.
        protected override bool AbsenceIsNormal => true;

        public WMappedDrives()
        {
            Title = "Mapped network drives";
            Info = "This will back up the network drives you have mapped to drive letters - which " +
                   "server share each letter points at. The passwords saved for those drives are " +
                   "not part of this; they live in Windows Credential Manager, which this item " +
                   "does not read.";

            WarningMessage = "Restoring this merges the drive-letter definitions from your backup " +
                             "back in, so a mapping you added since the backup is left alone unless " +
                             "it uses a letter the backup also claims. Saved credentials are NOT " +
                             "included - they are held in Windows Credential Manager, not in this " +
                             "key - so a drive that needs a user name and password will ask for " +
                             "them the first time you open it. Restored mappings appear at your " +
                             "next sign-in rather than immediately, and a mapping to a server that " +
                             "no longer exists comes back as a disconnected drive.";
        }
    }
}
