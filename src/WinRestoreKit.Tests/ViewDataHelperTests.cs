using Conf;
using System;
using System.Collections.Generic;
using System.Linq;
using Views;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class ViewDataHelperTests
    {
        [Fact]
        public void WatchedGroups_GetCurrent_ReflectsEveryCatalogCategory()
        {
            IReadOnlyList<ModuleRegistration> registrations = ModuleCatalog.CreateAll();
            IReadOnlyList<WatchedGroupSummary> groups = WatchedGroups.GetCurrent();

            Assert.Equal(
                registrations.Select(registration => registration.Category).Distinct(),
                groups.Select(group => group.Name));

            foreach (WatchedGroupSummary group in groups)
            {
                int expectedCount = registrations.Count(registration => registration.Category == group.Name);
                Assert.Equal($"{expectedCount} items", group.Count);
            }
        }

        [Fact]
        public void ScopeGroups_Build_UsesEightFixedScopesAndAssignsEachCatalogModuleOnce()
        {
            IReadOnlyList<ModuleRegistration> registrations = ModuleCatalog.CreateAll();
            IReadOnlyList<ScopeGroupRow> groups = ScopeGroups.Build();
            string[] expectedScopeNames =
            [
                "Registry branches",
                "Explorer & shell",
                "Power & devices",
                "Installed drivers",
                "Fonts",
                "Scheduled tasks",
                "Network profiles",
                "App settings (AppData)"
            ];
            IReadOnlyDictionary<string, Type[]> expectedModules = new Dictionary<string, Type[]>
            {
                ["Registry branches"] = Array.Empty<Type>(),
                ["Explorer & shell"] =
                [
                    typeof(WPersonalization),
                    typeof(WVisualEffects),
                    typeof(WTaskbar),
                    typeof(WThemes),
                    typeof(APinnedApps)
                ],
                ["Power & devices"] =
                [
                    typeof(WPowerPlans),
                    typeof(DPrinters),
                    typeof(DMouse),
                    typeof(DKeyboard),
                    typeof(DTouchpad)
                ],
                ["Installed drivers"] = Array.Empty<Type>(),
                ["Fonts"] = [typeof(WFonts)],
                ["Scheduled tasks"] = Array.Empty<Type>(),
                ["Network profiles"] =
                [
                    typeof(WNetworkConf),
                    typeof(WMappedDrives),
                    typeof(CWiFiConf),
                    typeof(EHosts)
                ],
                ["App settings (AppData)"] =
                [
                    typeof(WPrivacy),
                    typeof(WAPrivacy),
                    typeof(WTelemetry),
                    typeof(WUpdates),
                    typeof(WAccessibility),
                    typeof(WRegional),
                    typeof(WOther),
                    typeof(AppStoreApps),
                    typeof(GGaming),
                    typeof(ETerminal),
                    typeof(EVSCode),
                    typeof(ESsh),
                    typeof(EEnvironment),
                    typeof(EEnvironmentFiltered)
                ]
            };

            Assert.Equal(expectedScopeNames, groups.Select(group => group.Name));

            foreach (ScopeGroupRow group in groups)
            {
                Assert.Equal(expectedModules[group.Name], group.Modules.Select(module => module.GetType()));
                Assert.Equal(group.Modules.Any(module => module.IsInstalled()), group.DefaultChecked);
                Assert.Equal("--", group.SizeLabel);

                if (group.Modules.Count == 0)
                    Assert.Equal("No supported items detected", group.Detail);
                else
                    Assert.False(string.IsNullOrWhiteSpace(group.Detail));
            }

            Type[] mappedTypes = groups.SelectMany(group => group.Modules).Select(module => module.GetType()).ToArray();
            Assert.Equal(mappedTypes.Length, mappedTypes.Distinct().Count());
            Assert.Equal(
                registrations.Select(registration => registration.Module.GetType()).OrderBy(type => type.FullName),
                mappedTypes.OrderBy(type => type.FullName));
        }
    }
}
