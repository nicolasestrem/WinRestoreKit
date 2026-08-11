using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WinRestoreKit;
using WinRestoreKit.Wpf.Services;
using WinRestoreKit.Wpf.ViewModels;

namespace WinRestoreKit.Wpf.Navigation
{
    internal sealed class CompareWorkflowNavigator : ITimelineNavigator
    {
        private readonly ShellViewModel shell;
        private readonly Window owner;
        private readonly ICompareDialogService dialogs;
        private ComparisonWorkspaceViewModel currentWorkspace;
        private int transitionVersion;

        internal CompareWorkflowNavigator(ShellViewModel shell, Window owner, ICompareDialogService dialogs)
        {
            this.shell = shell ?? throw new ArgumentNullException(nameof(shell));
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        }

        internal Task PendingTransition { get; private set; } = Task.CompletedTask;
        internal ComparisonWorkspaceViewModel CurrentWorkspace => currentWorkspace;

        public void OpenCompare(SnapshotPayloadPreparation incoming)
        {
            if (incoming == null)
                throw new ArgumentNullException(nameof(incoming));

            if (string.Equals(currentWorkspace?.Snapshot.CanonicalPath, incoming.Snapshot.CanonicalPath,
                StringComparison.OrdinalIgnoreCase))
            {
                incoming.Dispose();
                return;
            }

            if (currentWorkspace?.RestoreSet.HasItems == true &&
                !dialogs.ConfirmDiscardRestoreSet(owner, currentWorkspace.Snapshot, incoming.Snapshot))
            {
                incoming.Dispose();
                return;
            }

            int version = Interlocked.Increment(ref transitionVersion);
            PendingTransition = ReplaceWorkspaceAsync(incoming, version);
        }

        public void ShowSnapshotDiagnostic(SnapshotEvent snapshot)
            => dialogs.ShowSnapshotDiagnostic(owner, snapshot);

        internal async Task LeaveCompareAsync()
        {
            Interlocked.Increment(ref transitionVersion);
            ComparisonWorkspaceViewModel workspace = currentWorkspace;
            currentWorkspace = null;
            if (workspace == null)
                return;

            await workspace.CancelAsync();
            workspace.RestoreSet.Clear();
        }

        private async Task ReplaceWorkspaceAsync(SnapshotPayloadPreparation incoming, int version)
        {
            bool comparisonStarted = false;
            try
            {
                ComparisonWorkspaceViewModel previous = currentWorkspace;
                if (previous != null)
                    await previous.CancelAsync();
                previous?.RestoreSet.Clear();

                if (version != Volatile.Read(ref transitionVersion))
                {
                    incoming.Dispose();
                    return;
                }

                currentWorkspace = new ComparisonWorkspaceViewModel(incoming.Snapshot,
                    BackupModuleCatalog.CreateAll(), new SnapshotComparisonService(), ShowConfirm);
                shell.ShowCompare(currentWorkspace);
                comparisonStarted = true;
                await currentWorkspace.StartAsync(incoming);
            }
            catch (Exception ex)
            {
                if (!comparisonStarted)
                    incoming.Dispose();
                if (version == Volatile.Read(ref transitionVersion))
                    shell.ShowInlineWorkflowError("Comparison could not start: " + ex.Message);
            }
        }

        private void ShowConfirm(SnapshotEvent snapshot, IReadOnlyList<BackupBase> modules)
        {
            ConfirmViewModel confirm = new ConfirmViewModel(snapshot, modules, () => shell.ShowCompare(currentWorkspace));
            shell.ShowConfirm(confirm);
        }
    }
}
