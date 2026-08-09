using System;
using System.IO;
using System.Text;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class RegFileValidateContentTests : IDisposable
    {
        private readonly string directory = Path.Combine(
            Path.GetTempPath(), "WinRestoreKit-RegFile-" + Guid.NewGuid().ToString("N"));

        public RegFileValidateContentTests()
        {
            Directory.CreateDirectory(directory);
        }

        public void Dispose()
        {
            try { Directory.Delete(directory, true); } catch { }
        }

        [Fact]
        public void Validate_HeaderOnlyExport_IsInvalid()
        {
            Assert.Equal(RegFileCheck.Invalid, Validate("header-only.reg", RegFile.Header + "\r\n"));
        }

        [Fact]
        public void Validate_HeaderAndPartialLineWithoutSection_IsInvalid()
        {
            Assert.Equal(RegFileCheck.Invalid, Validate("truncated.reg", RegFile.Header + "\r\n\r\n\"Name\"="));
        }

        [Fact]
        public void Validate_ExportWithKeySectionAndValue_IsValid()
        {
            Assert.Equal(RegFileCheck.Valid, Validate("complete.reg",
                RegFile.Header + "\r\n\r\n[HKEY_CURRENT_USER\\Software\\WinRestoreKit]\r\n\"Name\"=\"Value\"\r\n"));
        }

        [Fact]
        public void Validate_EmptyFile_IsInvalid()
        {
            Assert.Equal(RegFileCheck.Empty, Validate("empty.reg", ""));
        }

        private RegFileCheck Validate(string name, string content)
        {
            string path = Path.Combine(directory, name);
            File.WriteAllText(path, content, new UnicodeEncoding(false, true));
            return RegFile.Validate(path);
        }
    }
}
