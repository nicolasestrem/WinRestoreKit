using System;

namespace WinRestoreKit
{
    internal class LogHelper
    {
        private static readonly LogHelper instance = new LogHelper();
        private static ILogSink sink = null;

        private LogHelper()
        { }  // Private constructor to prevent external instantiation

        // Logger to the sink that renders it - see ILogSink. The app registers a RichTextBox-backed
        // sink; everything else in the process logs into nothing, silently and on purpose.
        public void SetSink(ILogSink logSink)
        {
            sink = logSink;
        }

        public void Log(string format, params object[] args)
        {
            format += "\r\n";

            try
            {
                // Read the field once. Modules log from thread-pool threads while the UI can clear
                // the sink underneath them, and a null check against one read followed by a
                // dereference of another loses the line to an NRE that AppendLog's catch routes to
                // Console.WriteLine - invisible in a WinForms app, which is the silent loss the
                // LogMessage discipline exists to prevent.
                ILogSink current = sink;

                if (current != null)
                {
                    AppendLog(current, format, args);
                }
            }
            catch (Exception ex)
            {
                LogError($"Error in log: {ex.Message}");
            }
        }

        /// <summary>
        /// Logs an already-composed message, with no <see cref="string.Format"/> pass over it.
        /// </summary>
        /// <remarks>
        /// Use this for anything whose text is data rather than a template - result reason strings,
        /// registry paths, exception messages. Log(string, params object[]) treats its first
        /// argument as a format string, so a single brace in the text throws FormatException inside
        /// AppendLog, which routes the line to Console.WriteLine - invisible in a WinForms app.
        /// The message is not lost loudly; it is lost silently, which is worse.
        /// </remarks>
        public void LogMessage(string message)
        {
            // "{0}" as the template and the caller's text as an ARGUMENT: string.Format then has
            // nothing to parse in the untrusted half.
            Log("{0}", message ?? string.Empty);
        }

        private void AppendLog(ILogSink target, string format, params object[] args)
        {
            try
            {
                target.Append(string.Format(format, args));
            }
            catch (FormatException ex)
            {
                LogError($"Exception in log: {ex.Message}");
                LogError($"Exception: {format}");
            }
            catch (Exception ex)
            {
                LogError($"Error in Log method: {ex.Message}");
            }
        }

        private void LogError(string message)
        {
            Console.WriteLine($"Error: {message}");
        }

        public void ClearLog()
        {
            // Same read-once rule as Log, and the same explicit null check rather than letting the
            // catch below absorb an NRE. Both methods document "silent when no sink is registered";
            // spelling one of them with an empty catch made an unregistered sink indistinguishable
            // from a sink that genuinely failed.
            ILogSink current = sink;

            if (current == null)
                return;

            try
            {
                current.Clear();
            }
            catch { }
        }

        public static LogHelper Instance
        {
            get => instance;
        }
    }
}
