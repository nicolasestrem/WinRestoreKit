using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WinRestoreKit
{
    internal static class FontLoader
    {
        private static readonly PrivateFontCollection Collection = new PrivateFontCollection();
        private static readonly object SyncRoot = new object();
        private static bool loaded;

        internal static void EnsureLoaded()
        {
            lock (SyncRoot)
            {
                if (loaded)
                {
                    return;
                }

                var assembly = typeof(FontLoader).Assembly;
                foreach (var resourceName in assembly.GetManifestResourceNames()
                    .Where(name => name.Contains(".Fonts.", StringComparison.Ordinal)
                        && (name.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)
                            || name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))))
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream == null)
                    {
                        continue;
                    }

                    using var buffer = new MemoryStream();
                    stream.CopyTo(buffer);
                    var fontBytes = buffer.ToArray();
                    var handle = GCHandle.Alloc(fontBytes, GCHandleType.Pinned);
                    try
                    {
                        Collection.AddMemoryFont(handle.AddrOfPinnedObject(), fontBytes.Length);
                    }
                    finally
                    {
                        handle.Free();
                    }
                }

                loaded = true;
            }
        }

        internal static Font Load(string familyName, float size, FontStyle style = FontStyle.Regular)
        {
            EnsureLoaded();

            var family = Collection.Families.FirstOrDefault(candidate =>
                             string.Equals(candidate.Name, familyName, StringComparison.OrdinalIgnoreCase))
                         ?? Collection.Families.FirstOrDefault(candidate =>
                             candidate.Name.Contains(familyName, StringComparison.OrdinalIgnoreCase))
                         ?? SystemFonts.MessageBoxFont?.FontFamily
                         ?? FontFamily.GenericSansSerif;

            return new Font(family, size, style, GraphicsUnit.Point);
        }
    }
}
