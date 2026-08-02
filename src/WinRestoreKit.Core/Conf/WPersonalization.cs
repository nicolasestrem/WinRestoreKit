using System;

namespace Conf
{
    public class WPersonalization : MultiKeyRegistryModule
    {
        public WPersonalization()
        {
            Title = "Personalization";
            Info = "This will export settings related to Themes and Personalization (Default app mode, Color prevalence, Transparency etc).";
            RequiresExplorerRestart = true;

            Keys.Add(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            Keys.Add(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent");
        }

        // Explorer\Accent is treated as legitimately absent because it is REMOVABLE - a key
        // Windows writes on demand and that policy or a cleanup tool can take away. It was probed
        // on the development machine and found PRESENT; the flag exists for the machines where it
        // is not, so that a healthy one is never marked red.
        protected override bool AbsenceIsNormal(string key)
            => key.EndsWith(@"\Accent", StringComparison.OrdinalIgnoreCase);
    }
}
