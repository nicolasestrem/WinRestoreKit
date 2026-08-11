using System.Collections.Generic;

namespace WinRestoreKit
{
    public static class BackupPresets
    {
        public static readonly IReadOnlyList<string> DeveloperMachine =
            new[] { "ETerminal", "EVSCode", "ESsh", "EEnvironment", "EHosts" };

        public static readonly IReadOnlyList<string> MinimalPrivacySafeExclusions =
            new[] { "WUpdates", "EEnvironment", "EEnvironmentFiltered", "CWiFiConf" };
    }
}
