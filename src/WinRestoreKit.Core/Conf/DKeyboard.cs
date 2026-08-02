using WinRestoreKit;

namespace Conf
{
    public class DKeyboard : RegistryModule
    {
        protected override string Key => @"HKEY_CURRENT_USER\Control Panel\Keyboard";

        // Core per-profile Control Panel key, so its absence means something is wrong.
        protected override bool AbsenceIsNormal => false;

        public DKeyboard()
        {
            Title = "Keyboard";
            Info = "This will back up the Windows Keyboard settings.";
        }
    }
}
