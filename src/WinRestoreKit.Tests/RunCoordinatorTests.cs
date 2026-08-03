using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class RunCoordinatorTests
    {
        [Fact]
        public void SetRunning_ChangesStateAndRaisesOnlyForTransitions()
        {
            int notifications = 0;
            Action<bool> observer = _ => notifications++;
            RunCoordinator.RunningChanged += observer;

            try
            {
                RunCoordinator.SetRunning(false);
                RunCoordinator.SetRunning(true);
                RunCoordinator.SetRunning(true);
                RunCoordinator.SetRunning(false);

                Assert.False(RunCoordinator.IsRunning);
                Assert.Equal(2, notifications);
            }
            finally
            {
                RunCoordinator.RunningChanged -= observer;
                RunCoordinator.SetRunning(false);
            }
        }

        [Fact]
        public void TryStart_AdmitsOneConcurrentRunAndReportsTruthfulTransitions()
        {
            RunCoordinator.SetRunning(false);
            List<bool> notifications = new List<bool>();
            Action<bool> observer = running =>
            {
                lock (notifications)
                    notifications.Add(running);
            };
            RunCoordinator.RunningChanged += observer;

            try
            {
                int admitted = 0;
                Parallel.For(0, 16, _ =>
                {
                    if (RunCoordinator.TryStart())
                        Interlocked.Increment(ref admitted);
                });

                Assert.Equal(1, admitted);
                Assert.True(RunCoordinator.IsRunning);

                RunCoordinator.SetRunning(false);

                Assert.False(RunCoordinator.IsRunning);
                Assert.Equal(new[] { true, false }, notifications);
            }
            finally
            {
                RunCoordinator.RunningChanged -= observer;
                RunCoordinator.SetRunning(false);
            }
        }
    }
}
