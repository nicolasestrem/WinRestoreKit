extern alias Application;

using System.Linq;
using ApplicationWinRestoreKit = Application::WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class ApplicationBoundaryTests
    {
        [Fact]
        public void ApplicationRunControl_IsAvailableWithoutConstructingAUiFrameworkObject()
        {
            using (ApplicationWinRestoreKit.RunControl control =
                   new ApplicationWinRestoreKit.RunControl())
            {
                Assert.False(control.IsPaused);
                Assert.False(control.IsCancellationRequested);
            }
        }

        [Fact]
        public void ApplicationAssembly_HasNoWinFormsOrWpfAssemblyReference()
        {
            string[] references = typeof(ApplicationWinRestoreKit.RunControl).Assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ToArray();

            Assert.DoesNotContain("System.Windows.Forms", references);
            Assert.DoesNotContain("PresentationFramework", references);
            Assert.DoesNotContain("WindowsBase", references);
        }
    }
}
