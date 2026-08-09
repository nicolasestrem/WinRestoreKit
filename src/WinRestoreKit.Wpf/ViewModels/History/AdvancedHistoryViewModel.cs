using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using System.Threading;
using System.Threading.Tasks;
using WinRestoreKit;
using WinRestoreKit.Wpf.Infrastructure;
using WinRestoreKit.Wpf.ViewModels.Snapshots;

namespace WinRestoreKit.Wpf.ViewModels.History
{
    internal sealed class AdvancedHistoryViewModel : ObservableObject
    {
        private readonly ISnapshotEventReader catalog;
        private readonly Dispatcher dispatcher;
        private readonly ObservableCollection<SnapshotEventViewModel> events;
        private string searchText;
        private SnapshotEventViewModel selectedEvent;

        internal AdvancedHistoryViewModel(ISnapshotEventReader catalog)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            dispatcher = Dispatcher.CurrentDispatcher;
            events = new ObservableCollection<SnapshotEventViewModel>();
            Events = CollectionViewSource.GetDefaultView(events);
            Events.Filter = MatchesSearch;
        }

        public ICollectionView Events { get; }

        public string SearchText
        {
            get => searchText;
            set
            {
                if (SetProperty(ref searchText, value, nameof(SearchText)))
                    Events.Refresh();
            }
        }

        public SnapshotEventViewModel SelectedEvent
        {
            get => selectedEvent;
            set => SetProperty(ref selectedEvent, value, nameof(SelectedEvent));
        }

        internal async Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<SnapshotEvent> snapshots = await Task.Run(catalog.Read, cancellationToken);

            if (!dispatcher.CheckAccess())
            {
                await dispatcher.InvokeAsync(
                    () => ApplySnapshots(snapshots, cancellationToken),
                    DispatcherPriority.DataBind,
                    cancellationToken);
                return;
            }

            ApplySnapshots(snapshots, cancellationToken);
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
        }

        private bool MatchesSearch(object item)
        {
            if (item is not SnapshotEventViewModel snapshot)
                return false;

            if (string.IsNullOrWhiteSpace(SearchText))
                return true;

            string query = SearchText.Trim();
            return Contains(snapshot.DisplayName, query)
                || Contains(snapshot.MachineName, query)
                || Contains(snapshot.CanonicalPath, query)
                || Contains(snapshot.Status.Label, query);
        }

        private static bool Contains(string value, string query)
            => value != null && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
