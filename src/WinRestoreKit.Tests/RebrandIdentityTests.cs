using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace WinRestoreKit.Tests
{
    /// <summary>
    /// Pins the public product identity so a copied endpoint or metadata change cannot silently point
    /// an update, attribution, or executable back at the original project.
    /// </summary>
    public class RebrandIdentityTests
    {
        [Fact]
        public void RepositoryUrls_ArePinnedToWinRestoreKit()
        {
            Assert.Equal("https://github.com/nicolasestrem/WinRestoreKit", global::DataHelper.Data.Uri.URL_GITREPO);
            Assert.Equal("https://github.com/nicolasestrem/WinRestoreKit/releases/latest", global::DataHelper.Data.Uri.URL_GITLATEST);
            Assert.Equal("https://api.github.com/repos/nicolasestrem/WinRestoreKit/releases/latest", global::DataHelper.Data.Uri.URL_GITAPI_LATEST);
            Assert.Equal("https://raw.githubusercontent.com/nicolasestrem/WinRestoreKit/main/src/WinRestoreKit/Properties/AssemblyInfo.cs", global::DataHelper.Data.Uri.URL_ASSEMBLY);
        }

        [Fact]
        public void AttributionUrls_ArePinnedToMaintainerAndOriginalProject()
        {
            Assert.Equal("https://github.com/nicolasestrem", global::DataHelper.Data.Uri.URL_MAINTAINER);

            // This intentionally names the original project, so provenance is not silently rewritten.
            Assert.Equal("https://github.com/builtbybel/Appcopier", global::DataHelper.Data.Uri.URL_ORIGINAL_PROJECT);
        }

        [Fact]
        public void UserAgent_IsPinnedToWinRestoreKit()
        {
            Assert.Equal("WinRestoreKit", global::DataHelper.Data.UserAgent);
        }

        [Fact]
        public void ProductAssembly_DeclaresProductAndExecutableNames()
        {
            Assembly productAssembly = typeof(global::WinRestoreKit.Program).Assembly;
            AssemblyProductAttribute product = productAssembly.GetCustomAttribute<AssemblyProductAttribute>();

            Assert.Equal("WinRestoreKit", product?.Product);

            // Test runners, not the product executable, are the entry assembly under vstest.
            Assert.Equal("WinRestoreKit", productAssembly.GetName().Name);
        }

        [Fact]
        public void UrlConstants_ExceptOriginalProject_ContainNoLegacyIdentity()
        {
            const string originalOwner = "built" + "bybel";
            const string originalProduct = "App" + "copier";

            foreach (FieldInfo field in typeof(global::DataHelper.Data.Uri).GetFields())
            {
                if (field.FieldType != typeof(string) || field.Name == nameof(global::DataHelper.Data.Uri.URL_ORIGINAL_PROJECT))
                {
                    continue;
                }

                string value = (string)field.GetRawConstantValue();
                Assert.True(value.IndexOf(originalOwner, StringComparison.OrdinalIgnoreCase) < 0,
                    $"{field.Name} must not reference the original owner.");
                Assert.True(value.IndexOf(originalProduct, StringComparison.OrdinalIgnoreCase) < 0,
                    $"{field.Name} must not reference the original product.");
            }
        }

        [Fact]
        public void ParseLatestVersion_OnRealAssemblyInfo_ReturnsPinnedFallbackVersion()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "TestData", "AssemblyInfo.cs");

            Assert.True(File.Exists(path), $"Expected AssemblyInfo test data at '{path}'.");
            Assert.Equal("0.31.0", global::DataHelper.Data.ParseLatestVersion(File.ReadAllText(path)));
        }
    }
}
