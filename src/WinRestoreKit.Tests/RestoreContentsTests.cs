using WinRestoreKit;
using System.Collections.Generic;
using Xunit;

namespace WinRestoreKit.Tests
{
    // RestoreContents is the restore wizard's step-2 view-model builder (Phase 4 PR 7). Presence is
    // the manifest where it speaks, and BackupBase.HasArtifactIn where it does not (so a module the
    // folder holds nothing for greys out before the run, not after); state is joined by CLR type
    // name; provenance is null unless something differs.
    //
    // Presence deliberately does NOT come from RestoreScope.HasBackup, which is what the first
    // version used. That fails open by design - it is the gate protecting process kills - so it
    // said yes for every module and the greying meant nothing. These tests pin the replacement.
    public class RestoreContentsTests
    {
        /// <summary>Its files are in the folder.</summary>
        private sealed class PresentModule : BackupBase
        {
            public PresentModule() { Title = "Present"; }
            public override bool? HasArtifactIn(string backupPath) => true;
        }

        /// <summary>It looked, and they are not.</summary>
        private sealed class AbsentModule : BackupBase
        {
            public AbsentModule() { Title = "Absent"; Info = "an item"; }
            public override bool? HasArtifactIn(string backupPath) => false;
        }

        /// <summary>
        /// Cannot tell - the BackupBase default, and the shape of any module never taught to probe.
        /// Also the one that would answer a misleading "true" if this used HasBackupIn.
        /// </summary>
        private sealed class SilentModule : BackupBase
        {
            public SilentModule() { Title = "Silent"; }
        }

        private static ManifestData Manifest(string machine, string user, params ManifestModule[] modules)
            => new ManifestData(1, "0.31.0", "now", machine, user, "build", modules);

        private static ManifestModule Entry(BackupBase module, string state)
            => new ManifestModule(module.GetType().Name, module.Title, state, "");

        [Fact]
        public void For_NoManifest_UsesTheModuleArtifactProbe()
        {
            IReadOnlyList<RestoreContentsRow> rows = RestoreContents.For(
                new BackupBase[] { new PresentModule(), new AbsentModule() }, @"X:\backup\", null);

            Assert.Equal(2, rows.Count);
            Assert.True(rows[0].HasBackup);
            Assert.False(rows[1].HasBackup);
        }

        [Fact]
        public void For_NoManifestAndModuleCannotTell_IsOffered()
        {
            // Nothing left to consult. Hiding a restore we cannot disprove is the worse error.
            IReadOnlyList<RestoreContentsRow> rows = RestoreContents.For(
                new BackupBase[] { new SilentModule() }, @"X:\backup\", null);

            Assert.True(rows[0].HasBackup);
        }

        [Fact]
        public void For_ManifestSucceeded_OverridesAProbeThatSaysNo()
        {
            AbsentModule module = new AbsentModule();
            ManifestData manifest = Manifest("M", "U", Entry(module, BackupManifest.StateSucceeded));

            IReadOnlyList<RestoreContentsRow> rows = RestoreContents.For(
                new BackupBase[] { module }, @"X:\backup\", manifest);

            Assert.True(rows[0].HasBackup);
        }

        [Theory]
        [InlineData(BackupManifest.StateSkipped)]
        [InlineData(BackupManifest.StateFailed)]
        public void For_ManifestSkippedOrFailed_OverridesAProbeThatSaysYes(string state)
        {
            // The reused-folder case: stale files from an earlier run into the same folder are
            // sitting there, and the manifest is the record of what THIS run wrote. It wins.
            PresentModule module = new PresentModule();
            ManifestData manifest = Manifest("M", "U", Entry(module, state));

            IReadOnlyList<RestoreContentsRow> rows = RestoreContents.For(
                new BackupBase[] { module }, @"X:\backup\", manifest);

            Assert.False(rows[0].HasBackup);
        }

        [Fact]
        public void For_ManifestSilentButArtifactsFound_IsOffered()
        {
            ManifestData manifest = Manifest("M", "U",
                new ManifestModule("SomethingElse", "t", BackupManifest.StateSucceeded, ""));

            IReadOnlyList<RestoreContentsRow> rows = RestoreContents.For(
                new BackupBase[] { new PresentModule() }, @"X:\backup\", manifest);

            Assert.True(rows[0].HasBackup);
        }

        [Fact]
        public void For_ManifestSilentAndModuleCannotTell_IsNotOffered()
        {
            // A manifest exists and does not mention this module, and the module cannot prove
            // otherwise: it was not part of that run. This is the case that used to render thirty
            // pre-ticked rows over a backup holding one module's files.
            ManifestData manifest = Manifest("M", "U",
                new ManifestModule("SomethingElse", "t", BackupManifest.StateSucceeded, ""));

            IReadOnlyList<RestoreContentsRow> rows = RestoreContents.For(
                new BackupBase[] { new SilentModule() }, @"X:\backup\", manifest);

            Assert.False(rows[0].HasBackup);
        }

        [Fact]
        public void For_ManifestStateMapsByTypeName()
        {
            PresentModule present = new PresentModule();
            ManifestData manifest = Manifest("M", "U",
                new ManifestModule(present.GetType().Name, "Present", BackupManifest.StateSucceeded, ""));

            IReadOnlyList<RestoreContentsRow> rows = RestoreContents.For(new[] { (BackupBase)present }, @"X:\backup\", manifest);

            Assert.Equal(BackupManifest.StateSucceeded, rows[0].ManifestState);
        }

        [Fact]
        public void For_TypeAbsentFromManifest_HasNullState()
        {
            ManifestData manifest = Manifest("M", "U",
                new ManifestModule("SomethingElse", "t", BackupManifest.StateSucceeded, ""));

            IReadOnlyList<RestoreContentsRow> rows = RestoreContents.For(new[] { new PresentModule() }, @"X:\backup\", manifest);

            Assert.Null(rows[0].ManifestState);
        }

        [Fact]
        public void For_NullManifest_AllStatesNull()
        {
            IReadOnlyList<RestoreContentsRow> rows = RestoreContents.For(new[] { new PresentModule() }, @"X:\backup\", null);

            Assert.Null(rows[0].ManifestState);
        }

        [Fact]
        public void For_NullModules_ReturnsEmpty()
        {
            Assert.Empty(RestoreContents.For(null, @"X:\backup\", null));
        }

        [Fact]
        public void DescribeProvenance_NullManifest_ReturnsNull()
        {
            Assert.Null(RestoreContents.DescribeProvenance(null, "M", "U"));
        }

        [Fact]
        public void DescribeProvenance_SameMachineAndUser_ReturnsNull()
        {
            Assert.Null(RestoreContents.DescribeProvenance(Manifest("M", "U"), "M", "U"));
        }

        [Fact]
        public void DescribeProvenance_IgnoresCase()
        {
            Assert.Null(RestoreContents.DescribeProvenance(Manifest("desk-top", "nicol"), "DESK-TOP", "NICOL"));
        }

        [Fact]
        public void DescribeProvenance_DifferingMachine_NamesIt()
        {
            string sentence = RestoreContents.DescribeProvenance(Manifest("OTHER-PC", "U"), "THIS-PC", "U");

            Assert.NotNull(sentence);
            Assert.Contains("OTHER-PC", sentence);
            Assert.DoesNotContain("user", sentence);
        }

        [Fact]
        public void DescribeProvenance_DifferingUser_NamesIt()
        {
            string sentence = RestoreContents.DescribeProvenance(Manifest("M", "alice"), "M", "bob");

            Assert.NotNull(sentence);
            Assert.Contains("alice", sentence);
            Assert.DoesNotContain("machine", sentence);
        }

        [Fact]
        public void DescribeProvenance_DifferingBoth_NamesBoth()
        {
            string sentence = RestoreContents.DescribeProvenance(Manifest("OTHER", "alice"), "THIS", "bob");

            Assert.Contains("OTHER", sentence);
            Assert.Contains("alice", sentence);
        }
    }
}
