using System;
using System.Threading;
using System.Threading.Tasks;

namespace WinRestoreKit
{
    public interface ISnapshotPayloadPreparationService
    {
        Task<SnapshotPayloadPreparation> PrepareAsync(
            SnapshotEvent snapshot, CancellationToken cancellationToken);
    }

    /// <summary>Owns a prepared payload scope until Compare accepts it or the selection is abandoned.</summary>
    public sealed class SnapshotPayloadPreparation : IDisposable
    {
        private readonly BackupPayload.ReadScope scope;
        private int disposed;

        internal SnapshotPayloadPreparation(SnapshotEvent snapshot, BackupPayload.ReadScope scope, string error)
        {
            Snapshot = snapshot;
            this.scope = scope;
            Error = error;
        }

        public SnapshotEvent Snapshot { get; }

        public string Path => scope?.Path;

        public string Error { get; }

        public bool IsPrepared => Error == null;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;

            scope?.Dispose();
        }
    }
}
