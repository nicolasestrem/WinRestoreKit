using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using WinRestoreKit;
using WinRestoreKit.Wpf.ViewModels;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class ResultWorkspaceViewModelTests
    {
        [Fact]
        public void Result_CompletedSummaryAfterLateCancel_IsNotRelabeledIncomplete()
        {
            using var control = new RunControl();
            control.RequestCancellation();
            RunSummary completed = RunSummary.For(new[] { SucceededOutcome() }, true, RunVerb.Backup);

            ResultWorkspaceViewModel vm = ResultWorkspaceViewModel.From(completed,
                new[] { SucceededOutcome() }, () => Task.CompletedTask);

            Assert.Equal("Run complete", vm.StatusLabel);
            Assert.DoesNotContain("canceled", vm.Headline, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(RunSeverity.Information, vm.Severity);
        }

        [Fact]
        public void Result_IncompleteSummary_UsesItsActualCancellationWording()
        {
            RunSummary incomplete = RunSummary.Incomplete(Array.Empty<ModuleOutcome>(), RunVerb.Backup,
                "Cancellation was requested. No further group was started.");

            ResultWorkspaceViewModel vm = ResultWorkspaceViewModel.From(incomplete,
                Array.Empty<ModuleOutcome>(), () => Task.CompletedTask);

            Assert.Equal("Run canceled, incomplete", vm.StatusLabel);
            Assert.Contains("canceled, run incomplete", vm.Headline, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(RunSeverity.Warning, vm.Severity);
        }

        [Fact]
        public void Result_CanceledBeforeRun_UsesNoChangesLabelAndPreservesOutcomes()
        {
            ModuleOutcome outcome = SucceededOutcome();
            ResultWorkspaceViewModel vm = ResultWorkspaceViewModel.From(RunSummary.Canceled(RunVerb.Restore),
                new[] { outcome }, () => Task.CompletedTask);

            Assert.Equal("Run canceled, no changes", vm.StatusLabel);
            Assert.Equal(RunSeverity.Information, vm.Severity);
            Assert.Same(outcome, Assert.Single(vm.Outcomes));
        }

        [Fact]
        public void ResultView_ExposesNeutralSeverityAndReturnToTimelineAction()
        {
            WpfTestHost.Run(() =>
            {
                ResultWorkspaceViewModel viewModel = ResultWorkspaceViewModel.From(
                    RunSummary.For(new[] { SucceededOutcome() }, true, RunVerb.Backup),
                    new[] { SucceededOutcome() }, () => Task.CompletedTask);
                var view = new WinRestoreKit.Wpf.Views.ResultWorkspaceView
                {
                    DataContext = viewModel
                };
                var host = new Window { Content = view, Width = 1024, Height = 720 };
                host.Show();
                host.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(() => { }));

                try
                {
                    TextBlock severity = Assert.IsType<TextBlock>(view.FindName("RunSeverityText"));
                    TextBlock headline = Assert.IsType<TextBlock>(view.FindName("RunHeadlineText"));
                    TextBlock detail = Assert.IsType<TextBlock>(view.FindName("RunDetailText"));
                    Assert.Equal(viewModel.SeverityAutomationName,
                        AutomationProperties.GetName(severity));
                    Assert.Equal(viewModel.HeadlineAutomationName,
                        AutomationProperties.GetName(headline));
                    Assert.Equal(viewModel.DetailAutomationName,
                        AutomationProperties.GetName(detail));
                    Assert.NotNull(view.FindName("RunOutcomesList"));
                    Assert.NotNull(view.FindName("ReturnTimelineButton"));
                }
                finally
                {
                    host.Close();
                }
            });
        }

        [Fact]
        public async Task Result_ReturnToTimelineCommand_InvokesTheSuppliedAction()
        {
            var returned = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            ResultWorkspaceViewModel vm = ResultWorkspaceViewModel.From(
                RunSummary.For(new[] { SucceededOutcome() }, true, RunVerb.Backup),
                new[] { SucceededOutcome() }, () =>
                {
                    returned.TrySetResult(null);
                    return Task.CompletedTask;
                });

            vm.ReturnToTimelineCommand.Execute(null);

            await returned.Task;
        }

        private static ModuleOutcome SucceededOutcome()
            => new ModuleOutcome("Mouse", ModuleResult.Aggregate(new[]
            {
                StepResult.Succeeded("Mouse", "exported setting")
            }));
    }
}
