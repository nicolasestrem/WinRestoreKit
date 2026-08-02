using WinRestoreKit;
using System;
using System.Collections.Generic;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class RestoreScopeTests
    {
        // --- Fakes ---

        private sealed class FakeModule : BackupBase
        {
            private readonly IReadOnlyList<RestoreCloseRequirement> requirements;
            private readonly bool makesChanges;

            public FakeModule(string title, bool makesChanges, params RestoreCloseRequirement[] requirements)
            {
                Title = title;
                this.makesChanges = makesChanges;
                this.requirements = requirements;
            }

            public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore => requirements;

            public override bool RestoreMakesChanges => makesChanges;
        }

        /// <summary>A module that declares nothing at all, exercising the BackupBase defaults.</summary>
        private sealed class BareModule : BackupBase
        {
            public BareModule() { Title = "Mouse"; }
        }

        /// <summary>
        /// A module whose declaration list is null rather than empty - the shape a future override
        /// that returns a field before it is assigned would produce.
        /// </summary>
        private sealed class NullDeclaringModule : BackupBase
        {
            public NullDeclaringModule() { Title = "Null declarer"; }

            public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore => null;
        }

        private static RestoreCloseRequirement Chrome()
            => new RestoreCloseRequirement("chrome", "Google Chrome", true);

        private static RestoreCloseRequirement Edge()
            => new RestoreCloseRequirement("msedge", "Microsoft Edge", true);

        private static RestoreCloseRequirement StartMenu()
            => new RestoreCloseRequirement("StartMenuExperienceHost", "the Start menu", false);

        private static RestoreScopeEntry Only(IReadOnlyList<RestoreScopeEntry> entries)
            => Assert.Single(entries);

        private static RestoreScopeEntry Scope(BackupBase module,
                                               IReadOnlyList<string> consented,
                                               IDictionary<string, CloseResult> closedUpFront)
            => Only(RestoreScope.For(new[] { module }, consented, closedUpFront));

        private static Dictionary<string, CloseResult> Closed(string process, CloseResult outcome)
            => new Dictionary<string, CloseResult> { { process, outcome } };

        /// <summary>A module that answers whether the backup folder holds anything for it.</summary>
        private sealed class ArtifactModule : BackupBase
        {
            private readonly bool hasBackup;

            public ArtifactModule(string title, bool hasBackup)
            {
                Title = title;
                this.hasBackup = hasBackup;
            }

            public string AskedAbout { get; private set; }

            public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore
                => new[] { Chrome() };

            public override bool HasBackupIn(string restorePath)
            {
                AskedAbout = restorePath;
                return hasBackup;
            }
        }

        private sealed class ThrowingArtifactModule : BackupBase
        {
            public ThrowingArtifactModule() { Title = "Throws"; }

            public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore
                => new[] { Chrome() };

            public override bool HasBackupIn(string restorePath)
                => throw new UnauthorizedAccessException("denied");
        }

        // --- Nothing to restore: refused before anything is closed on its behalf ---

        // The regression: what the user ticks in the tree is independent of what the backup folder
        // holds, so Chrome could be selected with no Chrome profile in the backup. Before this, the
        // browser was force-killed - every open tab lost - and only then did the restore report that
        // nothing had been backed up for it. Worse than the pre-2b behaviour, which closed nothing.
        [Fact]
        public void ModuleWithNothingInTheBackup_IsRefusedBeforeAnythingIsClosed()
        {
            RestoreScopeEntry entry = Only(RestoreScope.For(
                new[] { new ArtifactModule("Google Chrome", hasBackup: false) },
                new[] { "chrome" },
                new Dictionary<string, CloseResult>(),
                @"C:\backups\2026-01-01"));

            Assert.Equal(RestoreBlock.NothingToRestore, entry.Block);
            Assert.False(entry.WillBeRestored);
            Assert.False(entry.NeedsSnapshot);
        }

        [Fact]
        public void ModuleWithSomethingInTheBackup_IsRestoredAndSnapshotted()
        {
            ArtifactModule module = new ArtifactModule("Google Chrome", hasBackup: true);

            RestoreScopeEntry entry = Only(RestoreScope.For(
                new[] { module }, new[] { "chrome" },
                new Dictionary<string, CloseResult>(), @"C:\backups\2026-01-01"));

            Assert.Equal(RestoreBlock.None, entry.Block);
            Assert.True(entry.WillBeRestored);
            Assert.True(entry.NeedsSnapshot);
            Assert.Equal(@"C:\backups\2026-01-01", module.AskedAbout);
        }

        // Ordered ahead of the consent and close checks, so a module with nothing to restore is
        // refused for that reason rather than for one that implies the user could have avoided it.
        [Fact]
        public void NothingToRestore_OutranksAWithheldConsent()
        {
            RestoreScopeEntry entry = Only(RestoreScope.For(
                new[] { new ArtifactModule("Google Chrome", hasBackup: false) },
                new string[0],
                new Dictionary<string, CloseResult>(),
                @"C:\backups\2026-01-01"));

            Assert.Equal(RestoreBlock.NothingToRestore, entry.Block);
        }

        // A null source means the caller did not ask for the check - every module stays restorable,
        // which is what keeps the existing callers and their tests honest.
        [Fact]
        public void NullRestoreSource_SkipsTheCheckEntirely()
        {
            ArtifactModule module = new ArtifactModule("Google Chrome", hasBackup: false);

            RestoreScopeEntry entry = Scope(module, new[] { "chrome" }, new Dictionary<string, CloseResult>());

            Assert.Equal(RestoreBlock.None, entry.Block);
            Assert.Null(module.AskedAbout);
        }

        // Falls towards restoring. A probe that threw told us nothing, and silently cancelling a
        // restore the user asked for is worse than an unnecessary close.
        [Fact]
        public void AProbeThatThrows_LeavesTheModuleRestorable()
        {
            RestoreScopeEntry entry = Only(RestoreScope.For(
                new[] { new ThrowingArtifactModule() }, new[] { "chrome" },
                new Dictionary<string, CloseResult>(), @"C:\backups\2026-01-01"));

            Assert.Equal(RestoreBlock.None, entry.Block);
            Assert.True(entry.WillBeRestored);
        }

        [Fact]
        public void DescribeBlock_NothingToRestore_UsesTheModulesOwnWording()
        {
            RestoreScopeEntry entry = Only(RestoreScope.For(
                new[] { new ArtifactModule("Google Chrome", hasBackup: false) },
                new[] { "chrome" },
                new Dictionary<string, CloseResult>(),
                @"C:\backups\2026-01-01"));

            StepResult step = RestoreScope.DescribeBlock(entry);

            Assert.Equal(ResultState.Skipped, step.State);
            Assert.Equal("nothing was backed up for this item", step.Reason);
        }

        // --- 1. The invariant, exhaustively ---

        /// <summary>
        /// Every module shape this app can produce, so the cross product below covers the real
        /// declaration space rather than one convenient corner of it.
        /// </summary>
        private static IEnumerable<BackupBase> AllShapes()
        {
            yield return new BareModule();
            yield return new NullDeclaringModule();
            yield return new FakeModule("No close, writes", true);
            yield return new FakeModule("No close, writes nothing", false);
            yield return new FakeModule("Chrome", true, Chrome());
            yield return new FakeModule("Chrome, writes nothing", false, Chrome());
            yield return new FakeModule("Start menu", true, StartMenu());
            yield return new FakeModule("Start menu, writes nothing", false, StartMenu());
            yield return new FakeModule("Chrome and Edge", true, Chrome(), Edge());
            yield return new FakeModule("Chrome and Edge, writes nothing", false, Chrome(), Edge());
            yield return new FakeModule("Chrome and Start menu", true, Chrome(), StartMenu());
            yield return new FakeModule("Null requirement in the list", true, Chrome(), null);
        }

        private static IEnumerable<IReadOnlyList<string>> AllConsentStates()
        {
            yield return null;
            yield return new string[0];
            yield return new[] { "chrome" };
            yield return new[] { "msedge" };
            yield return new[] { "chrome", "msedge" };
            yield return new[] { "chrome", "msedge", "StartMenuExperienceHost" };
        }

        /// <summary>
        /// Driven off Enum.GetValues so a CloseResult member added later is covered without anyone
        /// remembering to add a row here, plus the two cases no enum member describes: a process
        /// absent from the dictionary, and a value outside the enum.
        /// </summary>
        private static IEnumerable<IDictionary<string, CloseResult>> AllCloseStates()
        {
            yield return null;
            yield return new Dictionary<string, CloseResult>();

            foreach (CloseResult outcome in Enum.GetValues(typeof(CloseResult)))
            {
                yield return Closed("chrome", outcome);
                yield return Closed("msedge", outcome);
                yield return new Dictionary<string, CloseResult>
                {
                    { "chrome", outcome },
                    { "msedge", outcome },
                    { "StartMenuExperienceHost", outcome }
                };
            }

            yield return Closed("chrome", (CloseResult)999);
            yield return Closed("msedge", (CloseResult)999);
        }

        // THE invariant this type exists to hold: a module the snapshot leaves out is a module the
        // restore refuses. The one legitimate exemption is a module that writes nothing at all, so
        // the implication is guarded by RestoreMakesChanges rather than asserted unconditionally.
        [Fact]
        public void AModuleTheSnapshotLeavesOut_IsAModuleTheRestoreRefuses()
        {
            int combinations = 0;

            foreach (BackupBase module in AllShapes())
            {
                foreach (IReadOnlyList<string> consented in AllConsentStates())
                {
                    foreach (IDictionary<string, CloseResult> closed in AllCloseStates())
                    {
                        RestoreScopeEntry entry = Scope(module, consented, closed);
                        combinations++;

                        if (entry.NeedsSnapshot || !entry.Module.RestoreMakesChanges)
                            continue;

                        Assert.False(
                            entry.WillBeRestored,
                            module.Title + " writes changes, was left out of the snapshot, and would " +
                            "still be restored - there would be nothing on disk to undo it.");
                    }
                }
            }

            // Guards the loops themselves: an AllShapes/AllConsentStates/AllCloseStates that quietly
            // stopped yielding would make every assertion above vacuous and the test still green.
            Assert.Equal(12 * 6 * 16, combinations);
        }

        // The other direction of the same rule, stated positively so a change that starts blocking
        // everything cannot pass the implication above by refusing every module.
        [Fact]
        public void ARestoredModuleThatWritesChanges_IsAlwaysSnapshotted()
        {
            foreach (BackupBase module in AllShapes())
            {
                foreach (IReadOnlyList<string> consented in AllConsentStates())
                {
                    foreach (IDictionary<string, CloseResult> closed in AllCloseStates())
                    {
                        RestoreScopeEntry entry = Scope(module, consented, closed);

                        if (entry.WillBeRestored && module.RestoreMakesChanges)
                            Assert.True(entry.NeedsSnapshot, module.Title + " would be restored unsnapshotted.");
                    }
                }
            }
        }

        // --- 2. The regression this type was written for ---

        // The defect: CloseProcess reports StillRunning whenever its shared five-second budget runs
        // out, which a Chrome process tree does routinely. The old pipeline dropped the module from
        // the snapshot on that reading and then restored it anyway from a second, later reading.
        [Theory]
        [InlineData(CloseResult.StillRunning)]
        [InlineData(CloseResult.AccessDenied)]
        public void ConsentedBrowserThatDidNotClose_IsNeitherSnapshottedNorRestored(CloseResult outcome)
        {
            RestoreScopeEntry entry = Scope(
                new FakeModule("Google Chrome", true, Chrome()),
                new[] { "chrome" },
                Closed("chrome", outcome));

            Assert.Equal(RestoreBlock.CouldNotClose, entry.Block);
            Assert.False(entry.WillBeRestored);
            Assert.False(entry.NeedsSnapshot);
            Assert.Equal(outcome, entry.BlockedClose);
            Assert.Equal("chrome", entry.BlockedBy.ProcessName);
        }

        // The block has to survive a second requirement that closed cleanly: one unclosable process
        // is enough to put the module's files at risk.
        [Fact]
        public void OneUnclosableProcessAmongSeveral_BlocksTheWholeModule()
        {
            RestoreScopeEntry entry = Scope(
                new FakeModule("Two browsers", true, Chrome(), Edge()),
                new[] { "chrome", "msedge" },
                new Dictionary<string, CloseResult>
                {
                    { "chrome", CloseResult.Exited },
                    { "msedge", CloseResult.StillRunning }
                });

            Assert.Equal(RestoreBlock.CouldNotClose, entry.Block);
            Assert.False(entry.NeedsSnapshot);
            Assert.Equal("msedge", entry.BlockedBy.ProcessName);
        }

        // --- 3. Consent withheld ---

        [Fact]
        public void ConsentWithheld_IsBlockedAndNeitherSnapshottedNorRestored()
        {
            RestoreScopeEntry entry = Scope(
                new FakeModule("Google Chrome", true, Chrome()),
                new string[0],
                Closed("chrome", CloseResult.Exited));

            Assert.Equal(RestoreBlock.ConsentWithheld, entry.Block);
            Assert.False(entry.WillBeRestored);
            Assert.False(entry.NeedsSnapshot);
            Assert.Equal("chrome", entry.BlockedBy.ProcessName);
        }

        // Withheld consent outranks a clean close: the user declined the operation, so how the
        // process happens to have ended is beside the point.
        [Fact]
        public void ConsentWithheld_OutranksACleanCloseOfTheSameProcess()
        {
            RestoreScopeEntry entry = Scope(
                new FakeModule("Google Chrome", true, Chrome()),
                new[] { "msedge" },
                Closed("chrome", CloseResult.Exited));

            Assert.Equal(RestoreBlock.ConsentWithheld, entry.Block);
        }

        // --- 4. Closes that succeeded ---

        [Theory]
        [InlineData(CloseResult.Exited)]
        [InlineData(CloseResult.NotRunning)]
        public void ConsentedBrowserThatClosed_IsRestoredAndSnapshotted(CloseResult outcome)
        {
            RestoreScopeEntry entry = Scope(
                new FakeModule("Google Chrome", true, Chrome()),
                new[] { "chrome" },
                Closed("chrome", outcome));

            Assert.Equal(RestoreBlock.None, entry.Block);
            Assert.True(entry.WillBeRestored);
            Assert.True(entry.NeedsSnapshot);
            Assert.Null(entry.BlockedBy);
        }

        [Fact]
        public void ModuleWithNothingToClose_IsRestoredAndSnapshotted()
        {
            RestoreScopeEntry entry = Scope(new BareModule(), null, null);

            Assert.Equal(RestoreBlock.None, entry.Block);
            Assert.True(entry.WillBeRestored);
            Assert.True(entry.NeedsSnapshot);
        }

        // --- 5. The one legitimate "not snapshotted yet restored" ---

        // AStoreApps relies on this: its restore opens a dialog and returns Skipped, so snapshotting
        // it would pay a full winget export to protect a restore that writes nothing.
        [Fact]
        public void ModuleThatWritesNothing_IsNotSnapshottedButIsStillRestored()
        {
            RestoreScopeEntry entry = Scope(new FakeModule("Remember installed apps", false), null, null);

            Assert.False(entry.NeedsSnapshot);
            Assert.True(entry.WillBeRestored);
            Assert.Equal(RestoreBlock.None, entry.Block);
        }

        // --- 6. Non-consent requirements ---

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NonConsentRequirement_DoesNotConsultConsent(bool listed)
        {
            IReadOnlyList<string> consented = listed ? new[] { "StartMenuExperienceHost" } : new string[0];

            RestoreScopeEntry entry = Scope(
                new FakeModule("Pinned apps", true, StartMenu()),
                consented,
                null);

            Assert.Equal(RestoreBlock.None, entry.Block);
            Assert.True(entry.WillBeRestored);
            Assert.True(entry.NeedsSnapshot);
        }

        // StartMenuExperienceHost is closed just-in-time by the dispatch loop, so an up-front close
        // result for it is not evidence about the close that will actually be attempted.
        [Theory]
        [InlineData(CloseResult.StillRunning)]
        [InlineData(CloseResult.AccessDenied)]
        public void NonConsentRequirement_IsNotBlockedByAnUpFrontCloseFailure(CloseResult outcome)
        {
            RestoreScopeEntry entry = Scope(
                new FakeModule("Pinned apps", true, StartMenu()),
                null,
                Closed("StartMenuExperienceHost", outcome));

            Assert.Equal(RestoreBlock.None, entry.Block);
            Assert.True(entry.WillBeRestored);
        }

        // --- 7. DescribeBlock ---

        [Fact]
        public void DescribeBlock_ConsentWithheld_SkipsWithTheWithheldWording()
        {
            RestoreScopeEntry entry = Scope(
                new FakeModule("Google Chrome", true, Chrome()), null, null);

            StepResult step = RestoreScope.DescribeBlock(entry);

            Assert.Equal(ResultState.Skipped, step.State);
            Assert.Equal("Google Chrome", step.Target);
            Assert.Equal(
                "you chose not to close Google Chrome, so its profile was not restored",
                step.Reason);
        }

        [Fact]
        public void DescribeBlock_CouldNotClose_FailsWithTheCouldNotCloseWording()
        {
            RestoreScopeEntry entry = Scope(
                new FakeModule("Google Chrome", true, Chrome()),
                new[] { "chrome" },
                Closed("chrome", CloseResult.StillRunning));

            StepResult step = RestoreScope.DescribeBlock(entry);

            Assert.Equal(ResultState.Failed, step.State);
            Assert.Equal("Google Chrome", step.Target);
            Assert.Equal(
                "Google Chrome could not be closed, so its profile was not restored",
                step.Reason);
        }

        // Asserted against the other producer rather than against a literal, so the two cannot drift:
        // a module refused here and one refused in the dispatch loop are the same refusal reached by
        // different evidence, and must read identically to the user.
        [Fact]
        public void DescribeBlock_WithheldWording_IsCharacterForCharacterRestoreDispatchs()
        {
            RestoreCloseRequirement chrome = Chrome();

            StepResult fromScope = RestoreScope.DescribeBlock(
                Scope(new FakeModule("Google Chrome", true, chrome), null, null));

            StepResult fromDispatch = RestoreDispatch
                .Decide("Google Chrome", chrome, false, true, CloseResult.Exited)
                .CloseStep;

            Assert.Equal(fromDispatch.Reason, fromScope.Reason);
            Assert.Equal(fromDispatch.State, fromScope.State);
            Assert.Equal(fromDispatch.Target, fromScope.Target);
        }

        [Fact]
        public void DescribeBlock_CouldNotCloseWording_IsCharacterForCharacterRestoreDispatchs()
        {
            RestoreCloseRequirement chrome = Chrome();

            StepResult fromScope = RestoreScope.DescribeBlock(
                Scope(new FakeModule("Google Chrome", true, chrome),
                      new[] { "chrome" },
                      Closed("chrome", CloseResult.StillRunning)));

            StepResult fromDispatch = RestoreDispatch
                .Decide("Google Chrome", chrome, true, true, CloseResult.StillRunning)
                .CloseStep;

            Assert.Equal(fromDispatch.Reason, fromScope.Reason);
            Assert.Equal(fromDispatch.State, fromScope.State);
            Assert.Equal(fromDispatch.Target, fromScope.Target);
        }

        // Describing an unblocked entry would invent a refusal for a module that is about to run.
        [Fact]
        public void DescribeBlock_UnblockedEntry_Throws()
        {
            RestoreScopeEntry entry = Scope(new BareModule(), null, null);

            Assert.Throws<ArgumentException>(() => RestoreScope.DescribeBlock(entry));
        }

        [Fact]
        public void DescribeBlock_Null_Throws()
            => Assert.Throws<ArgumentException>(() => RestoreScope.DescribeBlock(null));

        // --- 8. Degenerate inputs ---

        [Fact]
        public void For_NullModuleList_IsEmptyNotACrash()
            => Assert.Empty(RestoreScope.For(null, new[] { "chrome" }, Closed("chrome", CloseResult.Exited)));

        [Fact]
        public void For_NullModuleInTheList_IsDroppedNotACrash()
        {
            IReadOnlyList<RestoreScopeEntry> entries =
                RestoreScope.For(new BackupBase[] { null, new BareModule(), null }, null, null);

            RestoreScopeEntry entry = Only(entries);
            Assert.Equal("Mouse", entry.Module.Title);
        }

        [Fact]
        public void For_NullCloseResults_TreatsNothingAsHavingFailedToClose()
        {
            RestoreScopeEntry entry = Scope(
                new FakeModule("Google Chrome", true, Chrome()), new[] { "chrome" }, null);

            Assert.Equal(RestoreBlock.None, entry.Block);
            Assert.True(entry.NeedsSnapshot);
        }

        [Fact]
        public void For_ModuleDeclaringANullRequirementList_IsRestoredAndSnapshotted()
        {
            RestoreScopeEntry entry = Scope(new NullDeclaringModule(), null, null);

            Assert.Equal(RestoreBlock.None, entry.Block);
            Assert.True(entry.NeedsSnapshot);
        }

        [Fact]
        public void For_PreservesOneEntryPerModuleInOrder()
        {
            IReadOnlyList<RestoreScopeEntry> entries = RestoreScope.For(
                new BackupBase[]
                {
                    new FakeModule("first", true),
                    new FakeModule("second", true, Chrome()),
                    new FakeModule("third", false)
                },
                null,
                null);

            Assert.Equal(3, entries.Count);
            Assert.Equal("first", entries[0].Module.Title);
            Assert.Equal("second", entries[1].Module.Title);
            Assert.Equal("third", entries[2].Module.Title);
        }

        // --- 9. Consent matching ---

        // Process names come from the consent dialog's own declarations on one side and from
        // Process.GetProcessesByName on the other; neither guarantees casing.
        [Theory]
        [InlineData("chrome")]
        [InlineData("Chrome")]
        [InlineData("CHROME")]
        public void IsConsented_MatchesRegardlessOfCase(string listed)
            => Assert.True(RestoreScope.IsConsented(new[] { listed }, "chrome"));

        [Fact]
        public void IsConsented_UnlistedProcess_IsNotConsented()
            => Assert.False(RestoreScope.IsConsented(new[] { "msedge", "firefox" }, "chrome"));

        // Nothing consented is the safe reading of "no consent was collected", and it is the reading
        // that blocks rather than the one that proceeds.
        [Fact]
        public void IsConsented_NullList_ConsentsToNothing()
            => Assert.False(RestoreScope.IsConsented(null, "chrome"));

        [Fact]
        public void ConsentMatching_IsCaseInsensitiveEndToEnd()
        {
            RestoreScopeEntry entry = Scope(
                new FakeModule("Google Chrome", true, Chrome()),
                new[] { "CHROME" },
                Closed("chrome", CloseResult.Exited));

            Assert.Equal(RestoreBlock.None, entry.Block);
            Assert.True(entry.NeedsSnapshot);
        }

        // --- The shared HasBackupIn guard ---

        /// <summary>A module whose probe throws, i.e. one that has told us nothing.</summary>
        private sealed class ThrowingProbeModule : BackupBase
        {
            public ThrowingProbeModule() { Title = "Throws"; }

            public override bool HasBackupIn(string restorePath)
                => throw new UnauthorizedAccessException("cannot read the backup folder");
        }

        // The fallback direction, which is the whole content of this guard: an unreliable answer
        // falls TOWARDS restoring. Silently skipping a restore the user asked for loses their
        // intent; an unnecessary close loses their tabs, and only one of those is recoverable.
        [Fact]
        public void HasBackup_TreatsAModuleWhoseProbeThrowsAsHavingSomething()
        {
            Assert.True(RestoreScope.HasBackup(new ThrowingProbeModule(), @"C:\backups\whatever"));
        }

        [Fact]
        public void HasBackup_OtherwiseReportsWhatTheModuleSaid()
        {
            // BareModule inherits the BackupBase default, which is "assume there is something".
            Assert.True(RestoreScope.HasBackup(new BareModule(), @"C:\backups\whatever"));
        }

        // It is internal rather than private so ConfPageView.ProcessesWorthClosing can call it
        // instead of carrying its own copy. Two implementations of one rule is the drift this type
        // exists to remove: if the copies diverged, ProcessesWorthClosing would close an app that
        // Evaluate then blocks as NothingToRestore, spending the user's open tabs on a module that
        // is never restored. This asserts the seam stays reachable from outside the type.
        [Fact]
        public void HasBackup_IsReachableByBothCallers()
        {
            System.Reflection.MethodInfo m = typeof(RestoreScope).GetMethod(
                "HasBackup",
                System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Public);

            Assert.NotNull(m);
            Assert.True(m.IsAssembly || m.IsPublic,
                "RestoreScope.HasBackup must stay callable from ConfPageView, or that call site "
                + "grows a second copy of the rule again.");
        }
    }
}
