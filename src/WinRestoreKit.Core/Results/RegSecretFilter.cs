using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WinRestoreKit
{
    /// <summary>
    /// What <see cref="RegSecretFilter.FilterInPlace"/> did to a .reg file.
    /// </summary>
    internal sealed class RegFilterOutcome
    {
        /// <summary>Whether the file on disk is now the filtered one.</summary>
        internal bool Ok { get; }

        /// <summary>Why it is not, when it is not. Null on success.</summary>
        internal string Error { get; }

        /// <summary>
        /// The value names held back, in the order they appeared. Never null; empty when the file
        /// contained nothing the filter recognised.
        /// </summary>
        internal IReadOnlyList<string> Removed { get; }

        private RegFilterOutcome(bool ok, string error, IReadOnlyList<string> removed)
        {
            Ok = ok;
            Error = error;
            Removed = removed ?? new string[0];
        }

        internal static RegFilterOutcome Filtered(IReadOnlyList<string> removed)
            => new RegFilterOutcome(true, null, removed);

        internal static RegFilterOutcome Failed(string error)
            => new RegFilterOutcome(false, error, null);
    }

    /// <summary>
    /// Removes values whose NAMES look like they hold credentials from an exported .reg file.
    /// </summary>
    /// <remarks>
    /// Exists because regedit cannot export selectively: <c>regedit /e</c> takes a key and writes
    /// every value under it, so the only place to make a choice is the file afterwards.
    ///
    /// WHAT THIS IS NOT. It is not a security boundary and must never be described as one to the
    /// user. It matches name fragments, so it holds back GITHUB_TOKEN and sails straight past
    /// MY_COMPANY_DEPLOY_KEY or a variable called simply <c>GH</c>. A caller that presents its
    /// output as "secrets removed" is making a promise this code cannot keep; the honest claim is
    /// the one <see cref="Conf.EEnvironmentFiltered"/> makes - variables whose names look like
    /// secrets were held back, and anything named otherwise was not.
    ///
    /// It is opt-in for that reason. <see cref="Conf.EEnvironment"/> still exports everything and
    /// still carries the plaintext warning; this is a SECOND module the user ticks instead, so the
    /// default behaviour is unchanged and nobody is silently given a partial backup they believe
    /// is complete. Both modules read the same key and write different files, so ticking both is
    /// meaningful and harmless.
    ///
    /// Every removal is reported BY NAME to the caller, and the caller puts those names in the step
    /// reason the user reads. A filter that dropped values quietly would be worse than no filter:
    /// the user would find out at restore time, which is the one moment they cannot do anything
    /// about it.
    /// </remarks>
    internal static class RegSecretFilter
    {
        /// <summary>
        /// Name fragments that mark a value as probably a credential. Matched case-insensitively
        /// anywhere in the name.
        /// </summary>
        /// <remarks>
        /// Chosen to be specific rather than broad, and the asymmetry is deliberate. A false
        /// positive is VISIBLE and recoverable - the name appears in the step reason and the
        /// unfiltered module is one tick away - so it costs the user a moment's confusion. But
        /// breadth has its own cost: "KEY" alone would swallow MY_KEYBOARD_LAYOUT and "AUTH" would
        /// swallow AUTHORING_ROOT, and a filter that visibly mangles ordinary settings trains people
        /// to stop using it, which loses the whole benefit. Compound fragments (API_KEY, ACCESS_KEY)
        /// buy most of the coverage without that.
        ///
        /// PWD is deliberately absent though PASSWD is present: in a shell environment PWD is the
        /// working directory, and matching it would hold back a variable that is never a secret.
        /// </remarks>
        internal static readonly string[] NameFragments =
        {
            "SECRET",
            "TOKEN",
            "PASSWORD",
            "PASSWD",
            "PASSPHRASE",
            "CREDENTIAL",
            "APIKEY",
            "API_KEY",
            "ACCESS_KEY",
            "PRIVATE_KEY",
            "SIGNING_KEY",
            "SESSION_KEY",
            "AUTHORIZATION"
        };

        internal static bool LooksLikeSecret(string valueName)
        {
            if (string.IsNullOrEmpty(valueName))
                return false;

            for (int i = 0; i < NameFragments.Length; i++)
            {
                if (valueName.IndexOf(NameFragments[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Rewrites <paramref name="path"/> without the values whose names look like secrets.
        /// </summary>
        /// <remarks>
        /// Does not throw. A caller that cannot tell "filtered" from "threw and was caught" is the
        /// failure mode Phase 2a exists to remove, and here it is worse than usual: the unfiltered
        /// export is already on disk under a filename promising it is filtered.
        /// </remarks>
        internal static RegFilterOutcome FilterInPlace(string path)
        {
            string text;

            try
            {
                // File.ReadAllText detects and strips the byte order mark, which a real export has
                // - regedit writes UTF-16LE. Same call RegFile.Validate is pinned to, for the same
                // reason: a byte-wise read would see the encoding, not the text.
                text = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                return RegFilterOutcome.Failed("could not read the export: " + ex.Message);
            }

            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            List<string> kept = new List<string>();
            List<string> removed = new List<string>();

            int i = 0;
            string unparseable = null;

            while (i < lines.Length)
            {
                // One VALUE can span many lines: regedit wraps hex(2) payloads - which is how PATH
                // and every other REG_EXPAND_SZ variable is written - at about 80 columns with a
                // trailing backslash. Filtering line by line would delete the first line of a
                // secret and leave its continuation bytes behind as orphaned garbage that then
                // fails the header-and-parse checks on the way back in. So a whole logical value is
                // assembled before any decision is taken about it.
                List<string> block = new List<string> { lines[i] };
                int backslashContinued = 0;

                // WHICH continuation applies is decided from the value's FORM, before either walk
                // runs, and the order matters.
                //
                // These two ran the other way round - backslash walk first, then the quoted check -
                // and the backslash walk asked only "is the last character a \", which cannot tell a
                // hex wrap from a still-open quoted string whose fragment ends in an ESCAPED literal
                // backslash. Measured on real reg.exe output, a REG_SZ whose value is "abc\" + a raw
                // newline + "def":
                //
                //   "MULTILINE"="abc\\
                //   def"
                //
                // Line one ends in \\ - one escaped backslash, not a marker - so the old order
                // swallowed `def"` as hex payload without ever looking at it for a closing quote,
                // then started hunting for that quote one line too late. Result was a refusal of the
                // whole export. Fail-closed, so no leak, but a total outage of the feature reached
                // by ordinary content: Windows paths end in backslashes constantly.
                //
                // Asking OpensUnterminatedString first removes the ambiguity rather than patching
                // it, because that function reads the value's actual form - it walks the escapes, so
                // it knows \\ is a literal backslash and not a terminator. A hex payload is not a
                // quoted string, so exactly one of the two branches can apply. Third time this
                // parser has been wrong about a continuation shape, and the first time the fix has
                // been to stop guessing rather than to add another case.
                bool stringLeftOpen = OpensUnterminatedString(lines[i]);

                if (!stringLeftOpen)
                {
                    while (Continues(lines[i]) && i + 1 < lines.Length)
                    {
                        i++;
                        block.Add(lines[i]);
                        backslashContinued++;
                    }
                }

                if (stringLeftOpen)
                {
                    while (i + 1 < lines.Length)
                    {
                        i++;
                        block.Add(lines[i]);

                        int close = IndexOfUnescapedQuote(lines[i], 0);

                        if (close < 0)
                            continue;

                        stringLeftOpen = false;

                        // Nothing may follow the closing quote. A well-formed export puts the
                        // terminator at the end of the line, so trailing content means the value's
                        // real extent is not what this walk just decided.
                        //
                        // This is NOT pedantry, it is the leak. Measured against this code before
                        // the check existed:
                        //
                        //   "PATH"="line1
                        //   "GITHUB_TOKEN"="ghp_LEAKED"     -> Ok=True removed=[] LEAKED=True
                        //   xx"GITHUB_TOKEN"="ghp_LEAKED"   -> Ok=True removed=[] LEAKED=True
                        //
                        // The quote that closes PATH's string is the one at the START of the token
                        // line, so the walk swallowed the whole physical line - declaration and
                        // credential included - into PATH's block, found PATH innocent, and wrote it
                        // back whole. Same silent leak as the backslash over-consumption case,
                        // through the continuation shape that was deliberately exempted from that
                        // check on the grounds that its terminator was unambiguous. The terminator
                        // is unambiguous; what follows it is not, and that is where it got in.
                        if (unparseable == null
                            && lines[i].Substring(close + 1).Trim().Length > 0)
                        {
                            unparseable = lines[i].Trim();
                        }

                        break;
                    }
                }

                i++;

                // A quoted string that never closes before the end of the file. Truncated export,
                // and refusing is right: the value's real extent is unknown, so which lines belong
                // to it - and therefore what would be removed with it - is unknown too.
                if (stringLeftOpen && unparseable == null)
                    unparseable = block[0].Trim();

                // Fail closed when the walk over-consumed: a CONTINUATION that parses as its own
                // "NAME"= declaration is a boundary error by definition.
                //
                // This is the mirror of the block[0] check below, and it is needed because that one
                // is structurally one-directional. A boundary read EARLY strands a continuation at
                // the top level, where block[0] catches it. A boundary read LATE swallows the next
                // declaration INTO this block, where block[0] is a perfectly ordinary name and
                // nothing below ever inspects the rest. Demonstrated against this code:
                //
                //   "PATH"=hex(2):43,00,\
                //     3a,00,\                      <- final continuation still carries a backslash
                //   "GITHUB_TOKEN"="ghp_LEAKED"    <- eaten as more of PATH's payload
                //
                // block[0] is PATH, PATH is not a secret, so the block is kept WHOLE - token line
                // included - and the outcome is Ok with an empty removed list. A file that looks
                // filtered, reports success, and still holds the credential: precisely what the
                // other check exists to prevent, reached from the opposite side.
                //
                // Not reachable from well-formed regedit output, which escapes backslashes inside
                // quoted strings as \\ so such a line ends on the closing quote. It is reachable
                // from a TRUNCATED or malformed export - a .reg cut off mid-hex-block is exactly
                // the shape above - and this codebase already treats truncated dumps as a live
                // hazard, deleting partial captures on the grounds that they pass a non-empty check
                // and restore as though whole. A filter that exists because the file might not be
                // shaped as assumed has to cover both ways that assumption can break.
                // Applied ONLY to backslash-continued lines, not to the whole block.
                //
                // A backslash continuation carries hex payload, so a line inside one that parses as
                // a declaration is always a boundary error. A quoted-string continuation carries
                // arbitrary USER DATA: an environment variable whose value contains a newline
                // followed by text shaped like `"NAME"="x"` is legitimate content, and refusing it
                // would reintroduce the false-refusal this pass exists to remove. Its terminator is
                // the closing quote, which is unambiguous, so it does not need this check.
                for (int c = 1; c <= backslashContinued && unparseable == null; c++)
                {
                    if (ValueNameOf(block[c].Trim()) != null)
                        unparseable = block[c].Trim();
                }

                string name = ValueNameOf(block[0]);

                if (name != null && LooksLikeSecret(name))
                {
                    removed.Add(name);
                    continue;
                }

                // Fail closed on a top-level line this cannot account for.
                //
                // Every line of a well-formed export is one of: the header, a blank, a [key] line,
                // a "name"=value declaration, the @= default value, or a continuation - and
                // continuations are consumed by the walk above, so reaching here with one means the
                // structure is not what this code believes it is.
                //
                // Why that is fatal rather than something to skip past: this filter's entire job is
                // to remove a value COMPLETELY, and a value is a naming line plus its continuations.
                // If a boundary is misread, the failure is not a crash - it is a file where the
                // credential's NAME was stripped and its payload bytes were left behind, which is
                // indistinguishable from a correctly filtered file at a glance and reports success.
                // Observed exactly once, on a deliberately malformed fixture, and that is the whole
                // reason this check exists: the artifact looked filtered and was not.
                //
                // So: refuse the file rather than emit one that cannot be vouched for. The caller
                // deletes the unfiltered export and reports Failed. Same rule ImportRegistryKey
                // applies at the other end of the pipe.
                if (unparseable == null && IsUnaccountable(block[0]))
                    unparseable = block[0].Trim();

                kept.AddRange(block);
            }

            if (unparseable != null)
            {
                return RegFilterOutcome.Failed(
                    "the export contains a line this could not account for, so it cannot be " +
                    "filtered safely: " + Excerpt(unparseable));
            }

            try
            {
                // Encoding.Unicode, matching what regedit wrote. Writing UTF-8 here would produce a
                // file that still LOOKS right in an editor and that regedit /s reads as mojibake -
                // exactly the sort of artifact that imports with exit code 0 and does nothing.
                File.WriteAllText(path, string.Join("\r\n", kept), Encoding.Unicode);
            }
            catch (Exception ex)
            {
                return RegFilterOutcome.Failed("could not rewrite the export: " + ex.Message);
            }

            return RegFilterOutcome.Filtered(removed);
        }

        /// <summary>
        /// Whether a line appearing at the TOP level is one this code does not recognise.
        /// </summary>
        /// <remarks>
        /// Called only for lines the walk did not consume as a continuation, so the recognised
        /// shapes are the header, a blank, a <c>[key]</c> line, a <c>"name"=</c> declaration and
        /// the <c>@=</c> default value. Anything else means the file's structure is not what this
        /// code assumes - most importantly a stranded continuation, which is what a misread value
        /// boundary leaves behind.
        ///
        /// Deliberately conservative in the safe direction: it only has to be right about which
        /// lines are ORDINARY, because being wrong there refuses a backup that would have been
        /// fine, and the user still has the unfiltered module. Being wrong the other way ships a
        /// half-filtered file.
        /// </remarks>
        private static bool IsUnaccountable(string line)
        {
            if (line == null)
                return false;

            string trimmed = line.Trim();

            if (trimmed.Length == 0)
                return false;

            if (trimmed.StartsWith(Header, StringComparison.OrdinalIgnoreCase))
                return false;

            if (trimmed[0] == '[')
                return false;

            if (trimmed.StartsWith("@=", StringComparison.Ordinal))
                return false;

            // A value declaration. ValueNameOf is the same parser used for the filtering decision,
            // so a line it can read is by definition one this code accounts for.
            return ValueNameOf(trimmed) == null;
        }

        /// <summary>
        /// A short, safe fragment of a line for use in a message the user reads.
        /// </summary>
        /// <remarks>
        /// Truncated because the offending line can be the payload half of a value, and a value in
        /// this key can be a credential. The reason string ends up in the log and in the results
        /// summary, so pasting an unbounded slice of registry data into it would be its own leak.
        /// </remarks>
        private static string Excerpt(string line)
        {
            const int Limit = 40;

            if (line == null)
                return "(empty)";

            return line.Length <= Limit ? line : line.Substring(0, Limit) + "...";
        }

        private const string Header = RegFile.Header;

        /// <summary>
        /// Whether this line is continued on the next one.
        /// </summary>
        /// <remarks>
        /// A trailing backslash is the continuation marker. A quoted string value ending in a
        /// literal backslash - <c>"HOME"="C:\\Users\\me\\"</c>, which is ordinary - is not caught by
        /// this, because the line's last character there is the closing quote, not the backslash.
        /// </remarks>
        private static bool Continues(string line)
        {
            string trimmed = line.TrimEnd();
            return trimmed.Length > 0 && trimmed[trimmed.Length - 1] == '\\';
        }

        /// <summary>
        /// The value name a line declares, or null if the line does not declare one.
        /// </summary>
        /// <remarks>
        /// Returns null for the header, the blank lines, the <c>[HKEY_...]</c> key line and the
        /// <c>@=</c> default value, so all of those are kept. Only a line of the exact shape
        /// <c>"NAME"=...</c> can be filtered, which is the conservative direction: a line this
        /// cannot parse is a line that stays in the backup.
        /// </remarks>
        private static string ValueNameOf(string line)
        {
            string name;
            int valueStart;

            return TryReadDeclaration(line, out name, out valueStart) ? name : null;
        }

        /// <summary>
        /// Reads the <c>"NAME"=</c> half of a line, yielding the name and where its value begins.
        /// </summary>
        /// <remarks>
        /// One parser for both facts on purpose. These were briefly two methods walking the same
        /// escape rules for different answers, which is the shape that drifts: fix an escape bug in
        /// one and the other silently disagrees about where a value starts.
        /// </remarks>
        private static bool TryReadDeclaration(string line, out string name, out int valueStart)
        {
            name = null;
            valueStart = -1;

            if (line == null || line.Length < 2 || line[0] != '"')
                return false;

            StringBuilder builder = new StringBuilder();
            int i = 1;

            while (i < line.Length)
            {
                char c = line[i];

                // Backslash escapes the next character, so an embedded quote does not end the name.
                // Windows will not let you create an environment variable whose name contains a
                // quote, but this parses the file in front of it rather than the file it expects.
                if (c == '\\' && i + 1 < line.Length)
                {
                    builder.Append(line[i + 1]);
                    i += 2;
                    continue;
                }

                if (c == '"')
                {
                    // The closing quote must be followed by '=' for this to be a value assignment.
                    if (i + 1 < line.Length && line[i + 1] == '=')
                    {
                        name = builder.ToString();
                        valueStart = i + 2;
                        return true;
                    }

                    return false;
                }

                builder.Append(c);
                i++;
            }

            return false;
        }

        /// <summary>
        /// Whether this line opens a quoted string value that does not close on the same line.
        /// </summary>
        /// <remarks>
        /// The THIRD continuation shape, and the one that was missed. Measured on Windows 11,
        /// 2026-07-21, by setting a REG_SZ to "line1\r\nline2" and exporting it:
        ///
        ///   "PLAIN"="ordinary"
        ///   "MULTILINE"="line1
        ///   line2"
        ///   "EXPANDED"=hex(2):25,00,...,49,00,\
        ///     4c,00,45,00,...
        ///   "TRAILING_BS"="C:\\Users\\me\\"
        ///
        /// The newline inside a quoted string is emitted RAW - no escape, no continuation
        /// backslash. The same export confirms the two shapes already handled: REG_EXPAND_SZ gets
        /// the backslash wrap, and a value ending in a literal backslash still ends on its closing
        /// quote.
        ///
        /// This was written the other way round first, on the assumption that a CRLF would be
        /// escaped as \r\n and keep the value on one line. It is not, and the cost of believing it
        /// was a FALSE REFUSAL, not a leak: the walk took the first physical line as a whole value,
        /// met `line2"` at the top level, could not account for it, and refused the entire export.
        /// Every backup, permanently, for any user with a newline in an environment variable, with
        /// a reason they could do nothing about. Fail-closed kept it safe and made it useless.
        /// </remarks>
        private static bool OpensUnterminatedString(string line)
        {
            int valueStart = ValueStartOf(line);

            if (valueStart < 0)
                return false;

            if (valueStart >= line.Length || line[valueStart] != '"')
                return false;   // hex:, hex(2):, dword: - not a quoted string at all

            return IndexOfUnescapedQuote(line, valueStart + 1) < 0;
        }

        /// <summary>
        /// Where a line's value begins, across BOTH value-start forms, or -1 if it has neither.
        /// </summary>
        /// <remarks>
        /// The <c>@=</c> default value is a value-start form too, and forgetting it cost the same
        /// false refusal as the raw-newline defect, one form over: a default value containing a
        /// newline never entered the quoted-continuation walk, so its second physical line reached
        /// the top level and the whole export was refused. Found on real <c>reg.exe</c> output.
        ///
        /// Deliberately NOT folded into <see cref="TryReadDeclaration"/>, which must keep returning
        /// false for <c>@=</c>: <see cref="IsUnaccountable"/> and the over-consumption check both
        /// treat "parses as a declaration" as their test, and teaching that parser about <c>@=</c>
        /// would make a default-value line look like a swallowed declaration. Two questions - where
        /// does the value start, and is this a named declaration - that happen to share a prefix.
        ///
        /// Low reachability for this module, kept because it is cheap and the failure is a total
        /// outage of the feature. <c>HKCU\Environment</c> does not normally carry a default value,
        /// but <c>reg export</c> walks subkeys, so any subkey with a multi-line default trips it.
        /// </remarks>
        private static int ValueStartOf(string line)
        {
            if (line == null)
                return -1;

            if (line.StartsWith("@=", StringComparison.Ordinal))
                return 2;

            string name;
            int valueStart;

            return TryReadDeclaration(line, out name, out valueStart) ? valueStart : -1;
        }

        /// <summary>
        /// The index of the next unescaped <c>"</c> at or after <paramref name="from"/>, or -1.
        /// </summary>
        private static int IndexOfUnescapedQuote(string line, int from)
        {
            int i = from;

            while (i < line.Length)
            {
                if (line[i] == '\\' && i + 1 < line.Length)
                {
                    i += 2;   // \\ and \" both mean "this is not a terminator"
                    continue;
                }

                if (line[i] == '"')
                    return i;

                i++;
            }

            return -1;
        }
    }
}
