using WinRestoreKit;

namespace WinRestoreKit.Wpf.Services
{
    internal sealed class ExternalLinkService : IExternalLinkService
    {
        public void Open(string url) => Utils.OpenUrl(url);
    }
}
