using System.Collections.Generic;

namespace Views
{
    /// <summary>
    /// Named selections of module type names: pure UI curation, no engine concept. "Everything on
    /// this PC" is not a list - it is today's Select-available (<c>SelectInstalled</c>) - so only the
    /// two membership lists live here.
    /// </summary>
    /// <remarks>
    /// Membership is a deliberate curation decision the reviewer must see apart from the layout diff,
    /// which is why presets ship in their own PR after the Backup page rebuild. Changing membership is
    /// a one-line edit here plus its literal test.
    /// </remarks>
    internal static class BackupPresets
    {
        /// <summary>Type names for the Developer machine preset.</summary>
        internal static readonly IReadOnlyList<string> DeveloperMachine = new[]
        {
            "ETerminal",
            "EVSCode",
            "ESsh",
            "EEnvironment",
            "EHosts"
        };

        /// <summary>
        /// Type names EXCLUDED by Minimal privacy-safe: the Windows Update client identity, both env
        /// var variants, and the Wi-Fi keys.
        /// </summary>
        internal static readonly IReadOnlyList<string> MinimalPrivacySafeExclusions = new[]
        {
            "WUpdates",
            "EEnvironment",
            "EEnvironmentFiltered",
            "CWiFiConf"
        };
    }
}
