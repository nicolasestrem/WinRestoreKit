using WinRestoreKit;
using DataHelper;
using System.Collections.Generic;

namespace Conf
{
    public class APinnedApps : FolderModule
    {
        public APinnedApps()
            : base(Data.LocalAppData + "\\Packages\\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\\LocalState")
        {
            Title = "Remember pinned app preferences";
            Info = "The Start menu is comprised of three sections: Pinned, All apps, and Recommended.\n"
                 + "This will back up the pinned items on the Start menu and put them back. Windows builds "
                 + "this list for this specific PC and this specific Windows build, so it is meant for "
                 + "restoring onto the PC it came from.";

            // The roadmap records why this module was KEPT rather than retired: the Start menu
            // database it copies is a build-specific, notoriously non-portable file, but restoring
            // it on the same machine - after a reset, or to undo a bad pin session - is a genuine
            // use case. What is NOT acceptable is keeping that caveat out of the text the user
            // reads before consenting. The old warning ("This is reserved for Windows 11.") named
            // the one consequence that is harmless and omitted the one that is not.
            //
            // The undetectable clause is the important half. A restore here is a folder copy: it
            // succeeds whenever the files copy, and nothing downstream can tell a correct pin list
            // from one this build of Windows will render as wrong icons or as nothing at all. The
            // row reads Succeeded either way, so the text is the only warning that exists.
            WarningMessage = "This copies the Start menu's list of pinned apps, which Windows builds for this "
                           + "specific PC and this specific version of Windows. Restoring it on a different PC, "
                           + "or after a major Windows update, can bring back the wrong pins or leave the Start "
                           + "menu empty - and this app cannot detect that, so it will still report success. "
                           + "Restoring it on the same PC it came from is what this item is for. Windows 11 only.";
        }

        // No consent: Windows brings StartMenuExperienceHost back within seconds on its own, so the
        // user is not being asked to give up anything they can see. A checkbox here would spend the
        // dialog's attention budget on the one close that costs nothing, at the expense of the
        // closes that do.
        public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore
            => new[]
            {
                new RestoreCloseRequirement("StartMenuExperienceHost", "the Start menu", false)
            };
    }
}
