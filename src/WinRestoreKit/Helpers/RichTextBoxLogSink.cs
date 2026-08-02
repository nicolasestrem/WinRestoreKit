using System;
using System.Windows.Forms;

namespace WinRestoreKit
{
    /// <summary>
    /// The shipping <see cref="ILogSink"/>: appends to the activity log RichTextBox, marshaling onto
    /// the UI thread when the caller is not already on it.
    /// </summary>
    /// <remarks>
    /// This marshaling used to live inside LogHelper. It is here because it is the half that needs
    /// WinForms, and LogHelper is engine code that must not.
    /// </remarks>
    internal sealed class RichTextBoxLogSink : ILogSink
    {
        private readonly RichTextBox target;

        public RichTextBoxLogSink(RichTextBox target)
        {
            this.target = target;
        }

        public void Append(string text)
        {
            if (target.InvokeRequired)
            {
                target.Invoke(new Action(() => target.AppendText(text)));
            }
            else
            {
                target.AppendText(text);
            }
        }

        public void Clear()
        {
            if (target.InvokeRequired)
            {
                target.Invoke(new Action(() => target.Clear()));
            }
            else
            {
                target.Clear();
            }
        }
    }

    /// <summary>
    /// Keeps <c>logger.SetTarget(someRichTextBox)</c> spelled the way every existing call site spells
    /// it, now that LogHelper itself cannot name a RichTextBox.
    /// </summary>
    internal static class LogHelperTargetExtensions
    {
        /// <summary>
        /// Points the logger at a RichTextBox, or at nothing when <paramref name="richText"/> is null.
        /// </summary>
        public static void SetTarget(this LogHelper logger, RichTextBox richText)
        {
            logger.SetSink(richText == null ? null : new RichTextBoxLogSink(richText));
        }
    }
}
