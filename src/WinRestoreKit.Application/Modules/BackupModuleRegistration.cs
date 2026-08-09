using System;

namespace WinRestoreKit
{
    public sealed class BackupModuleRegistration
    {
        internal BackupModuleRegistration(BackupBase module, string category)
        {
            Module = module ?? throw new ArgumentNullException(nameof(module));
            Category = category ?? string.Empty;
            Title = module.Title ?? string.Empty;
        }

        public BackupBase Module { get; }

        public string Category { get; }

        public string Title { get; }
    }
}
