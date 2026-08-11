using Conf;
using DataHelper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WinRestoreKit;
using WinRestoreKit.Wpf.ViewModels;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class BackupWorkspaceViewModelTests
    {
        [Fact]
        public async Task StartAsync_SelectedScope_RequestsItsConcreteModulesAndFastCompression()
        {
            BackupRunRequest request = null;
            var vm = new BackupWorkspaceViewModel(r =>
            {
                request = r;
                return Task.CompletedTask;
            }, @"C:\snapshots");
            foreach (BackupScopeItemViewModel scope in vm.Scopes)
                scope.IsSelected = false;
            vm.Scopes.Single(scope => scope.Name == "Explorer & shell").IsSelected = true;

            await vm.StartAsync();

            Assert.Equal(SnapshotCompression.Fast, request.Compression);
            Assert.Equal(new[]
            {
                typeof(WPersonalization), typeof(WVisualEffects), typeof(WTaskbar),
                typeof(WThemes), typeof(APinnedApps)
            }, request.Modules.Select(module => module.GetType()));
        }

        [Fact]
        public async Task StartAsync_EmptySelectionOrDestination_ShowsValidationAndDoesNotRequestRun()
        {
            int starts = 0;
            var vm = new BackupWorkspaceViewModel(_ =>
            {
                starts++;
                return Task.CompletedTask;
            }, string.Empty);
            foreach (BackupScopeItemViewModel scope in vm.Scopes)
                scope.IsSelected = false;

            await vm.StartAsync();

            Assert.Equal(0, starts);
            Assert.Equal("Select at least one scope with supported items.", vm.ValidationMessage);
        }

        [Fact]
        public async Task StartAsync_BlankDestinationWithSelection_ShowsDestinationValidation()
        {
            int starts = 0;
            var vm = new BackupWorkspaceViewModel(_ =>
            {
                starts++;
                return Task.CompletedTask;
            }, " ");
            foreach (BackupScopeItemViewModel scope in vm.Scopes)
                scope.IsSelected = false;
            vm.Scopes.Single(scope => scope.Name == "Explorer & shell").IsSelected = true;

            await vm.StartAsync();

            Assert.Equal(0, starts);
            Assert.Equal("Choose a destination folder before capturing.", vm.ValidationMessage);
        }

        [Fact]
        public void UnfilteredEnvironmentScope_StartsUnselectedAndExposesItsCaution()
        {
            var vm = new BackupWorkspaceViewModel(_ => Task.CompletedTask, @"C:\snapshots");

            BackupScopeItemViewModel environment = vm.Scopes.Single(scope =>
                scope.Name == "Environment variables (unfiltered)");

            Assert.False(environment.IsSelected);
            Assert.True(environment.RequiresExplicitOptIn);
            Assert.True(environment.HasCaution);
            Assert.False(string.IsNullOrWhiteSpace(environment.CautionNote));
        }

        [Fact]
        public async Task StartAsync_CompressionOptions_MapDirectlyToCoreValues()
        {
            var seen = new List<SnapshotCompression>();
            var vm = new BackupWorkspaceViewModel(request =>
            {
                seen.Add(request.Compression);
                return Task.CompletedTask;
            }, @"C:\snapshots");
            foreach (BackupScopeItemViewModel scope in vm.Scopes)
                scope.IsSelected = false;
            vm.Scopes.Single(scope => scope.Name == "Explorer & shell").IsSelected = true;

            foreach (SnapshotCompression compression in new[]
                     { SnapshotCompression.None, SnapshotCompression.Fast, SnapshotCompression.Max })
            {
                vm.Compression = compression;
                await vm.StartAsync();
            }

            Assert.Equal(new[] { SnapshotCompression.None, SnapshotCompression.Fast, SnapshotCompression.Max },
                vm.CompressionOptions);
            Assert.Equal(vm.CompressionOptions, seen);
        }

        [Fact]
        public void PresetCommands_TransformApplicationLiteralTypeListsToWholeScopeSelection()
        {
            var vm = new BackupWorkspaceViewModel(_ => Task.CompletedTask, @"C:\snapshots");

            vm.DeveloperMachineCommand.Execute(null);

            Assert.Equal(vm.Scopes.Where(scope => scope.Modules.Any(module =>
                    BackupPresets.DeveloperMachine.Contains(module.GetType().Name)))
                    .Select(scope => scope.Name)
                    .OrderBy(name => name),
                vm.Scopes.Where(scope => scope.IsSelected)
                    .Select(scope => scope.Name)
                    .OrderBy(name => name));

            vm.MinimalPrivacySafeCommand.Execute(null);

            Assert.Equal(vm.Scopes.Where(scope => !scope.Modules.Any(module =>
                    BackupPresets.MinimalPrivacySafeExclusions.Contains(module.GetType().Name)))
                    .Select(scope => scope.Name)
                    .OrderBy(name => name),
                vm.Scopes.Where(scope => scope.IsSelected)
                    .Select(scope => scope.Name)
                    .OrderBy(name => name));
        }

        [Fact]
        public async Task StartAsync_SelectAll_FlattensDistinctModulesInCatalogOrder()
        {
            BackupRunRequest request = null;
            var vm = new BackupWorkspaceViewModel(r =>
            {
                request = r;
                return Task.CompletedTask;
            }, @"C:\snapshots");

            vm.SelectAllCommand.Execute(null);
            await vm.StartAsync();

            Type[] expected = vm.Scopes.Where(scope => scope.IsSelected)
                .SelectMany(scope => scope.Modules)
                .Select(module => module.GetType())
                .Distinct()
                .ToArray();
            Assert.Equal(expected, request.Modules.Select(module => module.GetType()));
        }

    }
}
