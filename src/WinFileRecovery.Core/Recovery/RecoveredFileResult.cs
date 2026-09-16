using WinFileRecovery.Core.Verification;

namespace WinFileRecovery.Core.Recovery;

/// <summary>Outcome of writing one recovered file to disk, with its post-write integrity checks.</summary>
public sealed record RecoveredFileResult(
    string OriginalDisplayName,
    string DestinationPath,
    long SizeBytes,
    string Sha256,
    AuthenticodeStatus SignatureStatus);
