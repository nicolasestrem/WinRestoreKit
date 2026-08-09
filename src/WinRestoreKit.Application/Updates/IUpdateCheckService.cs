using System.Threading;
using System.Threading.Tasks;

namespace WinRestoreKit
{
    internal interface IUpdateCheckService
    {
        Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken);
    }
}
