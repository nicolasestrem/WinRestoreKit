using WinRestoreKit;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace WinRestoreKit.Tests
{
    // The shared command runner and the export-artifact ladder it feeds. These run real
    // processes (cmd.exe), unelevated, and touch nothing outside their own temp folders.
    public class CommandToolTests
    {
        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "actool_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public async Task RunToolAsync_CapturesStdoutIntoTheRequestedFile()
        {
            string dir = NewTempDir();
            string file = Path.Combine(dir, "out.txt");

            try
            {
                ProcessOutcome outcome = await Utils.RunToolAsync(
                    "cmd.exe", new[] { "/c", "echo", "hello" }, stdoutFile: file);

                Assert.True(outcome.Started);
                Assert.False(outcome.TimedOut);
                Assert.Null(outcome.Error);
                Assert.Equal(0, outcome.ExitCode);
                Assert.Contains("hello", File.ReadAllText(file));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // The stale-artifact landmine, for command exports: the backup folder is reused across
        // Backup clicks in one session, so a failed re-export must not leave the previous run's
        // file there to be restored after the row said Failed. Same rule as ExportRegistryKey's
        // pre-clear, held here for RunToolAsync.
        [Fact]
        public async Task RunToolAsync_FailedRun_RemovesTheStaleArtifact()
        {
            string dir = NewTempDir();
            string file = Path.Combine(dir, "out.txt");

            try
            {
                File.WriteAllText(file, "the previous run's export");

                ProcessOutcome outcome = await Utils.RunToolAsync(
                    "cmd.exe", new[] { "/c", "exit", "3" }, stdoutFile: file);

                Assert.Equal(3, outcome.ExitCode);
                Assert.False(File.Exists(file));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // On a non-zero exit the captured text is the tool's error banner, and writing it would
        // hand the restore side a file it will happily "apply".
        [Fact]
        public async Task RunToolAsync_FailedRun_WritesNoErrorBannerArtifact()
        {
            string dir = NewTempDir();
            string file = Path.Combine(dir, "out.txt");

            try
            {
                ProcessOutcome outcome = await Utils.RunToolAsync(
                    "cmd.exe", new[] { "/c", "echo error banner & exit 5" }, stdoutFile: file);

                Assert.Equal(5, outcome.ExitCode);
                Assert.False(File.Exists(file));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // The pre-clear side of the same rule: a previous export that cannot be removed is a
        // file the runner cannot vouch for, so the run must fail before the tool starts.
        [Fact]
        public async Task RunToolAsync_UnclearablePreviousExport_FailsBeforeStarting()
        {
            string dir = NewTempDir();
            string file = Path.Combine(dir, "out.txt");

            try
            {
                File.WriteAllText(file, "the previous run's export");

                using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    ProcessOutcome outcome = await Utils.RunToolAsync(
                        "cmd.exe", new[] { "/c", "echo", "hello" }, stdoutFile: file);

                    Assert.False(outcome.Started);
                    Assert.Contains("could not clear", outcome.Error);
                }
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // A write that throws (here: the target path is a directory) must leave nothing the
        // artifact ladder would bless - the honest end state is "Failed: wrote no file", never
        // a green row over an artifact this run did not finish producing.
        [Fact]
        public async Task RunToolAsync_FailedArtifactWrite_LeavesNothingTheLadderWouldBless()
        {
            string dir = NewTempDir();
            string target = Path.Combine(dir, "out.txt");
            Directory.CreateDirectory(target);

            try
            {
                ProcessOutcome outcome = await Utils.RunToolAsync(
                    "cmd.exe", new[] { "/c", "echo", "hello" }, stdoutFile: target);

                Assert.True(outcome.Started);
                Assert.Equal(0, outcome.ExitCode);
                Assert.False(File.Exists(target));

                StepResult step = Utils.ValidateExportArtifact(target, "Target", "cmd", "exported");
                Assert.Equal(ResultState.Failed, step.State);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task RunToolAsync_ReportsTheRealExitCode()
        {
            ProcessOutcome outcome = await Utils.RunToolAsync("cmd.exe", new[] { "/c", "exit", "3" });

            Assert.True(outcome.Started);
            Assert.Equal(3, outcome.ExitCode);
        }

        // A tool that cannot be started must say so, not report an outcome it never had.
        [Fact]
        public async Task RunToolAsync_MissingTool_NeverStarted()
        {
            ProcessOutcome outcome = await Utils.RunToolAsync(
                Path.Combine(Path.GetTempPath(), "no-such-tool-" + Guid.NewGuid().ToString("N") + ".exe"),
                new string[0]);

            Assert.False(outcome.Started);
            Assert.NotNull(outcome.Error);
        }

        // Exit code 0 is not evidence: regedit /e on a nonexistent key and netsh wlan export have
        // both been measured exiting 0 having written nothing. The ladder is what stands between
        // that and a green row.
        [Fact]
        public void ValidateExportArtifact_MissingFile_IsFailed()
        {
            string missing = Path.Combine(Path.GetTempPath(), "actool_" + Guid.NewGuid().ToString("N") + ".txt");

            StepResult step = Utils.ValidateExportArtifact(missing, "Target", "sometool", "exported");

            Assert.Equal(ResultState.Failed, step.State);
            Assert.Contains("wrote no file", step.Reason);
        }

        [Fact]
        public void ValidateExportArtifact_EmptyFile_IsFailed()
        {
            string dir = NewTempDir();
            string file = Path.Combine(dir, "empty.txt");

            try
            {
                File.WriteAllBytes(file, new byte[0]);

                StepResult step = Utils.ValidateExportArtifact(file, "Target", "sometool", "exported");

                Assert.Equal(ResultState.Failed, step.State);
                Assert.Contains("empty", step.Reason);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void ValidateExportArtifact_NonEmptyFile_SucceedsWithTheCallersReason()
        {
            string dir = NewTempDir();
            string file = Path.Combine(dir, "full.txt");

            try
            {
                File.WriteAllText(file, "content");

                StepResult step = Utils.ValidateExportArtifact(file, "Target", "sometool", "exported the thing");

                Assert.Equal(ResultState.Succeeded, step.State);
                Assert.Equal("exported the thing", step.Reason);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
