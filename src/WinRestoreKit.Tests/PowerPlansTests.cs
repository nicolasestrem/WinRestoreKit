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
    // WPowerPlans, pinned at its parser, its artifact naming, and what it DECLARES.
    //
    // Nothing here launches powercfg. Every test either parses a captured string or drives the
    // restore ladder to a decision it reaches BEFORE any process is started - a missing manifest, an
    // unreadable one, one that names no active plan. Coverage stops exactly where elevation begins:
    // whether powercfg /setactive actually switches the plan, and what it returns for a GUID that is
    // not on the machine, are on the manual smoke matrix, as they are for the netsh modules.
    public class PowerPlansTests
    {
        // Verbatim output from powercfg on Windows 11, 2026-07-21. These strings are the fixture;
        // do not tidy the blank lines or the double spaces out of them.
        private const string MeasuredList =
            "\r\nExisting Power Schemes (* Active)\r\n" +
            "-----------------------------------\r\n" +
            "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)\r\n" +
            "Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance) *\r\n" +
            "Power Scheme GUID: a1841308-3541-4fab-bc81-f71556f20b4a  (Power saver)\r\n";

        private const string MeasuredActive =
            "Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)\r\n";

        private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
        private const string HighPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        private const string SaverGuid = "a1841308-3541-4fab-bc81-f71556f20b4a";

        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pp_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        // --- parsing ---------------------------------------------------------------------

        [Fact]
        public void ParseSchemeList_ReadsEveryGuidInOrder()
        {
            IReadOnlyList<PowerSchemeEntry> schemes = WPowerPlans.ParseSchemeList(MeasuredList);

            Assert.Equal(
                new[] { BalancedGuid, HighPerfGuid, SaverGuid },
                schemes.Select(s => s.Guid).ToArray());
        }

        // The active plan is identified by the trailing "*", not by the word "Active" in the banner
        // and not by the friendly name - both are localised.
        [Fact]
        public void ParseSchemeList_MarksTheStarredSchemeActive()
        {
            IReadOnlyList<PowerSchemeEntry> schemes = WPowerPlans.ParseSchemeList(MeasuredList);

            Assert.Equal(
                new[] { HighPerfGuid },
                schemes.Where(s => s.IsActive).Select(s => s.Guid).ToArray());
        }

        [Fact]
        public void ParseSchemeList_CapturesTheFriendlyNameForDisplay()
        {
            IReadOnlyList<PowerSchemeEntry> schemes = WPowerPlans.ParseSchemeList(MeasuredList);

            Assert.Equal(
                new[] { "Balanced", "High performance", "Power saver" },
                schemes.Select(s => s.Name).ToArray());
        }

        [Fact]
        public void ParseActiveScheme_ReadsTheOneGuid()
            => Assert.Equal(HighPerfGuid, WPowerPlans.ParseActiveSchemeGuid(MeasuredActive));

        // THE test that matters for anyone shipping this outside an English install. powercfg's
        // labels are translated; the GUIDs and the "*" are not. A parser that keyed off
        // "Power Scheme GUID:" would pass every other test in this file and find zero plans on a
        // German or French machine - the same defect as CWiFiConf's "WLAN*.xml" glob, which matched
        // 0 of 19 real exports because the filename it assumed was machine-specific.
        [Fact]
        public void ParseSchemeList_IsIndependentOfTheLocalisedLabels()
        {
            const string germanish =
                "\r\nVorhandene Energieschemas (* Aktiv)\r\n" +
                "-----------------------------------\r\n" +
                "GUID des Energieschemas: 381b4222-f694-41f0-9685-ff5bb260df2e  (Ausbalanciert)\r\n" +
                "GUID des Energieschemas: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (Höchstleistung) *\r\n" +
                "GUID des Energieschemas: a1841308-3541-4fab-bc81-f71556f20b4a  (Energiesparmodus)\r\n";

            IReadOnlyList<PowerSchemeEntry> translated = WPowerPlans.ParseSchemeList(germanish);

            Assert.Equal(
                WPowerPlans.ParseSchemeList(MeasuredList).Select(s => s.Guid).ToArray(),
                translated.Select(s => s.Guid).ToArray());

            Assert.Equal(new[] { HighPerfGuid }, translated.Where(s => s.IsActive).Select(s => s.Guid).ToArray());
        }

        [Fact]
        public void ParseSchemeList_TextWithNoGuids_YieldsNothing()
        {
            Assert.Empty(WPowerPlans.ParseSchemeList("powercfg is not recognised as a command"));
            Assert.Empty(WPowerPlans.ParseSchemeList(""));
            Assert.Empty(WPowerPlans.ParseSchemeList(null));
        }

        [Fact]
        public void ParseActiveScheme_TextWithNoGuid_YieldsNull()
        {
            Assert.Null(WPowerPlans.ParseActiveSchemeGuid("Zugriff verweigert"));
            Assert.Null(WPowerPlans.ParseActiveSchemeGuid(""));
            Assert.Null(WPowerPlans.ParseActiveSchemeGuid(null));
        }

        // --- naming ----------------------------------------------------------------------

        // A loop over N schemes must build N distinct filenames - the rule BackupBase.RegFileNameFor
        // exists to enforce, applied to this module's .pow exports. WThemes shipped the opposite:
        // one name built from the Title inside a foreach, so a second export deleted the first and
        // wrote over it while both steps reported success.
        [Fact]
        public void PowFileName_IsDistinctPerScheme()
        {
            Assert.NotEqual(
                WPowerPlans.PowFileNameFor(BalancedGuid),
                WPowerPlans.PowFileNameFor(HighPerfGuid));
        }

        [Fact]
        public void PowFileName_CarriesTheGuidAndNoPathSeparators()
        {
            string name = WPowerPlans.PowFileNameFor(HighPerfGuid);

            Assert.Contains(HighPerfGuid, name);
            Assert.EndsWith(".pow", name);
            Assert.DoesNotContain("\\", name);
            Assert.DoesNotContain("/", name);
            Assert.Equal(name, Path.GetFileName(name));
        }

        // --- the failed export leaves nothing behind --------------------------------------

        // MEASURED on Windows 11, 2026-07-21, unelevated: powercfg /export creates the target file
        // and THEN fails - "A required privilege is not held by the client", exit code 1, zero-byte
        // .pow left at the path. So a Failed row would otherwise ship a backup folder containing an
        // artifact this module's own warning text invites the user to import by hand.
        //
        // This test drives the real cleanup rather than a copy of it, so a call site that reverts to
        // a bare StepResult.Failed fails here.
        [Fact]
        public void FailedExport_RemovesTheFilePowercfgLeftBehind()
        {
            string dir = NewTempDir();

            try
            {
                string file = Path.Combine(dir, WPowerPlans.PowFileNameFor(BalancedGuid));
                File.WriteAllText(file, "");

                StepResult step = WPowerPlans.AbandonExport(file, "Balanced", "powercfg exited with code 1");

                Assert.False(File.Exists(file));
                Assert.Equal(ResultState.Failed, step.State);

                // The cleanup must not displace the reason the user has to act on.
                Assert.Equal("powercfg exited with code 1", step.Reason);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // The other route to a junk .pow: powercfg exits 0 and the artifact check rejects what it
        // wrote. Not hypothetical - the measurement that made ValidateExportArtifact mandatory at
        // all was netsh wlan export printing "saved successfully" with exit code 0 while writing
        // nothing. An empty .pow surviving that would be offered to the user as the file to import
        // by hand, which is the same dishonesty as the zero-byte case above by a different door.
        [Fact]
        public void AnExportRejectedByTheArtifactCheck_IsAlsoRemoved()
        {
            string dir = NewTempDir();

            try
            {
                string file = Path.Combine(dir, WPowerPlans.PowFileNameFor(BalancedGuid));
                File.WriteAllText(file, "");

                StepResult rejected = Utils.ValidateExportArtifact(file, "Balanced", "powercfg", "exported it");

                // Guards the premise: if this ever stops being Failed, the test below is vacuous.
                Assert.Equal(ResultState.Failed, rejected.State);

                StepResult step = WPowerPlans.AbandonIfFailed(file, rejected);

                Assert.False(File.Exists(file));
                Assert.Equal(ResultState.Failed, step.State);
                Assert.Equal(rejected.Reason, step.Reason);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // The other direction, which matters just as much: a successful export must survive. A
        // cleanup rule that deleted unconditionally would pass every failure test above and destroy
        // every backup this module takes.
        [Fact]
        public void AnExportThatPassedTheArtifactCheck_IsKept()
        {
            string dir = NewTempDir();

            try
            {
                string file = Path.Combine(dir, WPowerPlans.PowFileNameFor(BalancedGuid));
                File.WriteAllText(file, "a real .pow would have bytes in it");

                StepResult accepted = Utils.ValidateExportArtifact(file, "Balanced", "powercfg", "exported it");

                Assert.Equal(ResultState.Succeeded, accepted.State);

                StepResult step = WPowerPlans.AbandonIfFailed(file, accepted);

                Assert.True(File.Exists(file));
                Assert.Same(accepted, step);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        /// <summary>
        /// The import advice names the GUID, because without it the advice does not work.
        /// </summary>
        /// <remarks>
        /// Caught by the PR review. Measured from powercfg's own help, 2026-07-21:
        ///
        ///     POWERCFG /IMPORT &lt;FILENAME&gt; [&lt;GUID&gt;]
        ///     &lt;GUID&gt;  Specifies the GUID for the imported scheme. If no GUID is specified,
        ///             a new GUID will be created.
        ///
        /// The message used to say `powercfg /import "&lt;file&gt;"`. A user following it would get the
        /// plan back under a NEW GUID that the manifest does not name, so the very next restore
        /// would fail in the same branch and hand them the same non-working instruction. Advice that
        /// silently fails to fix the thing it is offered for is this project's failure mode arriving
        /// through text rather than through code, which is why it is pinned.
        /// </remarks>
        [Fact]
        public void TheImportByHandAdvice_KeepsTheGuid()
        {
            const string absent = "0f0f0f0f-0f0f-0f0f-0f0f-0f0f0f0f0f0f";

            string advice = WPowerPlans.ImportByHandAdvice(absent);

            Assert.Contains("powercfg /import", advice);

            // The GUID must follow the filename placeholder, not merely appear somewhere in the
            // sentence - the whole defect was a command that omitted it in that position.
            Assert.Contains("\"<file>\" " + absent, advice);

            // Naming the file is the other half: the advice is useless if the user cannot tell
            // which of several .pow exports in the folder is the one being talked about.
            Assert.Contains(WPowerPlans.PowFileNameFor(absent), advice);

            Assert.True(
                advice.Contains("new one") || advice.Contains("new GUID"),
                "The advice no longer explains WHY the GUID matters. Without it Windows imports the " +
                "plan under a fresh GUID and this item fails again next time. Actual: " + advice);
        }

        // --- the manifest name -----------------------------------------------------------

        [Fact]
        public void ManifestFileName_IsTheOneSpelling()
        {
            Assert.Equal("Power plans.json", WPowerPlans.ManifestFileName);

            Assert.Equal(
                Path.Combine("C:\\backup", WPowerPlans.ManifestFileName),
                WPowerPlans.ManifestPathIn("C:\\backup"));
        }

        // The reader looks for the const's name and nothing else. Asserted from BOTH sides: a file
        // with that exact name is picked up (it reaches the parse and fails there), and a .json with
        // a different name is not (the restore skips as though the folder were empty). Testing only
        // the first would pass for a reader that globbed *.json.
        [Fact]
        public async Task Restore_ReadsExactlyTheManifestFileName()
        {
            string dir = NewTempDir();

            try
            {
                File.WriteAllText(Path.Combine(dir, WPowerPlans.ManifestFileName), "{ not json");
                Assert.Equal(ResultState.Failed, (await new WPowerPlans().RestoreAsync(dir)).State);

                File.Delete(Path.Combine(dir, WPowerPlans.ManifestFileName));
                File.WriteAllText(Path.Combine(dir, "Power plans backup.json"), "{ not json");

                ModuleResult r = await new WPowerPlans().RestoreAsync(dir);
                Assert.Equal(ResultState.Skipped, r.State);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // --- restore, short of running powercfg ------------------------------------------

        // Nothing in the backup folder is a SKIP, and the reason must describe the backup rather
        // than the live machine: what is missing is the manifest, and this PC was never examined.
        [Fact]
        public async Task Restore_WithNothingBackedUp_IsSkipped()
        {
            string dir = NewTempDir();

            try
            {
                ModuleResult r = await new WPowerPlans().RestoreAsync(dir);

                Assert.Equal(ResultState.Skipped, r.State);
                Assert.Contains("nothing was backed up", r.Reason);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public async Task Restore_WithAnUnparsableManifest_IsFailed()
        {
            string dir = NewTempDir();

            try
            {
                File.WriteAllText(WPowerPlans.ManifestPathIn(dir), "this is not JSON at all");

                ModuleResult r = await new WPowerPlans().RestoreAsync(dir);

                Assert.Equal(ResultState.Failed, r.State);
                Assert.Contains("not valid JSON", r.Reason);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // Well-formed JSON that does not say which plan was active. Distinct from the corrupt case
        // and from the missing-file case: the backup exists and is readable, and the one field the
        // restore acts on is not in it - so failing is the only honest answer, and it must not be
        // rounded down to "nothing was backed up".
        [Fact]
        public async Task Restore_WithAManifestThatNamesNoActivePlan_IsFailed()
        {
            string dir = NewTempDir();

            try
            {
                File.WriteAllText(WPowerPlans.ManifestPathIn(dir), "{ \"schemes\": [] }");

                ModuleResult r = await new WPowerPlans().RestoreAsync(dir);

                Assert.Equal(ResultState.Failed, r.State);
                Assert.Contains("does not record which plan was active", r.Reason);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // --- declarations ----------------------------------------------------------------

        [Fact]
        public void Declares_OneCommandTargetInPlainLanguage()
        {
            IReadOnlyList<RestoreTarget> targets = new WPowerPlans().RestoreTargets;

            RestoreTarget only = Assert.Single(targets);

            Assert.Equal(RestoreTargetKind.Command, only.Kind);
            Assert.False(only.IsUndeclared);

            // A description, not a bare command line: this text is the only thing the user is given
            // to judge a command whose scope they cannot otherwise see.
            Assert.True(only.Path.Length > 30, only.Path);
        }

        // RestoreMakesChanges stays true. Setting it false exempts the module from the pre-restore
        // snapshot, and this restore does change the machine's active power plan.
        [Fact]
        public void Declares_ThatItsRestoreMakesChanges()
            => Assert.True(new WPowerPlans().RestoreMakesChanges);

        // No process owns the active-plan setting, so nothing is closed. This also keeps the module
        // inside RestoreDeclarationTests' rule that a module which closes nothing must assume the
        // backup has something for it (HasBackupIn is left at its default).
        [Fact]
        public void Declares_NoProcessesToClose()
        {
            Assert.Empty(new WPowerPlans().ProcessesToCloseBeforeRestore);
            Assert.True(new WPowerPlans().HasBackupIn("C:\\nowhere"));
        }

        // The warning is the only place the user is told that a plan missing from this PC will not
        // be recreated, and that the .pow files are theirs to import by hand.
        [Fact]
        public void Declares_AWarningAboutWhatRestoreDoesNotDo()
        {
            string warning = new WPowerPlans().WarningMessage;

            Assert.False(string.IsNullOrWhiteSpace(warning));
            Assert.Contains("powercfg /import", warning);
        }

        [Fact]
        public void HasATitleAndInfo()
        {
            WPowerPlans m = new WPowerPlans();

            Assert.Equal(WPowerPlans.ModuleTitle, m.Title);
            Assert.False(string.IsNullOrWhiteSpace(m.Info));
        }
    }
}
