using WinRestoreKit;

namespace Conf
{
    public class WAccessibility : RegistryModule
    {
        protected override string Key => @"HKEY_CURRENT_USER\Control Panel\Accessibility";

        // Core per-profile Control Panel key, so its absence means something is wrong.
        protected override bool AbsenceIsNormal => false;

        public WAccessibility()
        {
            Title = "Accessibility";
            Info = "This will backup settings related to accessibility resources for blind access, hearing, dexterity, mobility, focus, and more. ";
        }
    }
}
