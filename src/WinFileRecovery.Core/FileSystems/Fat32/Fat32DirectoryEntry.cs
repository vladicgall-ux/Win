using System.Text;

namespace WinFileRecovery.Core.FileSystems.Fat32;

/// <summary>
/// A parsed 32-byte FAT directory entry (8.3 short name form). Deleted
/// entries have their first name byte replaced with 0xE5; everything else
/// (size, starting cluster, attributes) survives until the entry's slot is
/// reused, which is what makes FAT undelete possible.
///
/// Long file names (VFAT LFN) are intentionally not reconstructed here:
/// their continuation entries are themselves marked deleted (0xE5) and can
/// be overwritten independently of the short-name entry, making faithful
/// reconstruction unreliable. Recovered files fall back to the 8.3 name.
/// </summary>
public sealed class Fat32DirectoryEntry
{
    public const byte DeletedMarker = 0xE5;
    public const byte FreeMarker = 0x00;
    private const byte LongNameAttribute = 0x0F;

    public string ShortName { get; init; } = "";
    public bool IsDeleted { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsVolumeLabel { get; init; }
    public uint StartCluster { get; init; }
    public uint FileSize { get; init; }

    public static Fat32DirectoryEntry? Parse(byte[] buffer, int offset)
    {
        byte firstByte = buffer[offset];
        if (firstByte == FreeMarker) return null; // end of directory

        byte attributes = buffer[offset + 11];
        if (attributes == LongNameAttribute) return null; // LFN continuation, skip

        bool isDeleted = firstByte == DeletedMarker;

        string namePart = Encoding.ASCII.GetString(buffer, offset, 8).TrimEnd();
        string extPart = Encoding.ASCII.GetString(buffer, offset + 8, 3).TrimEnd();

        if (isDeleted && namePart.Length > 0)
            namePart = "_" + namePart[1..]; // conventional undelete placeholder for the lost first char

        string shortName = extPart.Length > 0 ? $"{namePart}.{extPart}" : namePart;

        ushort highCluster = BitConverter.ToUInt16(buffer, offset + 20);
        ushort lowCluster = BitConverter.ToUInt16(buffer, offset + 26);
        uint startCluster = ((uint)highCluster << 16) | lowCluster;
        uint fileSize = BitConverter.ToUInt32(buffer, offset + 28);

        return new Fat32DirectoryEntry
        {
            ShortName = shortName,
            IsDeleted = isDeleted,
            IsDirectory = (attributes & 0x10) != 0,
            IsVolumeLabel = (attributes & 0x08) != 0,
            StartCluster = startCluster,
            FileSize = fileSize,
        };
    }
}
