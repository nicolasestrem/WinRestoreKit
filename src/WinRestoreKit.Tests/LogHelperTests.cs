using System;
using WinRestoreKit;
using System.Windows.Forms;
using Xunit;

namespace WinRestoreKit.Tests
{
    /// <summary>
    /// These are the only tests that give <see cref="LogHelper"/> a target, and they must take it
    /// away again.
    /// </summary>
    /// <remarks>
    /// LogHelper's target is static, so one left behind here outlives the test that set it and every
    /// later test in the run logs into it. That is not a tidiness point. The RichTextBox is created
    /// on a test thread with no message pump, so InvokeRequired answers true and Log() calls
    /// Invoke() - which waits for a pump that will never run. Product code logging afterwards blocks
    /// there, and if the control is finalized while that Invoke is outstanding the test host dies.
    ///
    /// Measured before this teardown existed: an unfiltered `dotnet test` aborted with "Test host
    /// process crashed" on roughly two runs in five, always with a CopyFolderTests locked-file test
    /// in flight - those are the tests whose failure paths log once per file. CopyFolderTests on its
    /// own never crashed, because nothing had set a target. The defect was in the harness, not in
    /// CopyFolder.
    /// </remarks>
    public class LogHelperTests : IDisposable
    {
        private readonly RichTextBox box;

        public LogHelperTests()
        {
            box = new RichTextBox();
            System.IntPtr unused = box.Handle;   // force handle creation so InvokeRequired answers honestly
            LogHelper.Instance.SetTarget(box);
        }

        public void Dispose()
        {
            // Order matters: drop the static reference first, so nothing can begin an Invoke against
            // a control that is about to be disposed.
            LogHelper.Instance.SetTarget(null);
            box.Dispose();
        }

        [Fact]
        public void LogMessage_TextContainingBraces_ReachesTheTarget()
        {
            // A real reason string: a registry path plus exception text with braces in it.
            const string reason = @"could not export HKEY_CURRENT_USER\Software\{4D36E96B}: access denied";
            LogHelper.Instance.LogMessage(reason);

            Assert.Contains("4D36E96B", box.Text);
            Assert.Contains("access denied", box.Text);
        }

        [Fact]
        public void LogMessage_UnmatchedBrace_DoesNotThrowAndStillLogs()
        {
            LogHelper.Instance.LogMessage("failed on {0 unbalanced");

            Assert.Contains("unbalanced", box.Text);
        }

        [Fact]
        public void Log_WithFormatArguments_StillFormats()
        {
            LogHelper.Instance.Log("exported {0} keys", 3);

            Assert.Contains("exported 3 keys", box.Text);
        }

        // Every other test class in this suite runs with no target, and product code logs freely
        // throughout. If that path were not silent, the teardown above would trade one crash for
        // another.
        [Fact]
        public void LoggingWithNoTarget_IsSilentRatherThanFatal()
        {
            LogHelper.Instance.SetTarget(null);

            LogHelper.Instance.LogMessage("nobody is listening");
            LogHelper.Instance.Log("nor to {0}", "this");
            LogHelper.Instance.ClearLog();

            LogHelper.Instance.SetTarget(box);
        }
    }
}
