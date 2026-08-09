using Conf;
using System.Collections.Generic;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class BackupModuleCatalogTests
    {
        [Fact]
        public void CreateAll_PreservesCoreCatalogOrderCategoryAndTitle()
        {
            IReadOnlyList<BackupModuleRegistration> actual = BackupModuleCatalog.CreateAll();
            IReadOnlyList<ModuleRegistration> core = ModuleCatalog.CreateAll();

            Assert.Equal(core.Count, actual.Count);
            for (int index = 0; index < core.Count; index++)
            {
                Assert.Same(core[index].Module.GetType(), actual[index].Module.GetType());
                Assert.Equal(core[index].Category, actual[index].Category);
                Assert.Equal(core[index].Module.Title, actual[index].Title);
            }
        }
    }
}
