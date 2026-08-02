using WinRestoreKit;
using Microsoft.Win32;
using System;

namespace DataHelper
{
    /// <summary>
    /// Reads the Windows build number out of the registry for display purposes.
    /// </summary>
    /// <remarks>
    /// Nothing in this class may throw. <see cref="GetVersion()"/> is called from
    /// ConfPageView.SetStyle, which runs from the ConfPageView constructor, which runs from the
    /// MainForm constructor - and that constructor is evaluated as the ARGUMENT to
    /// Application.Run, i.e. OUTSIDE the message pump. WinForms' ThreadExceptionDialog only
    /// catches exceptions that surface INSIDE the pump, so an exception here escapes to the CLR
    /// unhandled path and terminates the process via WER: no dialog, no log, no window. The user
    /// simply sees the app fail to start.
    ///
    /// This type deliberately declares NO EAGERLY-INITIALISED statics. It previously carried
    /// "public static readonly string thisOS = IsWin11() + ... + GetVersion();" that nothing read.
    /// A throw inside a static field initializer surfaces as TypeInitializationException and leaves
    /// the type permanently unusable for the life of the process, which turns a recoverable
    /// registry hiccup into a hard failure. OsVersionTests pins that absence.
    ///
    /// Utils.ProbeKey was considered for the reads below and REJECTED: it answers the question
    /// "does this key exist", not "what is this value", so it cannot supply CurrentBuild or UBR.
    /// Using it would mean probing the key and then opening it again anyway.
    /// </remarks>
    public static class OsHelper
    {
        // These are const, not static readonly. The distinction is the whole point of the guard in
        // OsVersionTests: a const is IsLiteral, is inlined at every call site, has no initializer
        // and contributes no .cctor, so it CANNOT throw at type load. A static readonly field does
        // emit a .cctor and is exactly the hazard (the thisOS mistake). The test permits the former
        // and rejects the latter; do not "fix" a const into a static readonly here.

        private const string CurrentVersionKeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

        /// <summary>
        /// Shown when the registry was readable but carried no build number.
        /// </summary>
        internal const string BuildUnknown = "(build unknown)";

        /// <summary>
        /// Shown when the registry could not be read at all (key open threw, e.g. ACL denial).
        /// </summary>
        /// <remarks>
        /// "Absent" and "unreadable" get DIFFERENT user-visible tokens on purpose, because at this
        /// point in startup the distinction is otherwise unobservable. LogHelper.Log only writes
        /// when its target is non-null (LogHelper.cs:26), and ConfPageView.cs sets that target on
        /// the line AFTER the one that calls GetVersion - so the diagnostic log line for the
        /// failure is guaranteed to be discarded. The string in the greeting is the only channel
        /// that actually reaches anyone, so it has to carry the distinction itself.
        /// </remarks>
        internal const string BuildUnreadable = "(build unreadable)";

        /// <summary>
        /// A display string for the running OS build, e.g. "Build 26100.4652".
        /// </summary>
        public static string GetVersion()
        {
            return GetVersion(OpenCurrentVersionKey);
        }

        /// <summary>
        /// The whole read-and-format path, against a caller-supplied key opener.
        /// </summary>
        /// <remarks>
        /// THE SEAM, and the reason it exists. The original defect was not in the formatting - it
        /// was <c>key.GetValue("UBR").ToString()</c>, a null dereference against a REGISTRY VALUE
        /// that is genuinely absent on imaged and sysprepped installs. That shape cannot be
        /// reproduced by handing strings to <see cref="ComposeVersion"/>, and it cannot be
        /// reproduced against HKLM on a healthy host either, so before this overload existed the
        /// reading path had no failing input anywhere in the suite.
        ///
        /// Same rationale as <c>IRegistryTool</c>: the seam does not eliminate the untestable
        /// surface, it CONFINES it. What stays uncovered is one expression -
        /// <see cref="OpenCurrentVersionKey"/>, a single OpenSubKey call. Everything above it -
        /// absent key, present key with no values, present key with a build but no UBR, an opener
        /// that throws - is driven by OsVersionTests against a real key it creates and deletes
        /// under HKCU, which needs no elevation and touches nothing the user owns.
        ///
        /// A Func rather than an interface deliberately: there is exactly one operation and one
        /// production implementation, so an interface plus a class would be more ceremony than the
        /// thing it wraps. Passed as a parameter rather than held in a static field, which would
        /// trip the no-eager-statics guard for good reason.
        /// </remarks>
        internal static string GetVersion(Func<RegistryKey> openKey)
        {
            if (!ReadBuildValues(openKey, out string build, out string ubr))
                return BuildUnreadable;

            return ComposeVersion(build, ubr);
        }

        /// <summary>
        /// Formats the two raw registry values into the displayed build string.
        /// </summary>
        /// <remarks>
        /// Pure and total. Every degraded shape has to be a self-describing token rather than a
        /// fragment: "Build " with nothing after it, or "Build 26100." with a trailing dot, reads
        /// as a rendering bug rather than as missing data. UBR in particular is absent (not zero)
        /// on some imaged and sysprepped installs, so the build-only shape is a normal outcome, and
        /// it must not be padded to "26100.0" - that would state a revision the machine never
        /// reported.
        /// </remarks>
        internal static string ComposeVersion(string build, string ubr)
        {
            if (string.IsNullOrWhiteSpace(build))
                return BuildUnknown;

            string version = build.Trim();

            if (!string.IsNullOrWhiteSpace(ubr))
                version += "." + ubr.Trim();

            return "Build " + version;
        }

        /// <summary>
        /// The one production key open. The only line in this file no test drives.
        /// </summary>
        private static RegistryKey OpenCurrentVersionKey()
        {
            return Registry.LocalMachine.OpenSubKey(CurrentVersionKeyPath);
        }

        /// <summary>
        /// Opens the key via <paramref name="openKey"/> and reads the build values out of it.
        /// </summary>
        /// <returns>
        /// False only when the key could not be READ (an exception). An absent key, or a key with
        /// no build value in it, returns true with <paramref name="build"/> null - that is a
        /// successful read of a machine that simply does not carry the value.
        /// </returns>
        /// <remarks>
        /// Opening a key has THREE outcomes, not two: it returns the key, or it returns null when
        /// the key does not exist, or it THROWS SecurityException when the key exists but its ACL
        /// denies access. This repo's own ProbeKeyTests verifies the throwing case against
        /// HKLM\SECURITY. The catch is therefore load-bearing, not defensive padding.
        ///
        /// The handle is disposed via using; the old code leaked both of them.
        /// </remarks>
        private static bool ReadBuildValues(Func<RegistryKey> openKey, out string build, out string ubr)
        {
            build = null;
            ubr = null;

            try
            {
                using (RegistryKey key = openKey())
                {
                    if (key == null)
                        return true;   // Key absent: read succeeded, there is nothing there.

                    // CurrentBuild is the modern name; CurrentBuildNumber is the legacy one and is
                    // still present on current Windows. Either is an acceptable source.
                    build = ValueOrNull(key, "CurrentBuild") ?? ValueOrNull(key, "CurrentBuildNumber");
                    ubr = ValueOrNull(key, "UBR");
                    return true;
                }
            }
            catch (Exception ex)
            {
                // LogMessage, never Log: this text is data and may contain braces.
                // Very likely discarded - see the remarks on BuildUnreadable - which is exactly why
                // the returned string has to be self-describing on its own.
                LogHelper.Instance.LogMessage("Could not read the Windows build number: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Reads a registry value as a trimmed string, or null when it is absent or empty.
        /// </summary>
        /// <remarks>
        /// This is where the original defect lived: the old code was
        /// <c>key.GetValue("UBR").ToString()</c>, which dereferences null the moment the value is
        /// not present. The null-conditional below is the fix, and OsVersionTests drives it against
        /// a real key that is missing the value.
        ///
        /// Deliberately private to this file rather than promoted to Utils: it has exactly one
        /// consumer, and Utils is the shared surface that every backup module depends on.
        /// </remarks>
        private static string ValueOrNull(RegistryKey key, string name)
        {
            object value = key.GetValue(name);
            string text = value?.ToString();

            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
    }
}
