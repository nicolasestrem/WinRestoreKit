using WinRestoreKit;
using Conf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace WinRestoreKit.Tests
{
    // Decision 8: every module states what its restore overwrites, and forgetting shows up.
    //
    // These enumerate the shipped assembly by reflection rather than listing modules by hand. A
    // hand-written list is exactly what a forgetful author also forgets to extend, which would let
    // a new module ship undeclared while the suite stayed green.
    public class RestoreDeclarationTests
    {
        // typeof(BackupBase).Assembly, not the test assembly: the fakes defined in these tests are
        // deliberately undeclared, and enumerating them would make the invariant unassertable.
        private static IEnumerable<Type> ModuleTypes()
            => typeof(BackupBase).Assembly
                .GetTypes()
                .Where(t => typeof(BackupBase).IsAssignableFrom(t) && !t.IsAbstract)
                .OrderBy(t => t.Name);

        private static IEnumerable<BackupBase> Modules()
            => ModuleTypes().Select(t => (BackupBase)Activator.CreateInstance(t));

        [Fact]
        public void EveryShippedModuleIsEnumeratedAndConstructible()
        {
            List<BackupBase> modules = Modules().ToList();

            // 19 before Phase 3b, plus the six Developer-category modules it added (six rather than
            // five because EEnvironment ships in two variants - the plain export and the opt-in
            // filtered one - which are separate tree entries by design, not a duplicate), plus the
            // four power-user Settings modules Phase 3c added: WPowerPlans, WFonts, WMappedDrives
            // and WRegional. 3c also retargeted WTaskbar and WUpdates, which changes what they
            // capture but not how many modules exist.
            Assert.Equal(29, modules.Count);
            Assert.All(modules, m => Assert.False(string.IsNullOrWhiteSpace(m.Title)));
        }

        /// <summary>
        /// No backup module lives in the app assembly. They all belong to WinRestoreKit.Core.
        /// </summary>
        /// <remarks>
        /// Phase 4 PR 2 split the engine into WinRestoreKit.Core, and five test sites - the ModuleTypes
        /// sweep above, ModuleTargetTests, DeveloperModuleTests and two in BackupFileNamingTests -
        /// enumerate modules through typeof(BackupBase).Assembly. That expression now resolves to
        /// Core, so a module accidentally left behind in, or later added to, the app project is
        /// invisible to every one of them: a suite that stays green while covering less, which is
        /// this codebase's signature failure.
        ///
        /// The Assert.Equal(29, ...) above catches that today, and it is the only thing that does -
        /// the other three sweeps filter to subsets where a fixed count would be brittle churn on
        /// every module addition. This asserts the membership rule directly instead of by counting,
        /// so it never needs renumbering and does not depend on the 29 surviving a future edit.
        /// </remarks>
        [Fact]
        public void NoModuleIsLeftBehindInTheAppAssembly()
        {
            Assembly app = typeof(global::WinRestoreKit.Wpf.App).Assembly;

            Assert.NotEqual(app, typeof(BackupBase).Assembly);

            Type[] strays = app.GetTypes()
                .Where(t => typeof(BackupBase).IsAssignableFrom(t) && !t.IsAbstract)
                .ToArray();

            Assert.Empty(strays);
        }

        [Fact]
        public void NoShippedModuleReturnsTheUndeclaredMarker()
        {
            foreach (BackupBase m in Modules())
            {
                Assert.NotEmpty(m.RestoreTargets);

                // Before the IsUndeclared predicate, which would throw on a null entry and report a
                // module bug as a broken test. A null here is its own defect: the dialog renders
                // UnnamedTargetMarker for it, which no shipped module may ever produce.
                Assert.DoesNotContain(m.RestoreTargets, t => t == null);

                Assert.DoesNotContain(m.RestoreTargets, t => t.IsUndeclared);
            }
        }

        [Fact]
        public void EveryDeclaredTargetCarriesText()
        {
            foreach (BackupBase m in Modules())
                foreach (RestoreTarget t in m.RestoreTargets)
                    Assert.False(string.IsNullOrWhiteSpace(t.Path));
        }

        // The default has to stay loud. A module that declares nothing must still produce a line in
        // the dialog, or consent is asked for an operation whose scope was never stated.
        private sealed class ForgetfulModule : BackupBase
        {
            public ForgetfulModule() { Title = "Forgetful"; }
        }

        [Fact]
        public void UndeclaredModule_ProducesTheVisibleMarker()
        {
            RestoreTarget only = Assert.Single(new ForgetfulModule().RestoreTargets);

            Assert.True(only.IsUndeclared);
            Assert.Equal(RestoreTargetKind.Command, only.Kind);
            Assert.Equal("(this item does not declare what it overwrites)", only.Path);
        }

        [Fact]
        public void UndeclaredModule_DeclaresNoCloseAndMakesChanges()
        {
            ForgetfulModule m = new ForgetfulModule();

            Assert.Empty(m.ProcessesToCloseBeforeRestore);
            Assert.True(m.RestoreMakesChanges);
        }

        [Fact]
        public void PinnedApps_DeclaresANonConsentedCloseOfTheStartMenu()
        {
            RestoreCloseRequirement req = Assert.Single(new APinnedApps().ProcessesToCloseBeforeRestore);

            Assert.Equal("StartMenuExperienceHost", req.ProcessName);
            Assert.False(req.NeedsConsent);
        }

        // Every module outside this roster closes nothing, so a close requirement appearing
        // anywhere else is a new prompt nobody decided to add. The three browser modules, which
        // declared consented closes, were retired in Phase 3a; the roster was empty apart from
        // APinnedApps until Phase 3b, whose Terminal and VS Code modules are the consented closes
        // this list was left open for.
        [Fact]
        public void OnlyTheRecordedRoster_DeclaresCloseRequirements()
        {
            string[] declaring = Modules()
                .Where(m => m.ProcessesToCloseBeforeRestore.Count > 0)
                .Select(m => m.GetType().Name)
                .OrderBy(n => n)
                .ToArray();

            Assert.Equal(new[] { "APinnedApps", "ETerminal", "EVSCode" }, declaring);
        }

        // Consent is the whole decision on RestoreCloseRequirement, so which modules ask for it is
        // pinned separately from which modules close something. APinnedApps deliberately does not
        // ask (Windows restarts the Start menu on its own); both 3b modules do, because closing
        // them costs the user shell sessions and unsaved editor buffers.
        [Fact]
        public void OnlyTheDeveloperModules_AskForCloseConsent()
        {
            string[] consenting = Modules()
                .Where(m => m.ProcessesToCloseBeforeRestore.Any(r => r != null && r.NeedsConsent))
                .Select(m => m.GetType().Name)
                .OrderBy(n => n)
                .ToArray();

            Assert.Equal(new[] { "ETerminal", "EVSCode" }, consenting);
        }

        [Fact]
        public void AppStoreApps_IsTheOnlyModuleThatMakesNoChanges()
        {
            string[] exempt = Modules()
                .Where(m => !m.RestoreMakesChanges)
                .Select(m => m.GetType().Name)
                .ToArray();

            Assert.Equal(new[] { "AppStoreApps" }, exempt);
        }

        // The declared key and the imported key come from one property, so this fails if a subclass
        // ever hand-rolls RestoreTargets and names a different key than the one it writes.
        [Fact]
        public void EveryRegistryModule_DeclaresExactlyItsOwnKey()
        {
            List<BackupBase> registryModules = Modules()
                .Where(m => m is RegistryModule)
                .ToList();

            // Still 11 after Phase 3c, but NOT the same eleven, and the count surviving unchanged is
            // exactly why this comment has to say so. 3c retargeted WTaskbar into a WThemes-style
            // hybrid, so it left this family, and added WMappedDrives, which joined it. A membership
            // change that nets to zero is invisible in the assertion and would otherwise read as
            // "nothing happened here".
            //
            // The nine before Phase 3b, plus EEnvironment and EEnvironmentFiltered - both plain
            // single-key registry modules despite shipping with the file-based Developer set - minus
            // WTaskbar, plus WMappedDrives.
            //
            // EEnvironment and EEnvironmentFiltered declare the SAME key, and this test is fine with
            // that: it asserts each module declares the key it writes, not that keys are unique
            // across modules. Two modules over one key is this pair's whole design - they differ in
            // what they keep.
            Assert.Equal(11, registryModules.Count);

            // Pinned as literals so the netting-to-zero above cannot happen again silently: a future
            // retarget that moves a module in or out of this family must state which one.
            Assert.DoesNotContain(registryModules, m => m is WTaskbar);
            Assert.Contains(registryModules, m => m is WMappedDrives);

            foreach (BackupBase m in registryModules)
            {
                RestoreTarget only = Assert.Single(m.RestoreTargets);

                Assert.Equal(RestoreTargetKind.RegistryKey, only.Kind);
                Assert.Equal(KeyOf(m), only.Path);
            }
        }

        // Key is protected, so the assertion reads it the only way a test can. Walking the
        // hierarchy rather than a single GetProperty call: the property is declared on each
        // subclass as an override, and a future subclass that inherits it instead would otherwise
        // silently produce a null here and pass against an equally null target path.
        private static string KeyOf(BackupBase module)
        {
            for (Type t = module.GetType(); t != null; t = t.BaseType)
            {
                PropertyInfo p = t.GetProperty("Key",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                if (p != null)
                {
                    string key = (string)p.GetValue(module);
                    Assert.False(string.IsNullOrWhiteSpace(key));
                    return key;
                }
            }

            throw new InvalidOperationException("No Key property on " + module.GetType().Name);
        }

        [Theory]
        [InlineData(typeof(WPersonalization))]
        [InlineData(typeof(WTelemetry))]
        [InlineData(typeof(WUpdates))]
        [InlineData(typeof(DPrinters))]
        [InlineData(typeof(GGaming))]
        // Phase 3c. WFonts and WTaskbar are deliberately NOT here despite holding a Keys field:
        // both are hybrids that declare folders as well, so keys alone are not all of their
        // targets. Their ordering is pinned in PowerUserModuleTests and TaskbarRetargetTests.
        [InlineData(typeof(WRegional))]
        public void MultiKeyModules_DeclareOneTargetPerKeyInOrder(Type type)
        {
            BackupBase m = (BackupBase)Activator.CreateInstance(type);

            List<string> keys = (List<string>)type.GetField("Keys").GetValue(m);

            Assert.Equal(keys, m.RestoreTargets.Select(t => t.Path).ToArray());
            Assert.All(m.RestoreTargets, t => Assert.Equal(RestoreTargetKind.RegistryKey, t.Kind));
        }

        // Keys is a mutable public field filled by LoadSettings, so the declaration must be read
        // from it rather than captured. Mutating it after construction is how a test can tell those
        // two implementations apart at all.
        [Fact]
        public void MultiKeyModules_ReadTheirKeysAtAccessTimeNotAtConstruction()
        {
            WUpdates m = new WUpdates();
            m.Keys.Add(@"HKEY_CURRENT_USER\Software\WinRestoreKit\AddedAfterConstruction");

            Assert.Equal(m.Keys, m.RestoreTargets.Select(t => t.Path).ToArray());
        }

        // Folders first, then keys - the order RestoreAsync applies them, which is the order the
        // confirmation dialog must read them out in.
        //
        // Written generically over the module's own fields, so it agrees with whatever those fields
        // say. That is the right shape for an ORDERING invariant and the wrong shape for pinning
        // WHICH targets are declared: it passed unchanged while Phase 2c removed a folder and added
        // a key. The literals live in ModuleTargetTests.Themes_DeclaresItsProfileFolderThenBothKeys,
        // which replaced this test's old name.
        [Fact]
        public void Themes_DeclaresItsFoldersBeforeItsKeys()
        {
            WThemes m = new WThemes();

            IReadOnlyList<RestoreTarget> targets = m.RestoreTargets;

            Assert.Equal(m.Folders.Count + m.Keys.Count, targets.Count);
            Assert.Equal(m.Folders, targets.Take(m.Folders.Count).Select(t => t.Path).ToArray());
            Assert.All(targets.Take(m.Folders.Count), t => Assert.Equal(RestoreTargetKind.Folder, t.Kind));
            Assert.Equal(m.Keys, targets.Skip(m.Folders.Count).Select(t => t.Path).ToArray());
            Assert.All(targets.Skip(m.Folders.Count), t => Assert.Equal(RestoreTargetKind.RegistryKey, t.Kind));
        }

        [Theory]
        [InlineData(typeof(APinnedApps))]
        public void FolderModules_DeclareTheFolderTheyOverwrite(Type type)
        {
            BackupBase m = (BackupBase)Activator.CreateInstance(type);

            string folder = (string)type.GetField("Folder").GetValue(m);

            RestoreTarget only = Assert.Single(m.RestoreTargets);

            Assert.Equal(RestoreTargetKind.Folder, only.Kind);
            Assert.Equal(folder, only.Path);
        }

        [Theory]
        [InlineData(typeof(WNetworkConf))]
        [InlineData(typeof(CWiFiConf))]
        [InlineData(typeof(AppStoreApps))]
        [InlineData(typeof(WPowerPlans))]
        public void CommandModules_DescribeWhatRunsInPlainLanguage(Type type)
        {
            BackupBase m = (BackupBase)Activator.CreateInstance(type);

            RestoreTarget only = Assert.Single(m.RestoreTargets);

            Assert.Equal(RestoreTargetKind.Command, only.Kind);
            Assert.False(only.IsUndeclared);

            // A description, not a key path or a bare command line: this text is the only thing the
            // user is given to judge a command whose scope they cannot otherwise see.
            Assert.True(only.Path.Length > 30, only.Path);
        }

        // Wi-Fi's restore is machine-wide, which is the part a user would not otherwise expect and
        // the reason this module carries a WarningMessage at all.
        [Fact]
        public void WiFi_DeclarationStatesTheRestoreIsMachineWide()
            => Assert.Contains("all accounts", new CWiFiConf().RestoreTargets.Single().Path);

        [Fact]
        public void RestoreTarget_RejectsEmptyPaths()
        {
            Assert.Throws<ArgumentException>(() => RestoreTarget.RegistryKey(null));
            Assert.Throws<ArgumentException>(() => RestoreTarget.Folder(""));
            Assert.Throws<ArgumentException>(() => RestoreTarget.Command("   "));
            Assert.Throws<ArgumentException>(() => RestoreTarget.Undeclared(""));
        }

        [Fact]
        public void RestoreCloseRequirement_RejectsEmptyNames()
        {
            Assert.Throws<ArgumentException>(() => new RestoreCloseRequirement("", "Chrome", true));
            Assert.Throws<ArgumentException>(() => new RestoreCloseRequirement("chrome", null, true));
        }

        [Fact]
        public void RestoreCloseRequirement_KeepsWhatItWasGiven()
        {
            RestoreCloseRequirement req = new RestoreCloseRequirement("firefox", "Mozilla Firefox", false);

            Assert.Equal("firefox", req.ProcessName);
            Assert.Equal("Mozilla Firefox", req.DisplayName);
            Assert.False(req.NeedsConsent);
        }

        // A declared target must not read as the marker by accident, or the dialog would show a
        // real declaration as a missing one.
        [Fact]
        public void OnlyTheUndeclaredMarkerIsUndeclared()
        {
            Assert.False(RestoreTarget.Command("runs netsh").IsUndeclared);
            Assert.False(RestoreTarget.Folder(RestoreTarget.UndeclaredMarker).IsUndeclared);

            // The other marker is a DIFFERENT fact - one entry of a real declaration is broken, not
            // the module declared nothing - so a target carrying it must not read as undeclared.
            Assert.False(RestoreTarget.Command(RestoreTarget.UnnamedTargetMarker).IsUndeclared);

            Assert.True(RestoreTarget.Undeclared("SomeModule").Single().IsUndeclared);
        }

        // Every module that closes something must say NO to an empty backup, because closing is
        // what costs the user work - shell sessions, unsaved editor buffers - and a restore with
        // nothing to copy must never be worth that.
        //
        // Reflection-driven rather than an [InlineData] roster. It was a Theory with a single
        // APinnedApps row through Phase 3a, when APinnedApps was the only module closing anything;
        // 3b added two more and the roster did not follow, which left ETerminal with no direct
        // coverage at all. That is the "hand-written list a forgetful author also forgets to
        // extend" this file warns about at the top, having happened to this file.
        //
        // Only the negative direction is swept, and deliberately: what counts as "something to
        // restore" differs by shape - a directory for a FolderModule, a named artifact inside it
        // for a FileModule - so the positive case is asserted per shape in FolderModuleTests,
        // FileModuleTests and VSCodeModuleTests. An empty backup meaning "no" is the invariant
        // they all genuinely share.
        [Fact]
        public void EveryModuleThatClosesSomething_SaysNoToAnEmptyBackup()
        {
            List<BackupBase> closing = Modules()
                .Where(m => m.ProcessesToCloseBeforeRestore.Count > 0)
                .ToList();

            // If this trips, the sweep found nothing and is no longer protecting anything.
            Assert.NotEmpty(closing);

            string root = Path.Combine(Path.GetTempPath(), "achas_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                foreach (BackupBase module in closing)
                {
                    Assert.False(module.HasBackupIn(root),
                        module.GetType().Name + " would close its process for an empty backup");
                }
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        // APinnedApps is a FolderModule, so for it the directory IS the artifact - its restore
        // copies the folder wholesale. Kept as its own case now that the sweep above covers only
        // the shared negative direction.
        [Fact]
        public void PinnedApps_TreatsItsBackupDirectoryAsSomethingToRestore()
        {
            BackupBase module = new APinnedApps();

            string root = Path.Combine(Path.GetTempPath(), "achas_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                Assert.False(module.HasBackupIn(root));

                Directory.CreateDirectory(Path.Combine(root, module.Title));
                Assert.True(module.HasBackupIn(root));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        // A null entry in a close declaration reaches the consent dialog's rendering, which is
        // user-facing text at a consent boundary. RestoreTargets gets this assertion; close
        // requirements did not.
        [Fact]
        public void NoShippedModuleDeclaresANullCloseRequirement()
        {
            foreach (BackupBase m in Modules())
            {
                Assert.NotNull(m.ProcessesToCloseBeforeRestore);
                Assert.DoesNotContain(m.ProcessesToCloseBeforeRestore, r => r == null);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ModulesThatCloseSomething_TreatAMissingPathAsNothingToRestore(string path)
            => Assert.False(new Conf.APinnedApps().HasBackupIn(path));

        // The default answers yes, so a module that has not been taught to check is never silently
        // skipped - being wrong that way costs a close, the other way cancels the user's restore.
        [Fact]
        public void ModulesThatCloseNothing_AssumeTheBackupHasSomethingForThem()
        {
            foreach (BackupBase module in Modules())
            {
                if (module.ProcessesToCloseBeforeRestore.Count == 0)
                    Assert.True(module.HasBackupIn(Path.GetTempPath()), module.Title);
            }
        }
    }
}
