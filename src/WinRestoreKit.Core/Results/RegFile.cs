using System;
using System.IO;

namespace WinRestoreKit
{
    internal enum RegFileCheck
    {
        Valid,
        Missing,
        Empty,
        BadHeader,

        /// <summary>Present, but we could not read it. Says nothing about its contents.</summary>
        Unreadable
    }

    /// <summary>
    /// Checks that a .reg file is what it claims to be.
    /// </summary>
    /// <remarks>
    /// This exists because regedit lies. Measured on Windows 11, 2026-07-20: "regedit /e" against a
    /// key that does not exist returns exit code 0 and writes no file at all. An exit code is
    /// therefore necessary but nowhere near sufficient, and the artifact itself has to be checked.
    /// </remarks>
    internal static class RegFile
    {
        internal const string Header = "Windows Registry Editor Version 5.00";

        internal static RegFileCheck Validate(string path)
        {
            string ignored;
            return Validate(path, out ignored);
        }

        internal static RegFileCheck Validate(string path, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return RegFileCheck.Missing;

            string text;

            try
            {
                // File.ReadAllText detects and strips the byte order mark. A real export is UTF-16LE
                // with a BOM (measured: FF FE 57 00 ...), so a byte-wise ASCII comparison against the
                // header would NOT match. Pinned to this call deliberately.
                text = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                // NOT BadHeader. We did not read the contents, so we know nothing about them -
                // saying "not a valid .reg file" here would send someone hunting for a corrupt
                // backup when what they have is a locked file or a permissions problem. Same rule
                // this design applies to registry keys: could-not-tell is its own answer.
                error = ex.Message;
                return RegFileCheck.Unreadable;
            }

            if (string.IsNullOrWhiteSpace(text))
                return RegFileCheck.Empty;

            return text.StartsWith(Header, StringComparison.OrdinalIgnoreCase)
                ? RegFileCheck.Valid
                : RegFileCheck.BadHeader;
        }

        /// <summary>
        /// Compares two valid registry exports after normalizing their text encoding and line endings.
        /// </summary>
        /// <remarks>
        /// Returns null when either artifact is absent, unreadable, or not a valid registry export.
        /// That is deliberately not drift evidence: a comparison is only meaningful when both sides
        /// are known representations of registry state.
        /// </remarks>
        internal static bool? HasSameCanonicalContent(string capturedPath, string currentPath)
        {
            string captured = ReadCanonicalContent(capturedPath);
            string current = ReadCanonicalContent(currentPath);

            if (captured == null || current == null)
                return null;

            return string.Equals(captured, current, StringComparison.Ordinal);
        }

        private static string ReadCanonicalContent(string path)
        {
            if (Validate(path) != RegFileCheck.Valid)
                return null;

            try
            {
                // File.ReadAllText handles the UTF-16LE BOM written by regedit. The content, rather
                // than its encoding or line-ending convention, is what captures registry state.
                return File.ReadAllText(path)
                    .Replace("\r\n", "\n")
                    .Replace("\r", "\n")
                    .TrimEnd('\n');
            }
            catch
            {
                return null;
            }
        }
    }
}
