using System;
using System.Globalization;
using System.IO;

namespace WinRestoreKit
{
    /// <summary>
    /// Validates an optional name for a user-created backup folder.
    /// </summary>
    /// <remarks>
    /// Custom names are stored and used verbatim. Validation deliberately rejects rather than changes
    /// input, because silently changing a name would make the destination shown to the user differ
    /// from the folder written to disk. Empty input means the caller keeps the legacy timestamp name.
    /// </remarks>
    internal static class BackupNaming
    {
        private const int MaxSegmentLength = 120;
        private const string TimestampFormat = "yyyy-MM-dd - HH.mm.ss";

        internal static string TimestampNameFor(DateTime now)
            => now.ToString(TimestampFormat, CultureInfo.InvariantCulture);

        internal static bool TryValidateCustomName(string value, out string name)
        {
            name = null;

            if (string.IsNullOrEmpty(value))
                return true;

            if (string.IsNullOrWhiteSpace(value)
                || value.Length > MaxSegmentLength
                || value == "."
                || value == ".."
                || value.EndsWith(" ", StringComparison.Ordinal)
                || value.EndsWith(".", StringComparison.Ordinal)
                || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || value.IndexOf('/') >= 0
                || value.IndexOf('\\') >= 0
                || IsReservedDeviceName(value))
            {
                return false;
            }

            name = value;
            return true;
        }

        private static bool IsReservedDeviceName(string value)
        {
            int extension = value.IndexOf('.');
            string baseName = extension < 0 ? value : value.Substring(0, extension);

            if (baseName.Equals("CON", StringComparison.OrdinalIgnoreCase)
                || baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
                || baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
                || baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return baseName.Length == 4
                && (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && baseName[3] >= '1'
                && baseName[3] <= '9';
        }
    }
}
