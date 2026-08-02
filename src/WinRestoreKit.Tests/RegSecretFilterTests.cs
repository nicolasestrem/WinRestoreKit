using WinRestoreKit;
using Conf;
using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace WinRestoreKit.Tests
{
    // RegSecretFilter and the EEnvironmentFiltered steps built on it.
    //
    // The module's Backup is NOT exercised end to end here: it shells out to regedit, and this
    // suite deliberately covers only what runs without elevation and without touching the live
    // machine. What that leaves uncovered is the wiring - export, then filter, then aggregate -
    // and the two pieces it wires together are each tested directly below.
    public class RegSecretFilterTests
    {
        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "acregfilter_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        // Written the way regedit writes: UTF-16LE with a BOM and CRLF line endings. Getting this
        // wrong would make every test here pass against a filter that cannot read a real export.
        private static string WriteReg(string dir, string body)
        {
            string path = Path.Combine(dir, "Environment.reg");
            File.WriteAllText(path, body.Replace("\n", "\r\n"), Encoding.Unicode);
            return path;
        }

        private static string ReadReg(string path) => File.ReadAllText(path);

        // --- LooksLikeSecret ---

        [Theory]
        [InlineData("GITHUB_TOKEN")]
        [InlineData("AWS_SECRET_ACCESS_KEY")]
        [InlineData("NPM_TOKEN")]
        [InlineData("MY_PASSWORD")]
        [InlineData("DB_PASSWD")]
        [InlineData("OPENAI_API_KEY")]
        [InlineData("SSH_PASSPHRASE")]
        [InlineData("AZURE_CREDENTIALS")]
        [InlineData("github_token")]      // case-insensitive
        public void NamesThatLookLikeCredentials_AreMatched(string name)
        {
            Assert.True(RegSecretFilter.LooksLikeSecret(name));
        }

        // The false-positive direction. These are ordinary variables that a broader fragment list
        // would swallow - PWD is the one that motivated leaving "PWD" out despite "PASSWD" being in,
        // and the two KEY/AUTH names are why the list uses compound fragments.
        [Theory]
        [InlineData("PATH")]
        [InlineData("PWD")]
        [InlineData("TEMP")]
        [InlineData("JAVA_HOME")]
        [InlineData("MY_KEYBOARD_LAYOUT")]
        [InlineData("AUTHORING_ROOT")]
        [InlineData("")]
        [InlineData(null)]
        public void OrdinaryNames_AreNotMatched(string name)
        {
            Assert.False(RegSecretFilter.LooksLikeSecret(name));
        }

        // --- FilterInPlace ---

        [Fact]
        public void AMatchingValue_IsRemovedAndReportedByName()
        {
            string dir = NewTempDir();

            try
            {
                string path = WriteReg(dir,
                    "Windows Registry Editor Version 5.00\n" +
                    "\n" +
                    "[HKEY_CURRENT_USER\\Environment]\n" +
                    "\"JAVA_HOME\"=\"C:\\\\jdk\"\n" +
                    "\"GITHUB_TOKEN\"=\"ghp_xxxxxxxxxxxx\"\n");

                RegFilterOutcome outcome = RegSecretFilter.FilterInPlace(path);

                Assert.True(outcome.Ok);
                Assert.Equal(new[] { "GITHUB_TOKEN" }, outcome.Removed.ToArray());

                string text = ReadReg(path);

                Assert.DoesNotContain("GITHUB_TOKEN", text);
                Assert.DoesNotContain("ghp_xxxxxxxxxxxx", text);   // the value, not just the name
                Assert.Contains("JAVA_HOME", text);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // THE ONE THAT MATTERS. regedit wraps hex(2) payloads - how every REG_EXPAND_SZ variable is
        // written, PATH included - across many lines with a trailing backslash. A filter that
        // decided line by line would drop the line naming the secret and leave its continuation
        // bytes behind as orphans: the credential still on disk in hex, and a .reg file that no
        // longer parses. This asserts the whole logical value goes.
        [Fact]
        public void AMatchingMultiLineHexValue_IsRemovedEntirely()
        {
            string dir = NewTempDir();

            try
            {
                string path = WriteReg(dir,
                    "Windows Registry Editor Version 5.00\n" +
                    "\n" +
                    "[HKEY_CURRENT_USER\\Environment]\n" +
                    // The trailing \\ is a LITERAL backslash at end of line - regedit's continuation
                    // marker - and it is the whole point of the fixture. An earlier draft ended
                    // this line "...,5c," (the hex bytes that encode a backslash) with no actual
                    // backslash character, which is a complete value followed by an unrelated line,
                    // and the filter treated it as exactly that. The test failed and the code was
                    // right. Noted because the two look identical at a glance in a hex payload.
                    "\"API_KEY\"=hex(2):73,00,65,00,63,00,72,00,65,00,74,00,5c,\\\n" +
                    "  00,64,00,65,00,61,00,64,00,62,00,65,00,65,00,66,00,00,00\n" +
                    "\"PATH\"=hex(2):25,00,55,00,53,00,45,00,52,00,00,00\n");

                RegFilterOutcome outcome = RegSecretFilter.FilterInPlace(path);

                Assert.True(outcome.Ok);
                Assert.Equal(new[] { "API_KEY" }, outcome.Removed.ToArray());

                string text = ReadReg(path);

                Assert.DoesNotContain("API_KEY", text);

                // The orphan check: the continuation line's distinctive bytes must be gone too.
                Assert.DoesNotContain("64,00,65,00,61,00,64", text);

                // And the value that follows it survived - proof the block walk resynchronised
                // rather than eating the rest of the file.
                Assert.Contains("\"PATH\"", text);
                Assert.Contains("25,00,55,00,53,00,45,00,52,00,00,00", text);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // A filtered export still has to be importable, or the module has produced an artifact that
        // fails at restore time - long after the user could have done anything about it. Checked
        // through the app's own validator so encoding and header are held to the same rule the
        // restore path applies.
        [Fact]
        public void TheFilteredFile_IsStillAValidRegFile()
        {
            string dir = NewTempDir();

            try
            {
                string path = WriteReg(dir,
                    "Windows Registry Editor Version 5.00\n" +
                    "\n" +
                    "[HKEY_CURRENT_USER\\Environment]\n" +
                    "\"SECRET_THING\"=\"x\"\n" +
                    "\"PATH\"=\"C:\\\\bin\"\n");

                Assert.True(RegSecretFilter.FilterInPlace(path).Ok);

                Assert.Equal(RegFileCheck.Valid, RegFile.Validate(path));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // A string value ending in a literal backslash - "C:\\Users\\me\\" - is ordinary, and its
        // line's last character is the closing quote, not the backslash. If the continuation test
        // were sloppy it would swallow the following line into this value's block, and the next
        // variable would vanish from the backup with nothing reported.
        [Fact]
        public void AValueEndingInABackslash_DoesNotSwallowTheNextLine()
        {
            string dir = NewTempDir();

            try
            {
                string path = WriteReg(dir,
                    "Windows Registry Editor Version 5.00\n" +
                    "\n" +
                    "[HKEY_CURRENT_USER\\Environment]\n" +
                    "\"HOME\"=\"C:\\\\Users\\\\me\\\\\"\n" +
                    "\"TOOLS\"=\"C:\\\\tools\"\n");

                RegFilterOutcome outcome = RegSecretFilter.FilterInPlace(path);

                Assert.True(outcome.Ok);
                Assert.Empty(outcome.Removed);

                string text = ReadReg(path);

                Assert.Contains("\"HOME\"", text);
                Assert.Contains("\"TOOLS\"", text);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void NothingMatching_LeavesEveryValueAndReportsNoRemovals()
        {
            string dir = NewTempDir();

            try
            {
                string path = WriteReg(dir,
                    "Windows Registry Editor Version 5.00\n" +
                    "\n" +
                    "[HKEY_CURRENT_USER\\Environment]\n" +
                    "\"PATH\"=\"C:\\\\bin\"\n" +
                    "\"TEMP\"=\"C:\\\\Temp\"\n");

                RegFilterOutcome outcome = RegSecretFilter.FilterInPlace(path);

                Assert.True(outcome.Ok);
                Assert.Empty(outcome.Removed);

                string text = ReadReg(path);

                Assert.Contains("\"PATH\"", text);
                Assert.Contains("\"TEMP\"", text);
                Assert.Contains("[HKEY_CURRENT_USER\\Environment]", text);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // A file whose structure this cannot account for is REFUSED, not half-filtered.
        //
        // The fixture is a stranded continuation - a payload line with no value declaration above
        // it - which is what a misread value boundary leaves behind. Found for real: a probe run
        // against an export with a malformed continuation produced a file where the credential's
        // NAME had been stripped and its payload bytes were still sitting there, and FilterInPlace
        // returned Ok. That artifact looks filtered, passes a header check, and is not filtered.
        //
        // Refusing is safe in the direction that matters: the caller deletes the export and reports
        // Failed, and the user still has the unfiltered module one tick away. Emitting a file that
        // cannot be vouched for is the only outcome with no recovery.
        [Fact]
        public void AFileWithAStrandedContinuation_IsRefusedRatherThanHalfFiltered()
        {
            string dir = NewTempDir();

            try
            {
                string path = WriteReg(dir,
                    "Windows Registry Editor Version 5.00\n" +
                    "\n" +
                    "[HKEY_CURRENT_USER\\Environment]\n" +
                    "\"PATH\"=\"C:\\\\bin\"\n" +
                    "  00,62,00,65,00,65,00,66,00,00,00\n");

                RegFilterOutcome outcome = RegSecretFilter.FilterInPlace(path);

                Assert.False(outcome.Ok);
                Assert.Contains("could not account for", outcome.Error);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // A REG_SZ containing a raw newline must NOT refuse the export.
        //
        // Measured on Windows 11, 2026-07-21: setting a REG_SZ to "line1\r\nline2" and exporting it
        // produces a value split across two physical lines with NO escape and NO continuation
        // backslash -
        //
        //   "MULTILINE"="line1
        //   line2"
        //
        // - which is a third continuation shape beside the hex backslash wrap. Believing regedit
        // escaped it as \r\n cost a FALSE REFUSAL, not a leak: the walk read the first line as a
        // whole value, met `line2"` at the top level, could not account for it, and refused the
        // whole export. For any user with a newline in an environment variable that is a red row
        // and no filtered backup, every time, with a reason they cannot act on.
        [Fact]
        public void AValueContainingARawNewline_IsFilteredRatherThanRefused()
        {
            string dir = NewTempDir();

            try
            {
                string path = WriteReg(dir,
                    "Windows Registry Editor Version 5.00\n" +
                    "\n" +
                    "[HKEY_CURRENT_USER\\Environment]\n" +
                    "\"PLAIN\"=\"ordinary\"\n" +
                    "\"MULTILINE\"=\"line1\n" +
                    "line2\"\n" +
                    "\"TRAILING_BS\"=\"C:\\\\Users\\\\me\\\\\"\n");

                RegFilterOutcome outcome = RegSecretFilter.FilterInPlace(path);

                Assert.True(outcome.Ok, "a legitimate export was refused: " + outcome.Error);
                Assert.Empty(outcome.Removed);

                string text = ReadReg(path);

                Assert.Contains("\"PLAIN\"", text);
                Assert.Contains("\"MULTILINE\"", text);
                Assert.Contains("line2\"", text);        // both halves survive
                Assert.Contains("\"TRAILING_BS\"", text);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // The fourth continuation shape: a still-open quoted string whose visible fragment ends in
        // an ESCAPED literal backslash.
        //
        //   "MULTILINE"="abc\\
        //   def"
        //
        // Line one ends in `\\` - one escaped backslash, which is ordinary content, not a marker.
        // The walk originally ran the backslash loop FIRST and asked only "is the last character a
        // backslash", which cannot tell this from a hex wrap. It swallowed `def"` as payload without
        // inspecting it for a closing quote, then hunted for that quote one line too late, and
        // refused the whole export. Fail-closed, so no leak - but a total outage of the feature,
        // reached by ordinary content, since Windows paths end in backslashes constantly.
        //
        // Fixed by deciding the shape from the value's FORM before either walk runs, rather than by
        // adding a fifth special case. Verified to fail without that reorder.
        [Fact]
        public void AQuotedValueEndingInAnEscapedBackslashBeforeANewline_IsNotMistakenForAHexWrap()
        {
            string dir = NewTempDir();

            try
            {
                string path = WriteReg(dir,
                    "Windows Registry Editor Version 5.00\n" +
                    "\n" +
                    "[HKEY_CURRENT_USER\\Environment]\n" +
                    "\"MULTILINE\"=\"abc\\\\\n" +
                    "def\"\n" +
                    "\"NEXT\"=\"somevalue\"\n" +
                    "\"GITHUB_TOKEN\"=\"ghp_LEAKED\"\n");

                RegFilterOutcome outcome = RegSecretFilter.FilterInPlace(path);

                Assert.True(outcome.Ok, "a legitimate export was refused: " + outcome.Error);
                Assert.Equal(new[] { "GITHUB_TOKEN" }, outcome.Removed.ToArray());

                string text = ReadReg(path);

                Assert.DoesNotContain("ghp_LEAKED", text);
                Assert.Contains("\"MULTILINE\"", text);
                Assert.Contains("def\"", text);
                Assert.Contains("\"NEXT\"", text);   // the walk resynchronised
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // ...and when such a value IS the secret, both of its physical lines must go. Removing only
        // the naming line would leave the credential's own text sitting in the backup.
        [Fact]
        public void ASecretWhoseValueContainsARawNewline_IsRemovedWhole()
        {
            string dir = NewTempDir();

            try
            {
                string path = WriteReg(dir,
                    "Windows Registry Editor Version 5.00\n" +
                    "\n" +
                    "[HKEY_CURRENT_USER\\Environment]\n" +
                    "\"GITHUB_TOKEN\"=\"ghp_first\n" +
                    "ghp_second\"\n" +
                    "\"AFTER\"=\"x\"\n");

                RegFilterOutcome outcome = RegSecretFilter.FilterInPlace(path);

                Assert.True(outcome.Ok, outcome.Error);
                Assert.Equal(new[] { "GITHUB_TOKEN" }, outcome.Removed.ToArray());

                string text = ReadReg(path);

                Assert.DoesNotContain("ghp_first", text);
                Assert.DoesNotContain("ghp_second", text);   // the continuation half too
                Assert.Contains("\"AFTER\"", text);          // and the walk resynchronised
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // The leak that the quoted-continuation exemption let back in.
        //
        // The over-consumption check is applied only to BACKSLASH continuations, on the reasoning
        // that a quoted continuation's terminator - the closing quote - is unambiguous. The
        // terminator is unambiguous. What comes AFTER it is not, and that is where a declaration
        // gets in. Measured against the code before this check existed:
        //
        //   "PATH"="line1
        //   "GITHUB_TOKEN"="ghp_LEAKED"    -> Ok=True, removed=[], credential still in the file
        //
        // The quote closing PATH's string is the one at the start of the token line, so the walk
        // swallowed that whole physical line into PATH's block, found PATH innocent and wrote it
        // back whole. Identical outcome to the backslash over-consumption leak, reached through the
        // shape that was exempted from that check.
        //
        // Both variants verified to return Ok=True and retain the credential without the fix.
        [Theory]
        [InlineData("\"GITHUB_TOKEN\"=\"ghp_LEAKED\"")]        // terminator at index 0
        [InlineData("xx\"GITHUB_TOKEN\"=\"ghp_LEAKED\"")]      // terminator mid-line
        [InlineData("junk\" \"GITHUB_TOKEN\"=\"ghp_LEAKED\"")] // whitespace between terminator and declaration
        public void AQuotedContinuationHidingADeclaration_IsRefused(string continuationLine)
        {
            string dir = NewTempDir();

            try
            {
                string path = WriteReg(dir,
                    "Windows Registry Editor Version 5.00\n" +
                    "\n" +
                    "[HKEY_CURRENT_USER\\Environment]\n" +
                    "\"PATH\"=\"line1\n" +
                    continuationLine + "\n" +
                    "\"AFTER\"=\"z\"\n");

                RegFilterOutcome outcome = RegSecretFilter.FilterInPlace(path);

                Assert.False(outcome.Ok);

                // As everywhere else here: refusing leaves the file alone, and discarding it is the
                // caller's job. Assert the composition rather than trusting the pair.
                new EEnvironmentFiltered().AbandonUnfiltered(path, outcome.Error);

                Assert.False(File.Exists(path));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // A quoted string that never closes is a truncated export. Refusing is right: the value's
        // real extent is unknown, so which lines belong to it is unknown, and so is what would be
        // removed along with it.
        [Fact]
        public void AQuotedStringThatNeverCloses_IsRefused()
        {
            string dir = NewTempDir();

            try
            {
                string path = WriteReg(dir,
                    "Windows Registry Editor Version 5.00\n" +
                    "\n" +
                    "[HKEY_CURRENT_USER\\Environment]\n" +
                    "\"CUT_OFF\"=\"the export stopped here\n");

                RegFilterOutcome outcome = RegSecretFilter.FilterInPlace(path);

                Assert.False(outcome.Ok);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // The same case against output this suite did NOT hand-write.
        //
        // Every other fixture here is a string literal, which means they all encode my beliefs about
        // the format - and one of those beliefs was wrong in a way that cost a permanent false
        // refusal. This one builds a real key, exports it with reg.exe (no elevation needed, unlike
        // regedit), and filters that. If the format assumptions drift again, this fails and the
        // literal-based tests do not.
        [Fact]
        public void ARealExportContainingARawNewline_IsFiltered()
        {
            const string KeyPath = @"Software\AppcopierFilterProbe";

            string dir = NewTempDir();
            string reg = Path.Combine(dir, "probe.reg");

            try
            {
                using (Microsoft.Win32.RegistryKey key =
                       Microsoft.Win32.Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    if (key == null)
                        return;   // cannot arrange the precondition; assert nothing

                    key.SetValue("PLAIN", "ordinary");
                    key.SetValue("MULTILINE", "line1\r\nline2");

                    // The DEFAULT value, which exports as `@="..."` rather than `"NAME"="..."`.
                    // With a newline in it this is the raw-newline defect one value-form over: the
                    // quoted-continuation walk keyed only on the "NAME"= form, so this never
                    // entered it, its second line reached the top level, and the whole export was
                    // refused. Set through the empty-string name, which is how the default value is
                    // addressed.
                    key.SetValue("", "default\r\nsecondline");

                    // A benign value whose CONTENT looks like a secret declaration, immediately
                    // before a real one. This is the case that justifies exempting quoted
                    // continuations from the over-consumption check: regedit escapes the embedded
                    // quotes as \" so the scanner correctly declines to close on them, the decoy
                    // survives as user data (it is not a registry value), and the real secret on the
                    // following line is still removed. Applying the check here would refuse a
                    // legitimate export.
                    key.SetValue("NOTE", "line1\r\n\"GITHUB_TOKEN\"=\"decoy\"\r\nline3");

                    // A value ending in a newline - the closing quote lands alone on its own line.
                    key.SetValue("ENDS_NL", "trailing\r\n");

                    // A path-like value whose fragment ends in a backslash immediately before the
                    // raw newline. Exports as `"BS_NL"="abc\\` - an escaped literal backslash that
                    // a last-character test cannot tell from a hex continuation marker.
                    key.SetValue("BS_NL", "abc\\\r\ndef");

                    key.SetValue("GITHUB_TOKEN", "ghp_secret_value");

                    // Written last so it lands after the secret: proves the walk resynchronised
                    // rather than eating the remainder of the file.
                    key.SetValue("AFTER_MARKER", "z");
                }

                System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "reg.exe",
                    Arguments = "export \"HKCU\\" + KeyPath + "\" \"" + reg + "\" /y",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(info))
                {
                    p.WaitForExit(20000);
                }

                if (!File.Exists(reg))
                    return;   // export did not happen; assert nothing rather than a false verdict

                // The preconditions this test is actually about. If reg.exe ever starts escaping
                // the newline, these fixtures stop covering the case and must not silently pass.
                string raw = File.ReadAllText(reg);

                Assert.Contains("line2\"", raw);
                Assert.Contains("@=\"default", raw);
                Assert.Contains("secondline\"", raw);
                Assert.Contains("\"abc\\\\", raw);   // escaped backslash immediately before a newline

                RegFilterOutcome outcome = RegSecretFilter.FilterInPlace(reg);

                Assert.True(outcome.Ok, "a real export was refused: " + outcome.Error);
                Assert.Equal(new[] { "GITHUB_TOKEN" }, outcome.Removed.ToArray());

                string text = ReadReg(reg);

                Assert.DoesNotContain("ghp_secret_value", text);
                Assert.Contains("\"MULTILINE\"", text);

                // The decoy is user data and must survive; the real secret next to it must not.
                // If the walk ever closed on the decoy's escaped quotes it would resynchronise in
                // the wrong place, and this is what would catch it.
                Assert.Contains("decoy", text);
                Assert.Contains("\"ENDS_NL\"", text);
                Assert.Contains("\"AFTER_MARKER\"", text);

                // The default value survives whole, both physical lines of it.
                Assert.Contains("@=\"default", text);
                Assert.Contains("secondline\"", text);

                Assert.Contains("\"BS_NL\"", text);
                Assert.Contains("def\"", text);

                Assert.Equal(RegFileCheck.Valid, RegFile.Validate(reg));
            }
            finally
            {
                try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(KeyPath, false); }
                catch (Exception) { }

                Directory.Delete(dir, recursive: true);
            }
        }

        // The mirror case, and the more dangerous one: the walk over-consumes and SWALLOWS the next
        // declaration into a benign block.
        //
        // The stranded-continuation test above covers a boundary read EARLY, where the orphan lands
        // at the top level and gets inspected. This covers a boundary read LATE. Here the final
        // continuation line still carries a trailing backslash, so PATH's block eats the
        // GITHUB_TOKEN line; block[0] is PATH, PATH is not a secret, and the block is written back
        // whole with the credential inside it. Before the fix this returned Ok with an EMPTY removed
        // list and the token still in the file - a filtered-looking artifact reporting success while
        // holding exactly what it promised to exclude.
        //
        // Verified to fail without the continuation check. Not reachable from well-formed regedit
        // output (it escapes backslashes in quoted strings as \\), but a .reg truncated mid-hex-
        // block has precisely this shape, and truncated dumps are a hazard this codebase already
        // takes seriously elsewhere.
        [Fact]
        public void AContinuationThatSwallowsTheNextDeclaration_IsRefusedRatherThanKeptWhole()
        {
            string dir = NewTempDir();

            try
            {
                string path = WriteReg(dir,
                    "Windows Registry Editor Version 5.00\n" +
                    "\n" +
                    "[HKEY_CURRENT_USER\\Environment]\n" +
                    "\"PATH\"=hex(2):43,00,\\\n" +
                    "  3a,00,\\\n" +                       // dangling continuation marker
                    "\"GITHUB_TOKEN\"=\"ghp_LEAKED\"\n" +
                    "\"AFTER\"=\"x\"\n");

                RegFilterOutcome outcome = RegSecretFilter.FilterInPlace(path);

                Assert.False(outcome.Ok);

                // FilterInPlace refuses and leaves the file ALONE - it does not rewrite and does not
                // delete. So the credential is still on disk at this point, and that is correct at
                // this layer: discarding the export belongs to the caller, which is the only party
                // that knows the file was written under a name promising it excludes secrets.
                Assert.Contains("ghp_LEAKED", File.ReadAllText(path));

                // The composition is the property that matters, so assert it here rather than
                // trusting two green tests to imply it. This is the pair EEnvironmentFiltered.Backup
                // runs, minus the regedit call this suite cannot make.
                new EEnvironmentFiltered().AbandonUnfiltered(path, outcome.Error);

                Assert.False(File.Exists(path),
                    "a refused export was left on disk still holding the credential");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // The refusal message quotes the offending line, and that line can be the payload half of a
        // value - which in this key can be a credential. It goes into the log and the results
        // summary, so an unbounded slice would be its own leak.
        [Fact]
        public void TheRefusalMessage_DoesNotQuoteAnUnboundedSliceOfRegistryData()
        {
            string dir = NewTempDir();

            try
            {
                string secret = new string('9', 400);

                string path = WriteReg(dir,
                    "Windows Registry Editor Version 5.00\n" +
                    "\n" +
                    "[HKEY_CURRENT_USER\\Environment]\n" +
                    "  " + secret + "\n");

                RegFilterOutcome outcome = RegSecretFilter.FilterInPlace(path);

                Assert.False(outcome.Ok);
                Assert.DoesNotContain(secret, outcome.Error);
                Assert.True(outcome.Error.Length < 200, "the refusal reason grew with the data it quotes");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void AnUnreadableFile_IsNotReportedAsFiltered()
        {
            string dir = NewTempDir();

            try
            {
                RegFilterOutcome outcome = RegSecretFilter.FilterInPlace(Path.Combine(dir, "absent.reg"));

                Assert.False(outcome.Ok);
                Assert.NotNull(outcome.Error);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // --- EEnvironmentFiltered's own steps ---

        // The safety-critical one. When the filter fails, what is on disk is a COMPLETE export -
        // credentials and all - under a filename promising it excludes them. It must not survive.
        [Fact]
        public void AFailedFilter_DeletesTheUnfilteredExport()
        {
            string dir = NewTempDir();

            try
            {
                string path = Path.Combine(dir, "Environment variables (excluding likely secrets).reg");
                File.WriteAllText(path, "everything including GITHUB_TOKEN", Encoding.Unicode);

                StepResult step = new EEnvironmentFiltered().AbandonUnfiltered(path, "the disk went away");

                Assert.Equal(ResultState.Failed, step.State);
                Assert.False(File.Exists(path), "an unfiltered export was left behind under a filtered name");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // When the file cannot be deleted either, the user has to be told it is still there and
        // told where. A reason that only said "could not filter" would leave them believing the
        // backup did not happen while their tokens sat on disk.
        [Fact]
        public void AFailedFilterThatCannotDelete_TellsTheUserWhereTheFileIs()
        {
            string dir = NewTempDir();

            try
            {
                string path = Path.Combine(dir, "Environment variables (excluding likely secrets).reg");
                File.WriteAllText(path, "everything", Encoding.Unicode);

                StepResult step;

                // Held open exclusively, so the delete inside AbandonUnfiltered throws.
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    step = new EEnvironmentFiltered().AbandonUnfiltered(path, "the disk went away");
                }

                Assert.Equal(ResultState.Failed, step.State);
                Assert.Contains(path, step.Reason);
                Assert.Contains("credentials", step.Reason);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // Every held-back name reaches the reason the user reads. At restore time the variables are
        // simply absent and nothing can distinguish that from a machine that never had them, so
        // backup time is the only moment this information is actionable.
        [Fact]
        public void TheStepReason_NamesEveryHeldBackVariable()
        {
            StepResult step = new EEnvironmentFiltered()
                .Describe(RegFilterOutcome.Filtered(new[] { "GITHUB_TOKEN", "AWS_SECRET_ACCESS_KEY" }));

            Assert.Equal(ResultState.Succeeded, step.State);
            Assert.Contains("GITHUB_TOKEN", step.Reason);
            Assert.Contains("AWS_SECRET_ACCESS_KEY", step.Reason);
        }

        [Fact]
        public void NoRemovals_SaysSoRatherThanImplyingAFilterRan()
        {
            StepResult step = new EEnvironmentFiltered().Describe(RegFilterOutcome.Filtered(new string[0]));

            Assert.Equal(ResultState.Succeeded, step.State);
            Assert.Contains("nothing was held back", step.Reason);
        }

        // The pair reads one key and writes two differently-named files. If the titles ever
        // collided, one export would overwrite the other while both steps reported success - the
        // failure mode BackupFileNamingTests exists to prevent, reached here through a different
        // door because these two modules are the only pair sharing a key.
        [Fact]
        public void TheTwoEnvironmentModules_ShareAKeyAndNotAFilename()
        {
            EEnvironment plain = new EEnvironment();
            EEnvironmentFiltered filtered = new EEnvironmentFiltered();

            Assert.Equal(plain.RestoreTargets.Single().Path, filtered.RestoreTargets.Single().Path);
            Assert.NotEqual(plain.Title, filtered.Title);
        }
    }
}
