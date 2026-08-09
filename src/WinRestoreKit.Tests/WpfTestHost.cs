using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace WinRestoreKit.Tests
{
    internal static class WpfTestHost
    {
        internal static void Run(Action action)
            => Run<object>(() =>
            {
                action();
                return null;
            });

        internal static T Run<T>(Func<T> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            T result = default;
            ExceptionDispatchInfo failure = null;
            Thread thread = new Thread(() =>
            {
                try
                {
                    result = action();
                }
                catch (Exception ex)
                {
                    failure = ExceptionDispatchInfo.Capture(ex);
                }
                finally
                {
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
            })
            {
                IsBackground = true,
                Name = "WinRestoreKit WPF test"
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            failure?.Throw();
            return result;
        }

        internal static Task RunAsync(Func<Task> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            TaskCompletionSource<object> completion =
                new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            Thread thread = new Thread(() =>
            {
                Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                dispatcher.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        await action();
                        completion.TrySetResult(null);
                    }
                    catch (Exception ex)
                    {
                        completion.TrySetException(ex);
                    }
                    finally
                    {
                        dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    }
                }));
                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "WinRestoreKit asynchronous WPF test"
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return completion.Task;
        }
    }
}
