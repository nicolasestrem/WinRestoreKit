using System;
using System.Windows.Forms;

namespace WinRestoreKit
{
    /// <summary>
    /// Sends engine log lines to the live progress view while its backup is running.
    /// </summary>
    internal sealed class ProgressLogSink : ILogSink
    {
        private readonly Control owner;
        private readonly Action<string> append;
        private readonly Action clear;

        internal ProgressLogSink(Control owner, Action<string> append, Action clear)
        {
            this.owner = owner;
            this.append = append;
            this.clear = clear;
        }

        public void Append(string text) => Dispatch(() => append(text ?? string.Empty));

        public void Clear() => Dispatch(clear);

        private void Dispatch(Action action)
        {
            if (owner == null || owner.IsDisposed || owner.Disposing || !owner.IsHandleCreated)
                return;

            try
            {
                if (owner.InvokeRequired)
                {
                    owner.BeginInvoke(action);
                }
                else
                {
                    action();
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
