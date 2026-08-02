using WinRestoreKit;

namespace Conf
{
    public class WOther : RegistryModule
    {
        protected override string Key => @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";

        // HKLM policies key holding the UAC values; always present.
        protected override bool AbsenceIsNormal => false;

        public WOther()
        {
            Title = "Other Windows settings";
            Info = "This will backup User Account Control settings, remote restrictions and configuration.";
        }
    }
}
