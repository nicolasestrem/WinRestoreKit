using System;
using System.Collections.Generic;
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

            PendingTransition = ReplaceWorkspaceAsync(incoming);
        }

        public void ShowSnapshotDiagnostic(SnapshotEvent snapshot)
            => dialogs.ShowSnapshotDiagnostic(owner, snapshot);

        private async Task ReplaceWorkspaceAsync(SnapshotPayloadPreparation incoming)
        {
            bool comparisonStarted = false;
            try
            {
                if (currentWorkspace != null)
                    await currentWorkspace.CancelAsync();
                currentWorkspace?.RestoreSet.Clear();

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
