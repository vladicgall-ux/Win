namespace WinFileRecovery.Core.FileSystems.Ntfs;

/// <summary>One contiguous cluster range backing a non-resident attribute.</summary>
public readonly record struct DataRun(long StartCluster, long ClusterCount);

/// <summary>
/// Decodes the compact run-length "data runs" byte stream NTFS uses to
/// describe non-resident attribute storage (e.g. $DATA for a normal file).
/// </summary>
public static class DataRunParser
{
    public static List<DataRun> Parse(byte[] buffer, int offset, int length)
    {
        var runs = new List<DataRun>();
        long currentLcn = 0;
        int pos = offset;
        int end = offset + length;

        while (pos < end)
        {
            byte header = buffer[pos++];
            if (header == 0) break; // terminator

            int lengthFieldSize = header & 0x0F;
            int offsetFieldSize = (header >> 4) & 0x0F;

            if (lengthFieldSize == 0 || pos + lengthFieldSize > end) break;

            long runLength = ReadLittleEndian(buffer, pos, lengthFieldSize, signed: false);
            pos += lengthFieldSize;

            long runOffset = 0;
            if (offsetFieldSize > 0)
            {
                if (pos + offsetFieldSize > end) break;
                runOffset = ReadLittleEndian(buffer, pos, offsetFieldSize, signed: true);
                pos += offsetFieldSize;
                currentLcn += runOffset;
            }
            // offsetFieldSize == 0 => sparse run (no physical clusters);
            // currentLcn stays unchanged, and we skip emitting a run.

            if (offsetFieldSize > 0)
                runs.Add(new DataRun(currentLcn, runLength));
        }

        return runs;
    }

    private static long ReadLittleEndian(byte[] buffer, int offset, int size, bool signed)
    {
        long value = 0;
        for (int i = 0; i < size; i++)
            value |= (long)buffer[offset + i] << (8 * i);

        if (signed && size < 8)
        {
            long signBit = 1L << (size * 8 - 1);
            if ((value & signBit) != 0)
                value -= 1L << (size * 8);
        }
        return value;
    }
}
