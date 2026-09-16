using WinFileRecovery.Core.Native;

namespace WinFileRecovery.Core.Carving.FormatValidators;

/// <summary>
/// Walks real JPEG marker structure to find the genuine EOI (End Of Image,
/// 0xFFD9) instead of trusting the first FF D9 byte pair found by a plain
/// footer search — which reliably produces false, truncated matches
/// because 0xFFD9 can occur by chance inside the compressed scan data
/// itself (see e.g. any JFIF/EXIF thumbnail embedded before the real image).
/// </summary>
internal sealed class JpegFormatValidator : IFormatValidator
{
    public string Extension => "jpg";

    private const int MaxSegmentLength = 0xFFFF;

    public long? DetermineLength(RawDisk disk, long headerOffset, long maxSizeBytes)
    {
        var reader = new SequentialDiskReader(disk, headerOffset);
        long limit = headerOffset + maxSizeBytes;

        byte[] soi = reader.Read(2);
        if (soi.Length != 2 || soi[0] != 0xFF || soi[1] != 0xD8) return null;

        while (reader.Position < limit)
        {
            byte[] marker = reader.Read(2);
            if (marker.Length != 2 || marker[0] != 0xFF) return null; // desynced — not a well-formed marker stream

            byte m = marker[1];

            // Fill bytes (0xFF repeated before the real marker byte) — keep
            // reading marker bytes until a non-0xFF one shows up.
            while (m == 0xFF)
            {
                byte[] next = reader.Read(1);
                if (next.Length != 1) return null;
                m = next[0];
            }

            if (m == 0xD9) // EOI
                return reader.Position - headerOffset;

            bool standalone = m == 0x01 || (m >= 0xD0 && m <= 0xD7); // TEM, RSTn
            if (standalone) continue;

            byte[] lenBytes = reader.Read(2);
            if (lenBytes.Length != 2) return null;
            int segmentLength = (lenBytes[0] << 8) | lenBytes[1];
            if (segmentLength < 2 || segmentLength > MaxSegmentLength) return null;

            reader.Skip(segmentLength - 2);

            if (m == 0xDA) // SOS — entropy-coded scan data follows, not another length-prefixed segment
            {
                if (!SkipScanData(reader, limit)) return null;
            }
        }

        return null; // exceeded MaxSizeBytes without finding EOI — treat as unbounded/corrupt, fall back
    }

    /// <summary>
    /// Scans past the compressed entropy-coded data following an SOS
    /// marker, byte by byte, until the next real marker (leaving the
    /// reader positioned right before that marker's 0xFF so the caller's
    /// main loop picks it up). 0xFF 0x00 is byte-stuffing (a literal 0xFF
    /// in the data) and 0xFF D0-D7 are restart markers — both are part of
    /// the scan and must not be mistaken for its end.
    ///
    /// Bounded by <paramref name="limit"/>: without it, a truncated/corrupt
    /// scan with no real marker ahead would read byte-by-byte all the way
    /// to the end of the device before giving up.
    /// </summary>
    private static bool SkipScanData(SequentialDiskReader reader, long limit)
    {
        while (reader.Position < limit)
        {
            byte[] b = reader.Read(1);
            if (b.Length != 1) return false;
            if (b[0] != 0xFF) continue;

            byte[] next = reader.Read(1);
            if (next.Length != 1) return false;

            if (next[0] == 0x00) continue; // stuffed literal 0xFF, still scan data
            if (next[0] >= 0xD0 && next[0] <= 0xD7) continue; // restart marker, scan continues

            // A real marker follows — back up 2 bytes so the main loop reads it.
            reader.Skip(-2);
            return true;
        }

        return false; // hit the size limit without finding the scan's end
    }
}
