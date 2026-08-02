using WinRestoreKit;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class RegFileTests : IDisposable
    {
        private readonly string _dir;

        public RegFileTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "acreg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private string Write(string name, string content, Encoding encoding)
        {
            string p = Path.Combine(_dir, name);
            File.WriteAllText(p, content, encoding);
            return p;
        }

        // This is the shape regedit /e actually produces (measured 2026-07-20): UTF-16LE with BOM.
        [Fact]
        public void Validate_RealShapedExport_Utf16WithBom_IsValid()
        {
            string p = Write("ok.reg",
                "Windows Registry Editor Version 5.00\r\n\r\n[HKEY_CURRENT_USER\\Control Panel\\Mouse]\r\n",
                new UnicodeEncoding(false, true));

            Assert.Equal(RegFileCheck.Valid, RegFile.Validate(p));
        }

        [Fact]
        public void Validate_Utf8Export_IsAlsoValid()
        {
            string p = Write("utf8.reg",
                "Windows Registry Editor Version 5.00\r\n\r\n[HKEY_CURRENT_USER\\X]\r\n",
                new UTF8Encoding(false));

            Assert.Equal(RegFileCheck.Valid, RegFile.Validate(p));
        }

        [Fact]
        public void Validate_MissingFile_IsMissing()
            => Assert.Equal(RegFileCheck.Missing, RegFile.Validate(Path.Combine(_dir, "nope.reg")));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Validate_NoPath_IsMissing(string path)
            => Assert.Equal(RegFileCheck.Missing, RegFile.Validate(path));

        [Fact]
        public void Validate_EmptyFile_IsEmpty()
        {
            string p = Path.Combine(_dir, "empty.reg");
            File.WriteAllBytes(p, new byte[0]);
            Assert.Equal(RegFileCheck.Empty, RegFile.Validate(p));
        }

        // A BOM and nothing else: 2 bytes on disk, so a naive Length > 0 check passes.
        [Fact]
        public void Validate_BomOnly_IsEmpty()
        {
            string p = Path.Combine(_dir, "bomonly.reg");
            File.WriteAllBytes(p, new byte[] { 0xFF, 0xFE });
            Assert.Equal(RegFileCheck.Empty, RegFile.Validate(p));
        }

        [Fact]
        public void Validate_WrongHeader_IsBadHeader()
        {
            string p = Write("wrong.reg", "REGEDIT4\r\n\r\n[HKEY_CURRENT_USER\\X]\r\n",
                new UnicodeEncoding(false, true));

            Assert.Equal(RegFileCheck.BadHeader, RegFile.Validate(p));
        }

        [Fact]
        public void Validate_TruncatedHeader_IsBadHeader()
        {
            string p = Write("trunc.reg", "Windows Registry Ed",
                new UnicodeEncoding(false, true));

            Assert.Equal(RegFileCheck.BadHeader, RegFile.Validate(p));
        }

        [Fact]
        public void Validate_HeaderWithLeadingWhitespace_IsBadHeader()
        {
            string p = Write("lead.reg", "   Windows Registry Editor Version 5.00\r\n",
                new UnicodeEncoding(false, true));

            Assert.Equal(RegFileCheck.BadHeader, RegFile.Validate(p));
        }

        // A present-but-unreadable file says NOTHING about its contents. Reporting it as
        // BadHeader would tell the user their backup is corrupt when it may be perfectly good
        // and merely locked.
        [Fact]
        public void Validate_LockedFile_IsUnreadableNotBadHeader()
        {
            string p = Write("locked.reg", RegFile.Header + "\r\n", new UnicodeEncoding(false, true));

            using (new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Equal(RegFileCheck.Unreadable, RegFile.Validate(p));
            }
        }

        [Fact]
        public void Validate_LockedFile_ReportsWhyItCouldNotBeRead()
        {
            string p = Write("locked2.reg", RegFile.Header + "\r\n", new UnicodeEncoding(false, true));

            using (new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                string error;
                RegFile.Validate(p, out error);

                Assert.False(string.IsNullOrWhiteSpace(error));
            }
        }

        [Fact]
        public void Validate_ReadableFile_ReportsNoError()
        {
            string p = Write("clean.reg", RegFile.Header + "\r\n", new UnicodeEncoding(false, true));

            string error;
            RegFile.Validate(p, out error);

            Assert.Null(error);
        }
    }
}
