using System;

namespace WinRestoreKit
{
    /// <summary>
    /// A process a module needs closed before its restore overwrites the files that process owns.
    /// </summary>
    /// <remarks>
    /// <see cref="NeedsConsent"/> is the whole decision. True means closing the process destroys
    /// work the user can see - an open browser with tabs - so it may only happen if they said yes.
    /// False is reserved for processes Windows brings straight back on its own, where a checkbox
    /// would ask for permission to do something the user cannot meaningfully decline and would only
    /// add to the dialog fatigue that makes the consented boxes stop being read.
    /// </remarks>
    public sealed class RestoreCloseRequirement
    {
        /// <summary>The process name as Process.GetProcessesByName takes it: no ".exe".</summary>
        public string ProcessName { get; }

        /// <summary>What the user calls it.</summary>
        public string DisplayName { get; }

        public bool NeedsConsent { get; }

        public RestoreCloseRequirement(string processName, string displayName, bool needsConsent)
        {
            if (string.IsNullOrWhiteSpace(processName))
                throw new ArgumentException("A close requirement must name its process.", nameof(processName));

            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("A close requirement must carry a display name.", nameof(displayName));

            ProcessName = processName;
            DisplayName = displayName;
            NeedsConsent = needsConsent;
        }
    }
}
