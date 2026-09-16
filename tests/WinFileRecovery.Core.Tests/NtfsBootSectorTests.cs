using System.Text;
using WinFileRecovery.Core.FileSystems.Ntfs;
using Xunit;

namespace WinFileRecovery.Core.Tests;

public class NtfsBootSectorTests
{
    private static byte[] BuildValid(
        ushort bytesPerSector = 512,
        byte sectorsPerCluster = 8,
        long totalSectors = 1_000_000,
        long mftStartCluster = 100,
        long mftMirrorCluster = 200,
        sbyte clustersPerFileRecordRaw = -10) // 2^10 = 1024 bytes/record
    {
        var sector = new byte[512];
        Encoding.ASCII.GetBytes("NTFS    ").CopyTo(sector, 3);
        BitConverter.GetBytes(bytesPerSector).CopyTo(sector, 11);
        sector[13] = sectorsPerCluster;
        BitConverter.GetBytes(totalSectors).CopyTo(sector, 40);
        BitConverter.GetBytes(mftStartCluster).CopyTo(sector, 48);
        BitConverter.GetBytes(mftMirrorCluster).CopyTo(sector, 56);
        sector[64] = unchecked((byte)clustersPerFileRecordRaw);
        return sector;
    }

    [Fact]
    public void Parse_ValidBootSector_Succeeds()
    {
        var boot = NtfsBootSector.Parse(BuildValid());

        Assert.Equal(512, boot.BytesPerSector);
        Assert.Equal(8, boot.SectorsPerCluster);
        Assert.Equal(1024, boot.BytesPerFileRecord);
        Assert.Equal(100, boot.MftStartCluster);
    }

    [Fact]
    public void Parse_WrongSignature_Throws()
    {
        var sector = BuildValid();
        Encoding.ASCII.GetBytes("FAT32   ").CopyTo(sector, 3);

        Assert.Throws<InvalidDataException>(() => NtfsBootSector.Parse(sector));
    }

    [Theory]
    [InlineData(513)]   // not a power-of-two-ish valid sector size
    [InlineData(0)]
    [InlineData(65535)]
    public void Parse_InvalidSectorSize_Throws(ushort badSectorSize)
    {
        var sector = BuildValid(bytesPerSector: badSectorSize);
        Assert.Throws<InvalidDataException>(() => NtfsBootSector.Parse(sector));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]   // not a power of two
    [InlineData(255)] // 255 * 512 exceeds MaxBytesPerCluster and isn't a power of two either
    public void Parse_InvalidSectorsPerCluster_Throws(byte badValue)
    {
        var sector = BuildValid(sectorsPerCluster: badValue);
        Assert.Throws<InvalidDataException>(() => NtfsBootSector.Parse(sector));
    }

    [Fact]
    public void Parse_ZeroTotalSectors_Throws()
    {
        var sector = BuildValid(totalSectors: 0);
        Assert.Throws<InvalidDataException>(() => NtfsBootSector.Parse(sector));
    }

    [Fact]
    public void Parse_NegativeMftStartCluster_Throws()
    {
        var sector = BuildValid(mftStartCluster: -1);
        Assert.Throws<InvalidDataException>(() => NtfsBootSector.Parse(sector));
    }

    [Fact]
    public void Parse_MftStartClusterBeyondVolume_Throws()
    {
        // totalSectors / sectorsPerCluster = 1_000_000 / 8 = 125_000 clusters;
        // a start cluster far past that must be rejected.
        var sector = BuildValid(mftStartCluster: 10_000_000);
        Assert.Throws<InvalidDataException>(() => NtfsBootSector.Parse(sector));
    }

    [Fact]
    public void Parse_ImplausibleFileRecordSize_Throws()
    {
        // clustersPerFileRecordRaw positive and huge => absurd bytes-per-record.
        var sector = BuildValid(clustersPerFileRecordRaw: 100);
        Assert.Throws<InvalidDataException>(() => NtfsBootSector.Parse(sector));
    }

    [Fact]
    public void Parse_TooShortBuffer_Throws()
    {
        Assert.Throws<InvalidDataException>(() => NtfsBootSector.Parse(new byte[10]));
    }
}
