using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using WinRestoreKit;
using WinRestoreKit.Wpf.Infrastructure;

namespace WinRestoreKit.Wpf.Views.Dialogs
{
    internal sealed class ConsentChoice : ObservableObject
    {
        private bool isSelected;

        internal ConsentChoice(RestoreConsentEntry entry)
        {
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        }

        internal RestoreConsentEntry Entry { get; }
        public string Label => Entry.Label;
        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value)
                    return;

                isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }
    }

    internal sealed partial class RestoreConsentDialog : Window
    {
        private readonly RestorePlan plan;
        private readonly ObservableCollection<ConsentChoice> consentChoices;

        internal RestoreConsentDialog(RestorePlan plan)
        {
            this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
            consentChoices = new ObservableCollection<ConsentChoice>(this.plan.ConsentEntries.Select(entry => new ConsentChoice(entry)));
            InitializeComponent();
            ConstrainToWorkArea();
            Loaded += (_, _) => ConstrainToWorkArea();
            DataContext = this;
        }

        internal static RestoreConsentDialog Create(Window owner, RestorePlan plan)
            => new RestoreConsentDialog(plan) { Owner = owner ?? throw new ArgumentNullException(nameof(owner)) };

        public string ConfirmationText => plan.ConfirmationText;
        public string SnapshotNotice => plan.SnapshotNotice;
        public IReadOnlyList<string> InformationalCloseLines => plan.InformationalCloseLines;
        public IEnumerable ConsentChoices => consentChoices;
        public IReadOnlyList<string> ConsentedProcessNames
            => consentChoices.Where(choice => choice.IsSelected).Select(choice => choice.Entry.ProcessName).ToArray();

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void ConstrainToWorkArea()
        {
            const double workAreaInset = 32;
            Rect workArea = SystemParameters.WorkArea;
            MaxWidth = Math.Max(MinWidth, Math.Min(960, workArea.Width - workAreaInset));
            MaxHeight = Math.Max(MinHeight, workArea.Height - workAreaInset);
            Width = Math.Min(680, MaxWidth);
            Height = Math.Min(620, MaxHeight);
        }
    }
}
