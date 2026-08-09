using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WinRestoreKit;
using WinRestoreKit.Wpf.Services;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class WpfRunUiTests
    {
        [Fact]
        public void ProgressCallbacksMarshalToTheDispatcherAndSummaryIsStoredBeforeReturn()
        {
            WpfTestHost.Run(() =>
            {
                var presentation = new RecordingPresentation();
                var ui = new WpfRunUi(Dispatcher.CurrentDispatcher, presentation,
                    new RecordingRunDialogs(), () => null);
                RunSummary summary = RunSummary.For(new[] { SucceededOutcome() }, true, RunVerb.Backup);

                Task.Run(() => ((IRunUi)ui).SetProgressText("Copying settings")).GetAwaiter().GetResult();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                ((IRunUi)ui).ShowSummary(summary, "Backup", new[] { SucceededOutcome() });

                Assert.Equal("Copying settings", presentation.ProgressText);
                Assert.Same(summary, presentation.Summary);
                Assert.Single(presentation.Outcomes);
            });
        }

        [Fact]
        public void DialogOwner_ReturnsOnlyALiveVisibleWpfWindow()
        {
            WpfTestHost.Run(() =>
            {
                Window owner = new Window();
                var ui = new WpfRunUi(Dispatcher.CurrentDispatcher, new RecordingPresentation(),
                    new RecordingRunDialogs(), () => owner);

                Assert.Null(((IRunUi)ui).DialogOwner);
                owner.Show();
                Assert.Same(owner, ((IRunUi)ui).DialogOwner);
                owner.Close();
                Assert.Null(((IRunUi)ui).DialogOwner);
            });
        }

        private static ModuleOutcome SucceededOutcome()
            => new ModuleOutcome("Mouse", ModuleResult.Aggregate(new[]
            {
                StepResult.Succeeded("Mouse", "exported setting")
            }));

        private sealed class RecordingPresentation : IRunPresentation
        {
            internal string ProgressText { get; private set; }
            internal RunSummary Summary { get; private set; }
            internal IReadOnlyList<ModuleOutcome> Outcomes { get; private set; }

            public void SetProgressText(string text) => ProgressText = text;
            public void SetProgressPercent(int percent) { }
            public void SetProgressDetail(string groupInfo, string elapsed, string remaining, string throughput,
                                          long bytesWritten, int errors, int warnings) { }
            public void ShowSummary(RunSummary summary, string caption, IReadOnlyList<ModuleOutcome> outcomes)
            {
                Summary = summary;
                Outcomes = outcomes;
            }
            public void SetExplorerRestartVisible(bool visible) { }
        }

        private sealed class RecordingRunDialogs : IRunDialogService
        {
            public IReadOnlyList<string> ShowRestoreConsent(RestorePlan plan) => null;
            public bool ConfirmSnapshotOverride(string text, string caption) => false;
            public void ShowPlanCompositionError(string text, string caption) { }
        }
    }
}
