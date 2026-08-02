using WinRestoreKit;

namespace Conf
{
    public class DTouchpad : RegistryModule
    {
        protected override string Key => @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\PrecisionTouchPad";

        // Absent by design on every desktop PC.
        protected override bool AbsenceIsNormal => true;

        public DTouchpad()
        {
            Title = "Touchpad";
            Info = "This will backup the Windows Touchpad settings.";
        }
    }
}
