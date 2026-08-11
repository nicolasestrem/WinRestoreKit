using WinRestoreKit;
using WinRestoreKit.Wpf.ViewModels;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class RestoreSetViewModelTests
    {
        [Fact]
        public void RestoreSet_OnlyAcceptsRowsWithUsableArtifacts()
        {
            WpfTestHost.Run(() =>
            {
                RestoreSetViewModel restoreSet = new RestoreSetViewModel();
                TestModule usableModule = new TestModule("Terminal");
                TestModule absentModule = new TestModule("Fonts");
                ModuleComparison unavailableButUsable = new ModuleComparison(
                    usableModule, ComparisonState.Unavailable, true, "Artifact captured.", "Unable to compare.");
                ModuleComparison absent = new ModuleComparison(
                    absentModule, ComparisonState.NotCaptured, false, "No artifact captured.", "Not captured.");

                restoreSet.Add(unavailableButUsable);
                restoreSet.Add(absent);

                Assert.Single(restoreSet.Modules);
                Assert.Same(usableModule, restoreSet.Modules[0]);
                Assert.False(restoreSet.Contains(absentModule));
            });
        }

        internal sealed class TestModule : BackupBase
        {
            internal TestModule(string title) => Title = title;
        }
    }
}
