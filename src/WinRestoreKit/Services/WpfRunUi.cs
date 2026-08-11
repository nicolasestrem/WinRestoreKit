using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using WinRestoreKit;

namespace WinRestoreKit.Wpf.Services
{
    internal sealed class WpfRunUi : IRunUi
    {
        private readonly Dispatcher dispatcher;
        private readonly IRunPresentation presentation;
        private readonly IRunDialogService dialogs;
        private readonly Func<Window> ownerProvider;
        private Window observedOwner;
        private bool observedOwnerClosing;

        internal WpfRunUi(Dispatcher dispatcher, IRunPresentation presentation,
                          IRunDialogService dialogs, Func<Window> ownerProvider)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
            this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            this.ownerProvider = ownerProvider ?? throw new ArgumentNullException(nameof(ownerProvider));
        }

        void IRunUi.SetProgressText(string text)
            => DispatchPresentation(() => presentation.SetProgressText(text));

        void IRunUi.SetProgressPercent(int percent)
            => DispatchPresentation(() => presentation.SetProgressPercent(percent));

        void IRunUi.SetProgressDetail(string groupInfo, string elapsed, string remaining, string throughput,
                                      long bytesWritten, int errors, int warnings)
            => DispatchPresentation(() => presentation.SetProgressDetail(
                groupInfo, elapsed, remaining, throughput, bytesWritten, errors, warnings));

        void IRunUi.ShowSummary(RunSummary summary, string caption, IReadOnlyList<ModuleOutcome> outcomes)
            => InvokePresentation(() => presentation.ShowSummary(summary, caption, outcomes));

        IReadOnlyList<string> IRunUi.ShowConsentDialog(RestorePlan plan)
            => InvokeDialog(() => dialogs.ShowRestoreConsent(plan));

        object IRunUi.DialogOwner
            => InvokeDialog(() => (object)GetLiveOwner());

        bool IRunUi.ConfirmSnapshotOverride(string text, string caption)
            => InvokeDialog(() => dialogs.ConfirmSnapshotOverride(text, caption));

        void IRunUi.ShowPlanCompositionError(string text, string caption)
            => InvokeDialog(() => dialogs.ShowPlanCompositionError(text, caption));

        void IRunUi.SetExplorerRestartVisible(bool visible)
            => DispatchPresentation(() => presentation.SetExplorerRestartVisible(visible));

        private void DispatchPresentation(Action action)
        {
            if (dispatcher.CheckAccess())
            {
                action();
                return;
            }

            try
            {
                dispatcher.BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void InvokePresentation(Action action)
        {
            if (dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }

        private void InvokeDialog(Action action)
        {
            if (dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }

        private T InvokeDialog<T>(Func<T> action)
            => dispatcher.CheckAccess() ? action() : dispatcher.Invoke(action);

        private Window GetLiveOwner()
        {
            Window owner = ownerProvider();
            if (!ReferenceEquals(owner, observedOwner))
            {
                if (observedOwner != null)
                {
                    observedOwner.Closing -= OwnerClosing;
                    observedOwner.Closed -= OwnerClosed;
                }

                observedOwner = owner;
                observedOwnerClosing = false;
                if (observedOwner != null)
                {
                    observedOwner.Closing += OwnerClosing;
                    observedOwner.Closed += OwnerClosed;
                }
            }

            return observedOwner != null && !observedOwnerClosing && observedOwner.IsLoaded &&
                   observedOwner.IsVisible
                ? observedOwner
                : null;
        }

        private void OwnerClosing(object sender, CancelEventArgs e)
        {
            observedOwnerClosing = true;
            try
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ReferenceEquals(sender, observedOwner) && observedOwner.IsVisible)
                        observedOwnerClosing = false;
                }));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void OwnerClosed(object sender, EventArgs e)
            => observedOwnerClosing = true;
    }
}
