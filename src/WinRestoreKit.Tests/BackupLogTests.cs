using WinRestoreKit;
using Conf;
using System.Collections.Generic;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class BackupLogTests
    {
        private static ModuleResult Ok() => ModuleResult.Aggregate(new[] { StepResult.Succeeded("k", "exported k") });
        private static ModuleResult Skip() => ModuleResult.Aggregate(new[] { StepResult.Skipped("k", "not present on this system") });
        private static ModuleResult Bad() => ModuleResult.Aggregate(new[] { StepResult.Failed("k", "access denied") });

        private static List<BackupBase> Modules() => new List<BackupBase> { new DMouse(), new DTouchpad() };

        [Fact]
        public void Compose_StartsWithTheVersionHeader()
        {
            string text = BackupLog.Compose(Modules(), new List<ModuleResult> { Ok(), Skip() }, "2026-07-20");
            Assert.StartsWith(BackupLog.VersionHeader, text);
        }

        [Fact]
        public void Compose_RecordsTheOutcomeNotJustTheSelection()
        {
            string text = BackupLog.Compose(Modules(), new List<ModuleResult> { Ok(), Bad() }, "2026-07-20");

            Assert.Contains("Mouse", text);
            Assert.Contains("access denied", text);
        }

        [Fact]
        public void Compose_DistinguishesSkippedFromFailed()
        {
            string text = BackupLog.Compose(Modules(), new List<ModuleResult> { Ok(), Skip() }, "2026-07-20");

            Assert.Contains("SKIPPED", text);
            Assert.DoesNotContain("FAILED", text);
        }

        [Fact]
        public void Compose_NamesEveryModule()
        {
            string text = BackupLog.Compose(Modules(), new List<ModuleResult> { Ok(), Skip() }, "2026-07-20");

            Assert.Contains("Mouse", text);
            Assert.Contains("Touchpad", text);
        }

        [Fact]
        public void Compose_MismatchedCounts_DoesNotThrow()
        {
            string text = BackupLog.Compose(Modules(), new List<ModuleResult> { Ok() }, "2026-07-20");
            Assert.Contains("Mouse", text);
        }
    }
}
