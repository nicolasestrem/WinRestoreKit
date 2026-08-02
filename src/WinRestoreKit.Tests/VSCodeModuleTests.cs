using WinRestoreKit;
using Conf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace WinRestoreKit.Tests
{
    // EVSCode's BEHAVIOUR, not just its declaration.
    //
    // It is the one module in the Developer category that hand-rolls its async pair instead of
    // inheriting FileModule's sealed one, so FileModuleTests proves nothing about it. The three
    // rules it re-implements by hand - the {Title}\ layout, per-file naming, and the restore-side
    // "absence is always normal, reason is always NothingBackedUp" - are duplicated copies with
    // nothing holding them in agreement with the original. Duplicated rules drift; these tests are
    // what would notice.
    //
    // Both targets are repointed at temp trees (Files is a public field, SnippetsFolder has an
    // internal setter reachable through InternalsVisibleTo), so nothing here reads or writes the
    // tester's real VS Code profile.
    public class VSCodeModuleTests
    {
        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "acvsc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>An EVSCode pointed entirely at <paramref name="root"/>.</summary>
        private static EVSCode PointedAt(string root)
        {
            EVSCode m = new EVSCode();

            m.Files.Clear();
            m.Files.Add(Path.Combine(root, "settings.json"));
            m.Files.Add(Path.Combine(root, "keybindings.json"));
            m.SnippetsFolder = Path.Combine(root, "snippets");

            return m;
        }

        [Fact]
        public async Task WritesOneArtifactPerDeclaredFile_UnderItsOwnSubfolder()
        {
            string live = NewTempDir();
            string backup = NewTempDir();

            try
            {
                EVSCode m = PointedAt(live);

                File.WriteAllText(m.Files[0], "{\"editor.fontSize\":14}");
                File.WriteAllText(m.Files[1], "[{\"key\":\"ctrl+k\"}]");
                Directory.CreateDirectory(m.SnippetsFolder);
                File.WriteAllText(Path.Combine(m.SnippetsFolder, "csharp.json"), "{\"snip\":1}");

                ModuleResult r = await m.BackupAsync(backup);
                Assert.Equal(ResultState.Succeeded, r.State);

                string dir = Path.Combine(backup, m.Title);

                // One artifact per file, names distinct - the check ETerminal gets and this
                // module previously had nothing equivalent to.
                string[] names = Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(n => n).ToArray();

                Assert.Equal(new[] { "keybindings.json", "settings.json" }, names);
                Assert.Equal(m.Files.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());

                // Snippets inside {Title}\, which is what HasBackupIn's probe depends on.
                Assert.True(File.Exists(Path.Combine(dir, "snippets", "csharp.json")));
            }
            finally
            {
                Directory.Delete(live, recursive: true);
                Directory.Delete(backup, recursive: true);
            }
        }

        // The one test that catches the two hand-written path computations diverging: backup
        // writes a name, restore reads a name, and nothing but this asserts they are the same.
        [Fact]
        public async Task BackupThenRestore_RoundTripsBothFilesAndTheSnippets()
        {
            string live = NewTempDir();
            string backup = NewTempDir();
            string restored = NewTempDir();

            try
            {
                EVSCode writer = PointedAt(live);

                File.WriteAllText(writer.Files[0], "{\"editor.fontSize\":14}");
                File.WriteAllText(writer.Files[1], "[{\"key\":\"ctrl+k\"}]");
                Directory.CreateDirectory(writer.SnippetsFolder);
                File.WriteAllText(Path.Combine(writer.SnippetsFolder, "csharp.json"), "{\"snip\":1}");

                Assert.Equal(ResultState.Succeeded, (await writer.BackupAsync(backup)).State);

                EVSCode reader = PointedAt(restored);
                ModuleResult r = await reader.RestoreAsync(backup);

                Assert.Equal(ResultState.Succeeded, r.State);
                Assert.Equal("{\"editor.fontSize\":14}", File.ReadAllText(reader.Files[0]));
                Assert.Equal("[{\"key\":\"ctrl+k\"}]", File.ReadAllText(reader.Files[1]));
                Assert.Equal("{\"snip\":1}",
                    File.ReadAllText(Path.Combine(reader.SnippetsFolder, "csharp.json")));
            }
            finally
            {
                Directory.Delete(live, recursive: true);
                Directory.Delete(backup, recursive: true);
                Directory.Delete(restored, recursive: true);
            }
        }

        // The duplicated restore-side rule. FileModule hard-codes it in a sealed method; this
        // module repeats it by hand for both the files and the snippets folder, so it is pinned
        // by hand too.
        [Fact]
        public async Task RestoreWithNothingBackedUp_SaysSoInTheBackupFoldersTerms()
        {
            string live = NewTempDir();
            string backup = NewTempDir();

            try
            {
                ModuleResult r = await PointedAt(live).RestoreAsync(backup);

                Assert.Equal(ResultState.Skipped, r.State);
                Assert.Equal(3, r.Steps.Count);

                // Every step, snippets included - no per-step wording about the live machine.
                Assert.All(r.Steps, s =>
                    Assert.Equal("nothing was backed up for this item", s.Reason));
            }
            finally
            {
                Directory.Delete(live, recursive: true);
                Directory.Delete(backup, recursive: true);
            }
        }

        // The CLAUDE.md snapshot invariant: anything the restore writes must be inside the
        // pre-restore snapshot, and the snapshot is taken by running this module's own Backup.
        // Structural for FileModule subclasses; this module declares by hand, so it can be wrong.
        [Fact]
        public async Task RestoreWritesOnlyWhatBackupReadsAndWhatItDeclared()
        {
            string live = NewTempDir();
            string backup = NewTempDir();

            try
            {
                EVSCode m = PointedAt(live);

                File.WriteAllText(m.Files[0], "a");
                File.WriteAllText(m.Files[1], "b");
                Directory.CreateDirectory(m.SnippetsFolder);
                File.WriteAllText(Path.Combine(m.SnippetsFolder, "x.json"), "c");

                await m.BackupAsync(backup);

                // What restore would write, per the module's own declaration.
                string[] declared = m.RestoreTargets.Select(t => t.Path).OrderBy(p => p).ToArray();

                string[] expected = m.Files
                    .Concat(new[] { m.SnippetsFolder })
                    .OrderBy(p => p)
                    .ToArray();

                Assert.Equal(expected, declared);

                // And every declared path is one Backup actually captured, so the snapshot covers
                // the whole restore surface.
                foreach (string f in m.Files)
                    Assert.True(File.Exists(Path.Combine(backup, m.Title, Path.GetFileName(f))));

                Assert.True(Directory.Exists(Path.Combine(backup, m.Title, "snippets")));
            }
            finally
            {
                Directory.Delete(live, recursive: true);
                Directory.Delete(backup, recursive: true);
            }
        }

        // The earned close check, now artifact-aware rather than directory-aware. An empty
        // snippets folder is the cheap way to produce a {Title}\ directory holding nothing, and
        // it must NOT be enough to get VS Code killed.
        [Fact]
        public async Task HasBackupIn_IsFalseForABackupDirectoryHoldingNothing()
        {
            string live = NewTempDir();
            string backup = NewTempDir();

            try
            {
                EVSCode m = PointedAt(live);

                // No settings, no keybindings, and a snippets folder that exists but is empty.
                Directory.CreateDirectory(m.SnippetsFolder);

                await m.BackupAsync(backup);

                // The directory is there...
                Assert.True(Directory.Exists(Path.Combine(backup, m.Title, "snippets")));

                // ...and it still must not earn the close, because there is nothing to restore.
                Assert.False(m.HasBackupIn(backup));
            }
            finally
            {
                Directory.Delete(live, recursive: true);
                Directory.Delete(backup, recursive: true);
            }
        }

        [Fact]
        public async Task HasBackupIn_IsTrueOnceAnyRealArtifactExists()
        {
            string live = NewTempDir();
            string fileOnly = NewTempDir();
            string snippetOnly = NewTempDir();

            try
            {
                EVSCode m = PointedAt(live);

                File.WriteAllText(m.Files[0], "{}");
                await m.BackupAsync(fileOnly);
                Assert.True(m.HasBackupIn(fileOnly));

                // And a snippet on its own is equally enough.
                File.Delete(m.Files[0]);
                Directory.CreateDirectory(m.SnippetsFolder);
                File.WriteAllText(Path.Combine(m.SnippetsFolder, "s.json"), "{}");
                await m.BackupAsync(snippetOnly);
                Assert.True(m.HasBackupIn(snippetOnly));
            }
            finally
            {
                Directory.Delete(live, recursive: true);
                Directory.Delete(fileOnly, recursive: true);
                Directory.Delete(snippetOnly, recursive: true);
            }
        }

        // A file present and one absent must not fail the module: VS Code writes keybindings.json
        // only once the user customises a binding, so its absence is the common case.
        [Fact]
        public async Task Backup_WithOnlySettingsPresent_SucceedsAndSkipsTheRest()
        {
            string live = NewTempDir();
            string backup = NewTempDir();

            try
            {
                EVSCode m = PointedAt(live);
                File.WriteAllText(m.Files[0], "{}");

                ModuleResult r = await m.BackupAsync(backup);

                Assert.Equal(ResultState.Succeeded, r.State);
                Assert.Equal(ResultState.Succeeded, r.Steps[0].State);
                Assert.Equal(ResultState.Skipped, r.Steps[1].State);
                Assert.Equal(ResultState.Skipped, r.Steps[2].State);
            }
            finally
            {
                Directory.Delete(live, recursive: true);
                Directory.Delete(backup, recursive: true);
            }
        }
    }
}
