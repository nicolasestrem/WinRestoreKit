using System;

namespace WinRestoreKit
{
    internal static class RunCoordinator
    {
        private static readonly object sync = new object();
        private static bool isRunning;

        internal static bool IsRunning
        {
            get
            {
                lock (sync)
                    return isRunning;
            }
        }

        internal static event Action<bool> RunningChanged;

        internal static bool TryStart()
        {
            lock (sync)
            {
                if (isRunning)
                    return false;

                isRunning = true;
                RunningChanged?.Invoke(true);
                return true;
            }
        }

        internal static void SetRunning(bool running)
        {
            lock (sync)
            {
                if (isRunning == running)
                    return;

                isRunning = running;
                RunningChanged?.Invoke(running);
            }
        }
    }
}
