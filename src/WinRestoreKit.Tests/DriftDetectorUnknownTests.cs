using System;
using System.Collections.Generic;
using System.Reflection;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    public sealed class DriftDetectorUnknownTests
    {
        [Fact]
        public void Detect_ConfirmedDrift_ReportsOnlyTheDriftedModule()
        {
            Detection detection = Detect(new ReportedDriftModule("Changed", true));

            DriftItem item = Assert.Single(detection.Drifted);
            Assert.Equal("Changed", item.Name);
            Assert.Empty(detection.Unavailable);
        }

        [Fact]
        public void Detect_ConfirmedNoDrift_ReportsNeitherDriftNorUnavailable()
        {
            Detection detection = Detect(new ReportedDriftModule("Unchanged", false));

            Assert.Empty(detection.Drifted);
            Assert.Empty(detection.Unavailable);
        }

        [Fact]
        public void Detect_UnableToCompare_ReportsTheModuleAsUnavailable()
        {
            Detection detection = Detect(new ReportedDriftModule("Cannot compare", null));

            Assert.Empty(detection.Drifted);
            DriftItem item = Assert.Single(detection.Unavailable);
            Assert.Equal("Cannot compare", item.Name);
        }

        [Fact]
        public void Detect_MixedResults_PreservesEveryDistinctOutcome()
        {
            Detection detection = Detect(
                new ReportedDriftModule("Changed", true),
                new ReportedDriftModule("Unchanged", false),
                new ReportedDriftModule("Cannot compare", null));

            Assert.Collection(detection.Drifted, item => Assert.Equal("Changed", item.Name));
            Assert.Collection(detection.Unavailable, item => Assert.Equal("Cannot compare", item.Name));
        }

        private static Detection Detect(params BackupBase[] modules)
        {
            MethodInfo detect = typeof(DriftDetector).GetMethod(
                "Detect",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(IReadOnlyList<BackupBase>), typeof(IReadOnlyList<DriftItem>).MakeByRefType() },
                null);

            Assert.NotNull(detect);
            object[] arguments = { "snapshot", modules, null };
            IReadOnlyList<DriftItem> drifted = (IReadOnlyList<DriftItem>)detect.Invoke(null, arguments);
            return new Detection(drifted, (IReadOnlyList<DriftItem>)arguments[2]);
        }

        private sealed class Detection
        {
            internal Detection(IReadOnlyList<DriftItem> drifted, IReadOnlyList<DriftItem> unavailable)
            {
                Drifted = drifted;
                Unavailable = unavailable;
            }

            internal IReadOnlyList<DriftItem> Drifted { get; }

            internal IReadOnlyList<DriftItem> Unavailable { get; }
        }

        private sealed class ReportedDriftModule : BackupBase
        {
            private readonly bool? _drift;

            internal ReportedDriftModule(string title, bool? drift)
            {
                Title = title;
                _drift = drift;
            }

            public override bool? HasDriftedFrom(string backupPath) => _drift;
        }
    }
}
