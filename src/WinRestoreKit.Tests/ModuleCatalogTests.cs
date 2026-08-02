using WinRestoreKit;
using Conf;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace WinRestoreKit.Tests
{
    // ModuleCatalog is the single registration point for backup modules (Phase 4 PR 6). These tests
    // pin it against the assembly itself: a module added to Core but forgotten in the catalog turns
    // red by name, and the same reflection sweep RestoreDeclarationTests uses confirms the count.
    public class ModuleCatalogTests
    {
        // typeof(BackupBase).Assembly, not the test assembly: the fakes defined in other tests are
        // deliberately unregistered, and enumerating them would make the invariant unassertable.
        private static IEnumerable<Type> ShippedModuleTypes()
            => typeof(BackupBase).Assembly
                .GetTypes()
                .Where(t => typeof(BackupBase).IsAssignableFrom(t) && !t.IsAbstract);

        [Fact]
        public void CreateAll_HasExpectedCount()
        {
            // The same 29 RestoreDeclarationTests.EveryShippedModuleIsEnumeratedAndConstructible
            // counts. If a module is added, both this and that test move together.
            Assert.Equal(29, ModuleCatalog.CreateAll().Count);
        }

        [Fact]
        public void CreateAll_RegistersExactlyTheShippedConcreteModules()
        {
            HashSet<Type> registered = ModuleCatalog.CreateAll()
                .Select(r => r.Module.GetType())
                .ToHashSet();

            HashSet<Type> shipped = ShippedModuleTypes().ToHashSet();

            // Two directions so a failure names the offending module type directly.
            Assert.Empty(shipped.Except(registered));   // in the assembly but not the catalog
            Assert.Empty(registered.Except(shipped));   // in the catalog but not the assembly
        }

        [Fact]
        public void CreateAll_ListsEachTypeOnce()
        {
            List<string> typeNames = ModuleCatalog.CreateAll()
                .Select(r => r.Module.GetType().FullName)
                .ToList();

            Assert.Equal(typeNames.Count, typeNames.Distinct().Count());
        }

        [Fact]
        public void CreateAll_EveryRegistrationCarriesACategory()
        {
            Assert.All(ModuleCatalog.CreateAll(),
                r => Assert.False(string.IsNullOrWhiteSpace(r.Category)));
        }
    }
}
