using WinFileRecovery.Core.Native;

namespace WinFileRecovery.Core.Carving;

/// <summary>
/// Buffered forward-only byte reader over a <see cref="RawDisk"/>, used by
/// format-aware carvers that need to walk a file's real internal structure
/// (JPEG markers, PNG chunks, ZIP entries) rather than just searching for a
/// footer pattern. RawDisk only reads whole sectors, so this fetches ahead
/// in chunks and serves individual byte/int reads out of that buffer.
/// </summary>
internal sealed class SequentialDiskReader
{
    private const int ChunkSectors = 256; // 128 KiB @ 512-byte sectors

    private readonly RawDisk _disk;
    private byte[] _buffer = Array.Empty<byte>();
    private long _bufferStartOffset;

    public long Position { get; private set; }

    public SequentialDiskReader(RawDisk disk, long startOffset)
    {
        _disk = disk;
        Position = startOffset;
    }

    /// <summary>Reads exactly <paramref name="count"/> bytes at the current position and advances. Returns fewer bytes only at the real end of the device.</summary>
    public byte[] Read(int count)
    {
        var result = new byte[count];
        int written = 0;

        while (written < count)
        {
            EnsureBuffered(count - written);
            int bufferOffset = (int)(Position - _bufferStartOffset);
            if (bufferOffset < 0 || bufferOffset >= _buffer.Length)
                break; // end of device reached

            int available = Math.Min(count - written, _buffer.Length - bufferOffset);
            Array.Copy(_buffer, bufferOffset, result, written, available);
            written += available;
            Position += available;

            if (available == 0) break;
        }

        return written == count ? result : result[..written];
    }

    /// <summary>Advances the position without materializing the skipped bytes — used to jump over a chunk's declared data length (PNG/ZIP) without re-reading it.</summary>
    public void Skip(long count) => Position += count;

    private void EnsureBuffered(int neededBytes)
    {
        long bufferEnd = _bufferStartOffset + _buffer.Length;
        if (Position >= _bufferStartOffset && Position + neededBytes <= bufferEnd)
            return; // already covered

        long sector = Position / _disk.SectorSize;
        int leadingSkip = (int)(Position % _disk.SectorSize);
        int sectorsWanted = (int)Math.Max(ChunkSectors, (leadingSkip + neededBytes + _disk.SectorSize - 1) / _disk.SectorSize);

        long sectorsAvailable = _disk.TotalSectors < 0 ? sectorsWanted : Math.Max(0, _disk.TotalSectors - sector);
        int sectorsToRead = (int)Math.Min(sectorsWanted, sectorsAvailable);

        if (sectorsToRead <= 0)
        {
            _buffer = Array.Empty<byte>();
            _bufferStartOffset = Position;
            return;
        }

        try
        {
            _buffer = _disk.ReadSectors(sector, sectorsToRead);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _buffer = Array.Empty<byte>();
        }
        _bufferStartOffset = sector * _disk.SectorSize;
    }
}
