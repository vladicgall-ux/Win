using WinFileRecovery.Core.FileSystems.Ntfs;
using Xunit;

namespace WinFileRecovery.Core.Tests;

public class DataRunParserTests
{
    [Fact]
    public void Parse_SingleValidRun_ReturnsIt()
    {
        // header: length field 1 byte, offset field 1 byte; length=5, offset=+10
        byte[] buffer = { 0x11, 0x05, 0x0A, 0x00 };

        var runs = DataRunParser.Parse(buffer, 0, buffer.Length);

        Assert.Single(runs);
        Assert.Equal(10, runs[0].StartCluster);
        Assert.Equal(5, runs[0].ClusterCount);
    }

    [Fact]
    public void Parse_TwoValidRuns_AccumulatesClusterOffset()
    {
        // Run 1: length=5, offset=+10 -> cluster 10
        // Run 2: length=3, offset=+20 -> cluster 30 (10 + 20)
        byte[] buffer = { 0x11, 0x05, 0x0A, 0x11, 0x03, 0x14, 0x00 };

        var runs = DataRunParser.Parse(buffer, 0, buffer.Length);

        Assert.Equal(2, runs.Count);
        Assert.Equal(10, runs[0].StartCluster);
        Assert.Equal(30, runs[1].StartCluster);
    }

    [Fact]
    public void Parse_NegativeResultingCluster_RejectsAndStops()
    {
        // offset=-5 (0xFB as signed byte) on an empty run list => currentLcn goes negative.
        byte[] buffer = { 0x11, 0x05, 0xFB, 0x00 };

        var runs = DataRunParser.Parse(buffer, 0, buffer.Length);

        Assert.Empty(runs);
    }

    [Fact]
    public void Parse_SecondRunCorrupt_KeepsFirstValidRunOnly()
    {
        // Run 1: valid (length=5, offset=+10).
        // Run 2: header claims a 5-byte offset field but the buffer is cut
        // short right after, which must stop parsing rather than read past the buffer.
        byte[] buffer = { 0x11, 0x05, 0x0A, 0x51, 0x02 };

        var runs = DataRunParser.Parse(buffer, 0, buffer.Length);

        Assert.Single(runs);
        Assert.Equal(10, runs[0].StartCluster);
    }

    [Fact]
    public void Parse_RunBeyondMaxCluster_Rejected()
    {
        // length=100 clusters starting at offset 10, with a maxCluster of 50 — run overruns the volume.
        byte[] buffer = { 0x11, 100, 0x0A, 0x00 };

        var runs = DataRunParser.Parse(buffer, 0, buffer.Length, maxCluster: 50);

        Assert.Empty(runs);
    }

    [Fact]
    public void Parse_NegativeOffsetArgument_ReturnsEmptyWithoutThrowing()
    {
        byte[] buffer = { 0x11, 0x05, 0x0A, 0x00 };

        var runs = DataRunParser.Parse(buffer, offset: -1, length: 4);

        Assert.Empty(runs);
    }

    [Fact]
    public void Parse_LengthOverrunsBuffer_ClampsWithoutThrowing()
    {
        byte[] buffer = { 0x11, 0x05, 0x0A, 0x00 };

        // Declares far more bytes than the buffer actually has.
        var runs = DataRunParser.Parse(buffer, 0, 1_000_000);

        Assert.Single(runs); // still parses the one real run within the actual buffer bounds
    }

    [Fact]
    public void Parse_EmptyBuffer_ReturnsEmpty()
    {
        var runs = DataRunParser.Parse(Array.Empty<byte>(), 0, 0);
        Assert.Empty(runs);
    }
}
