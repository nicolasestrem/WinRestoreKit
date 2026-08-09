using System.Collections.ObjectModel;
using WinRestoreKit;
using WinRestoreKit.Wpf.Infrastructure;

namespace WinRestoreKit.Wpf.ViewModels
{
    internal sealed class RestoreSetViewModel : ObservableObject
    {
        private readonly ObservableCollection<BackupBase> modules = new ObservableCollection<BackupBase>();

        internal RestoreSetViewModel()
        {
            Modules = new ReadOnlyObservableCollection<BackupBase>(modules);
        }

        public ReadOnlyObservableCollection<BackupBase> Modules { get; }
        public bool HasItems => modules.Count != 0;

        internal bool Contains(BackupBase module) => modules.Contains(module);

        internal void Add(ModuleComparison comparison)
        {
            if (comparison?.CanRestore != true || modules.Contains(comparison.Module))
                return;

            modules.Add(comparison.Module);
            OnPropertyChanged(nameof(HasItems));
        }

        internal void Remove(BackupBase module)
        {
            if (!modules.Remove(module))
                return;

            OnPropertyChanged(nameof(HasItems));
        }

        internal void Clear()
        {
            if (modules.Count == 0)
                return;

            modules.Clear();
            OnPropertyChanged(nameof(HasItems));
        }
    }
}
