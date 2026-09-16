using WinFileRecovery.Core.Native;

namespace WinFileRecovery.Core.FileSystems.Ntfs;

/// <summary>
/// Streams MFT records off a volume. The $MFT itself is a file (record 0),
/// but for scanning purposes we read it as raw clusters starting at the
/// boot sector's MftStartCluster, which works even when record 0's own
/// $DATA runs are damaged, as long as the table is mostly contiguous.
/// </summary>
public sealed class MftReader
{
    private readonly RawDisk _volume;
    private readonly NtfsBootSector _boot;

    public MftReader(RawDisk volume, NtfsBootSector boot)
    {
        _volume = volume;
        _boot = boot;
    }

    public IEnumerable<MftRecord> ReadAll(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        int recordSize = _boot.BytesPerFileRecord;
        int sectorsPerRecord = recordSize / _boot.BytesPerSector;
        long mftStartSector = _boot.MftStartCluster * _boot.SectorsPerCluster;

        // First, parse record 0 ($MFT itself) to learn its real extent via
        // its own $DATA data runs; fall back to a heuristic scan length if
        // that fails (e.g. record 0 is damaged).
        byte[] firstRecordRaw = _volume.ReadSectors(mftStartSector, sectorsPerRecord);
        var mftSelfRecord = MftRecord.Parse(firstRecordRaw, 0, _boot.BytesPerSector);

        long totalRecords = EstimateTotalRecords(mftSelfRecord, recordSize);

        long recordsRead = 0;
        foreach (var run in EnumerateMftClusterRuns(mftSelfRecord, mftStartSector))
        {
            long sector = run.StartSector;
            long sectorsRemaining = run.SectorCount;

            while (sectorsRemaining >= sectorsPerRecord)
            {
                cancellationToken.ThrowIfCancellationRequested();

                byte[] raw;
                try
                {
                    raw = _volume.ReadSectors(sector, sectorsPerRecord);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Read past the end of the device, or a bad sector —
                    // stop this run rather than aborting the whole scan;
                    // records already found stay valid.
                    yield break;
                }

                if (raw.Length < sectorsPerRecord * _boot.BytesPerSector)
                    yield break; // short read: reached the real end of the device

                MftRecord? rec = null;
                try
                {
                    rec = MftRecord.Parse(raw, recordsRead, _boot.BytesPerSector);
                }
                catch (Exception)
                {
                    // A single corrupt/garbage record shouldn't abort
                    // scanning the rest of the table.
                }

                if (rec is not null)
                    yield return rec;

                sector += sectorsPerRecord;
                sectorsRemaining -= sectorsPerRecord;
                recordsRead++;

                if (totalRecords > 0 && recordsRead % 256 == 0)
                    progress?.Report(Math.Min(1.0, (double)recordsRead / totalRecords));
            }
        }

        progress?.Report(1.0);
    }

    private IEnumerable<(long StartSector, long SectorCount)> EnumerateMftClusterRuns(MftRecord? mftSelf, long fallbackStartSector)
    {
        if (mftSelf?.DataRuns is { Count: > 0 })
        {
            foreach (var run in mftSelf.DataRuns)
            {
                long startSector = run.StartCluster * _boot.SectorsPerCluster;
                long sectorCount = run.ClusterCount * _boot.SectorsPerCluster;
                yield return (startSector, sectorCount);
            }
            yield break;
        }

        // Fallback: assume contiguity for a generous span; still bounded by
        // the volume length via RawDisk itself.
        yield return (fallbackStartSector, 2_000_000); // ~1 GiB worth of records
    }

    private long EstimateTotalRecords(MftRecord? mftSelf, int recordSize)
    {
        if (mftSelf is { LogicalFileSize: > 0 })
            return mftSelf.LogicalFileSize / recordSize;
        return 0;
    }
}
