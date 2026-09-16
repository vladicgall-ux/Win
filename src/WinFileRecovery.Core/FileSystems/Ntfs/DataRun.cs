namespace WinFileRecovery.Core.FileSystems.Ntfs;

/// <summary>One contiguous cluster range backing a non-resident attribute.</summary>
public readonly record struct DataRun(long StartCluster, long ClusterCount);

/// <summary>
/// Decodes the compact run-length "data runs" byte stream NTFS uses to
/// describe non-resident attribute storage (e.g. $DATA for a normal file).
/// </summary>
    // A run claiming more clusters than any real volume could have is a
    // sure sign of corruption; reject rather than let it flow into a huge
    // allocation or read range further down the pipeline. 2^40 clusters is
    // already far larger than any real NTFS volume (petabytes at 4 KiB/cluster).
    private const long MaxPlausibleClusterCount = 1L << 40;

    public static List<DataRun> Parse(byte[] buffer, int offset, int length, long? maxCluster = null)
    {
        var runs = new List<DataRun>();
        if (offset < 0 || offset > buffer.Length || length < 0) return runs;

        long currentLcn = 0;
        int pos = offset;
        // Clamp to the buffer's real bounds: a corrupt record could declare
        // a run-list length that overruns the record, and buffer[pos++]
        // below would throw IndexOutOfRangeException otherwise.
        int end = (int)Math.Min((long)offset + length, buffer.Length);

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

            // Reject an individually corrupt run instead of trusting it: a
            // negative or out-of-volume cluster, or an absurd run length,
            // would otherwise become a negative/oversized byte offset once
            // multiplied by the cluster size downstream.
            bool runIsValid = runLength > 0
                && runLength <= MaxPlausibleClusterCount
                && currentLcn >= 0
                && (maxCluster is null || currentLcn + runLength <= maxCluster.Value);

            if (offsetFieldSize > 0 && runIsValid)
                runs.Add(new DataRun(currentLcn, runLength));
            else if (offsetFieldSize > 0)
                break; // stop at the first bad run rather than risk resuming out of sync
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
