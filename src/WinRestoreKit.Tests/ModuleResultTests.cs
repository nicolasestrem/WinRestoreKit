using WinRestoreKit;
using System;
using System.Collections.Generic;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class ModuleResultTests
    {
        private static StepResult Ok(string t = "key") => StepResult.Succeeded(t, "exported 1 key");
        private static StepResult Skip(string t = "key") => StepResult.Skipped(t, "not present on this system");
        private static StepResult Bad(string t = "key") => StepResult.Failed(t, "access denied");

        // --- Aggregation rule 1: no steps ---

        [Fact]
        public void Aggregate_NoSteps_IsSkipped()
        {
            ModuleResult r = ModuleResult.Aggregate(new StepResult[0]);
            Assert.Equal(ResultState.Skipped, r.State);
            Assert.False(string.IsNullOrWhiteSpace(r.Reason));
        }

        // --- Rule 2: any failure dominates ---

        [Fact]
        public void Aggregate_AnyFailed_IsFailed()
        {
            ModuleResult r = ModuleResult.Aggregate(new[] { Ok("a"), Bad("b"), Skip("c") });
            Assert.Equal(ResultState.Failed, r.State);
        }

        // The two failures carry DISTINCT reasons deliberately. With the same reason on both, this
        // could not tell "names the first failure" apart from "names any failure" - it passed
        // either way, which made the assertion worthless.
        [Fact]
        public void Aggregate_Failed_ReasonNamesCountAndFirstFailure()
        {
            ModuleResult r = ModuleResult.Aggregate(new[]
            {
                Ok("a"),
                StepResult.Failed("b", "access denied"),
                StepResult.Failed("c", "regedit exited with code 1")
            });

            Assert.Contains("2 of 3", r.Reason);
            Assert.Contains("access denied", r.Reason);
            Assert.DoesNotContain("regedit exited with code 1", r.Reason);
        }

        // --- Rule 3: all skipped stays skipped (the rule the inventory forced) ---

        [Fact]
        public void Aggregate_AllSkipped_IsSkippedNotSucceeded()
        {
            ModuleResult r = ModuleResult.Aggregate(new[] { Skip("a"), Skip("b") });
            Assert.Equal(ResultState.Skipped, r.State);
        }

        // --- Rule 4: a mix of success and legitimate absence is success ---

        [Fact]
        public void Aggregate_SucceededPlusSkipped_IsSucceeded()
        {
            ModuleResult r = ModuleResult.Aggregate(new[] { Ok("Personalize"), Skip("Accent") });
            Assert.Equal(ResultState.Succeeded, r.State);
        }

        [Fact]
        public void Aggregate_SucceededPlusSkipped_ReasonQuotesTheStepsOwnSkipReason()
        {
            ModuleResult r = ModuleResult.Aggregate(new[]
            {
                Ok("Personalize"),
                StepResult.Skipped("Accent", "not present on this system")
            });

            Assert.Contains("1 skipped", r.Reason);
            Assert.Contains("not present on this system", r.Reason);
        }

        // Aggregate serves BOTH directions and has no RunVerb, so it must never invent text about
        // the machine. On a restore the file was absent from the BACKUP - asserting "not present on
        // this system" would be a claim about the user's live hardware that the restore never
        // checked, and the step's own reason already says the right thing.
        [Fact]
        public void Aggregate_Restore_DoesNotClaimAnythingAboutTheLiveMachine()
        {
            ModuleResult r = ModuleResult.Aggregate(new[]
            {
                StepResult.Applied("Mouse.reg", "1 key"),
                StepResult.Skipped("Touchpad.reg", "nothing was backed up for this item")
            });

            Assert.Contains("nothing was backed up for this item", r.Reason);
            Assert.DoesNotContain("not present on this system", r.Reason);
            Assert.DoesNotContain("captured", r.Reason, StringComparison.OrdinalIgnoreCase);
        }

        // Rule 5 used to render the step's TARGET, which threw away every count the modules
        // compute: "copied 1204 file(s)" was reported as "Google Chrome".
        [Fact]
        public void Aggregate_SingleSucceededStep_KeepsItsReasonNotItsTarget()
        {
            ModuleResult r = ModuleResult.Aggregate(new[]
            {
                StepResult.Succeeded("Google Chrome", "copied 1204 file(s)")
            });

            Assert.Equal("copied 1204 file(s)", r.Reason);
        }

        [Fact]
        public void Aggregate_AllSucceeded_ReasonIsDirectionNeutral()
        {
            ModuleResult r = ModuleResult.Aggregate(new[] { Ok("a"), Ok("b") });

            Assert.DoesNotContain("captured", r.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("backed up", r.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("2", r.Reason);
        }

        // Rule 4 must not read as a bare ratio - "1 of 2" under a "Done" heading reads as
        // partial failure, which is the ambiguity that justified dropping a Partial state.
        [Fact]
        public void Aggregate_SucceededPlusSkipped_ReasonIsNotABareRatio()
        {
            ModuleResult r = ModuleResult.Aggregate(new[] { Ok("Personalize"), Skip("Accent") });
            Assert.DoesNotContain("1 of 2", r.Reason);
        }

        // --- Rule 5: all succeeded ---

        [Fact]
        public void Aggregate_AllSucceeded_IsSucceeded()
        {
            ModuleResult r = ModuleResult.Aggregate(new[] { Ok("a"), Ok("b") });
            Assert.Equal(ResultState.Succeeded, r.State);
        }

        [Fact]
        public void Aggregate_PreservesSteps()
        {
            ModuleResult r = ModuleResult.Aggregate(new[] { Ok("a"), Skip("b") });
            Assert.Equal(2, r.Steps.Count);
        }

        [Fact]
        public void Aggregate_NullSteps_IsSkippedNotCrash()
        {
            ModuleResult r = ModuleResult.Aggregate(null);
            Assert.Equal(ResultState.Skipped, r.State);
        }

        // --- Factory invariants ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void StepResult_SkippedWithoutReason_Throws(string reason)
            => Assert.Throws<ArgumentException>(() => StepResult.Skipped("t", reason));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void StepResult_FailedWithoutReason_Throws(string reason)
            => Assert.Throws<ArgumentException>(() => StepResult.Failed("t", reason));

        [Fact]
        public void StepResult_SucceededWithoutReason_Throws()
            => Assert.Throws<ArgumentException>(() => StepResult.Succeeded("t", ""));

        [Fact]
        public void StepResult_NullTarget_Throws()
            => Assert.Throws<ArgumentException>(() => StepResult.Succeeded(null, "fine"));

        // --- The restore-side wording rule ---

        [Fact]
        public void StepResult_Applied_IsSucceeded()
            => Assert.Equal(ResultState.Succeeded, StepResult.Applied("t", "1 key").State);

        [Fact]
        public void StepResult_Applied_ReasonSaysAppliedAndNeverVerified()
        {
            StepResult s = StepResult.Applied("Mouse.reg", "1 key");
            Assert.Contains("applied", s.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("verified", s.Reason, StringComparison.OrdinalIgnoreCase);
        }
    }
}
