using WinFileRecovery.Core.Native;

namespace WinFileRecovery.Core.Carving.FormatValidators;

/// <summary>
/// Walks a ZIP's real record structure (local file headers, central
/// directory, end-of-central-directory) to find its true end, instead of
/// capping at an arbitrary MaxSizeBytes with no real end marker at all —
/// which is what the plain signature carver falls back to for ZIP today.
///
/// Covers the common case: entries with sizes known up front (general
/// purpose bit 3 clear), which is how virtually every ZIP written by a
/// normal archiver — including docx/xlsx/pptx — is produced. A streamed
/// ZIP (bit 3 set, sizes unknown until a trailing data descriptor) isn't
/// walked reliably here and falls back to the plain heuristic.
/// </summary>
internal sealed class ZipFormatValidator : IFormatValidator
{
    public string Extension => "zip";

    private const uint LocalFileHeaderSig = 0x04034B50;
    private const uint CentralDirectorySig = 0x02014B50;
    private const uint EndOfCentralDirectorySig = 0x06054B50;
    private const int DataDescriptorFlag = 0x0008;

    public long? DetermineLength(RawDisk disk, long headerOffset, long maxSizeBytes)
    {
        var reader = new SequentialDiskReader(disk, headerOffset);

        while (reader.Position - headerOffset < maxSizeBytes)
        {
            uint sig = ReadUInt32(reader);

            if (sig == LocalFileHeaderSig)
            {
                if (!SkipLocalFileEntry(reader)) return null;
            }
            else if (sig == CentralDirectorySig)
            {
                if (!SkipCentralDirectoryEntry(reader)) return null;
            }
            else if (sig == EndOfCentralDirectorySig)
            {
                return SkipEndOfCentralDirectory(reader) ? reader.Position - headerOffset : null;
            }
            else
            {
                return null; // unrecognized record — not a structure we can confidently walk
            }
        }

        return null;
    }

    private static bool SkipLocalFileEntry(SequentialDiskReader reader)
    {
        byte[] fixedPart = reader.Read(26); // everything after the 4-byte signature, up to (not including) name/extra lengths... see layout below
        if (fixedPart.Length != 26) return false;

        ushort flag = ReadUInt16(fixedPart, 2);
        uint compressedSize = ReadUInt32(fixedPart, 14);
        ushort nameLength = ReadUInt16(fixedPart, 22);
        ushort extraLength = ReadUInt16(fixedPart, 24);

        if ((flag & DataDescriptorFlag) != 0) return false; // streamed entry — size not known here

        reader.Skip(nameLength + extraLength + compressedSize);
        return true;
    }

    private static bool SkipCentralDirectoryEntry(SequentialDiskReader reader)
    {
        byte[] fixedPart = reader.Read(42); // everything after the 4-byte signature
        if (fixedPart.Length != 42) return false;

        ushort nameLength = ReadUInt16(fixedPart, 24);
        ushort extraLength = ReadUInt16(fixedPart, 26);
        ushort commentLength = ReadUInt16(fixedPart, 28);

        reader.Skip(nameLength + extraLength + commentLength);
        return true;
    }

    private static bool SkipEndOfCentralDirectory(SequentialDiskReader reader)
    {
        byte[] fixedPart = reader.Read(18); // everything after the 4-byte signature, up to comment length
        if (fixedPart.Length != 18) return false;

        ushort commentLength = ReadUInt16(fixedPart, 16);
        reader.Skip(commentLength);
        return true;
    }

    private static uint ReadUInt32(SequentialDiskReader reader)
    {
        byte[] b = reader.Read(4);
        return b.Length == 4 ? ReadUInt32(b, 0) : 0;
    }

    private static uint ReadUInt32(byte[] b, int offset) =>
        (uint)(b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16) | (b[offset + 3] << 24));

    private static ushort ReadUInt16(byte[] b, int offset) =>
        (ushort)(b[offset] | (b[offset + 1] << 8));
}
