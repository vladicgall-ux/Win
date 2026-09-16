using WinFileRecovery.Core.Native;

namespace WinFileRecovery.Core.FileSystems.Ntfs;

/// <summary>
/// Parsed NTFS boot sector (sector 0 of the volume). Only the fields needed
/// to locate the $MFT and interpret cluster addressing are kept.
/// </summary>
public sealed class NtfsBootSector
{
    // Values from a genuine NTFS boot sector are always drawn from a small,
    // well-known set / range. A corrupt sector (or a deliberately crafted
    // one, e.g. a raw-carved image someone hands the app) can contain
    // anything, and letting an out-of-range field flow into a cluster ->
    // sector -> byte-offset calculation is exactly how a "read 4 exabytes"
    // or "negative offset" bug happens. So every field is validated against
    // real NTFS constraints before this object is trusted by any caller.
    private static readonly ushort[] ValidSectorSizes = { 512, 1024, 2048, 4096 };
    private const int MaxBytesPerCluster = 2 * 1024 * 1024; // NTFS caps at 2 MiB; 64 KiB is the common real-world max
    private const int MinBytesPerFileRecord = 256;
    private const int MaxBytesPerFileRecord = 4096;

    public ushort BytesPerSector { get; init; }
    public byte SectorsPerCluster { get; init; }
    public long MftStartCluster { get; init; }
    public long MftMirrorStartCluster { get; init; }
    public int BytesPerFileRecord { get; init; }
    public long TotalSectors { get; init; }

    public int BytesPerCluster => BytesPerSector * SectorsPerCluster;

    public static NtfsBootSector Parse(byte[] sector0)
    {
        if (sector0.Length < 512)
            throw new InvalidDataException("Boot sector too short");

        string oemId = System.Text.Encoding.ASCII.GetString(sector0, 3, 8);
        if (oemId != "NTFS    ")
            throw new InvalidDataException("Not an NTFS boot sector");

        ushort bytesPerSector = BitConverter.ToUInt16(sector0, 11);
        byte sectorsPerCluster = sector0[13];
        long totalSectors = BitConverter.ToInt64(sector0, 40);
        long mftStartCluster = BitConverter.ToInt64(sector0, 48);
        long mftMirrorStartCluster = BitConverter.ToInt64(sector0, 56);
        sbyte clustersPerFileRecordRaw = (sbyte)sector0[64];

        if (Array.IndexOf(ValidSectorSizes, bytesPerSector) < 0)
            throw new InvalidDataException($"Повреждённый NTFS-том: недопустимый размер сектора ({bytesPerSector}).");

        if (sectorsPerCluster == 0 || !IsPowerOfTwo(sectorsPerCluster))
            throw new InvalidDataException($"Повреждённый NTFS-том: недопустимое число секторов на кластер ({sectorsPerCluster}).");

        long bytesPerClusterCheck = (long)bytesPerSector * sectorsPerCluster;
        if (bytesPerClusterCheck > MaxBytesPerCluster)
            throw new InvalidDataException("Повреждённый NTFS-том: размер кластера превышает допустимый предел.");

        if (totalSectors <= 0)
            throw new InvalidDataException("Повреждённый NTFS-том: некорректный размер тома.");

        if (mftStartCluster < 0 || mftMirrorStartCluster < 0)
            throw new InvalidDataException("Повреждённый NTFS-том: отрицательный номер кластера $MFT.");

        long totalClusters = totalSectors / sectorsPerCluster;
        if (mftStartCluster >= totalClusters || mftMirrorStartCluster >= totalClusters)
            throw new InvalidDataException("Повреждённый NTFS-том: $MFT указывает за пределы тома.");

        int bytesPerFileRecord = clustersPerFileRecordRaw > 0
            ? clustersPerFileRecordRaw * bytesPerSector * sectorsPerCluster
            : clustersPerFileRecordRaw < 0 && -clustersPerFileRecordRaw < 31
                ? 1 << -clustersPerFileRecordRaw // negative => 2^|n| bytes
                : 0;

        if (bytesPerFileRecord < MinBytesPerFileRecord || bytesPerFileRecord > MaxBytesPerFileRecord
            || bytesPerFileRecord % bytesPerSector != 0)
            throw new InvalidDataException($"Повреждённый NTFS-том: недопустимый размер MFT-записи ({bytesPerFileRecord}).");

        return new NtfsBootSector
        {
            BytesPerSector = bytesPerSector,
            SectorsPerCluster = sectorsPerCluster,
            TotalSectors = totalSectors,
            MftStartCluster = mftStartCluster,
            MftMirrorStartCluster = mftMirrorStartCluster,
            BytesPerFileRecord = bytesPerFileRecord,
        };
    }

    public static NtfsBootSector Read(RawDisk volume)
    {
        byte[] sector0 = volume.ReadSectors(0, 1);
        var boot = Parse(sector0);

        // Cross-check against the physical device's own reported size —
        // a boot sector can declare a TotalSectors that's internally
        // self-consistent but still larger than the actual device.
        if (volume.TotalSectors >= 0 && boot.TotalSectors > volume.TotalSectors)
            throw new InvalidDataException("Повреждённый NTFS-том: заявленный размер тома больше физического диска.");

        return boot;
    }

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
}
