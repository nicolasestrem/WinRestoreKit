using WinRestoreKit;
using Conf;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System;
using Xunit;

namespace WinRestoreKit.Tests
{
    // BackupBase.HasArtifactIn - the restore wizard's "is anything for this module actually in this
    // folder" probe (Phase 4, after PR #15 review).
    //
    // Why it exists at all: the wizard first asked RestoreScope.HasBackup, which ends at
    // HasBackupIn, whose documented default is TRUE because it guards process kills. Every module
    // answered yes, so a backup holding one module's files rendered as thirty enabled, pre-ticked
    // rows and the run snapshotted and attempted all thirty. EmptyFolder_NoModuleClaimsAnArtifact
    // below is the test that fails loudly against that old behaviour.
    public class ArtifactProbeTests
    {
        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "acprobe_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static IEnumerable<BackupBase> Catalog()
        {
            foreach (ModuleRegistration r in ModuleCatalog.CreateAll())
                yield return r.Module;
        }

        /// <summary>
        /// Modules that answer a flat true regardless of what the folder holds, because folder
        /// contents are the wrong question for them.
        /// </summary>
        /// <remarks>
        /// Exactly one, and it should stay that way: AppStoreApps restores by opening a dialog with
        /// its own backup-folder picker, so the folder being examined is not the one it will read.
        /// Its class comment carries the full argument. Anything added here is exempting itself from
        /// the invariant below, so it needs the same standard of reasoning written down first.
        /// </remarks>
        private static bool AnswersTrueByDesign(BackupBase module)
            => module.GetType().Name == "AppStoreApps";

        [Fact]
        public void EmptyFolder_NoModuleClaimsAnArtifact()
        {
            // The invariant the whole seam exists for. False or "cannot tell" are both acceptable;
            // TRUE over an empty directory is the bug, whoever reintroduces it.
            string dir = NewTempDir();

            try
            {
                List<string> liars = new List<string>();

                foreach (BackupBase module in Catalog())
                {
                    if (!AnswersTrueByDesign(module) && module.HasArtifactIn(dir) == true)
                        liars.Add(module.GetType().Name);
                }

                Assert.Empty(liars);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void EveryCatalogModuleGivesADefiniteAnswer()
        {
            // EVERY module, not just the four probed families. A null here means the wizard falls
            // through to the manifest-silence rule, which resolves to "not offered" whenever a
            // manifest exists without naming the module - and that happens on any second backup in
            // one app session, because the backup folder is reused and its manifest rewritten. So a
            // module that cannot look gets silently greyed out over a folder holding its files.
            //
            // This started as a four-family sweep and passed while eight modules answered null. The
            // safety review caught it; the sweep is the whole catalog now so it cannot recur. A new
            // module that cannot answer must say so out loud by overriding to a literal, the way
            // AppStoreApps does - see its comment for the one legitimate reason to.
            string dir = NewTempDir();

            try
            {
                List<string> undecided = new List<string>();

                foreach (BackupBase module in Catalog())
                {
                    if (module.HasArtifactIn(dir) == null)
                        undecided.Add(module.GetType().Name);
                }

                Assert.Empty(undecided);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void FolderAndKeyModules_FindEitherHalf()
        {
            // WFonts, WTaskbar and WThemes each back up folders AND registry keys, and the halves
            // are independently restorable - so either one alone has to count.
            string dir = NewTempDir();

            try
            {
                BackupBase module = ByName("WThemes");

                Assert.False(module.HasArtifactIn(dir));

                // The key half only. Uses the module's own naming, which for WThemes routes through
                // a RegFileNameFor override for its legacy "Themes.reg" spelling.
                List<string> keys = (List<string>)module.GetType().GetField("Keys").GetValue(module);
                File.WriteAllText(Path.Combine(dir, RegNameFor(module, keys[0])), "REGEDIT4\r\n");

                Assert.True(module.HasArtifactIn(dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void FolderAndKeyModules_EmptyCopiedFolderIsNotABackup()
        {
            string dir = NewTempDir();

            try
            {
                BackupBase module = ByName("WThemes");
                List<string> folders = (List<string>)module.GetType().GetField("Folders").GetValue(module);

                // Utils.CopyFolder creates the destination before copying, so a run where every file
                // failed leaves exactly this.
                string copied = Path.Combine(dir, FolderNameFor(module, folders[0]));
                Directory.CreateDirectory(copied);
                Assert.False(module.HasArtifactIn(dir));

                File.WriteAllText(Path.Combine(copied, "theme.deskthemepack"), "x");
                Assert.True(module.HasArtifactIn(dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void AppStoreApps_SaysYesOnPurposeRatherThanCannotTell()
        {
            // Its restore opens a dialog with its own folder picker, so folder contents are the
            // wrong question. It must answer a literal true, never null - null would let the
            // manifest-silence rule grey out a dialog that was perfectly able to open.
            string dir = NewTempDir();

            try
            {
                Assert.True(ByName("AppStoreApps").HasArtifactIn(dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void RegistryModule_FindsTheRegFileItWouldWrite()
        {
            string dir = NewTempDir();

            try
            {
                BackupBase module = First<RegistryModule>();

                Assert.False(module.HasArtifactIn(dir));

                // Asks the module for its own filename rather than restating the rule - the naming
                // contract itself is pinned by BackupFileNamingTests, and a second copy here would
                // just be another thing to update.
                File.WriteAllText(Path.Combine(dir, RegNameFor(module, KeyOf(module))), "REGEDIT4\r\n");

                Assert.True(module.HasArtifactIn(dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void MultiKeyRegistryModule_OneKeysFileIsEnough()
        {
            string dir = NewTempDir();

            try
            {
                MultiKeyRegistryModule module = (MultiKeyRegistryModule)First<MultiKeyRegistryModule>();

                Assert.False(module.HasArtifactIn(dir));

                // A partial export is a routine outcome - AbsenceIsNormal makes a missing key a
                // non-failure - and still leaves something restorable.
                File.WriteAllText(Path.Combine(dir, RegNameFor(module, module.Keys[0])), "REGEDIT4\r\n");

                Assert.True(module.HasArtifactIn(dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void FolderModule_EmptyDirectoryIsNotABackup()
        {
            string dir = NewTempDir();

            try
            {
                BackupBase module = First<FolderModule>();
                string inner = Path.Combine(dir, module.Title);

                // Utils.CopyFolder creates its destination before copying, so a backup where every
                // file failed leaves exactly this behind. Existence alone would call it a backup.
                Directory.CreateDirectory(inner);
                Assert.False(module.HasArtifactIn(dir));

                File.WriteAllText(Path.Combine(inner, "something.txt"), "x");
                Assert.True(module.HasArtifactIn(dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void NullOrBlankPath_IsNotAnArtifact()
        {
            foreach (BackupBase module in Catalog())
            {
                // Same documented exception as the empty-folder invariant; covered on its own by
                // AppStoreApps_SaysYesOnPurposeRatherThanCannotTell.
                if (AnswersTrueByDesign(module))
                    continue;

                Assert.NotEqual(true, module.HasArtifactIn(null));
                Assert.NotEqual(true, module.HasArtifactIn("   "));
            }
        }

        private static BackupBase First<T>() where T : BackupBase
        {
            foreach (BackupBase module in Catalog())
            {
                if (module is T)
                    return module;
            }

            throw new InvalidOperationException("No " + typeof(T).Name + " in the catalog.");
        }

        private static BackupBase ByName(string typeName)
        {
            foreach (BackupBase module in Catalog())
            {
                if (module.GetType().Name == typeName)
                    return module;
            }

            throw new InvalidOperationException("No " + typeName + " in the catalog.");
        }

        /// <summary>The module's own BackupFolderNameFor - protected, so reflection.</summary>
        private static string FolderNameFor(BackupBase module, string folder)
        {
            MethodInfo m = typeof(BackupBase).GetMethod("BackupFolderNameFor",
                BindingFlags.Instance | BindingFlags.NonPublic);

            return (string)m.Invoke(module, new object[] { folder });
        }

        /// <summary>
        /// The module's own RegFileNameFor. Protected, so reflection - the same approach
        /// BackupFileNamingTests uses, and for the same reason: asking the module beats restating
        /// its rule in a test that is not about naming.
        /// </summary>
        private static string RegNameFor(BackupBase module, string key)
        {
            for (Type t = module.GetType(); t != null; t = t.BaseType)
            {
                MethodInfo m = t.GetMethod("RegFileNameFor",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                if (m != null)
                    return (string)m.Invoke(module, new object[] { key });
            }

            throw new InvalidOperationException("No RegFileNameFor on " + module.GetType().Name);
        }

        /// <summary>The single Key a RegistryModule exports, which is likewise protected.</summary>
        private static string KeyOf(BackupBase module)
        {
            for (Type t = module.GetType(); t != null; t = t.BaseType)
            {
                PropertyInfo p = t.GetProperty("Key",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                if (p != null)
                    return (string)p.GetValue(module);
            }

            throw new InvalidOperationException("No Key on " + module.GetType().Name);
        }
    }
}
