using WinRestoreKit;
using WinRestoreKit.Wpf.ViewModels;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class RestoreSetViewModelTests
    {
        [Fact]
        public void RestoreSet_AcceptsEveryUsableArtifactEvenWhenDriftIsUnavailable()
        {
            WpfTestHost.Run(() =>
            {
                RestoreSetViewModel restoreSet = new RestoreSetViewModel();
                TestModule unavailableModule = new TestModule("Terminal");
                TestModule usableModule = new TestModule("Mouse");
                TestModule absentModule = new TestModule("Fonts");
                ModuleComparison unavailableButUsable = new ModuleComparison(
                    unavailableModule, ComparisonState.Unavailable, true, "Artifact captured.", "Unable to compare.");
                ModuleComparison changed = new ModuleComparison(
                    usableModule, ComparisonState.Changed, true, "Artifact captured.", "Changed.");
                ModuleComparison absent = new ModuleComparison(
                    absentModule, ComparisonState.NotCaptured, false, "No artifact captured.", "Not captured.");

                restoreSet.Add(unavailableButUsable);
                restoreSet.Add(changed);
                restoreSet.Add(absent);

                Assert.Collection(restoreSet.Modules,
                    module => Assert.Same(unavailableModule, module),
                    module => Assert.Same(usableModule, module));
                Assert.True(restoreSet.Contains(unavailableModule));
                Assert.False(restoreSet.Contains(absentModule));
            });
        }

        internal sealed class TestModule : BackupBase
        {
            internal TestModule(string title) => Title = title;
        }
    }
}
