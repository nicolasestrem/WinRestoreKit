using System.Threading.Tasks;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class RunControlTests
    {
        [Fact]
        public void PauseAndResume_ChangesState()
        {
            RunControl control = new RunControl();

            Assert.False(control.IsPaused);
            Assert.False(control.IsCancellationRequested);

            control.Pause();

            Assert.True(control.IsPaused);
            Assert.False(control.IsCancellationRequested);

            control.Resume();

            Assert.False(control.IsPaused);
            Assert.False(control.IsCancellationRequested);
        }

        [Fact]
        public async Task WaitIfPaused_WaitsUntilResume()
        {
            RunControl control = new RunControl();
            control.Pause();

            Task<bool> waiting = Task.Run(control.WaitIfPaused);
            await Task.Delay(100);

            Assert.False(waiting.IsCompleted);

            control.Resume();

            Assert.True(await waiting);
        }

        [Fact]
        public async Task WaitIfPausedAsync_ResumesWithoutBlockingCaller()
        {
            RunControl control = new RunControl();
            control.Pause();

            Task<bool> waiting = control.WaitIfPausedAsync();

            Assert.False(waiting.IsCompleted);

            control.Resume();

            Assert.True(await waiting);
        }

        [Fact]
        public async Task RepeatedPause_DoesNotStrandExistingWaiters()
        {
            RunControl control = new RunControl();
            control.Pause();
            Task<bool> waiting = control.WaitIfPausedAsync();

            control.Pause();
            control.Resume();

            Assert.True(await waiting);
        }

        [Fact]
        public async Task RequestCancellation_ReleasesPausedWaitAndPreventsFurtherWork()
        {
            RunControl control = new RunControl();
            control.Pause();

            Task<bool> waiting = Task.Run(control.WaitIfPaused);
            await Task.Delay(100);

            control.RequestCancellation();

            Assert.True(control.IsCancellationRequested);
            Assert.False(control.IsPaused);
            Assert.False(await waiting);
            Assert.False(control.WaitIfPaused());
        }
    }
}
