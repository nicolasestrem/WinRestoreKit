using Conf;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace WinRestoreKit
{
    internal enum AppExportState
    {
        Ok,
        Absent,
        Unreadable
    }

    internal enum AppRestoreProblemRouting
    {
        None,
        ShowNow,
        Defer
    }

    internal sealed class AppRestoreSource
    {
        internal AppRestoreSource(string path, string displayName, bool isSelectedRestoreSource,
                                  bool isPreparedPayload)
        {
            Path = path ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            IsSelectedRestoreSource = isSelectedRestoreSource;
            IsPreparedPayload = isPreparedPayload;
        }

        public string Path { get; }
        public string DisplayName { get; }
        public bool IsSelectedRestoreSource { get; }
        internal bool IsPreparedPayload { get; }

        public override string ToString() => DisplayName;
    }

    internal sealed class AppExport
    {
        private AppExport(AppExportState state, IReadOnlyList<string> packageIdentifiers, string message)
        {
            State = state;
            PackageIdentifiers = packageIdentifiers == null
                ? Array.Empty<string>()
                : Array.AsReadOnly(packageIdentifiers.ToArray());
            Message = message ?? string.Empty;
        }

        internal AppExportState State { get; }
        internal IReadOnlyList<string> PackageIdentifiers { get; }
        internal string Message { get; }
        internal bool IsProblem => State == AppExportState.Unreadable;

        internal static AppExport Ok(IReadOnlyList<string> ids, string message) =>
            new AppExport(AppExportState.Ok, ids, message);
        internal static AppExport Absent(string message) =>
            new AppExport(AppExportState.Absent, null, message);
        internal static AppExport Unreadable(string message) =>
            new AppExport(AppExportState.Unreadable, null, message);
    }

    internal sealed class AppRestoreListState
    {
        internal AppRestoreListState(IReadOnlyList<string> items, bool installEnabled)
        {
            Items = items == null ? Array.Empty<string>() : Array.AsReadOnly(items.ToArray());
            InstallEnabled = installEnabled;
        }

        internal IReadOnlyList<string> Items { get; }
        internal bool InstallEnabled { get; }
    }

    internal sealed class AppRestoreOutcome
    {
        internal AppRestoreOutcome(string caption, string text, RunSeverity severity)
            => (Caption, Text, Severity) = (caption ?? string.Empty, text ?? string.Empty, severity);

        internal string Caption { get; }
        internal string Text { get; }
        internal RunSeverity Severity { get; }
    }

    internal static class AppRestoreService
    {
        internal static IReadOnlyList<AppRestoreSource> BuildSources(string selectedRestorePath,
            IReadOnlyList<SnapshotEvent> snapshots)
        {
            var sources = new List<AppRestoreSource>();
            AddDistinct(sources, selectedRestorePath, "Selected restore source", true, true);

            foreach (SnapshotEvent snapshot in snapshots ?? Array.Empty<SnapshotEvent>())
            {
                if (snapshot == null || !snapshot.IsRestorable)
                    continue;

                AddDistinct(sources, snapshot.CanonicalPath, snapshot.DisplayName, false, false);
            }

            return Array.AsReadOnly(sources.ToArray());
        }

        internal static AppRestoreListState ComposeListState(AppExport export) =>
            export == null
                ? new AppRestoreListState(null, false)
                : new AppRestoreListState(export.PackageIdentifiers, export.PackageIdentifiers.Count > 0);

        internal static AppRestoreProblemRouting RouteProblem(AppExport export, bool windowShown)
        {
            if (export == null || !export.IsProblem)
                return AppRestoreProblemRouting.None;

            return windowShown ? AppRestoreProblemRouting.ShowNow : AppRestoreProblemRouting.Defer;
        }

        internal static AppExport ReadFromSource(string sourcePath)
        {
            if (!BackupPayload.TryPrepareForRead(sourcePath, out BackupPayload.ReadScope scope, out string error))
                return AppExport.Unreadable("Could not prepare the app export source: " + error);

            using (scope)
                return ReadExport(AppStoreApps.ExportPathIn(scope.Path));
        }

        internal static AppExport ReadFromSourceEntry(AppRestoreSource source)
        {
            if (source == null)
                return AppExport.Unreadable("No app backup source is selected.");

            return source.IsPreparedPayload
                ? ReadExport(AppStoreApps.ExportPathIn(source.Path))
                : ReadFromSource(source.Path);
        }

        internal static Task<AppRestoreOutcome> InstallAsync(IReadOnlyList<string> packageIdentifiers,
            Func<bool> stopRequested) =>
            InstallAsync(packageIdentifiers, id => Utils.RunWingetAsync(true, "install", "--id", id,
                "--accept-source-agreements", "--accept-package-agreements"), stopRequested);

        internal static async Task<AppRestoreOutcome> InstallAsync(IReadOnlyList<string> packageIdentifiers,
            Func<string, Task<ProcessOutcome>> installOneAsync, Func<bool> stopRequested)
        {
            string[] requested = (packageIdentifiers ?? Array.Empty<string>())
                .Where(identifier => !string.IsNullOrWhiteSpace(identifier)).ToArray();
            var failures = new List<string>();
            int attempted = 0;

            foreach (string identifier in requested)
            {
                if (stopRequested?.Invoke() == true)
                    break;

                attempted++;
                string reason = Describe(await installOneAsync(identifier));
                if (reason != null)
                    failures.Add(identifier + ": " + reason);
            }

            return ComposeOutcome(requested.Length, attempted, failures);
        }

        internal static string Describe(ProcessOutcome outcome)
        {
            if (outcome == null)
                return "winget returned no outcome";
            if (!outcome.Started)
                return "could not run winget: " + outcome.Error;
            if (outcome.TimedOut)
                return "winget did not finish, and was stopped";
            if (outcome.Error != null)
                return "winget ran but its outcome could not be determined: " + outcome.Error;
            return outcome.ExitCode != 0 ? "winget exited with code " + outcome.ExitCode : null;
        }

        internal static AppRestoreOutcome ComposeOutcome(int requested, int attempted,
            IReadOnlyList<string> failures)
        {
            int failed = failures?.Count ?? 0;
            string failureList = failures == null ? string.Empty : string.Join("\n", failures);
            bool stoppedEarly = attempted < requested;
            int notStarted = requested - attempted;

            if (requested == 0)
                return new AppRestoreOutcome("Nothing to do", "No apps were ticked, so nothing was installed.",
                    RunSeverity.Information);

            if (stoppedEarly)
            {
                string stopped = $"Stopped after {attempted} of {requested} app(s). The remaining {notStarted} were not started.";
                if (failed == 0)
                    return new AppRestoreOutcome("Stopped", stopped + $"\n\nwinget installed {attempted} app(s).",
                        RunSeverity.Information);

                return new AppRestoreOutcome("Stopped, and some apps failed",
                    stopped + $"\n\n{failed} of the {attempted} attempted failed:\n\n{failureList}",
                    RunSeverity.Warning);
            }

            if (failed == 0)
                return new AppRestoreOutcome("Done",
                    $"winget installed {attempted} app(s).\n\nAlready-installed apps are upgraded to the current version rather than put back at the backed-up one.",
                    RunSeverity.Information);

            return new AppRestoreOutcome("Some apps did not install",
                $"{failed} of {attempted} app(s) did not install:\n\n{failureList}", RunSeverity.Warning);
        }

        private static void AddDistinct(List<AppRestoreSource> sources, string path, string displayName,
            bool isSelectedRestoreSource, bool isPreparedPayload)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            string canonicalPath;
            try
            {
                canonicalPath = Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return;
            }

            if (sources.Any(source => string.Equals(source.Path, canonicalPath,
                StringComparison.OrdinalIgnoreCase)))
                return;

            sources.Add(new AppRestoreSource(canonicalPath, displayName, isSelectedRestoreSource,
                isPreparedPayload));
        }

        private static AppExport ReadExport(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return AppExport.Absent("No app export path could be worked out, so no apps are listed.");

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (FileNotFoundException)
            {
                return AppExport.Absent("This backup contains no app export (" + path +
                    "), so there are no apps to list for it.");
            }
            catch (Exception ex)
            {
                return AppExport.Unreadable("Could not read the app export at " + path + ": " + ex.Message);
            }

            if (string.IsNullOrWhiteSpace(json))
                return AppExport.Unreadable("The app export at " + path + " is empty.");

            JArray packages;
            try
            {
                packages = JObject.Parse(json)["Sources"]?.FirstOrDefault()?["Packages"] as JArray;
            }
            catch (Exception ex)
            {
                return AppExport.Unreadable("The app export at " + path + " is not valid JSON: " + ex.Message);
            }

            if (packages == null)
                return AppExport.Unreadable("The app export at " + path +
                    " has no list of packages in it, so nothing could be restored from it.");

            var identifiers = new List<string>();
            foreach (JToken package in packages)
            {
                string identifier = package?.Value<string>("PackageIdentifier");
                if (!string.IsNullOrWhiteSpace(identifier))
                    identifiers.Add(identifier);
            }

            return AppExport.Ok(identifiers, "Read " + identifiers.Count + " app(s) from " + path + ".");
        }
    }
}
