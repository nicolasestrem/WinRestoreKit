using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using WinRestoreKit;
using WinRestoreKit.Wpf.Infrastructure;
using WinRestoreKit.Wpf.Navigation;
using WinRestoreKit.Wpf.ViewModels.Snapshots;

namespace WinRestoreKit.Wpf.ViewModels.Timeline
{
    internal sealed class TimelineViewModel : ObservableObject
    {
        private readonly ISnapshotEventReader catalog;
        private readonly ISnapshotPayloadPreparationService preparationService;
        private readonly ITimelineNavigator navigator;
        private readonly Dispatcher dispatcher;
        private readonly ObservableCollection<SnapshotEventViewModel> events;
        private readonly SemaphoreSlim refreshGate = new SemaphoreSlim(1, 1);
        private SnapshotEventViewModel selectedEvent;
        private string selectionError;
        private bool isLoading = true;
        private bool isOpening;

        internal TimelineViewModel(ISnapshotEventReader catalog,
            ISnapshotPayloadPreparationService preparationService, ITimelineNavigator navigator)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.preparationService = preparationService ?? throw new ArgumentNullException(nameof(preparationService));
            this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
            dispatcher = Dispatcher.CurrentDispatcher;
            events = new ObservableCollection<SnapshotEventViewModel>();
            Events = new ReadOnlyObservableCollection<SnapshotEventViewModel>(events);
        }

        public ReadOnlyObservableCollection<SnapshotEventViewModel> Events { get; }

        public SnapshotEventViewModel SelectedEvent
        {
            get => selectedEvent;
            set => SetProperty(ref selectedEvent, value, nameof(SelectedEvent));
        }

        public string SelectionError => selectionError;

        public bool HasSelectionError => !string.IsNullOrWhiteSpace(SelectionError);

        public bool IsLoading => isLoading;

        internal async Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await refreshGate.WaitAsync(cancellationToken);
            try
            {
                await DispatchAsync(() => SetIsLoading(true), cancellationToken);
                try
                {
                    IReadOnlyList<SnapshotEvent> snapshots = await Task.Run(catalog.Read, cancellationToken);
                    await DispatchAsync(() => ApplySnapshots(snapshots, cancellationToken), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await DispatchAsync(
                        () => SetSelectionError("Timeline could not be refreshed: " + ex.Message),
                        CancellationToken.None);
                }
                finally
                {
                    await DispatchAsync(() => SetIsLoading(false), CancellationToken.None);
                }
            }
            finally
            {
                refreshGate.Release();
            }
        }

        private void ApplySnapshots(IReadOnlyList<SnapshotEvent> snapshots,
            CancellationToken cancellationToken)
        {
            events.Clear();
            foreach (SnapshotEvent snapshot in snapshots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                events.Add(new SnapshotEventViewModel(snapshot));
            }

            SelectedEvent = null;
            SetSelectionError(null);
        }

        internal async Task OpenSelectedAsync(CancellationToken cancellationToken = default)
        {
            if (isOpening)
                return;

            SnapshotEventViewModel selected = SelectedEvent;
            if (selected == null)
                return;

            isOpening = true;
            try
            {
                SetSelectionError(null);
                if (!selected.Event.IsRestorable)
                {
                    navigator.ShowSnapshotDiagnostic(selected.Event);
                    return;
                }

                SnapshotPayloadPreparation prepared = await preparationService
                    .PrepareAsync(selected.Event, cancellationToken);
                if (!prepared.IsPrepared)
                {
                    SetSelectionError(prepared.Error);
                    prepared.Dispose();
                    return;
                }

                try
                {
                    navigator.OpenCompare(prepared);
                    prepared = null;
                }
                finally
                {
                    prepared?.Dispose();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                SetSelectionError("Snapshot could not be opened: " + ex.Message);
            }
            finally
            {
                isOpening = false;
            }
        }

        private Task DispatchAsync(Action action, CancellationToken cancellationToken)
        {
            if (dispatcher.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }

            return dispatcher.InvokeAsync(action, DispatcherPriority.DataBind, cancellationToken).Task;
        }

        private void SetIsLoading(bool value)
            => SetProperty(ref isLoading, value, nameof(IsLoading));

        private void SetSelectionError(string value)
        {
            if (string.Equals(selectionError, value, StringComparison.Ordinal))
                return;

            selectionError = value;
            OnPropertyChanged(nameof(SelectionError));
            OnPropertyChanged(nameof(HasSelectionError));
        }
    }
}
