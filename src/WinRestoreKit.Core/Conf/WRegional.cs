namespace Conf
{
    /// <summary>
    /// Regional formats and input languages for the current user: number, date, time and currency
    /// formats, the country setting, and the keyboard layouts loaded at sign-in.
    /// </summary>
    public class WRegional : MultiKeyRegistryModule
    {
        public WRegional()
        {
            Title = "Regional & input settings";
            Info = "This will back up your regional formats - how numbers, dates, times and " +
                   "currency are written - along with your country setting and the keyboard " +
                   "layouts and input languages you have added.";

            WarningMessage = "Restoring this lists your languages and keyboard layouts by " +
                             "identifier - it does not install language packs. An entry for a " +
                             "language this PC does not have stays inert until that language pack " +
                             "is installed, so the layout will not appear in the language bar. " +
                             "Number, date, time and currency formats change for your account, " +
                             "which can alter how figures read in apps that follow the system " +
                             "format. Most of this takes effect at your next sign-in rather than " +
                             "immediately.";

            // Measured 2026-07-21 on this machine: 40 values, plus subkeys Geo,
            // LanguageComponentsAvailable, User Profile and User Profile System Backup.
            Keys.Add(@"HKEY_CURRENT_USER\Control Panel\International");

            // Measured 2026-07-21: subkeys Preload, ShowToast, Substitutes and Toggle.
            Keys.Add(@"HKEY_CURRENT_USER\Keyboard Layout");

            // Those subkeys are deliberately NOT listed separately. regedit /e exports a key's
            // whole subtree, so Preload, Substitutes, Toggle, Geo and User Profile are already
            // inside the two files above; naming them again would only mint extra .reg files
            // holding data these two already carry, and give the restore a second, redundant
            // import of the same values.
        }

        // False for both: every user profile is created with Control Panel\International and
        // Keyboard Layout - measured present on this machine, and they are what Windows reads to
        // decide how to format a date and which layout to load. An absent one is a damaged profile,
        // not a machine that simply never used the feature, so it must read as a failure.
        protected override bool AbsenceIsNormal(string key) => false;
    }
}
