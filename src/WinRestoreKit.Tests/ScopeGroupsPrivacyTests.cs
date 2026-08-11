using Conf;
using System;
using System.Collections.Generic;
using System.Linq;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class ScopeGroupsPrivacyTests
    {
        [Fact]
        public void Build_RequiresExplicitOptInForUnfilteredEnvironmentVariables()
        {
            IReadOnlyList<BackupModuleRegistration> registrations = BackupModuleCatalog.CreateAll();
            IReadOnlyList<ScopeGroupRow> groups = ScopeGroups.Build();

            ScopeGroupRow appSettings = Assert.Single(groups, group => group.Name == "App settings (AppData)");
            ScopeGroupRow unfilteredEnvironment = Assert.Single(groups,
                group => group.Name == "Environment variables (unfiltered)");
            Type[] appSettingsModules = appSettings.Modules.Select(module => module.GetType()).ToArray();
            Type[] selectedUnfilteredModules = unfilteredEnvironment.Modules.Select(module => module.GetType()).ToArray();
            EEnvironment unfilteredModule = Assert.IsType<EEnvironment>(Assert.Single(unfilteredEnvironment.Modules));

            Assert.DoesNotContain(typeof(EEnvironment), appSettingsModules);
            Assert.Contains(typeof(EEnvironmentFiltered), appSettingsModules);
            Assert.Contains(typeof(EEnvironment), selectedUnfilteredModules);
            Assert.False(unfilteredEnvironment.DefaultChecked);
            Assert.True(unfilteredEnvironment.RequiresExplicitOptIn);
            Assert.Equal(unfilteredModule.WarningMessage, unfilteredEnvironment.CautionNote);

            Type[] mappedTypes = groups.SelectMany(group => group.Modules).Select(module => module.GetType()).ToArray();
            Assert.Equal(mappedTypes.Length, mappedTypes.Distinct().Count());
            Assert.Equal(
                registrations.Select(registration => registration.Module.GetType()).OrderBy(type => type.FullName),
                mappedTypes.OrderBy(type => type.FullName));
        }
    }
}
