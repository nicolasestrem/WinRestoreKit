using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using Views;
using WinRestoreKit;
using Xunit;
namespace WinRestoreKit.Tests
{
    public class ProgressPageViewTests
    {
        [Fact]
        public void Constructor_DoesNotStartBackupBeforeViewIsShown()
        {
            RunCoordinator.SetRunning(false);

            using (Panel host = new Panel())
            using (ProgressPageView view = new ProgressPageView(
                       new NavigationService(host),
                       Array.Empty<BackupBase>(),
                       "nightly",
                       SnapshotCompression.Fast,
                       @"C:\backup"))
            {
                Assert.IsAssignableFrom<IRunUi>(view);
                Assert.False(RunCoordinator.IsRunning);
            }
        }

        [Fact]
        public void RenderSummary_LateCancellationAfterCompletedBackup_DoesNotLabelRunCanceledOrIncomplete()
        {
            using (ProgressPageView view = CreateView())
            {
                RequestCancellation(view);
                Render(view, RunSummary.For(new List<ModuleOutcome> { SucceededOutcome() }, true, RunVerb.Backup));

                Assert.Equal("RUN COMPLETE", KickerText(view));
            }
        }

        [Fact]
        public void RenderSummary_CanceledBeforeAnyChange_LabelsRunCanceledWithNoChanges()
        {
            using (ProgressPageView view = CreateView())
            {
                Render(view, RunSummary.Canceled(RunVerb.Backup));

                Assert.Equal("RUN CANCELED, NO CHANGES", KickerText(view));
            }
        }

        [Fact]
        public void RenderSummary_CanceledAtModuleBoundary_LabelsRunCanceledIncomplete()
        {
            using (ProgressPageView view = CreateView())
            {
                Render(view, RunSummary.Incomplete(new List<ModuleOutcome> { SucceededOutcome() }, RunVerb.Backup,
                    "Cancellation was requested. No further group was started."));

                Assert.Equal("RUN CANCELED, INCOMPLETE", KickerText(view));
            }
        }


        [Fact]
        public void SetProgressText_ArchivePhase_DisablesCancellation()
        {
            using (ProgressPageView view = CreateView())
            {
                ((IRunUi)view).SetProgressText("Archiving backup payload");

                Assert.False(CancelButton(view).Enabled);
            }
        }
        private static ProgressPageView CreateView()
        {
            var view = new ProgressPageView(new NavigationService(new Panel()), Array.Empty<BackupBase>(), "nightly",
                SnapshotCompression.Fast, @"C:\backup");

            // RenderSummary (called by the Render helper below) sets control properties that lazily
            // create the view's handle, which fires OnLoad -> RunAsync -> a real async backup run.
            // That run's completion calls ShowSummary and overwrites the very kicker these tests
            // assert on, racing the test's read (RenderSummary_LateCancellationAfterCompletedBackup
            // went deterministically red from this). OnLoad guards on `runStarted`; set it so the
            // spurious run never starts and RenderSummary is exercised in isolation, which is what
            // these tests intend.
            FieldInfo runStarted = typeof(ProgressPageView).GetField("runStarted",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(runStarted);
            runStarted.SetValue(view, true);

            return view;
        }

        private static ModuleOutcome SucceededOutcome()
        {
            return new ModuleOutcome("Mouse",
                ModuleResult.Aggregate(new[] { StepResult.Succeeded("key", "exported key") }));
        }

        private static void RequestCancellation(ProgressPageView view)
        {
            FieldInfo control = typeof(ProgressPageView).GetField("runControl",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(control);
            ((RunControl)control.GetValue(view)).RequestCancellation();
        }

        private static void Render(ProgressPageView view, RunSummary summary)
        {
            MethodInfo render = typeof(ProgressPageView).GetMethod("RenderSummary",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(render);
            render.Invoke(view, new object[] { summary, Array.Empty<ModuleOutcome>() });
        }

        private static Button CancelButton(ProgressPageView view)
        {
            FieldInfo cancel = typeof(ProgressPageView).GetField("cancelButton",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(cancel);
            return (Button)cancel.GetValue(view);
        }

        private static string KickerText(ProgressPageView view)
        {
            FieldInfo kicker = typeof(ProgressPageView).GetField("kickerLabel",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(kicker);
            return ((Label)kicker.GetValue(view)).Text;
        }
    }
}
