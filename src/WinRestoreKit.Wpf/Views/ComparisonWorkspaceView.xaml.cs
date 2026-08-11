using System;
using System.Windows;
using System.Windows.Controls;

namespace WinRestoreKit.Wpf.Views
{
    public partial class ComparisonWorkspaceView : UserControl
    {
        private const double NarrowLayoutThreshold = 1040;
        private bool? narrowLayout;

        public ComparisonWorkspaceView()
        {
            InitializeComponent();
        }

        internal bool IsNarrowLayout => narrowLayout == true;

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
            => ApplyLayout(e.NewSize.Width < NarrowLayoutThreshold);

        private void ApplyLayout(bool narrow)
        {
            if (narrowLayout == narrow)
                return;

            narrowLayout = narrow;
            ComparisonPrimaryColumn.MinWidth = narrow ? 0 : 420;
            ComparisonPrimaryColumn.Width = new GridLength(1, GridUnitType.Star);
            ComparisonSecondaryColumn.Width = narrow ? new GridLength(0) : new GridLength(360);
            ComparisonPaneScroller.VerticalScrollBarVisibility = narrow
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Disabled;
            ComparisonPaneScroller.Padding = narrow ? new Thickness(0, 0, 8, 0) : new Thickness(0);

            ComparisonTopRow.Height = GridLength.Auto;
            ComparisonMainRow.Height = narrow ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            ComparisonDetailRow.Height = narrow ? GridLength.Auto : new GridLength(0);

            Place(EvidenceCard, 0, 0, narrow ? 1 : 3);
            Place(RestoreSetCard, narrow ? 0 : 1, narrow ? 1 : 0, 1);
            Place(DetailCard, narrow ? 0 : 1, narrow ? 2 : 1, narrow ? 1 : 2);
            Place(DetailPlaceholder, narrow ? 0 : 1, narrow ? 2 : 1, narrow ? 1 : 2);

            EvidenceCard.Height = narrow ? 300 : double.NaN;
            RestoreSetCard.Margin = narrow
                ? new Thickness(0, 12, 0, 12)
                : new Thickness(16, 0, 0, 12);
            DetailCard.Margin = narrow ? new Thickness(0) : new Thickness(16, 0, 0, 0);
            DetailPlaceholder.Margin = narrow ? new Thickness(0) : new Thickness(16, 0, 0, 0);
            DetailCard.MinHeight = narrow ? 220 : 0;
            DetailPlaceholder.MinHeight = narrow ? 150 : 0;
        }

        private static void Place(FrameworkElement element, int column, int row, int rowSpan)
        {
            Grid.SetColumn(element, column);
            Grid.SetRow(element, row);
            Grid.SetRowSpan(element, rowSpan);
        }
    }
}
