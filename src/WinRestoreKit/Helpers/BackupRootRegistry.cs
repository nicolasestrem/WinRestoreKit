using DataHelper;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;

namespace WinRestoreKit
{
    /// <summary>Persists custom roots that have successfully received a backup.</summary>
    internal static class BackupRootRegistry
    {
        private const string RegistryKeyPath = @"Software\WinRestoreKit";
        private const string RegistryValueName = "BackupRoots";

        /// <summary>Known custom roots, in the order they were recorded.</summary>
        internal static IReadOnlyList<string> Read()
        {
            List<string> roots = new List<string>();

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath))
                {
                    if (key == null || key.GetValue(RegistryValueName) is not string[] storedRoots)
                        return roots;

                    foreach (string root in storedRoots)
                        AddCanonical(roots, root);
                }
            }
            catch (Exception)
            {
                // Persistence is best-effort. Existing default-root discovery remains available.
            }

            return roots;
        }

        /// <summary>Records a custom root after its backup manifest has been written.</summary>
        internal static void Remember(string root)
        {
            if (!TryCanonicalize(root, out string canonicalRoot)
                || (TryCanonicalize(Data.DataRootDir, out string defaultRoot)
                    && string.Equals(canonicalRoot, defaultRoot, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            List<string> roots = new List<string>(Read());

            foreach (string existing in roots)
            {
                if (string.Equals(existing, canonicalRoot, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            roots.Add(canonicalRoot);

            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath))
                {
                    key?.SetValue(RegistryValueName, roots.ToArray(), RegistryValueKind.MultiString);
                }
            }
            catch (Exception)
            {
                // A backup that was written successfully remains successful if its root cannot be remembered.
            }
        }

        internal static bool TryCanonicalize(string root, out string canonicalRoot)
        {
            canonicalRoot = null;

            if (string.IsNullOrWhiteSpace(root))
                return false;

            try
            {
                canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void AddCanonical(List<string> roots, string root)
        {
            if (!TryCanonicalize(root, out string canonicalRoot))
                return;

            foreach (string existing in roots)
            {
                if (string.Equals(existing, canonicalRoot, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            roots.Add(canonicalRoot);
        }
    }
}
