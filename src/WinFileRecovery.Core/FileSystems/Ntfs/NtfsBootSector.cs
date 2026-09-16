using WinFileRecovery.Core.Native;

namespace WinFileRecovery.Core.FileSystems.Ntfs;

/// <summary>
/// Parsed NTFS boot sector (sector 0 of the volume). Only the fields needed
/// to locate the $MFT and interpret cluster addressing are kept.
/// </summary>
public sealed class NtfsBootSector
{
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

        int bytesPerFileRecord = clustersPerFileRecordRaw > 0
            ? clustersPerFileRecordRaw * bytesPerSector * sectorsPerCluster
            : 1 << -clustersPerFileRecordRaw; // negative => 2^|n| bytes

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
        return Parse(sector0);
    }
}
