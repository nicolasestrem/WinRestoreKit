using System;
using System.Reflection;

namespace WinRestoreKit
{
    internal static class VersionInfo
    {
        internal const string UnknownVersion = "unknown";

        internal static string GetCurrentVersion(Assembly assembly)
            => Normalize(assembly?.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
                         ?? assembly?.GetName().Version?.ToString());

        internal static string Normalize(string rawVersion)
        {
            if (string.IsNullOrWhiteSpace(rawVersion))
                return UnknownVersion;

            string raw = rawVersion.Trim();
            int suffix = raw.IndexOfAny(new[] { '+', '-' });
            string candidate = suffix >= 0 ? raw.Substring(0, suffix) : raw;
            return Version.TryParse(candidate, out Version parsed) && parsed.Build >= 0
                ? parsed.ToString(3)
                : raw;
        }
    }
}
