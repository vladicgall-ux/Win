using WinFileRecovery.Core.Native;

namespace WinFileRecovery.Core.FileSystems.Fat32;

public sealed class Fat32BootSector
{
    // See NtfsBootSector for why these are validated as strictly as they
    // are: every field here is untrusted input straight off the disk, and
    // an out-of-range value flowing into a cluster/sector calculation is
    // how a corrupt volume turns into an out-of-bounds read or a runaway loop.
    private static readonly ushort[] ValidSectorSizes = { 512, 1024, 2048, 4096 };
    private const int MaxBytesPerCluster = 2 * 1024 * 1024;

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
        if (sector0.Length < 512)
            throw new InvalidDataException("Boot sector too short");

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

        if (Array.IndexOf(ValidSectorSizes, bytesPerSector) < 0)
            throw new InvalidDataException($"Повреждённый FAT32-том: недопустимый размер сектора ({bytesPerSector}).");

        if (sectorsPerCluster == 0 || (sectorsPerCluster & (sectorsPerCluster - 1)) != 0)
            throw new InvalidDataException($"Повреждённый FAT32-том: недопустимое число секторов на кластер ({sectorsPerCluster}).");

        if ((long)bytesPerSector * sectorsPerCluster > MaxBytesPerCluster)
            throw new InvalidDataException("Повреждённый FAT32-том: размер кластера превышает допустимый предел.");

        if (numberOfFats == 0 || numberOfFats > 8)
            throw new InvalidDataException($"Повреждённый FAT32-том: недопустимое число таблиц FAT ({numberOfFats}).");

        if (sectorsPerFat32 == 0 || totalSectors32 == 0)
            throw new InvalidDataException("Повреждённый FAT32-том: нулевой размер тома или таблицы FAT.");

        long dataStart = reservedSectors + (long)numberOfFats * sectorsPerFat32;
        if (dataStart <= 0 || dataStart >= totalSectors32)
            throw new InvalidDataException("Повреждённый FAT32-том: область данных выходит за пределы тома.");

        if (rootCluster < 2)
            throw new InvalidDataException($"Повреждённый FAT32-том: недопустимый корневой кластер ({rootCluster}).");

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
        var boot = Parse(sector0);

        if (volume.TotalSectors >= 0 && boot.TotalSectors > volume.TotalSectors)
            throw new InvalidDataException("Повреждённый FAT32-том: заявленный размер тома больше физического диска.");

        return boot;
    }

    public long ClusterToSector(uint cluster) =>
        DataStartSector + (long)(cluster - 2) * SectorsPerCluster;
}
