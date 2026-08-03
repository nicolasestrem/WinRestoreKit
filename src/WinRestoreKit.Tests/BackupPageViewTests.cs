using System;
using Conf;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Views;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class BackupPageViewTests
    {
        [Fact]
        public void Capture_RequestsSelectedConcreteScopeModulesWithoutOwningRunUi()
        {
            using (BackupPageView view = new BackupPageView())
            {
                Assert.False(view is IRunUi);

                foreach (CustomCheckbox scope in Descendants(view).OfType<CustomCheckbox>())
                    scope.Checked = false;

                CustomCheckbox explorerScope = view.Controls
                    .Find("scopeToggle1", true)
                    .OfType<CustomCheckbox>()
                    .Single();
                explorerScope.Checked = true;

                IReadOnlyList<BackupBase> requestedModules = null;
                view.StartBackupRequested = (modules, snapshotName, compression, destination) =>
                    requestedModules = modules;

                view.Controls.Find("captureButton", true).OfType<Button>().Single().PerformClick();

                Assert.NotNull(requestedModules);
                Assert.Equal(
                    new[]
                    {
                        typeof(WPersonalization),
                        typeof(WVisualEffects),
                        typeof(WTaskbar),
                        typeof(WThemes),
                        typeof(APinnedApps)
                    },
                    requestedModules.Select(module => module.GetType()));
            }
        }

        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;

                foreach (Control descendant in Descendants(child))
                    yield return descendant;
            }
        }
    }
}
