using System;
using System.Text;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    /// <summary>
    /// The only tests that give <see cref="LogHelper"/> a sink, and they must take it away again.
    /// </summary>
    /// <remarks>
    /// LogHelper's sink is static, so one left behind here outlives the test that set it and every
    /// later test in the run logs into it. That is not a tidiness point: product code in other tests
    /// logs freely, and a stray sink would capture text this suite never asserts on while silently
    /// changing LogHelper's behaviour under load.
    ///
    /// These pin the format-string discipline that is this logger's whole reason for existing.
    /// LogMessage treats its single argument as literal text - a reason string can contain braces,
    /// and treating it as a format string is the silent-swallow defect this guards. Log, by contrast,
    /// treats its first argument as a format string. The capture sink lets both be observed without a
    /// UI control.
    /// </remarks>
    public class LogHelperTests : IDisposable
    {
        private readonly CaptureSink sink;

        public LogHelperTests()
        {
            sink = new CaptureSink();
            LogHelper.Instance.SetSink(sink);
        }

        public void Dispose()
        {
            // Drop the static reference first: nothing after this test may log into a captured sink
            // owned by a disposed instance.
            LogHelper.Instance.SetSink(null);
        }

        [Fact]
        public void LogMessage_TextContainingBraces_ReachesTheSink()
        {
            // A real reason string: a registry path plus exception text with braces in it.
            const string reason = @"could not export HKEY_CURRENT_USER\Software\{4D36E96B}: access denied";
            LogHelper.Instance.LogMessage(reason);

            Assert.Contains("4D36E96B", sink.Text);
            Assert.Contains("access denied", sink.Text);
        }

        [Fact]
        public void LogMessage_UnmatchedBrace_DoesNotThrowAndStillLogs()
        {
            LogHelper.Instance.LogMessage("failed on {0 unbalanced");

            Assert.Contains("unbalanced", sink.Text);
        }

        [Fact]
        public void Log_WithFormatArguments_StillFormats()
        {
            LogHelper.Instance.Log("exported {0} keys", 3);

            Assert.Contains("exported 3 keys", sink.Text);
        }

        // Every other test class in this suite runs with no sink, and product code logs freely
        // throughout. If that path were not silent, the teardown above would trade one failure for
        // another.
        [Fact]
        public void LoggingWithNoSink_IsSilentRatherThanFatal()
        {
            LogHelper.Instance.SetSink(null);

            LogHelper.Instance.LogMessage("nobody is listening");
            LogHelper.Instance.Log("nor to {0}", "this");
            LogHelper.Instance.ClearLog();

            LogHelper.Instance.SetSink(sink);
        }

        private sealed class CaptureSink : ILogSink
        {
            private readonly StringBuilder builder = new StringBuilder();

            internal string Text => builder.ToString();

            public void Append(string text) => builder.Append(text);
            public void Clear() => builder.Clear();
        }
    }
}
