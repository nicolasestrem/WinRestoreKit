using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WinRestoreKit
{
    internal readonly struct ComparisonProgress
    {
        internal ComparisonProgress(int ordinal, ModuleComparison comparison)
        {
            Ordinal = ordinal;
            Comparison = comparison;
        }

        internal int Ordinal { get; }

        internal ModuleComparison Comparison { get; }
    }

    public sealed class SnapshotComparisonService
    {
        private const int MaximumConcurrentProbes = 4;

        public async Task<IReadOnlyList<ModuleComparison>> CompareAsync(
            SnapshotEvent snapshot, IReadOnlyList<BackupBase> modules,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (!snapshot.IsRestorable)
            {
                throw new ArgumentException(
                    "Only a verified or partial snapshot can be compared.", nameof(snapshot));
            }

            using (SnapshotPayloadPreparation preparation = await new SnapshotPayloadPreparationService()
                .PrepareAsync(snapshot, cancellationToken).ConfigureAwait(false))
            {
                return await CompareAsync(preparation, modules, cancellationToken, progress: null)
                    .ConfigureAwait(false);
            }
        }

        internal Task<IReadOnlyList<ModuleComparison>> CompareAsync(
            SnapshotPayloadPreparation preparation, IReadOnlyList<BackupBase> modules,
            CancellationToken cancellationToken, IProgress<ComparisonProgress> progress)
        {
            ArgumentNullException.ThrowIfNull(preparation);
            return ComparePreparedAsync(preparation, modules, cancellationToken, progress);
        }

        private async Task<IReadOnlyList<ModuleComparison>> ComparePreparedAsync(
            SnapshotPayloadPreparation preparation, IReadOnlyList<BackupBase> modules,
            CancellationToken cancellationToken, IProgress<ComparisonProgress> progress)
        {
            cancellationToken.ThrowIfCancellationRequested();

            BackupBase[] catalog = (modules ?? Array.Empty<BackupBase>())
                .Where(module => module != null)
                .ToArray();

            if (!string.IsNullOrWhiteSpace(preparation.Error))
            {
                ModuleComparison[] failedRows = PayloadFailureRows(catalog, preparation.Snapshot.Manifest,
                    preparation.Error);
                ReportRows(failedRows, progress);
                return failedRows;
            }

            using (SemaphoreSlim gate = new SemaphoreSlim(MaximumConcurrentProbes, MaximumConcurrentProbes))
            {
                Task<ModuleComparison>[] workers = catalog.Select((module, ordinal) =>
                    CompareAtOrdinalAsync(module, ordinal, preparation.Path, preparation.Snapshot.Manifest,
                        gate, cancellationToken, progress)).ToArray();

                try
                {
                    return await Task.WhenAll(workers).ConfigureAwait(false);
                }
                finally
                {
                    // The caller owns preparation and can dispose it once this task settles. Wait for
                    // every synchronous probe before that happens, including probes that outlast cancellation.
                    await Task.WhenAll(workers.Select(ObserveCompletionAsync)).ConfigureAwait(false);
                }
            }
        }

        private static async Task<ModuleComparison> CompareAtOrdinalAsync(
            BackupBase module, int ordinal, string payloadPath, ManifestData manifest,
            SemaphoreSlim gate, CancellationToken token, IProgress<ComparisonProgress> progress)
        {
            bool enteredGate = false;
            try
            {
                await gate.WaitAsync(token).ConfigureAwait(false);
                enteredGate = true;
                token.ThrowIfCancellationRequested();

                ModuleComparison row = await Task.Run(() => CompareOne(module, payloadPath, manifest), token)
                    .ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                progress?.Report(new ComparisonProgress(ordinal, row));
                return row;
            }
            finally
            {
                if (enteredGate)
                    gate.Release();
            }
        }

        private static async Task ObserveCompletionAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
            }
        }

        private static void ReportRows(IReadOnlyList<ModuleComparison> rows,
                                       IProgress<ComparisonProgress> progress)
        {
            if (progress == null)
                return;

            for (int ordinal = 0; ordinal < rows.Count; ordinal++)
                progress.Report(new ComparisonProgress(ordinal, rows[ordinal]));
        }

        private static ModuleComparison[] PayloadFailureRows(IReadOnlyList<BackupBase> modules,
                                                              ManifestData manifest, string error)
        {
            ModuleComparison[] rows = new ModuleComparison[modules.Count];
            for (int index = 0; index < modules.Count; index++)
            {
                BackupBase module = modules[index];
                ManifestModule entry = FindManifestEntry(manifest, module.GetType().Name);

                rows[index] = IsManifestWithoutArtifact(entry)
                    ? NotCapturedFromManifest(module, entry)
                    : new ModuleComparison(module, ComparisonState.Unavailable, false,
                        "The selected backup payload could not be prepared.", error);
            }

            return rows;
        }

        private static ModuleComparison CompareOne(BackupBase module, string payloadPath,
                                                   ManifestData manifest)
        {
            ManifestModule entry = FindManifestEntry(manifest, module.GetType().Name);

            if (IsManifestWithoutArtifact(entry))
                return NotCapturedFromManifest(module, entry);

            bool usableArtifact;
            string artifactSummary;
            if (entry?.State == BackupManifest.StateSucceeded)
            {
                usableArtifact = true;
                artifactSummary = "The snapshot manifest records this module as captured.";
            }
            else
            {
                bool? probe;
                try
                {
                    probe = module.HasArtifactIn(payloadPath);
                }
                catch (Exception ex)
                {
                    LogHelper.Instance.LogMessage("Comparison artifact probe failed for " + module.Title
                        + ": " + ex.Message);
                    return new ModuleComparison(module, ComparisonState.Unavailable, false,
                        "Artifact presence could not be determined.", ex.Message);
                }

                if (probe == false)
                {
                    return new ModuleComparison(module, ComparisonState.NotCaptured, false,
                        "The module proved that this snapshot has no restore artifact.", string.Empty);
                }

                if (!probe.HasValue)
                {
                    if (manifest != null)
                    {
                        return new ModuleComparison(module, ComparisonState.NotCaptured, false,
                            "The manifest does not record this module and the module cannot prove an artifact.",
                            string.Empty);
                    }

                    // Preserve RestoreContents' no-manifest fallback; drift remains explicit below.
                    usableArtifact = true;
                    artifactSummary = "No manifest is available and the module cannot disprove a legacy artifact.";
                }
                else
                {
                    usableArtifact = true;
                    artifactSummary = "The module verified a captured artifact.";
                }
            }

            try
            {
                bool? drifted = module.HasDriftedFrom(payloadPath);
                if (drifted == true)
                {
                    return new ModuleComparison(module, ComparisonState.Changed, usableArtifact, artifactSummary,
                        "Core confirmed that current state differs from the snapshot.");
                }

                if (drifted == false)
                {
                    return new ModuleComparison(module, ComparisonState.Same, usableArtifact, artifactSummary,
                        "Core confirmed that current state matches the snapshot.");
                }

                return new ModuleComparison(module, ComparisonState.Unavailable, usableArtifact, artifactSummary,
                    "Core could not establish a trustworthy live comparison.");
            }
            catch (Exception ex)
            {
                LogHelper.Instance.LogMessage("Comparison drift probe failed for " + module.Title
                    + ": " + ex.Message);
                return new ModuleComparison(module, ComparisonState.Unavailable, usableArtifact, artifactSummary,
                    ex.Message);
            }
        }

        private static ModuleComparison NotCapturedFromManifest(BackupBase module, ManifestModule entry)
            => new ModuleComparison(module, ComparisonState.NotCaptured, false,
                "The snapshot manifest records no usable artifact for this module.",
                string.IsNullOrWhiteSpace(entry.Reason) ? entry.State : entry.Reason);

        private static bool IsManifestWithoutArtifact(ManifestModule entry)
            => entry?.State == BackupManifest.StateSkipped || entry?.State == BackupManifest.StateFailed;

        private static ManifestModule FindManifestEntry(ManifestData manifest, string typeName)
        {
            if (manifest?.Modules == null)
                return null;

            foreach (ManifestModule entry in manifest.Modules)
            {
                if (entry != null && string.Equals(entry.Type, typeName, StringComparison.Ordinal))
                    return entry;
            }

            return null;
        }
    }
}
