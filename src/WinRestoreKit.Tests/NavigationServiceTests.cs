using System.Windows.Forms;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class NavigationServiceTests
    {
        [Fact]
        public void ShowPushAndPop_RendersExpectedUserControl()
        {
            using (Panel host = new Panel())
            using (UserControl root = new UserControl())
            using (UserControl detail = new UserControl())
            {
                NavigationService navigation = new NavigationService(host)
                {
                    Root = root
                };

                navigation.Show(root);
                navigation.Push(detail);
                navigation.Pop();

                Assert.Same(root, navigation.Current);
                Assert.Single(host.Controls);
                Assert.Same(root, host.Controls[0]);
            }
        }
    }
}
