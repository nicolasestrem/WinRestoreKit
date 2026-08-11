using System;
using System.Windows.Threading;

namespace WinRestoreKit.Wpf.Services
{
    internal sealed class WpfDispatcher
    {
        private readonly Dispatcher dispatcher;

        internal WpfDispatcher(Dispatcher dispatcher)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        internal bool CheckAccess() => dispatcher.CheckAccess();

        internal void Invoke(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            dispatcher.Invoke(action);
        }

        internal T Invoke<T>(Func<T> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            return dispatcher.Invoke(action);
        }

        internal void BeginInvoke(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            dispatcher.BeginInvoke(action);
        }
    }
}
