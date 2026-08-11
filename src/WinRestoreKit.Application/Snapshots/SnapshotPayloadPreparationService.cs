using System;
using System.Threading;
using System.Threading.Tasks;

namespace WinRestoreKit
{
    public sealed class SnapshotPayloadPreparationService : ISnapshotPayloadPreparationService
    {
        public async Task<SnapshotPayloadPreparation> PrepareAsync(
            SnapshotEvent snapshot, CancellationToken cancellationToken)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            if (!snapshot.IsRestorable)
            {
                return new SnapshotPayloadPreparation(snapshot, null,
                    "This backup attempt cannot be selected because it is failed or unreadable.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!BackupPayload.TryPrepareForRead(snapshot.CanonicalPath, out BackupPayload.ReadScope scope,
                                                     out string error))
                {
                    scope?.Dispose();
                    return new SnapshotPayloadPreparation(snapshot, null,
                        "The selected backup payload could not be prepared: " + error);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    scope.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return new SnapshotPayloadPreparation(snapshot, scope, null);
            }, cancellationToken).ConfigureAwait(false);
        }
    }
}
