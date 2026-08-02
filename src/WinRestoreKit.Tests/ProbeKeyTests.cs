using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    // These run unelevated against real HKCU/HKLM keys that exist on every Windows 11 install.
    public class ProbeKeyTests
    {
        [Fact]
        public void ProbeKey_CoreHkcuKey_IsPresent()
            => Assert.Equal(KeyProbe.Present, Utils.ProbeKey(@"HKEY_CURRENT_USER\Control Panel\Mouse"));

        [Fact]
        public void ProbeKey_CoreHklmKey_IsPresent()
            => Assert.Equal(KeyProbe.Present,
                   Utils.ProbeKey(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion"));

        [Fact]
        public void ProbeKey_NonexistentKey_IsAbsent()
            => Assert.Equal(KeyProbe.Absent,
                   Utils.ProbeKey(@"HKEY_CURRENT_USER\Software\WinRestoreKit\NoSuchKeyAtAll"));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ProbeKey_NoKey_IsAbsent(string key)
            => Assert.Equal(KeyProbe.Absent, Utils.ProbeKey(key));

        // The HKCU-probed-under-HKLM bug: the old prefix strip only removed the MATCHING base name,
        // so an HKCU path was additionally probed under HKLM with its full prefix still attached.
        [Fact]
        public void ProbeKey_HkcuPath_IsNotMatchedUnderHklm()
            => Assert.Equal(KeyProbe.Absent,
                   Utils.ProbeKey(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows NT\CurrentVersion\NoSuchSubkey"));

        [Fact]
        public void KeyExists_ShimAgreesWithProbeOnPresent()
            => Assert.True(Utils.KeyExists(@"HKEY_CURRENT_USER\Control Panel\Mouse"));

        [Fact]
        public void KeyExists_ShimAgreesWithProbeOnAbsent()
            => Assert.False(Utils.KeyExists(@"HKEY_CURRENT_USER\Software\WinRestoreKit\NoSuchKeyAtAll"));

        // The shim must never throw - SelectInstalled calls it for every module at tree-build time.
        [Fact]
        public void KeyExists_MalformedKey_ReturnsFalseInsteadOfThrowing()
            => Assert.False(Utils.KeyExists(@"NOT_A_HIVE\whatever"));

        // --- Indeterminate: the state this task exists to create ---
        //
        // HKLM\SECURITY is ACL-restricted to SYSTEM, so OpenSubKey throws SecurityException for
        // standard users AND for administrators. Verified on this machine, 2026-07-20, unelevated.
        // Without this test the catch blocks - the only genuinely new logic here - have no coverage
        // at all, and the Absent-vs-Indeterminate distinction rests entirely on a code comment.
        //
        // NOTE for anyone seeing this fail: that means the key became readable, not that ProbeKey
        // regressed. Check the hive's ACL before changing the assertion.

        [Fact]
        public void ProbeKey_AccessDeniedKey_IsIndeterminateNotAbsent()
            => Assert.Equal(KeyProbe.Indeterminate, Utils.ProbeKey(@"HKEY_LOCAL_MACHINE\SECURITY"));

        // The deliberate asymmetry: the backup path treats Indeterminate as a failure, but the
        // tree-build shim must map it to false, so an unprobeable module is never auto-selected.
        [Fact]
        public void KeyExists_AccessDeniedKey_IsFalse()
            => Assert.False(Utils.KeyExists(@"HKEY_LOCAL_MACHINE\SECURITY"));
    }
}
