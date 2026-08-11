using System;
using System.Collections.Generic;
using System.Linq;
using WinRestoreKit;

namespace WinRestoreKit.Wpf.ViewModels
{
    internal sealed class ModuleImpactViewModel
    {
        internal ModuleImpactViewModel(BackupBase module)
        {
            if (module == null)
                throw new ArgumentNullException(nameof(module));

            Targets = (module.RestoreTargets ?? Array.Empty<RestoreTarget>()).ToArray();
            Processes = (module.ProcessesToCloseBeforeRestore ?? Array.Empty<RestoreCloseRequirement>())
                .Where(requirement => requirement != null).ToArray();
            RequiresExplorerRestart = module.RequiresExplorerRestart;
            WarningMessage = module.WarningMessage ?? string.Empty;
        }

        public IReadOnlyList<RestoreTarget> Targets { get; }
        public IReadOnlyList<RestoreCloseRequirement> Processes { get; }
        public bool RequiresExplorerRestart { get; }
        public string WarningMessage { get; }
    }
}
