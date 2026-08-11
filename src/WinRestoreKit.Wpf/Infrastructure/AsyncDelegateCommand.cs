using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace WinRestoreKit.Wpf.Infrastructure
{
    internal sealed class AsyncDelegateCommand : ICommand
    {
        private readonly Func<Task> executeAsync;
        private readonly Action<Exception> reportFailure;
        private bool executing;

        internal AsyncDelegateCommand(Func<Task> executeAsync, Action<Exception> reportFailure = null)
        {
            this.executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
            this.reportFailure = reportFailure;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter) => !executing;

        public async void Execute(object parameter)
        {
            if (executing)
                return;

            executing = true;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            try
            {
                await executeAsync();
            }
            catch (Exception ex)
            {
                reportFailure?.Invoke(ex);
            }
            finally
            {
                executing = false;
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
