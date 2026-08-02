using WinRestoreKit;

namespace Conf
{
    public class WVisualEffects : RegistryModule
    {
        protected override string Key => @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";

        // Unverified judgement, and the borderline call in this set.
        protected override bool AbsenceIsNormal => true;

        public WVisualEffects()
        {
            Title = "Visual Effects";
            Info = "This will export all Windows Visual Effects settings. These settings can be found in the GUI by Start menu or Run box 'SystemPropertiesPerformance'.";
            RequiresExplorerRestart = true;
        }
    }
}
