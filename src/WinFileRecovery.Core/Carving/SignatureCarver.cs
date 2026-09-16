using WinFileRecovery.Core.Native;
using WinFileRecovery.Core.Recovery;

namespace WinFileRecovery.Core.Carving;

/// <summary>
/// Scans raw disk space in overlapping chunks looking for known file
/// signatures. Used when filesystem metadata (MFT / directory entries) is
/// missing or corrupted, so recovery falls back to content-based carving.
/// </summary>
public sealed class SignatureCarver
{
    // 32 MiB chunks, sector aligned, with an overlap equal to the largest
    // header/footer so matches spanning a chunk boundary are not missed.
    // Larger chunks amortize the per-ReadFile syscall/seek overhead, which
    // dominates total scan time far more than the in-memory signature search.
    private const int ChunkSectors = 65536; // 32 MiB @ 512-byte sectors
    private const int OverlapBytes = 64 * 1024;

    private readonly IReadOnlyList<FileSignature> _signatures;

    public SignatureCarver(IReadOnlyList<FileSignature>? signatures = null)
    {
        _signatures = signatures ?? SignatureCatalog.Default;
    }

    public IEnumerable<RecoverableFile> Scan(
        RawDisk disk,
        long startSector,
        long endSector,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        int chunkBytes = ChunkSectors * disk.SectorSize;
        long totalSectors = endSector - startSector;
        byte[] carry = Array.Empty<byte>();
        long carryStartOffset = 0;

        for (long sector = startSector; sector < endSector; sector += ChunkSectors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int sectorsToRead = (int)Math.Min(ChunkSectors, endSector - sector);
            byte[] chunk = disk.ReadSectors(sector, sectorsToRead);
            long chunkOffset = sector * disk.SectorSize;

            byte[] window = Combine(carry, chunk);
            long windowStartOffset = chunkOffset - carry.Length;

            foreach (var hit in FindSignaturesInWindow(window, windowStartOffset, disk))
                yield return hit;

            // Keep the tail of this chunk as carry-over for the next window.
            int keep = Math.Min(OverlapBytes, chunk.Length);
            carry = chunk[^keep..];
            carryStartOffset = chunkOffset + chunk.Length - keep;

            progress?.Report(Math.Min(1.0, (double)(sector - startSector + sectorsToRead) / totalSectors));
        }
    }

    private IEnumerable<RecoverableFile> FindSignaturesInWindow(byte[] window, long windowStartOffset, RawDisk disk)
    {
        foreach (var sig in _signatures)
        {
            int searchFrom = 0;
            while (true)
            {
                int idx = IndexOf(window, sig.Header, searchFrom);
                if (idx < 0) break;

                long absoluteStart = windowStartOffset + idx;
                long length = DetermineLength(window, idx, sig, disk, absoluteStart);

                yield return new RecoverableFile(
                    Extension: sig.Extension,
                    StartOffsetBytes: absoluteStart,
                    LengthBytes: length,
                    Source: RecoverySource.SignatureCarving,
                    OriginalName: null,
                    IsDeleted: true);

                searchFrom = idx + sig.Header.Length;
            }
        }
    }

    private long DetermineLength(byte[] window, int headerIndex, FileSignature sig, RawDisk disk, long absoluteStart)
    {
        if (sig.Footer is { Length: > 0 })
        {
            int footerIdx = IndexOf(window, sig.Footer, headerIndex + sig.Header.Length);
            if (footerIdx >= 0)
                return footerIdx + sig.Footer.Length - headerIndex;
        }

        // No footer found within this window (or format has no footer):
        // cap at MaxSizeBytes; the recovery engine truncates on write if the
        // filesystem's own metadata later gives a tighter bound.
        return Math.Min(sig.MaxSizeBytes, disk.LengthBytes - absoluteStart);
    }

    /// <summary>
    /// Span-based search: .NET's MemoryExtensions.IndexOf for byte spans is
    /// SIMD-vectorized, unlike a hand-rolled nested loop, and is what
    /// actually matters once disk I/O itself is no longer the bottleneck.
    /// </summary>
    private static int IndexOf(byte[] haystack, byte[] needle, int from)
    {
        if (needle.Length == 0 || from < 0 || from >= haystack.Length) return -1;
        int found = haystack.AsSpan(from).IndexOf(needle);
        return found < 0 ? -1 : found + from;
    }

    private static byte[] Combine(byte[] a, byte[] b)
    {
        if (a.Length == 0) return b;
        var result = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, result, 0, a.Length);
        Buffer.BlockCopy(b, 0, result, a.Length, b.Length);
        return result;
    }
}
