using System;
using System.Threading;
using System.Windows.Threading;
using WinRestoreKit;

namespace WinRestoreKit.Wpf.Services
{
    internal sealed class WpfLogSink : ILogSink, IDisposable
    {
        private readonly Dispatcher dispatcher;
        private readonly Action<string> append;
        private readonly Action clear;
        private int disposed;

        internal WpfLogSink(Dispatcher dispatcher, Action<string> append, Action clear)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.append = append ?? throw new ArgumentNullException(nameof(append));
            this.clear = clear ?? throw new ArgumentNullException(nameof(clear));
        }

        public void Append(string text) => Post(() => append(text ?? string.Empty));

        public void Clear() => Post(clear);

        public void Dispose() => Interlocked.Exchange(ref disposed, 1);

        private void Post(Action action)
        {
            if (Volatile.Read(ref disposed) != 0 || dispatcher.HasShutdownStarted ||
                dispatcher.HasShutdownFinished)
            {
                return;
            }

            try
            {
                dispatcher.BeginInvoke(() =>
                {
                    if (Volatile.Read(ref disposed) == 0)
                        action();
                });
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
