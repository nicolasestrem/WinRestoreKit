using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;

namespace WinRestoreKit
{
    /// <summary>
    /// Composes and parses backup_manifest.json - the machine-readable record of a backup run.
    /// </summary>
    /// <remarks>
    /// backup_log.txt is written for a human and is explicitly free to change shape (see BackupLog).
    /// Home and History cannot make a status claim like "24 items, 1 failed" by parsing prose, because
    /// the failure mode of a text parser is a confidently wrong verdict rather than a visible error.
    /// This file exists so the reader has something it is allowed to trust.
    ///
    /// Two rules are load-bearing and neither is stylistic:
    ///
    /// 1. <see cref="Version"/> is the SCHEMA version and is not the app version, which is recorded
    ///    separately for provenance only. App versions change on every release and mostly do not
    ///    change the shape; conflating them means a reader cannot tell "written by an older build"
    ///    from "written to an older shape".
    /// 2. <see cref="TryParse"/> returns null on ANY defect. Callers render null as unknown -
    ///    "details unavailable" - and never as an inferred success. Every backup taken before this
    ///    file existed has no manifest, and a dashboard that guesses at those is the cry-wolf failure
    ///    running in the dangerous direction. Best-effort parsing may label something approximate; it
    ///    may not produce a verdict.
    ///
    /// The composer takes the same (modules, results) pair BackupLog.Compose takes, and is tolerant of
    /// the same ragged input for the same reason: a module that threw before producing a result leaves
    /// the two lists at different lengths, and reporting that beats indexing past the end.
    /// </remarks>
    internal static class BackupManifest
    {
        /// <summary>
        /// Schema version of the document, NOT the app version. Bump only when the shape changes.
        /// </summary>
        internal const int Version = 1;

        internal const string FileName = "backup_manifest.json";

        internal const string StateSucceeded = "succeeded";
        internal const string StateSkipped = "skipped";
        internal const string StateFailed = "failed";

        /// <summary>
        /// The state written when a module produced no result at all.
        /// </summary>
        internal const string StateUnknown = "unknown";

        internal const string NoResultReason = "no result was recorded";

        internal static string Compose(IReadOnlyList<BackupBase> modules,
                                       IReadOnlyList<ModuleResult> results,
                                       DateTime when,
                                       string machineName,
                                       string userName,
                                       string osBuild,
                                       string appVersion,
                                       string snapshotName = null,
                                       SnapshotCompression compression = SnapshotCompression.None,
                                       string payloadFile = null)
        {
            JArray moduleRows = new JArray();

            int count = modules == null ? 0 : modules.Count;

            for (int i = 0; i < count; i++)
            {
                BackupBase module = modules[i];

                if (module == null)
                    continue;

                // Same divergence tolerance as BackupLog.Compose: a module that threw before
                // producing a result leaves the lists ragged.
                ModuleResult result = (results != null && i < results.Count) ? results[i] : null;

                JObject row = new JObject
                {
                    ["type"] = module.GetType().Name,
                    ["title"] = module.Title,
                    ["state"] = result == null ? StateUnknown : Label(result.State),
                    ["reason"] = result == null ? NoResultReason : result.Reason
                };

                moduleRows.Add(row);
            }

            JObject root = new JObject
            {
                ["manifest_version"] = Version,
                ["app_version"] = appVersion,
                ["created"] = when.ToString("o"),
                ["machine_name"] = machineName,
                ["user_name"] = userName,
                ["os_build"] = osBuild,
                ["modules"] = moduleRows
            };

            if (!string.IsNullOrEmpty(snapshotName))
                root["snapshot_name"] = snapshotName;

            if (!string.IsNullOrEmpty(payloadFile))
            {
                root["compression"] = compression.ToString().ToLowerInvariant();
                root["payload_file"] = payloadFile;
            }

            return root.ToString(Formatting.Indented);
        }

        /// <summary>
        /// Reads a manifest, returning null if it cannot be trusted for any reason at all.
        /// </summary>
        /// <remarks>
        /// Null covers: not JSON, not an object, no manifest_version, a manifest_version that is not
        /// an integer, a manifest_version this build does not know, and no modules array. There is
        /// deliberately no partial-credit return - a caller handed half a document would have to
        /// decide what the missing half meant, and the whole point of the type is that it never has
        /// to guess.
        /// </remarks>
        internal static ManifestData TryParse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            JObject root = ReadObject(json);

            if (root == null)
                return null;

            JToken versionToken = root["manifest_version"];

            if (versionToken == null || versionToken.Type != JTokenType.Integer)
                return null;

            // Read the underlying value rather than converting. Newtonsoft stores a JSON integer as
            // a long, or as a BigInteger when it does not fit one - and Value<int>() is
            // Convert.ToInt32 underneath, which throws OverflowException (NOT a JsonException, so it
            // escapes this method entirely) for "manifest_version": 9999999999. Requiring the
            // underlying value to be a long rejects both oversized shapes with no conversion at all,
            // which is what keeps the never-throws contract true rather than nearly true.
            if (!(versionToken is JValue versionValue) || !(versionValue.Value is long version))
                return null;

            if (version != Version)
                return null;

            if (!(root["modules"] is JArray moduleRows))
                return null;

            List<ManifestModule> modules = new List<ManifestModule>();

            foreach (JToken rowToken in moduleRows)
            {
                if (!(rowToken is JObject row))
                    return null;

                string type = Text(row["type"]);
                string title = Text(row["title"]);
                string state = Text(row["state"]);
                string reason = Text(row["reason"]);

                // Every row field must be present and a string. A row like {} or {"state":42}
                // otherwise becomes a ManifestModule of nulls, and a reader counting items and
                // matching State against "failed" would count it as an item that did not fail -
                // a green verdict derived from a document that says nothing. Same no-partial-credit
                // rule as the document as a whole: one malformed row means the file is not
                // trustworthy, so the whole thing reads as unknown.
                if (type == null || title == null || state == null || reason == null)
                    return null;

                // state has a CLOSED domain in this schema version, so a value outside it is a
                // malformed row rather than a value to pass along. A typo like "success" would
                // otherwise arrive at a reader that compares against "failed", match nothing, and
                // be counted as an item that did not fail - the same inferred green a row of nulls
                // produced. A genuinely new literal is a new shape and belongs to a new
                // manifest_version, which is what that field is for.
                if (!IsKnownState(state))
                    return null;

                modules.Add(new ManifestModule(type, title, state, reason));
            }

            return new ManifestData(
                (int)version,
                Text(root["app_version"]),
                Text(root["created"]),
                Text(root["machine_name"]),
                Text(root["user_name"]),
                Text(root["os_build"]),
                Text(root["snapshot_name"]),
                Text(root["compression"]),
                Text(root["payload_file"]),
                modules);
        }

        /// <summary>
        /// Parses the document into an object, leaving every scalar exactly as it was written.
        /// </summary>
        /// <remarks>
        /// JObject.Parse cannot be used here, and the reason is not a preference. Its default
        /// DateParseHandling rewrites any string that LOOKS like a date into a Date token, so
        /// "created" came back as JTokenType.Date rather than String - which made <see cref="Text"/>
        /// return null for the one field whose whole job is to say when the run happened, and did it
        /// silently. Turning the behaviour off keeps the manifest what it claims to be: a record of
        /// what was written, read back verbatim. It also stops the same trap firing on any future
        /// date-shaped value, including a machine name or a reason string that happens to parse.
        ///
        /// The trailing-token check restores what JObject.Parse gave for free: JToken.ReadFrom stops
        /// at the end of the first value, so "{...} garbage" would otherwise be accepted.
        /// </remarks>
        private static JObject ReadObject(string json)
        {
            try
            {
                using (StringReader text = new StringReader(json))
                using (JsonTextReader reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None })
                {
                    JToken token = JToken.ReadFrom(reader);

                    if (reader.Read())
                        return null;

                    return token as JObject;
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// A string field, or null when absent or not a string.
        /// </summary>
        /// <remarks>
        /// Absent provenance fields are tolerated where absent STRUCTURE is not: machine_name being
        /// missing costs the reader a line of context, whereas a missing modules array would cost it
        /// the ability to count anything.
        /// </remarks>
        private static string Text(JToken token)
            => token != null && token.Type == JTokenType.String ? token.Value<string>() : null;

        /// <summary>
        /// The complete set of state literals a version-1 manifest may carry.
        /// </summary>
        /// <remarks>
        /// Ordinal and case-sensitive. Compose writes these exact lowercase strings, so a differing
        /// case is a differing value written by something that is not this app.
        /// </remarks>
        private static bool IsKnownState(string state)
            => state == StateSucceeded
                || state == StateSkipped
                || state == StateFailed
                || state == StateUnknown;

        private static string Label(ResultState state)
        {
            switch (state)
            {
                case ResultState.Succeeded: return StateSucceeded;
                case ResultState.Skipped: return StateSkipped;
                default: return StateFailed;
            }
        }
    }

    /// <summary>
    /// A parsed manifest. Only ever produced by <see cref="BackupManifest.TryParse"/>, which returns
    /// null rather than an instance carrying doubt - so holding one of these means the document was
    /// well-formed and of a known schema version.
    /// </summary>
    internal sealed class ManifestData
    {
        internal ManifestData(int manifestVersion, string appVersion, string created, string machineName,
                              string userName, string osBuild, IReadOnlyList<ManifestModule> modules)
            : this(manifestVersion, appVersion, created, machineName, userName, osBuild, null, null, null, modules)
        {
        }

        internal ManifestData(int manifestVersion, string appVersion, string created, string machineName,
                              string userName, string osBuild, string snapshotName,
                              IReadOnlyList<ManifestModule> modules)
            : this(manifestVersion, appVersion, created, machineName, userName, osBuild,
                   snapshotName, null, null, modules)
        {
        }

        internal ManifestData(int manifestVersion, string appVersion, string created, string machineName,
                              string userName, string osBuild, string snapshotName,
                              string compression, string payloadFile, IReadOnlyList<ManifestModule> modules)
        {
            ManifestVersion = manifestVersion;
            AppVersion = appVersion;
            Created = created;
            MachineName = machineName;
            UserName = userName;
            OsBuild = osBuild;
            SnapshotName = snapshotName;
            Compression = compression;
            PayloadFile = payloadFile;
            Modules = modules;
        }

        internal int ManifestVersion { get; }

        internal string AppVersion { get; }

        /// <summary>Round-trip ("o") timestamp of the run, as written.</summary>
        internal string Created { get; }

        internal string MachineName { get; }

        internal string UserName { get; }

        internal string OsBuild { get; }

        /// <summary>
        /// Optional user-supplied name for the backup folder. A missing value identifies a legacy
        /// timestamp-named folder and callers must display the folder name instead.
        /// </summary>
        internal string SnapshotName { get; }


        /// <summary>Optional archive compression used for the payload, when one exists.</summary>
        internal string Compression { get; }

        /// <summary>Optional archive file that holds this backup's module artifacts.</summary>
        internal string PayloadFile { get; }
        internal IReadOnlyList<ManifestModule> Modules { get; }
    }

    /// <summary>
    /// One module's row in a parsed manifest.
    /// </summary>
    internal sealed class ManifestModule
    {
        internal ManifestModule(string type, string title, string state, string reason)
        {
            Type = type;
            Title = title;
            State = state;
            Reason = reason;
        }

        /// <summary>The module's CLR type name, e.g. "DMouse". May name a retired module.</summary>
        internal string Type { get; }

        internal string Title { get; }

        /// <summary>One of the BackupManifest.State* literals, or anything a future writer wrote.</summary>
        internal string State { get; }

        internal string Reason { get; }
    }
}
