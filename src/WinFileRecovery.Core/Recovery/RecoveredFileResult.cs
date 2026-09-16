using WinFileRecovery.Core.Verification;

namespace WinFileRecovery.Core.Recovery;

/// <summary>
/// Outcome of writing one recovered file to disk, with its post-write
/// integrity checks. When recovering this particular file failed (corrupt
/// metadata, an invalid extent, disk full, etc.), Error is set and the
/// other fields carry best-effort/default values — this is how one bad
/// file gets reported without aborting recovery of the rest of the batch.
/// </summary>
public sealed record RecoveredFileResult(
    string OriginalDisplayName,
    string DestinationPath,
    long SizeBytes,
    string Sha256,
    AuthenticodeStatus SignatureStatus,
    string? Error = null)
{
    public bool Succeeded => Error is null;
}
