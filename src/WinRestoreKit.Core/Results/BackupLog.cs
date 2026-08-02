using System.Collections.Generic;
using System.Text;

namespace WinRestoreKit
{
    /// <summary>
    /// Composes backup_log.txt.
    /// </summary>
    /// <remarks>
    /// v1 listed what was SELECTED, which is the same category of lie as the old success dialog: it
    /// described an intention as though it were an outcome. v2 records what happened per module.
    ///
    /// Safe to change format: the only reader (the History timeline) dumps the file verbatim into a
    /// textbox and never parses it. The version
    /// header is cheap insurance in case anything ever does parse it.
    /// </remarks>
    internal static class BackupLog
    {
        // Older backups on disk still carry the Appcopier header, which is fine because readers never parse it.
        internal const string VersionHeader = "# WinRestoreKit backup log v2";

        /// <param name="extraHeaderLines">
        /// Written verbatim between the version header and the timestamp, or null for none. Verbatim
        /// because the caller - RestoreLog - owns how its lines read; prefixing them here would put
        /// this class in charge of wording it does not compose.
        /// </param>
        internal static string Compose(IReadOnlyList<BackupBase> modules,
                                       IReadOnlyList<ModuleResult> results,
                                       string when,
                                       IEnumerable<string> extraHeaderLines = null)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine(VersionHeader);

            if (extraHeaderLines != null)
            {
                foreach (string line in extraHeaderLines)
                {
                    if (line != null)
                        sb.AppendLine(line);
                }
            }

            sb.AppendLine("# " + when);
            sb.AppendLine();

            int count = modules == null ? 0 : modules.Count;

            for (int i = 0; i < count; i++)
            {
                BackupBase module = modules[i];

                // Counts can diverge if a module threw before producing a result. Report that
                // rather than indexing past the end.
                ModuleResult result = (results != null && i < results.Count) ? results[i] : null;

                if (result == null)
                {
                    sb.AppendLine(string.Format("{0} ({1})  UNKNOWN  no result was recorded",
                        module.Title, module.GetType().Name));
                    continue;
                }

                sb.AppendLine(string.Format("{0} ({1})  {2}  {3}",
                    module.Title, module.GetType().Name, Label(result.State), result.Reason));
            }

            return sb.ToString();
        }

        private static string Label(ResultState state)
        {
            switch (state)
            {
                case ResultState.Succeeded: return "OK";
                case ResultState.Skipped: return "SKIPPED";
                default: return "FAILED";
            }
        }
    }
}
