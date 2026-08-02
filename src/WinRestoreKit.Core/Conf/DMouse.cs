using WinRestoreKit;

namespace Conf
{
    public class DMouse : RegistryModule
    {
        protected override string Key => @"HKEY_CURRENT_USER\Control Panel\Mouse";

        // Core per-profile Control Panel key, so its absence means something is wrong.
        protected override bool AbsenceIsNormal => false;

        public DMouse()
        {
            Title = "Mouse";
            Info = "This will back up the Windows Mouse settings.";
        }
    }
}
