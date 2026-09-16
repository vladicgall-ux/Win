using WinFileRecovery.Core.Native;

namespace WinFileRecovery.Core.Carving.FormatValidators;

/// <summary>
/// Walks PNG chunk framing (each chunk is length + type + data + CRC) to
/// find the real IEND chunk, rather than the first occurrence of the IEND
/// byte pattern via a plain footer search — reliable because PNG's chunk
/// lengths are exact, so unlike JPEG there's no ambiguity once framing is followed.
/// </summary>
internal sealed class PngFormatValidator : IFormatValidator
{
    public string Extension => "png";

    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private const uint MaxChunkDataLength = 0x7FFFFFFF; // PNG spec: chunk length must not exceed 2^31-1

    public long? DetermineLength(RawDisk disk, long headerOffset, long maxSizeBytes)
    {
        var reader = new SequentialDiskReader(disk, headerOffset);

        byte[] sig = reader.Read(8);
        if (sig.Length != 8 || !sig.AsSpan().SequenceEqual(Signature)) return null;

        while (reader.Position - headerOffset < maxSizeBytes)
        {
            byte[] lengthBytes = reader.Read(4);
            if (lengthBytes.Length != 4) return null;
            uint chunkLength = ((uint)lengthBytes[0] << 24) | ((uint)lengthBytes[1] << 16) | ((uint)lengthBytes[2] << 8) | lengthBytes[3];
            if (chunkLength > MaxChunkDataLength) return null;

            byte[] type = reader.Read(4);
            if (type.Length != 4) return null;

            reader.Skip(chunkLength); // chunk data — not needed to determine total length
            byte[] crc = reader.Read(4);
            if (crc.Length != 4) return null;

            if (type[0] == 'I' && type[1] == 'E' && type[2] == 'N' && type[3] == 'D')
                return reader.Position - headerOffset;
        }

        return null;
    }
}
