using WinRestoreKit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Conf
{
    public class WNetworkConf : BackupBase
    {
        public WNetworkConf()
        {
            Title = "Network configuration";
            Info = "This will back up and restore TCP/IP network configuration.";
        }

        // A command rather than a list of keys or folders: netsh applies the dump itself, and the
        // set of interfaces it rewrites is decided by the backup file's contents, not by anything
        // this module could enumerate up front.
        public override IReadOnlyList<RestoreTarget> RestoreTargets
            => new[]
            {
                RestoreTarget.Command(
                    "runs netsh over the backed-up dump, which reapplies its IP addresses, DNS servers, " +
                    "routes and interface settings to this machine's network adapters")
            };

        public override async Task<ModuleResult> BackupAsync(string path)
        {
            string filePath = Path.Combine(path, $"{Title}.txt");

            // netsh prints the dump to stdout; RunToolAsync captures it and lands it in the file.
            ProcessOutcome outcome = await Utils.RunToolAsync(
                "netsh", new[] { "interface", "dump" }, stdoutFile: filePath);

            return ModuleResult.Aggregate(new[] { Verify(outcome, filePath) });
        }

        // The exit code alone is not enough. netsh can exit 0 having produced nothing, and an
        // empty dump restores nothing, so the artifact is checked as well.
        private StepResult Verify(ProcessOutcome outcome, string filePath)
        {
            if (outcome == null)
                return StepResult.Failed(Title, "the netsh export returned no outcome");

            if (!outcome.Started)
                return StepResult.Failed(Title, "could not run netsh: " + outcome.Error);

            if (outcome.TimedOut)
                return StepResult.Failed(Title, "netsh did not finish");

            if (outcome.Error != null)
                return StepResult.Failed(Title, "netsh ran but its outcome could not be determined: " + outcome.Error);

            if (outcome.ExitCode != 0)
                return StepResult.Failed(Title, $"netsh exited with code {outcome.ExitCode}");

            return Utils.ValidateExportArtifact(filePath, Title, "netsh", "exported the TCP/IP configuration");
        }

        public override async Task<ModuleResult> RestoreAsync(string path)
        {
            string filePath = Path.Combine(path, $"{Title}.txt");

            if (!File.Exists(filePath))
            {
                return ModuleResult.Aggregate(new[]
                {
                    StepResult.Skipped(Title, NothingBackedUp)
                });
            }

            ProcessOutcome outcome = await Utils.RunToolAsync(
                "netsh", new[] { "exec", filePath });

            StepResult step;

            if (outcome == null)
                step = StepResult.Failed(Title, "the netsh restore returned no outcome");
            else if (!outcome.Started)
                step = StepResult.Failed(Title, "could not run netsh: " + outcome.Error);
            else if (outcome.TimedOut)
                step = StepResult.Failed(Title, "netsh did not finish, and may have partly applied the configuration");
            else if (outcome.Error != null)
                step = StepResult.Failed(Title, "netsh ran but its outcome could not be determined, so the configuration may have been partly applied: " + outcome.Error);
            else if (outcome.ExitCode != 0)
                step = StepResult.Failed(Title, $"netsh exited with code {outcome.ExitCode}");
            else
                step = StepResult.Applied(Title, "the backed-up TCP/IP configuration");

            return ModuleResult.Aggregate(new[] { step });
        }

        /// <inheritdoc/>
        /// <remarks>
        /// One file, "{Title}.txt", written by netsh dump and read back by the restore - the same
        /// path both halves compose at lines 30 and 63.
        /// </remarks>
        public override bool? HasArtifactIn(string backupPath)
            => !string.IsNullOrWhiteSpace(backupPath)
               && File.Exists(Path.Combine(backupPath, $"{Title}.txt"));
    }
}
