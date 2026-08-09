namespace WinRestoreKit.Wpf.Services
{
    internal interface IWpfDialogService
    {
        void ShowInformation(string text, string caption);
        void ShowWarning(string text, string caption);
        void ShowError(string text, string caption);
        bool Confirm(string text, string caption);
    }
}
