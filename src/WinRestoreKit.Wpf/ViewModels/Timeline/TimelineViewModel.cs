using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly ObservableCollection<SnapshotEventViewModel> events;
        private SnapshotEventViewModel selectedEvent;
        private string selectionError;

        internal TimelineViewModel(ISnapshotEventReader catalog,
            ISnapshotPayloadPreparationService preparationService, ITimelineNavigator navigator)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.preparationService = preparationService ?? throw new ArgumentNullException(nameof(preparationService));
            this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
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

        internal Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<SnapshotEvent> snapshots = catalog.Read();

            events.Clear();
            foreach (SnapshotEvent snapshot in snapshots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                events.Add(new SnapshotEventViewModel(snapshot));
            }

            SelectedEvent = null;
            SetSelectionError(null);
            return Task.CompletedTask;
        }

        internal async Task OpenSelectedAsync(CancellationToken cancellationToken = default)
        {
            SnapshotEventViewModel selected = SelectedEvent;
            if (selected == null)
                return;

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
