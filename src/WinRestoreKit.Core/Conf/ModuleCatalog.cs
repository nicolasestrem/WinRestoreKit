using WinRestoreKit;
using System.Collections.Generic;

namespace Conf
{
    /// <summary>
    /// One backup module plus the tree category node it belongs under.
    /// </summary>
    /// <remarks>
    /// Extracted from <c>ConfPageView.InitializeConfigurations</c> in Phase 4 PR 6 so the module
    /// inventory is owned by the engine, not a WinForms view. The category is a free string because
    /// <c>FindOrCreateNode</c> still creates the node from the first call - consistent spelling is
    /// the whole mechanism, exactly as before.
    /// </remarks>
    internal sealed class ModuleRegistration
    {
        internal BackupBase Module { get; }

        internal string Category { get; }

        internal ModuleRegistration(BackupBase module, string category)
        {
            Module = module;
            Category = category;
        }
    }

    /// <summary>
    /// The single registration point for backup modules: fresh instances, in tree order.
    /// </summary>
    /// <remarks>
    /// Mirrors the order and category strings <c>ConfPageView.InitializeConfigurations</c> used to
    /// hardcode. Adding a module is now a one-place edit here (plus the class itself); the view and
    /// the test suite both enumerate this catalog. <see cref="ModuleCatalogTests"/> cross-checks it
    /// against the concrete <see cref="BackupBase"/> subclasses in the assembly, so a module added
    /// to Core but not registered here turns red by name.
    /// </remarks>
    internal static class ModuleCatalog
    {
        /// <summary>Fresh instances, tree order. The single registration point for modules.</summary>
        internal static IReadOnlyList<ModuleRegistration> CreateAll()
        {
            return new List<ModuleRegistration>
            {
                new ModuleRegistration(new WPersonalization(), "Settings"),
                new ModuleRegistration(new WVisualEffects(), "Settings"),
                new ModuleRegistration(new WTaskbar(), "Settings"),
                new ModuleRegistration(new WPrivacy(), "Settings"),
                new ModuleRegistration(new WAPrivacy(), "Settings"),
                new ModuleRegistration(new WTelemetry(), "Settings"),
                new ModuleRegistration(new WNetworkConf(), "Settings"),
                new ModuleRegistration(new WMappedDrives(), "Settings"),
                new ModuleRegistration(new WUpdates(), "Settings"),
                new ModuleRegistration(new WPowerPlans(), "Settings"),
                new ModuleRegistration(new WThemes(), "Settings"),
                new ModuleRegistration(new WFonts(), "Settings"),
                new ModuleRegistration(new WAccessibility(), "Settings"),
                new ModuleRegistration(new WRegional(), "Settings"),
                new ModuleRegistration(new WOther(), "Settings"),
                new ModuleRegistration(new AppStoreApps(), "Apps"),
                new ModuleRegistration(new APinnedApps(), "Apps"),
                // The browser modules (Chrome, Edge, Firefox) were retired in Phase 3a: they copied
                // whole profile directories - caches, GPU data, live locked databases - and browser
                // sync solves the problem better than a local export can. Backups made with earlier
                // versions keep their browser folders on disk; this app no longer restores them.
                new ModuleRegistration(new DPrinters(), "Devices"),
                new ModuleRegistration(new DMouse(), "Devices"),
                new ModuleRegistration(new DKeyboard(), "Devices"),
                new ModuleRegistration(new DTouchpad(), "Devices"),
                new ModuleRegistration(new GGaming(), "Gaming"),
                new ModuleRegistration(new CWiFiConf(), "Credentials"),
                new ModuleRegistration(new ETerminal(), "Developer"),
                new ModuleRegistration(new EVSCode(), "Developer"),
                new ModuleRegistration(new ESsh(), "Developer"),
                new ModuleRegistration(new EEnvironment(), "Developer"),

                // Directly after EEnvironment on purpose: the two read one key and differ only in what
                // they keep, and the tree checkbox is the opt-in. Adjacent rows are what makes that a
                // choice the user can see rather than one buried in the list.
                new ModuleRegistration(new EEnvironmentFiltered(), "Developer"),

                new ModuleRegistration(new EHosts(), "Developer"),
            };
        }
    }
}
