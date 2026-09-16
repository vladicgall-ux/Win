namespace WinFileRecovery.Core.Recovery;

public enum RecoverySource
{
    SignatureCarving,
    NtfsMft,
    Fat32DirectoryEntry,
}

/// <summary>One contiguous byte range on the source volume backing (part of) a recoverable file.</summary>
public readonly record struct FileExtent(long OffsetBytes, long LengthBytes);

/// <summary>
/// A candidate recoverable file found during a scan. Immutable snapshot;
/// actual bytes are re-read from disk lazily at recovery time so scanning
/// stays memory-light even with thousands of hits.
///
/// For carved files (no filesystem metadata) the data is always contiguous,
/// so StartOffsetBytes/LengthBytes describe it fully and Extents is null.
/// For filesystem-based recovery (e.g. a fragmented NTFS $DATA attribute
/// with several data runs), Extents lists every run in order; consumers
/// that care about exact bytes (recovery) must use Extents when present
/// rather than assuming the single-range fields are contiguous.
///
/// A tiny NTFS file can be "resident": its content is stored inline inside
/// the MFT record rather than in disk clusters. Such files carry their
/// bytes directly in ResidentData instead of an offset/extents.
///
/// EstimatedDeletionUtc is a best-effort timestamp for when the file was
/// deleted (NTFS: the MFT record's own last-changed time; FAT32: the
/// directory entry's last-write time, the closest thing FAT tracks). It is
/// null for signature-carved files, which carry no filesystem metadata at
/// all, and is always an approximation — treat it as "around this time",
/// not an exact deletion timestamp.
/// </summary>
public sealed record RecoverableFile(
    string Extension,
    long StartOffsetBytes,
    long LengthBytes,
    RecoverySource Source,
    string? OriginalName,
    bool IsDeleted,
    IReadOnlyList<FileExtent>? Extents = null,
    byte[]? ResidentData = null,
    DateTime? EstimatedDeletionUtc = null)
{
    public string DisplayName => OriginalName ?? $"recovered_{StartOffsetBytes:X}.{Extension}";
}
