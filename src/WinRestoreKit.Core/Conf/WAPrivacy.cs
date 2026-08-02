using WinRestoreKit;

namespace Conf
{
    public class WAPrivacy : RegistryModule
    {
        protected override string Key => @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

        // CapabilityAccessManager ConsentStore is present on every Windows 11 install.
        protected override bool AbsenceIsNormal => false;

        public WAPrivacy()
        {
            Title = "Apps Privacy";
            Info = "This will export Application privacy settings. These settings can be found in the GUI by going to SETTINGS\\PRIVACY.";
        }
    }
}
