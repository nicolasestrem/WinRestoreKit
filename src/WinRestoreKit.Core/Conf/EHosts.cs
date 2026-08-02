using DataHelper;
using System.IO;

namespace Conf
{
    /// <summary>
    /// The system-wide <c>hosts</c> file.
    /// </summary>
    /// <remarks>
    /// The one module in the Developer category that writes outside the current user's profile.
    /// %WINDIR%\System32\drivers\etc\hosts is machine-wide and read by every program that resolves
    /// a name, so restoring it changes where OTHER accounts' browsers and tools connect - which is
    /// why it carries a WarningMessage even though the mechanics are an ordinary file copy.
    ///
    /// Writing it needs elevation. The app manifests highestAvailable, so the ordinary case is
    /// covered; an unelevated run produces an honest Failed step out of the copy primitive rather
    /// than a special case here. Deliberately no pre-flight elevation probe: it would report the
    /// same fact one step earlier while adding a second place that has to agree with the first
    /// about what "can write" means.
    ///
    /// Note that ModuleTargetTests.Themes_WritesNothingMachineWide is WThemes-specific on purpose
    /// and is NOT a global sweep. This module would legitimately fail such a sweep, which is the
    /// reason that test names the module it constrains.
    /// </remarks>
    public class EHosts : FileModule
    {
        public EHosts()
        {
            Title = "Hosts file";
            Info = "This will back up the Windows hosts file, where manual name-to-address mappings live - the entries used to point a domain at a local server or to block one. It affects every user and every program on this PC, not just your account.";
            WarningMessage = "The hosts file is shared by the whole PC. Restoring it replaces the mappings every user and program on this machine resolves names through, and can redirect or block sites system-wide. Entries added since the backup are lost, not merged.";

            Files.Add(Path.Combine(Data.WindowsFolder, "System32", "drivers", "etc", "hosts"));
        }

        // False: every Windows install ships this file, and Windows does not remove it. Absent
        // means something is wrong with the install (or the probe could not read System32), and
        // both deserve a red row - a green "not present on this system" here would describe a
        // healthy machine, which this is not.
        protected override bool AbsenceIsNormal(string file) => false;

        // No close requirement. Nothing holds hosts open: the DNS Client service reads it on
        // demand and does not lock it. Declaring one would close a system service for no gain.
    }
}
