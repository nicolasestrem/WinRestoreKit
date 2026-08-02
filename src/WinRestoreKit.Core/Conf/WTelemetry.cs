namespace Conf
{
    public class WTelemetry : MultiKeyRegistryModule
    {
        public WTelemetry()
        {
            Title = "Telemetry";
            Info = "This will export Diagnostic data settings and services.";

            // Added with the CurrentControlSet correction below, because that correction raised the
            // stakes of restoring this module. Before it, a restore wrote into a control set the
            // running system was usually not using, and the write was inert. It now lands on the
            // live one, so it changes the configuration of a running Windows service - and the
            // post-import check can only confirm the key EXISTS, not that the values in it describe
            // a service this build can start. A key restored from another machine or another
            // Windows build can therefore leave the service permanently unable to start while the
            // row reports success, which is precisely why the user gets told before consenting
            // rather than after.
            WarningMessage = "This restores settings for a Windows system service, for all users of this PC. "
                           + "Restoring it from a backup taken on a different PC or a different Windows version "
                           + "can leave the diagnostics service unable to start, and this app cannot detect that.";

            Keys.Add(@"HKEY_LOCAL_MACHINE\Software\Policies\Microsoft\Windows\DataCollection");

            // CurrentControlSet, not ControlSet001. Windows resolves CurrentControlSet to whichever
            // control set it actually booted; ControlSet001 is merely the usual one. After a
            // LastKnownGood boot promotes a different set, ControlSet001 normally still EXISTS as a
            // stale hive, so this did not fail - it succeeded against the wrong data. The key probed
            // Present, the export wrote a file, and the module reported a green row over service
            // configuration the running system was not using. Restore then wrote back into the same
            // unused hive and reported success again.
            //
            // That is the silent-wrong-data direction rather than the cry-wolf one, which is why it
            // survived: on a machine that never had a LastKnownGood boot - including this one, where
            // HKLM\SYSTEM\Select\Current is 1 - the two paths name the same hive and the bug is
            // invisible. DPrinters.cs already used the correct idiom.
            //
            // KNOWN CONSEQUENCE, accepted deliberately: the .reg filename is derived from the key,
            // so this rename orphans the DiagTrack file in every backup taken before this version.
            // Restoring one of those reports Skipped("nothing was backed up for this item") for
            // this key while the sibling DataCollection key restores normally - which reads as
            // though nothing was ever captured, when in fact the file is sitting in the folder
            // under its old name.
            //
            // Simply looking for the old filename would be WORSE, not better: that file's contents
            // are headed [HKEY_LOCAL_MACHINE\SYSTEM\ControlSet001\...], so regedit /s would apply
            // them to the stale hive regardless of which key we passed - re-committing the exact
            // defect this change exists to remove, and reporting success for it. An honest fallback
            // has to rewrite the payload, not just find the file, and it has to be covered by the
            // pre-restore snapshot before it can claim to be undoable. Deferred as its own item;
            // the skip is the honest interim answer and is disclosed in the changelog.
            Keys.Add(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\DiagTrack");
        }

        // True for both keys, because both are REMOVABLE without anything being wrong: the
        // DataCollection policy key exists only where Group Policy or an edition difference put it
        // there, and the DiagTrack service key is a routine target of debloat scripts. Both were
        // probed on the development machine and found PRESENT, so this is not a claim that they are
        // typically missing - only that their absence is a legitimate state and not a failure.
        protected override bool AbsenceIsNormal(string key) => true;
    }
}
