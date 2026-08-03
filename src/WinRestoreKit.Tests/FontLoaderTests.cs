using System;
using System.Collections;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class FontLoaderTests
    {
        [Fact]
        public void Load_RetainsPinnedEmbeddedFontBuffersThroughGcPressure()
        {
            FontLoader.EnsureLoaded();

            FieldInfo field = typeof(FontLoader).GetField("PinnedFontBuffers", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(field);

            GCHandle[] handles = ((IEnumerable)field.GetValue(null)).Cast<GCHandle>().ToArray();
            Assert.NotEmpty(handles);
            Assert.All(handles, handle => Assert.True(handle.IsAllocated));

            using (Font beforeCollection = FontLoader.Load(Ui.FontBody, 14f))
            {
                Assert.Contains("Barlow", beforeCollection.FontFamily.Name, StringComparison.OrdinalIgnoreCase);
            }

            byte[][] pressure = Enumerable.Range(0, 64).Select(_ => new byte[1024 * 1024]).ToArray();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.KeepAlive(pressure);

            Assert.All(handles, handle => Assert.True(handle.IsAllocated));
            using (Font afterCollection = FontLoader.Load(Ui.FontBody, 14f))
            {
                Assert.Contains("Barlow", afterCollection.FontFamily.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
