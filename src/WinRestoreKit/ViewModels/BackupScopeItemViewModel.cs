using System;
using System.Collections.Generic;
using WinRestoreKit;
using WinRestoreKit.Wpf.Infrastructure;

namespace WinRestoreKit.Wpf.ViewModels
{
    internal sealed class BackupScopeItemViewModel : ObservableObject
    {
        private bool isSelected;

        internal BackupScopeItemViewModel(ScopeGroupRow scope)
        {
            if (scope == null)
                throw new ArgumentNullException(nameof(scope));

            Name = scope.Name;
            Detail = scope.Detail;
            CautionNote = scope.CautionNote;
            RequiresExplicitOptIn = scope.RequiresExplicitOptIn;
            Modules = scope.Modules;
            isSelected = scope.DefaultChecked;
        }

        public string Name { get; }
        public string Detail { get; }
        public string CautionNote { get; }
        public bool HasCaution => !string.IsNullOrWhiteSpace(CautionNote);
        public bool RequiresExplicitOptIn { get; }
        public IReadOnlyList<BackupBase> Modules { get; }

        public bool IsSelected
        {
            get => isSelected;
            set => SetProperty(ref isSelected, value, nameof(IsSelected));
        }
    }
}
