using WinFileRecovery.Core.Native;

namespace WinFileRecovery.Core.FileSystems.Fat32;

public sealed class Fat32BootSector
{
    public ushort BytesPerSector { get; init; }
    public byte SectorsPerCluster { get; init; }
    public ushort ReservedSectors { get; init; }
    public byte NumberOfFats { get; init; }
    public uint SectorsPerFat { get; init; }
    public uint RootCluster { get; init; }
    public uint TotalSectors { get; init; }

    public int BytesPerCluster => BytesPerSector * SectorsPerCluster;
    public long FatStartSector => ReservedSectors;
    public long DataStartSector => ReservedSectors + (long)NumberOfFats * SectorsPerFat;

    public static Fat32BootSector Parse(byte[] sector0)
    {
        ushort bytesPerSector = BitConverter.ToUInt16(sector0, 11);
        byte sectorsPerCluster = sector0[13];
        ushort reservedSectors = BitConverter.ToUInt16(sector0, 14);
        byte numberOfFats = sector0[16];
        uint sectorsPerFat32 = BitConverter.ToUInt32(sector0, 36);
        uint rootCluster = BitConverter.ToUInt32(sector0, 44);
        uint totalSectors32 = BitConverter.ToUInt32(sector0, 32);

        string fsType = System.Text.Encoding.ASCII.GetString(sector0, 82, 8);
        if (!fsType.StartsWith("FAT32"))
            throw new InvalidDataException("Not a FAT32 boot sector");

        return new Fat32BootSector
        {
            BytesPerSector = bytesPerSector,
            SectorsPerCluster = sectorsPerCluster,
            ReservedSectors = reservedSectors,
            NumberOfFats = numberOfFats,
            SectorsPerFat = sectorsPerFat32,
            RootCluster = rootCluster,
            TotalSectors = totalSectors32,
        };
    }

    public static Fat32BootSector Read(RawDisk volume)
    {
        byte[] sector0 = volume.ReadSectors(0, 1);
        return Parse(sector0);
    }

    public long ClusterToSector(uint cluster) =>
        DataStartSector + (long)(cluster - 2) * SectorsPerCluster;
}
