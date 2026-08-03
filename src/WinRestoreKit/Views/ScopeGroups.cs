using Conf;
using System;
using System.Collections.Generic;
using System.Linq;
using WinRestoreKit;

namespace Views
{
    internal sealed class ScopeGroupRow
    {
        internal ScopeGroupRow(string name, string detail, string sizeLabel, bool defaultChecked,
                               IReadOnlyList<BackupBase> modules)
        {
            Name = name;
            Detail = detail;
            SizeLabel = sizeLabel;
            DefaultChecked = defaultChecked;
            Modules = modules;
        }

        internal string Name { get; }

        internal string Detail { get; }

        internal string SizeLabel { get; }

        internal bool DefaultChecked { get; }

        internal IReadOnlyList<BackupBase> Modules { get; }
    }

    internal static class ScopeGroups
    {
        private const int DetailLimit = 96;

        private static readonly ScopeDefinition[] Definitions =
        {
            new ScopeDefinition("Registry branches", static module => false),
            new ScopeDefinition("Explorer & shell", IsExplorerAndShell),
            new ScopeDefinition("Power & devices", IsPowerAndDevices),
            new ScopeDefinition("Installed drivers", static module => false),
            new ScopeDefinition("Fonts", static module => module is WFonts),
            new ScopeDefinition("Scheduled tasks", static module => false),
            new ScopeDefinition("Network profiles", IsNetworkProfile),
            new ScopeDefinition("App settings (AppData)", IsAppSetting)
        };

        internal static IReadOnlyList<ScopeGroupRow> Build()
        {
            IReadOnlyList<ModuleRegistration> registrations = ModuleCatalog.CreateAll();
            Dictionary<ScopeDefinition, List<BackupBase>> modulesByScope = Definitions.ToDictionary(
                definition => definition,
                definition => new List<BackupBase>());

            foreach (ModuleRegistration registration in registrations)
            {
                ScopeDefinition match = null;

                foreach (ScopeDefinition definition in Definitions)
                {
                    if (!definition.Includes(registration.Module))
                        continue;

                    if (match != null)
                        throw new InvalidOperationException(
                            $"Module '{registration.Module.GetType().FullName}' belongs to multiple backup scopes.");

                    match = definition;
                }

                if (match == null)
                    throw new InvalidOperationException(
                        $"Module '{registration.Module.GetType().FullName}' has no backup scope.");

                modulesByScope[match].Add(registration.Module);
            }

            return Definitions
                .Select(definition =>
                {
                    IReadOnlyList<BackupBase> modules = modulesByScope[definition];

                    return new ScopeGroupRow(
                        definition.Name,
                        modules.Count == 0 ? "No supported items detected" : Truncate(modules[0].Info),
                        "--",
                        modules.Any(module => module.IsInstalled()),
                        modules);
                })
                .ToList();
        }

        private static bool IsExplorerAndShell(BackupBase module)
        {
            return module is WPersonalization
                or WVisualEffects
                or WTaskbar
                or WThemes
                or APinnedApps;
        }

        private static bool IsPowerAndDevices(BackupBase module)
        {
            return module is WPowerPlans
                or DPrinters
                or DMouse
                or DKeyboard
                or DTouchpad;
        }

        private static bool IsNetworkProfile(BackupBase module)
        {
            return module is WNetworkConf
                or WMappedDrives
                or CWiFiConf
                or EHosts;
        }

        private static bool IsAppSetting(BackupBase module)
        {
            return module is WPrivacy
                or WAPrivacy
                or WTelemetry
                or WUpdates
                or WAccessibility
                or WRegional
                or WOther
                or AppStoreApps
                or GGaming
                or ETerminal
                or EVSCode
                or ESsh
                or EEnvironment
                or EEnvironmentFiltered;
        }

        private sealed class ScopeDefinition
        {
            internal ScopeDefinition(string name, Func<BackupBase, bool> includes)
            {
                Name = name;
                Includes = includes;
            }

            internal string Name { get; }

            internal Func<BackupBase, bool> Includes { get; }
        }

        private static string Truncate(string value)
        {
            string singleLine = string.Join(" ", (value ?? string.Empty)
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

            return singleLine.Length <= DetailLimit
                ? singleLine
                : singleLine.Substring(0, DetailLimit - 3).TrimEnd() + "...";
        }
    }
}
