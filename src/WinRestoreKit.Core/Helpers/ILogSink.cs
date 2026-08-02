namespace WinRestoreKit
{
    /// <summary>
    /// Where <see cref="LogHelper"/> puts composed log text.
    /// </summary>
    /// <remarks>
    /// This is the seam that keeps the engine free of a UI reference. LogHelper composes the line -
    /// including the format-string discipline documented on LogMessage - and hands the finished text
    /// here; the implementation decides what a "log" is. The only shipping implementation marshals
    /// onto the UI thread and appends to a RichTextBox, and it lives in the app project because that
    /// is where WinForms lives.
    ///
    /// Implementations are called from thread-pool threads (modules run there) and must do their own
    /// marshaling. They may throw: LogHelper catches everything, because a failed log line must never
    /// take down a backup run.
    /// </remarks>
    internal interface ILogSink
    {
        void Append(string text);

        void Clear();
    }
}
