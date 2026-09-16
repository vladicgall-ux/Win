using System.Text;
using WinFileRecovery.Core.FileSystems.Fat32;
using Xunit;

namespace WinFileRecovery.Core.Tests;

public class Fat32BootSectorTests
{
    private static byte[] BuildValid(
        ushort bytesPerSector = 512,
        byte sectorsPerCluster = 8,
        ushort reservedSectors = 32,
        byte numberOfFats = 2,
        uint sectorsPerFat = 1000,
        uint rootCluster = 2,
        uint totalSectors = 2_000_000)
    {
        var sector = new byte[512];
        BitConverter.GetBytes(bytesPerSector).CopyTo(sector, 11);
        sector[13] = sectorsPerCluster;
        BitConverter.GetBytes(reservedSectors).CopyTo(sector, 14);
        sector[16] = numberOfFats;
        BitConverter.GetBytes(totalSectors).CopyTo(sector, 32);
        BitConverter.GetBytes(sectorsPerFat).CopyTo(sector, 36);
        BitConverter.GetBytes(rootCluster).CopyTo(sector, 44);
        Encoding.ASCII.GetBytes("FAT32   ").CopyTo(sector, 82);
        return sector;
    }

    [Fact]
    public void Parse_ValidBootSector_Succeeds()
    {
        var boot = Fat32BootSector.Parse(BuildValid());
        Assert.Equal(512, boot.BytesPerSector);
        Assert.Equal(2u, boot.RootCluster);
    }

    [Fact]
    public void Parse_WrongSignature_Throws()
    {
        var sector = BuildValid();
        Encoding.ASCII.GetBytes("NTFS    ").CopyTo(sector, 82);
        Assert.Throws<InvalidDataException>(() => Fat32BootSector.Parse(sector));
    }

    [Fact]
    public void Parse_ZeroSectorsPerFat_Throws()
    {
        var sector = BuildValid(sectorsPerFat: 0);
        Assert.Throws<InvalidDataException>(() => Fat32BootSector.Parse(sector));
    }

    [Fact]
    public void Parse_DataStartBeyondVolume_Throws()
    {
        // reservedSectors + fats*sectorsPerFat must stay well inside totalSectors.
        var sector = BuildValid(sectorsPerFat: 5_000_000);
        Assert.Throws<InvalidDataException>(() => Fat32BootSector.Parse(sector));
    }

    [Fact]
    public void Parse_RootClusterBelowTwo_Throws()
    {
        var sector = BuildValid(rootCluster: 1);
        Assert.Throws<InvalidDataException>(() => Fat32BootSector.Parse(sector));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)] // FAT allows at most 8 copies of the FAT table
    public void Parse_InvalidNumberOfFats_Throws(byte badNumberOfFats)
    {
        var sector = BuildValid(numberOfFats: badNumberOfFats);
        Assert.Throws<InvalidDataException>(() => Fat32BootSector.Parse(sector));
    }
}
