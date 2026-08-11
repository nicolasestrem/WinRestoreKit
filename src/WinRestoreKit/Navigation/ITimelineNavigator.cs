using WinRestoreKit;

namespace WinRestoreKit.Wpf.Navigation
{
    internal interface ITimelineNavigator
    {
        void OpenCompare(SnapshotPayloadPreparation preparation);

        void ShowSnapshotDiagnostic(SnapshotEvent snapshot);
    }
}
