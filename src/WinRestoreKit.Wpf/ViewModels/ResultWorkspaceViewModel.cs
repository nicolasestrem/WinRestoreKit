using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using WinRestoreKit;
using WinRestoreKit.Wpf.Infrastructure;

namespace WinRestoreKit.Wpf.ViewModels
{
    internal sealed class ResultWorkspaceViewModel
    {
        private ResultWorkspaceViewModel(RunSummary summary, IReadOnlyList<ModuleOutcome> outcomes,
                                         Func<Task> returnToTimelineAsync)
        {
            Summary = summary ?? throw new ArgumentNullException(nameof(summary));
            Outcomes = outcomes ?? Array.Empty<ModuleOutcome>();
            StatusLabel = StatusLabelFor(Summary);
            ReturnToTimelineCommand = new AsyncDelegateCommand(
                returnToTimelineAsync ?? throw new ArgumentNullException(nameof(returnToTimelineAsync)));
        }

        public RunSummary Summary { get; }
        public IReadOnlyList<ModuleOutcome> Outcomes { get; }
        public string StatusLabel { get; }
        public string Headline => Summary.Headline;
        public string Detail => Summary.Detail;
        public RunSeverity Severity => Summary.Severity;
        public string SeverityText => Severity switch
        {
            RunSeverity.Information => "Information",
            RunSeverity.Warning => "Warning",
            RunSeverity.Error => "Error",
            _ => "Information"
        };
        public ICommand ReturnToTimelineCommand { get; }

        internal static ResultWorkspaceViewModel From(RunSummary summary,
                                                      IReadOnlyList<ModuleOutcome> outcomes,
                                                      Func<Task> returnToTimelineAsync)
            => new ResultWorkspaceViewModel(summary, outcomes, returnToTimelineAsync);

        private static string StatusLabelFor(RunSummary summary)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));

            if (summary.State == RunState.Canceled)
                return "Run canceled, no changes";

            return summary.Headline?.IndexOf("canceled, run incomplete", StringComparison.OrdinalIgnoreCase) >= 0
                ? "Run canceled, incomplete"
                : "Run complete";
        }
    }
}
