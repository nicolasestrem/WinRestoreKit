using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Threading;
using System.Threading.Tasks;
using WinRestoreKit;
using WinRestoreKit.Wpf.ViewModels.Snapshots;

namespace WinRestoreKit.Wpf.ViewModels.History
{
    public sealed class AdvancedHistoryViewModel : INotifyPropertyChanged
    {
        private readonly ISnapshotEventReader catalog;
        private readonly ObservableCollection<SnapshotEventViewModel> events;
        private string searchText;
        private SnapshotEventViewModel selectedEvent;

        internal AdvancedHistoryViewModel(ISnapshotEventReader catalog)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
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
                if (string.Equals(searchText, value, StringComparison.Ordinal))
                    return;

                searchText = value;
                Events.Refresh();
                OnPropertyChanged(nameof(SearchText));
            }
        }

        public SnapshotEventViewModel SelectedEvent
        {
            get => selectedEvent;
            set
            {
                if (ReferenceEquals(selectedEvent, value))
                    return;

                selectedEvent = value;
                OnPropertyChanged(nameof(SelectedEvent));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

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
            return Task.CompletedTask;
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

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
