using System;

namespace Conf
{
    public class DPrinters : MultiKeyRegistryModule
    {
        public DPrinters()
        {
            Title = "Printers";
            Info = "This will backup the Windows Printers configuration.";
            WarningMessage = "The restoration of this backup could affect your printer configurations. Proceed with caution.";

            Keys.Add(@"HKEY_CURRENT_USER\Printers");
            Keys.Add(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Print\Printers");
        }

        // The per-user HKCU\Printers key is populated lazily and is legitimately absent on an
        // account that has never added a printer. The HKLM key under Print\Printers is created by
        // the spooler on every Windows install, so its absence means something is wrong.
        protected override bool AbsenceIsNormal(string key)
            => key.StartsWith(@"HKEY_CURRENT_USER\", StringComparison.OrdinalIgnoreCase);
    }
}
