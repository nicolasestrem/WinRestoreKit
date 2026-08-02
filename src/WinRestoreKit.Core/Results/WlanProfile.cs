using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace WinRestoreKit
{
    /// <summary>
    /// Finds exported Wi-Fi profiles by what they contain rather than what they are called.
    /// </summary>
    /// <remarks>
    /// The old code globbed "WLAN*.xml". Measured on Windows 11, 2026-07-20: netsh names its exports
    /// "&lt;interface name&gt;-&lt;SSID&gt;.xml" - on the test machine "Wi-Fi 2-Home.xml" - so the
    /// filter matched 0 of 19 exported profiles and restore silently found nothing.
    ///
    /// A corrected wildcard would not fix it either: the prefix is the network interface's name,
    /// which differs per machine and is localised. Content is the only stable discriminator.
    /// </remarks>
    internal static class WlanProfile
    {
        private const string ProfileNamespace = "http://www.microsoft.com/networking/WLAN/profile/v1";
        private const string RootElement = "WLANProfile";

        internal static bool IsWlanProfile(string xmlPath)
        {
            if (string.IsNullOrWhiteSpace(xmlPath) || !File.Exists(xmlPath))
                return false;

            try
            {
                XDocument doc = XDocument.Load(xmlPath);

                if (doc.Root == null)
                    return false;

                // Match on the local name, and on the namespace when one is present. Hand-edited
                // profiles sometimes lose the xmlns; the root element name is the reliable part.
                if (!string.Equals(doc.Root.Name.LocalName, RootElement, StringComparison.OrdinalIgnoreCase))
                    return false;

                string ns = doc.Root.Name.NamespaceName;

                return string.IsNullOrEmpty(ns)
                    || string.Equals(ns, ProfileNamespace, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                // Not XML, unreadable, or truncated. Either way it is not a profile we can restore.
                return false;
            }
        }

        internal static string[] FindIn(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return new string[0];

            List<string> found = new List<string>();

            try
            {
                foreach (string path in Directory.GetFiles(folder, "*.xml"))
                {
                    if (IsWlanProfile(path))
                        found.Add(path);
                }
            }
            catch (Exception)
            {
                return found.ToArray();
            }

            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found.ToArray();
        }
    }
}
