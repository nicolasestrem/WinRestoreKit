using WinRestoreKit;
using Conf;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class BackupManifestTests
    {
        private static ModuleResult Ok() => ModuleResult.Aggregate(new[] { StepResult.Succeeded("k", "exported k") });
        private static ModuleResult Skip() => ModuleResult.Aggregate(new[] { StepResult.Skipped("k", "not present on this system") });
        private static ModuleResult Bad() => ModuleResult.Aggregate(new[] { StepResult.Failed("k", "access denied") });

        private static List<BackupBase> Modules() => new List<BackupBase> { new DMouse(), new DTouchpad() };

        private static readonly DateTime When = new DateTime(2026, 8, 1, 14, 22, 3, DateTimeKind.Utc);

        private static string Compose(IReadOnlyList<BackupBase> modules, IReadOnlyList<ModuleResult> results)
            => BackupManifest.Compose(modules, results, When, "DESKTOP-NB01", "nicol", "Build 26100.4652", "0.0.1");

        // -------------------------------------------------------------------------------------
        // Compose
        // -------------------------------------------------------------------------------------

        [Fact]
        public void Compose_ProducesParseableJson()
        {
            string json = Compose(Modules(), new List<ModuleResult> { Ok(), Skip() });

            JObject root = JObject.Parse(json);

            Assert.NotNull(root["modules"]);
        }

        [Fact]
        public void Compose_WritesTheSchemaVersion()
        {
            JObject root = JObject.Parse(Compose(Modules(), new List<ModuleResult> { Ok(), Skip() }));

            Assert.Equal(1, root["manifest_version"].Value<int>());
        }

        [Fact]
        public void Compose_KeepsTheSchemaVersionSeparateFromTheAppVersion()
        {
            // The correction that matters: conflating them means a reader cannot tell "written by an
            // older build" from "written to an older shape".
            JObject root = JObject.Parse(Compose(Modules(), new List<ModuleResult> { Ok(), Skip() }));

            Assert.Equal(JTokenType.Integer, root["manifest_version"].Type);
            Assert.Equal("0.0.1", root["app_version"].Value<string>());
        }

        [Fact]
        public void Compose_RecordsTypeTitleStateAndReasonForEveryModule()
        {
            JArray modules = (JArray)JObject.Parse(Compose(Modules(), new List<ModuleResult> { Ok(), Bad() }))["modules"];

            Assert.Equal(2, modules.Count);

            Assert.Equal("DMouse", modules[0]["type"].Value<string>());
            Assert.Equal("Mouse", modules[0]["title"].Value<string>());
            Assert.Equal("succeeded", modules[0]["state"].Value<string>());
            Assert.Equal(Ok().Reason, modules[0]["reason"].Value<string>());

            Assert.Equal("DTouchpad", modules[1]["type"].Value<string>());
            Assert.Equal("failed", modules[1]["state"].Value<string>());

            // Verbatim, not re-worded. The reason is the only channel a failed row has, and
            // ModuleResult already folded it ("1 of 1 operations failed: access denied").
            Assert.Equal(Bad().Reason, modules[1]["reason"].Value<string>());
            Assert.Contains("access denied", modules[1]["reason"].Value<string>());
        }

        [Fact]
        public void Compose_UsesExactlyTheLowercaseStateLiterals()
        {
            // Pinned because the reader compares against them and a casing drift would silently
            // demote every row to unrecognised.
            JArray modules = (JArray)JObject.Parse(
                Compose(new List<BackupBase> { new DMouse(), new DTouchpad(), new DKeyboard() },
                        new List<ModuleResult> { Ok(), Skip(), Bad() }))["modules"];

            Assert.Equal("succeeded", modules[0]["state"].Value<string>());
            Assert.Equal("skipped", modules[1]["state"].Value<string>());
            Assert.Equal("failed", modules[2]["state"].Value<string>());
        }

        [Fact]
        public void Compose_MissingResultRow_IsUnknownRatherThanIndexingPastTheEnd()
        {
            // Same divergence BackupLog tolerates: a module that threw before producing a result.
            JArray modules = (JArray)JObject.Parse(Compose(Modules(), new List<ModuleResult> { Ok() }))["modules"];

            Assert.Equal(2, modules.Count);
            Assert.Equal("unknown", modules[1]["state"].Value<string>());
            Assert.Equal("no result was recorded", modules[1]["reason"].Value<string>());
        }

        [Fact]
        public void Compose_MissingResultRow_IsNotReportedAsSucceeded()
        {
            // The direction that matters. An absent verdict must never read as a good one.
            JArray modules = (JArray)JObject.Parse(Compose(Modules(), null))["modules"];

            Assert.All(modules, row => Assert.NotEqual("succeeded", row["state"].Value<string>()));
        }

        [Fact]
        public void Compose_RecordsTheEnvironmentFields()
        {
            JObject root = JObject.Parse(Compose(Modules(), new List<ModuleResult> { Ok(), Skip() }));

            Assert.Equal("DESKTOP-NB01", root["machine_name"].Value<string>());
            Assert.Equal("nicol", root["user_name"].Value<string>());
            Assert.Equal("Build 26100.4652", root["os_build"].Value<string>());
        }

        [Fact]
        public void Compose_WritesTheTimestampInRoundTripFormat()
        {
            // Asserted against the raw text, not through JObject.Parse: that helper would rewrite
            // the value into a DateTime and re-render it, hiding the very format under test.
            string json = Compose(Modules(), new List<ModuleResult> { Ok(), Skip() });

            Assert.Contains("\"created\": \"" + When.ToString("o") + "\"", json);
            Assert.Equal(When, DateTime.Parse(When.ToString("o"),
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime());
        }

        [Fact]
        public void Compose_NoModules_StillProducesAWellFormedDocument()
        {
            JObject root = JObject.Parse(Compose(new List<BackupBase>(), new List<ModuleResult>()));

            Assert.Equal(1, root["manifest_version"].Value<int>());
            Assert.Empty((JArray)root["modules"]);
        }

        // -------------------------------------------------------------------------------------
        // TryParse - the round trip
        // -------------------------------------------------------------------------------------

        [Fact]
        public void TryParse_ReadsBackWhatComposeWrote()
        {
            ManifestData data = BackupManifest.TryParse(Compose(Modules(), new List<ModuleResult> { Ok(), Bad() }));

            Assert.NotNull(data);
            Assert.Equal(1, data.ManifestVersion);
            Assert.Equal("0.0.1", data.AppVersion);
            Assert.Equal("DESKTOP-NB01", data.MachineName);
            Assert.Equal("nicol", data.UserName);
            Assert.Equal("Build 26100.4652", data.OsBuild);
            Assert.Equal(When, DateTime.Parse(data.Created,
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime());
        }

        [Fact]
        public void TryParse_ReturnsTheTimestampExactlyAsWritten()
        {
            // Regression. Newtonsoft's default DateParseHandling rewrote any date-shaped string
            // into a Date token, so this field came back NULL from a document that had it - the
            // reader lost the run time and nothing said so. The parser now reads scalars verbatim.
            ManifestData data = BackupManifest.TryParse(Compose(Modules(), new List<ModuleResult> { Ok(), Skip() }));

            Assert.Equal(When.ToString("o"), data.Created);
        }

        [Fact]
        public void TryParse_ReadsEveryModuleRow()
        {
            ManifestData data = BackupManifest.TryParse(Compose(Modules(), new List<ModuleResult> { Ok(), Bad() }));

            Assert.Equal(2, data.Modules.Count);
            Assert.Equal("DMouse", data.Modules[0].Type);
            Assert.Equal("Mouse", data.Modules[0].Title);
            Assert.Equal("succeeded", data.Modules[0].State);
            Assert.Equal(Ok().Reason, data.Modules[0].Reason);
            Assert.Equal("failed", data.Modules[1].State);
            Assert.Equal(Bad().Reason, data.Modules[1].Reason);
        }

        // -------------------------------------------------------------------------------------
        // TryParse - null on any defect. Each defect is its own fact because each is a separate
        // way the reader could otherwise be handed a document it would go on to trust.
        // -------------------------------------------------------------------------------------

        [Fact]
        public void TryParse_Null_ReturnsNull()
            => Assert.Null(BackupManifest.TryParse(null));

        [Fact]
        public void TryParse_Empty_ReturnsNull()
            => Assert.Null(BackupManifest.TryParse(""));

        [Fact]
        public void TryParse_Whitespace_ReturnsNull()
            => Assert.Null(BackupManifest.TryParse("   \r\n  "));

        [Fact]
        public void TryParse_Garbage_ReturnsNull()
            => Assert.Null(BackupManifest.TryParse("not json at all"));

        [Fact]
        public void TryParse_TruncatedJson_ReturnsNull()
        {
            // The interrupted-write shape, defended twice: the writer makes it unreachable and the
            // reader refuses it anyway.
            string json = Compose(Modules(), new List<ModuleResult> { Ok(), Skip() });

            Assert.Null(BackupManifest.TryParse(json.Substring(0, json.Length / 2)));
        }

        [Fact]
        public void TryParse_JsonArrayRatherThanObject_ReturnsNull()
            => Assert.Null(BackupManifest.TryParse("[1, 2, 3]"));

        [Fact]
        public void TryParse_MissingManifestVersion_ReturnsNull()
            => Assert.Null(BackupManifest.TryParse(@"{ ""modules"": [] }"));

        [Fact]
        public void TryParse_NonIntegerManifestVersion_ReturnsNull()
            => Assert.Null(BackupManifest.TryParse(@"{ ""manifest_version"": ""1"", ""modules"": [] }"));

        [Fact]
        public void TryParse_UnknownManifestVersion_ReturnsNull()
        {
            // A future writer's shape. Refusing it is the point: this build cannot know what a
            // version-2 row means, and reading it as version 1 is exactly the confident lie the
            // separate schema field exists to prevent.
            Assert.Null(BackupManifest.TryParse(@"{ ""manifest_version"": 2, ""modules"": [] }"));
        }

        [Fact]
        public void TryParse_MissingModules_ReturnsNull()
            => Assert.Null(BackupManifest.TryParse(@"{ ""manifest_version"": 1 }"));

        [Fact]
        public void TryParse_ModulesNotAnArray_ReturnsNull()
            => Assert.Null(BackupManifest.TryParse(@"{ ""manifest_version"": 1, ""modules"": ""two"" }"));

        [Fact]
        public void TryParse_ModuleRowNotAnObject_ReturnsNull()
            => Assert.Null(BackupManifest.TryParse(@"{ ""manifest_version"": 1, ""modules"": [ ""DMouse"" ] }"));

        [Theory]
        [InlineData("9999999999")]                      // valid JSON integer, too big for int
        [InlineData("-9999999999")]
        [InlineData("99999999999999999999999999999")]   // too big for long: Newtonsoft holds BigInteger
        public void TryParse_OutOfRangeManifestVersion_ReturnsNull(string version)
        {
            // Regression. The type check says "integer" but says nothing about magnitude, and
            // Value<int>() is Convert.ToInt32 underneath - it throws OverflowException, which is not
            // a JsonException and so escaped TryParse entirely. One corrupt file would have taken
            // down a caller that was promised it never has to catch anything.
            Assert.Null(BackupManifest.TryParse(
                @"{ ""manifest_version"": " + version + @", ""modules"": [] }"));
        }

        [Theory]
        [InlineData(@"{}")]
        [InlineData(@"{ ""state"": 42 }")]
        [InlineData(@"{ ""type"": ""DMouse"", ""title"": ""Mouse"", ""state"": ""succeeded"" }")]
        [InlineData(@"{ ""type"": ""DMouse"", ""title"": ""Mouse"", ""state"": null, ""reason"": ""x"" }")]
        public void TryParse_IncompleteModuleRow_ReturnsNull(string row)
        {
            // Regression. Such a row used to become a ManifestModule of nulls inside an otherwise
            // trusted document - and a reader counting items and matching State against "failed"
            // would count it as an item that did NOT fail. A green verdict derived from a row that
            // says nothing is the exact failure this type exists to prevent, so one bad row means
            // the whole file reads as unknown.
            Assert.Null(BackupManifest.TryParse(
                @"{ ""manifest_version"": 1, ""modules"": [ " + row + @" ] }"));
        }

        [Fact]
        public void TryParse_CompleteModuleRow_IsAccepted()
        {
            // The other side of the rule above: a row carrying all four string fields is fine, and
            // an empty reason is a value rather than an absence.
            ManifestData data = BackupManifest.TryParse(
                @"{ ""manifest_version"": 1, ""modules"": [
                     { ""type"": ""DMouse"", ""title"": ""Mouse"", ""state"": ""succeeded"", ""reason"": """" } ] }");

            Assert.NotNull(data);
            Assert.Equal("DMouse", Assert.Single(data.Modules).Type);
            Assert.Equal("", data.Modules[0].Reason);
        }

        [Theory]
        [InlineData("success")]     // plausible typo
        [InlineData("Succeeded")]   // wrong case
        [InlineData("partial")]     // a state the engine deliberately does not have
        [InlineData("")]
        public void TryParse_UnrecognisedState_ReturnsNull(string state)
        {
            // state has a closed domain in this schema version. A value outside it reaches a reader
            // that compares against "failed", matches nothing, and gets counted as an item that did
            // not fail - inferred green from a row nobody can interpret. A genuinely new literal is
            // a new shape and belongs to a new manifest_version.
            Assert.Null(BackupManifest.TryParse(
                @"{ ""manifest_version"": 1, ""modules"": [ { ""type"": ""DMouse"", ""title"": ""Mouse"",
                     ""state"": """ + state + @""", ""reason"": ""x"" } ] }"));
        }

        [Theory]
        [InlineData("succeeded")]
        [InlineData("skipped")]
        [InlineData("failed")]
        [InlineData("unknown")]
        public void TryParse_EveryStateComposeCanWrite_IsAccepted(string state)
        {
            // The closed domain must contain everything Compose emits - including "unknown", which
            // it writes for a module that produced no result. Rejecting that would make the very
            // documents this app writes unreadable.
            ManifestData data = BackupManifest.TryParse(
                @"{ ""manifest_version"": 1, ""modules"": [ { ""type"": ""DMouse"", ""title"": ""Mouse"",
                     ""state"": """ + state + @""", ""reason"": ""x"" } ] }");

            Assert.Equal(state, Assert.Single(data.Modules).State);
        }

        [Fact]
        public void TryParse_AcceptsEveryDocumentComposeProduces()
        {
            // The round trip that stops the two halves drifting apart: whatever Compose writes,
            // including the ragged-list unknown row, TryParse must accept.
            string json = BackupManifest.Compose(
                new List<BackupBase> { new DMouse(), new DTouchpad(), new DKeyboard() },
                new List<ModuleResult> { Ok(), Skip() },   // third module has no result -> unknown
                When, "m", "u", "b", "0.0.1");

            ManifestData data = BackupManifest.TryParse(json);

            Assert.NotNull(data);
            Assert.Equal(3, data.Modules.Count);
            Assert.Equal("unknown", data.Modules[2].State);
        }

        [Fact]
        public void TryParse_AbsentProvenanceFields_StillParses()
        {
            // Missing context costs the reader a line; missing structure costs it the count. Only
            // the second is fatal.
            ManifestData data = BackupManifest.TryParse(@"{ ""manifest_version"": 1, ""modules"": [] }");

            Assert.NotNull(data);
            Assert.Null(data.MachineName);
            Assert.Null(data.AppVersion);
            Assert.Empty(data.Modules);
        }
    }
}
