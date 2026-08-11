using Conf;
using System.Collections.Generic;
using System.Linq;

namespace WinRestoreKit
{
    public static class BackupModuleCatalog
    {
        public static IReadOnlyList<BackupModuleRegistration> CreateAll()
            => ModuleCatalog.CreateAll()
                .Select(entry => new BackupModuleRegistration(entry.Module, entry.Category))
                .ToArray();
    }
}
