using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace WinRestoreKit.Wpf.Infrastructure
{
    internal sealed class AsyncDelegateCommand : ICommand
    {
        private readonly Func<Task> executeAsync;
        private readonly Action<Exception> reportFailure;
        private readonly Func<bool> canExecute;
        private bool executing;

        internal AsyncDelegateCommand(Func<Task> executeAsync, Action<Exception> reportFailure = null,
            Func<bool> canExecute = null)
        {
            this.executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
            this.reportFailure = reportFailure;
            this.canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter) => !executing && (canExecute?.Invoke() ?? true);

        public async void Execute(object parameter)
        {
            if (!CanExecute(parameter))
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

        internal void RaiseCanExecuteChanged()
            => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
