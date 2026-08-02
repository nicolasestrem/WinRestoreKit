using WinRestoreKit;
using Conf;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace WinRestoreKit.Tests
{
    // Decision 4: the confirmation dialog is thin, so everything it says is composed here and is
    // asserted here. A sentence that only exists inside the Form is a sentence no test can read.
    public class RestorePlanTests
    {
        private const string Source = @"C:\Appcopier\app\2026-07-19 - 11.02";
        private const string Snapshot = @"C:\Appcopier\app\2026-07-20 - 09.31.14 (pre-restore)";

        private sealed class FakeModule : BackupBase
        {
            private readonly IReadOnlyList<RestoreTarget> targets;
            private readonly IReadOnlyList<RestoreCloseRequirement> closes;

            public FakeModule(string title,
                              IReadOnlyList<RestoreTarget> targets = null,
                              IReadOnlyList<RestoreCloseRequirement> closes = null,
                              string warning = "")
            {
                Title = title;
                this.targets = targets;
                this.closes = closes;
                WarningMessage = warning;
            }

            public override IReadOnlyList<RestoreTarget> RestoreTargets
                => targets ?? base.RestoreTargets;

            public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore
                => closes ?? base.ProcessesToCloseBeforeRestore;
        }

        private static RestorePlan PlanFor(params BackupBase[] modules)
            => new RestorePlan(modules, Source, Snapshot);

        // --- What is overwritten ---

        [Fact]
        public void ConfirmationText_NamesEverySelectedModule()
        {
            RestorePlan plan = PlanFor(
                new FakeModule("Mouse", new[] { RestoreTarget.RegistryKey(@"HKEY_CURRENT_USER\Control Panel\Mouse") }),
                new FakeModule("Touchpad", new[] { RestoreTarget.RegistryKey(@"HKEY_CURRENT_USER\Software\Touchpad") }));

            Assert.Contains("Mouse", plan.ConfirmationText);
            Assert.Contains("Touchpad", plan.ConfirmationText);
        }

        [Fact]
        public void ConfirmationText_ShowsEveryDeclaredTargetOfEveryKind()
        {
            RestorePlan plan = PlanFor(
                new FakeModule("Themes", new[]
                {
                    RestoreTarget.Folder(@"C:\Users\me\AppData\Local\Microsoft\Windows\Themes"),
                    RestoreTarget.RegistryKey(@"HKEY_CURRENT_USER\Control Panel\Desktop")
                }),
                new FakeModule("Wi-Fi", new[]
                {
                    RestoreTarget.Command("runs netsh wlan add profile for every saved network")
                }),
                new FakeModule("SSH", new[]
                {
                    RestoreTarget.File(@"C:\Users\me\.ssh\config")
                }));

            Assert.Contains(@"C:\Users\me\AppData\Local\Microsoft\Windows\Themes", plan.ConfirmationText);
            Assert.Contains(@"HKEY_CURRENT_USER\Control Panel\Desktop", plan.ConfirmationText);
            Assert.Contains("runs netsh wlan add profile for every saved network", plan.ConfirmationText);
            Assert.Contains(@"C:\Users\me\.ssh\config", plan.ConfirmationText);
        }

        // Each kind renders with its own label, so the user reading the dialog can tell a file
        // from the folder containing it - "folder: C:\Users\me\.ssh" and "file: C:\Users\me\.ssh\config"
        // are very different promises about what survives the restore. A kind added without a
        // Render arm falls through to the bare path and fails here rather than shipping unlabelled.
        [Fact]
        public void ConfirmationText_LabelsEachTargetKind()
        {
            RestorePlan plan = PlanFor(new FakeModule("Mixed", new[]
            {
                RestoreTarget.RegistryKey(@"HKEY_CURRENT_USER\Control Panel\Desktop"),
                RestoreTarget.Folder(@"C:\Users\me\.ssh"),
                RestoreTarget.File(@"C:\Users\me\.ssh\config")
            }));

            Assert.Contains(@"registry key: HKEY_CURRENT_USER\Control Panel\Desktop", plan.ConfirmationText);
            Assert.Contains(@"folder: C:\Users\me\.ssh", plan.ConfirmationText);
            Assert.Contains(@"file: C:\Users\me\.ssh\config", plan.ConfirmationText);
        }

        // A module declaring several keys must show all of them: rendering only the first would
        // understate the scope of exactly the modules whose scope is widest.
        [Fact]
        public void ConfirmationText_ShowsEveryKeyOfAMultiKeyModule()
        {
            RestorePlan plan = PlanFor(new FakeModule("Updates", new[]
            {
                RestoreTarget.RegistryKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate"),
                RestoreTarget.RegistryKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\WindowsUpdate\UX")
            }));

            Assert.Contains(@"Policies\Microsoft\Windows\WindowsUpdate", plan.ConfirmationText);
            Assert.Contains(@"Microsoft\WindowsUpdate\UX", plan.ConfirmationText);
        }

        // --- Warnings, shown at the moment they apply ---

        [Fact]
        public void ConfirmationText_FoldsAModulesWarningIntoItsOwnBlock()
        {
            RestorePlan plan = PlanFor(
                new FakeModule("Printers",
                    new[] { RestoreTarget.RegistryKey(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Print") },
                    warning: "This could affect your printer configurations."),
                new FakeModule("Mouse",
                    new[] { RestoreTarget.RegistryKey(@"HKEY_CURRENT_USER\Control Panel\Mouse") }));

            string[] lines = plan.ConfirmationText.Split(new[] { "\r\n" }, StringSplitOptions.None);

            int printers = Array.IndexOf(lines, "Printers");
            int mouse = Array.IndexOf(lines, "Mouse");
            int warning = Array.FindIndex(lines, l => l.Contains("This could affect your printer configurations."));

            Assert.True(printers >= 0 && mouse > printers, plan.ConfirmationText);
            Assert.InRange(warning, printers, mouse);
        }

        [Fact]
        public void ConfirmationText_ModuleWithoutAWarning_GetsNoNoteLine()
        {
            RestorePlan plan = PlanFor(new FakeModule("Mouse",
                new[] { RestoreTarget.RegistryKey(@"HKEY_CURRENT_USER\Control Panel\Mouse") }));

            Assert.DoesNotContain("Note:", plan.ConfirmationText);
        }

        // --- Where things come from and go ---

        [Fact]
        public void ConfirmationText_NamesTheSourceAndTheSnapshotDestination()
        {
            RestorePlan plan = PlanFor(new FakeModule("Mouse",
                new[] { RestoreTarget.RegistryKey(@"HKEY_CURRENT_USER\Control Panel\Mouse") }));

            Assert.Contains(Source, plan.ConfirmationText);
            Assert.Contains(Snapshot, plan.ConfirmationText);
            Assert.Equal(Snapshot, plan.SnapshotDestination);
            Assert.Equal(Source, plan.RestoreSourcePath);
        }

        // --- The caveat ---

        // Verbatim, because this is the sentence that keeps "rollback" from being a stronger claim
        // than the app can hold. A reworded copy elsewhere would be a second, weaker guarantee.
        [Fact]
        public void FidelityCaveat_IsWordedExactly()
        {
            Assert.Equal(
                "The snapshot can put back settings this restore overwrites. It cannot remove " +
                "registry values or files that this restore adds \u2014 restoring the snapshot " +
                "merges it over the current state rather than resetting to it.",
                RestorePlan.FidelityCaveat);
        }

        // The dialog shows the caveat from the constant, prominently and on its own. Composing it
        // into the scrolling module text as well would let one copy drift from the other.
        [Fact]
        public void FidelityCaveat_IsNotRestatedInsideTheConfirmationText()
        {
            RestorePlan plan = PlanFor(new FakeModule("Mouse",
                new[] { RestoreTarget.RegistryKey(@"HKEY_CURRENT_USER\Control Panel\Mouse") }));

            Assert.DoesNotContain("merges it over the current state", plan.ConfirmationText);
        }

        // --- Consent ---

        [Fact]
        public void ConsentEntries_AreProducedForProcessesThatNeedConsent()
        {
            RestorePlan plan = PlanFor(
                new FakeModule("Chrome", closes: new[] { new RestoreCloseRequirement("chrome", "Google Chrome", true) }),
                new FakeModule("Firefox", closes: new[] { new RestoreCloseRequirement("firefox", "Mozilla Firefox", true) }));

            Assert.Equal(new[] { "chrome", "firefox" }, plan.ConsentEntries.Select(e => e.ProcessName).ToArray());
            Assert.All(plan.ConsentEntries, e => Assert.Contains(e.DisplayName, e.Label));
        }

        // Two modules naming one process is one question. Asking it twice is how a dialog teaches
        // people to tick boxes without reading them.
        [Fact]
        public void ConsentEntries_AreDeduplicatedAcrossModules()
        {
            RestorePlan plan = PlanFor(
                new FakeModule("Chrome profile", closes: new[] { new RestoreCloseRequirement("chrome", "Google Chrome", true) }),
                new FakeModule("Chrome extensions", closes: new[] { new RestoreCloseRequirement("chrome", "Google Chrome", true) }));

            RestoreConsentEntry only = Assert.Single(plan.ConsentEntries);
            Assert.Equal("chrome", only.ProcessName);
        }

        [Fact]
        public void InformationalCloses_AreTextAndNeverConsentEntries()
        {
            RestorePlan plan = PlanFor(new FakeModule("Pinned apps",
                closes: new[] { new RestoreCloseRequirement("StartMenuExperienceHost", "the Start menu", false) }));

            Assert.Empty(plan.ConsentEntries);

            string line = Assert.Single(plan.InformationalCloseLines);
            Assert.Contains("the Start menu", line);
            Assert.Contains("restart itself", line);
        }

        [Fact]
        public void InformationalCloses_AreDeduplicatedToo()
        {
            RestoreCloseRequirement startMenu =
                new RestoreCloseRequirement("StartMenuExperienceHost", "the Start menu", false);

            RestorePlan plan = PlanFor(
                new FakeModule("Pinned apps", closes: new[] { startMenu }),
                new FakeModule("Taskbar", closes: new[] { startMenu }));

            Assert.Single(plan.InformationalCloseLines);
        }

        [Fact]
        public void ModulesThatCloseNothing_ProduceNoConsentAndNoInformationalLines()
        {
            RestorePlan plan = PlanFor(new FakeModule("Mouse",
                new[] { RestoreTarget.RegistryKey(@"HKEY_CURRENT_USER\Control Panel\Mouse") }));

            Assert.Empty(plan.ConsentEntries);
            Assert.Empty(plan.InformationalCloseLines);
        }

        // --- The forgetful author ---

        [Fact]
        public void UndeclaredModule_RendersTheMarkerInTheText()
        {
            RestorePlan plan = PlanFor(new FakeModule("Something new"));

            Assert.Contains("Something new", plan.ConfirmationText);
            Assert.Contains(RestoreTarget.UndeclaredMarker, plan.ConfirmationText);
        }

        // An empty declaration is the same defect as a missing one, and reads worse: a module that
        // contributes no lines looks exactly like a module that touches nothing.
        [Fact]
        public void ModuleDeclaringAnEmptyList_StillRendersTheMarker()
        {
            RestorePlan plan = PlanFor(new FakeModule("Empty declarer", new RestoreTarget[0]));

            Assert.Contains(RestoreTarget.UndeclaredMarker, plan.ConfirmationText);
        }

        // A null entry inside a real declaration is a bug in the module, and the dialog is the last
        // place it may be silently absorbed: dereferencing it threw out of a stage with no catch
        // above it, and dropping the line would understate what the restore overwrites.
        [Fact]
        public void NullTargetEntry_RendersItsOwnMarkerInsteadOfThrowing()
        {
            RestorePlan plan = PlanFor(new FakeModule("Half declarer", new[]
            {
                RestoreTarget.RegistryKey(@"HKEY_CURRENT_USER\Control Panel\Mouse"),
                null
            }));

            Assert.Contains(@"HKEY_CURRENT_USER\Control Panel\Mouse", plan.ConfirmationText);
            Assert.Contains(RestoreTarget.UnnamedTargetMarker, plan.ConfirmationText);
        }

        // "The module declared nothing" and "one entry of its declaration is broken" are different
        // facts. Rendering the undeclared marker here would tell the user the real, named targets
        // this module DID declare do not exist.
        [Fact]
        public void NullTargetEntry_DoesNotClaimTheModuleDeclaredNothing()
        {
            RestorePlan plan = PlanFor(new FakeModule("Half declarer", new[]
            {
                RestoreTarget.RegistryKey(@"HKEY_CURRENT_USER\Control Panel\Mouse"),
                null
            }));

            Assert.DoesNotContain(RestoreTarget.UndeclaredMarker, plan.ConfirmationText);
        }

        [Fact]
        public void SeveralNullTargetEntries_RenderOneMarkerEach()
        {
            RestorePlan plan = PlanFor(new FakeModule("Broken declarer", new RestoreTarget[]
            {
                null,
                RestoreTarget.Folder(@"C:\Users\me\AppData\Local\Microsoft\Windows\Themes"),
                null
            }));

            string[] lines = plan.ConfirmationText.Split(new[] { "\r\n" }, StringSplitOptions.None);

            Assert.Equal(2, lines.Count(l => l.Trim() == RestoreTarget.UnnamedTargetMarker));
            Assert.Contains(@"C:\Users\me\AppData\Local\Microsoft\Windows\Themes", plan.ConfirmationText);
        }

        // Two markers, two facts. One sentence used for both would make the dialog unable to say
        // which of them happened.
        [Fact]
        public void TheTwoMarkers_AreDifferentSentences()
        {
            Assert.NotEqual(RestoreTarget.UndeclaredMarker, RestoreTarget.UnnamedTargetMarker);
            Assert.False(string.IsNullOrWhiteSpace(RestoreTarget.UnnamedTargetMarker));
            Assert.DoesNotContain(RestoreTarget.UnnamedTargetMarker, RestoreTarget.UndeclaredMarker);
            Assert.DoesNotContain(RestoreTarget.UndeclaredMarker, RestoreTarget.UnnamedTargetMarker);
        }

        // --- Against the shipped modules ---

        [Fact]
        public void RealModules_ContributeTheirOwnDeclarationsAndWarnings()
        {
            CWiFiConf wifi = new CWiFiConf();
            APinnedApps pinned = new APinnedApps();

            RestorePlan plan = PlanFor(wifi, pinned);

            Assert.Contains(wifi.Title, plan.ConfirmationText);
            Assert.Contains(wifi.WarningMessage, plan.ConfirmationText);
            Assert.Contains(wifi.RestoreTargets.Single().Path, plan.ConfirmationText);
            Assert.Contains(pinned.RestoreTargets.Single().Path, plan.ConfirmationText);
            Assert.DoesNotContain(RestoreTarget.UndeclaredMarker, plan.ConfirmationText);

            // The roster was empty between Phase 3a (which retired the browsers) and Phase 3b.
            // Neither module here declares a consented close, so the emptiness is still asserted -
            // it is now a fact about these two rather than about the whole app.
            Assert.Empty(plan.ConsentEntries);
        }

        // The end-to-end consent path over a REAL module, which nothing asserted between 3a and 3b
        // because no shipped module declared one. The FakeModule tests above cover the mechanics;
        // this covers that a shipped module's declaration actually reaches the dialog.
        [Fact]
        public void RealModuleWithAConsentedClose_ProducesAConsentEntry()
        {
            ETerminal terminal = new ETerminal();

            RestorePlan plan = PlanFor(terminal);

            RestoreConsentEntry only = Assert.Single(plan.ConsentEntries);

            Assert.Equal("WindowsTerminal", only.ProcessName);
            Assert.Contains("Windows Terminal", only.Label);
            Assert.Contains(terminal.WarningMessage, plan.ConfirmationText);
        }

        // --- Construction ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Plan_WithoutASourceOrASnapshotDestination_Throws(string missing)
        {
            Assert.Throws<ArgumentException>(() => new RestorePlan(new BackupBase[0], missing, Snapshot));
            Assert.Throws<ArgumentException>(() => new RestorePlan(new BackupBase[0], Source, missing));
        }

        [Fact]
        public void Plan_WithNoModules_StillStatesWhereItRestoresFromAndSnapshotsTo()
        {
            RestorePlan plan = new RestorePlan(null, Source, Snapshot);

            Assert.Empty(plan.Modules);
            Assert.Contains(Source, plan.ConfirmationText);
            Assert.Contains(Snapshot, plan.ConfirmationText);
        }
    }
}
