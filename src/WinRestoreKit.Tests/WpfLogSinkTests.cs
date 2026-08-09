using System.Collections.Generic;
using System.Windows.Threading;
using WinRestoreKit.Wpf.Services;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class WpfLogSinkTests
    {
        [Fact]
        public void LogSink_AfterDispose_DoesNotPostAnotherLogLine()
        {
            WpfTestHost.Run(() =>
            {
                var lines = new List<string>();
                using var sink = new WpfLogSink(Dispatcher.CurrentDispatcher, lines.Add, () => lines.Clear());
                sink.Append("before");
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                sink.Dispose();
                sink.Append("after");
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                Assert.Equal(new[] { "before" }, lines);
            });
        }

        [Fact]
        public void LogSink_QueuedLineDisposedBeforeDispatch_DoesNotMutateThePresentation()
        {
            WpfTestHost.Run(() =>
            {
                var lines = new List<string>();
                using var sink = new WpfLogSink(Dispatcher.CurrentDispatcher, lines.Add, () => lines.Clear());
                sink.Append("queued");
                sink.Dispose();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                Assert.Empty(lines);
            });
        }
    }
}
