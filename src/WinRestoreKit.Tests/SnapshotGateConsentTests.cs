using System.Collections.Generic;
using WinRestoreKit;
using Xunit;

namespace WinRestoreKit.Tests
{
    /// <summary>
    /// Regression coverage for the second consent required when a pre-restore snapshot cannot undo
    /// every write the selected restore may make.
    /// </summary>
    public class SnapshotGateConsentTests
    {
        private static ModuleResult Captured()
            => ModuleResult.Aggregate(new[] { StepResult.Succeeded("test", "captured") });

        private static ModuleResult NotCaptured()
            => ModuleResult.Aggregate(new[] { StepResult.Skipped("test", "live state was absent") });

        [Fact]
        public void Evaluate_PartiallyCapturedSnapshot_RequiresOverrideConsentBeforeRestore()
        {
            SnapshotDecision decision = SnapshotGate.Evaluate(new List<ModuleOutcome>
            {
                new ModuleOutcome("Captured module", Captured()),
                new ModuleOutcome("Absent live module", NotCaptured())
            });

            Assert.Equal(SnapshotVerdict.PartiallyCaptured, decision.Verdict);
            Assert.True(decision.RequiresOverride);
            Assert.Contains("cannot be undone", decision.Describe());
            Assert.Contains("Absent live module", decision.Describe());
        }

        [Fact]
        public void Evaluate_EmptyButConsideredSnapshot_RequiresOverrideConsentBeforeRestore()
        {
            SnapshotDecision decision = SnapshotGate.Evaluate(new List<ModuleOutcome>
            {
                new ModuleOutcome("Absent live module", NotCaptured())
            });

            Assert.Equal(SnapshotVerdict.NothingCaptured, decision.Verdict);
            Assert.True(decision.RequiresOverride);
            Assert.Contains("captured nothing", decision.Describe());
            Assert.Contains("Absent live module", decision.Describe());
        }
    }
}
