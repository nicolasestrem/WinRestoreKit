using System;
using System.Collections.Generic;
using WinRestoreKit;

namespace WinRestoreKit.Wpf.ViewModels
{
    internal sealed class BackupRunCompletion
    {
        internal BackupRunCompletion(RunSummary summary, IReadOnlyList<ModuleOutcome> outcomes,
            string attemptedBackupPath)
        {
            Summary = summary ?? throw new ArgumentNullException(nameof(summary));
            Outcomes = outcomes ?? Array.Empty<ModuleOutcome>();
            AttemptedBackupPath = attemptedBackupPath;
        }

        internal RunSummary Summary { get; }
        internal IReadOnlyList<ModuleOutcome> Outcomes { get; }
        internal string AttemptedBackupPath { get; }
    }
}
