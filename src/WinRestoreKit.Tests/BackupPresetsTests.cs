using Conf;
using System.Linq;
using Xunit;

namespace WinRestoreKit.Tests
{
    // Presets are curated module-name lists (Phase 4 PR 6, second half). The literals are pinned so a
    // membership change is a deliberate, visible edit; and every name must resolve to a registered
    // module, so renaming a module without updating its preset turns red here rather than silently
    // dropping it from the selection.
    public class BackupPresetsTests
    {
        [Fact]
        public void DeveloperMachine_IsPinnedLiteral()
        {
            Assert.Equal(
                new[] { "ETerminal", "EVSCode", "ESsh", "EEnvironment", "EHosts" },
                Views.BackupPresets.DeveloperMachine);
        }

        [Fact]
        public void MinimalPrivacySafeExclusions_IsPinnedLiteral()
        {
            Assert.Equal(
                new[] { "WUpdates", "EEnvironment", "EEnvironmentFiltered", "CWiFiConf" },
                Views.BackupPresets.MinimalPrivacySafeExclusions);
        }

        [Fact]
        public void PresetNames_AllResolveToRegisteredModules()
        {
            System.Collections.Generic.HashSet<string> registered = ModuleCatalog.CreateAll()
                .Select(r => r.Module.GetType().Name)
                .ToHashSet();

            foreach (string name in Views.BackupPresets.DeveloperMachine
                .Concat(Views.BackupPresets.MinimalPrivacySafeExclusions))
            {
                Assert.True(registered.Contains(name),
                    "preset names a module not in the catalog: " + name);
            }
        }
    }
}
