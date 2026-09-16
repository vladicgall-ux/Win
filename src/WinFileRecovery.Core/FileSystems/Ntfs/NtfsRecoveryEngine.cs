using WinFileRecovery.Core.Native;
using WinFileRecovery.Core.Recovery;

namespace WinFileRecovery.Core.FileSystems.Ntfs;

/// <summary>
/// Finds deleted files on an NTFS volume by walking the $MFT and selecting
/// records whose InUse flag is clear but which still carry a $FILE_NAME and
/// $DATA attribute — i.e. the entry hasn't been overwritten yet.
/// </summary>
public sealed class NtfsRecoveryEngine
{
    public IEnumerable<RecoverableFile> FindDeletedFiles(
        RawDisk volume,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var boot = NtfsBootSector.Read(volume);
        var reader = new MftReader(volume, boot);

        foreach (var record in reader.ReadAll(progress, cancellationToken))
        {
            if (!record.IsDeleted) continue;
            if (record.IsDirectory) continue;
            if (!record.HasFileName) continue;
            if (record.LogicalFileSize <= 0) continue;

            string ext = GetExtension(record.FileName!);

            if (record.DataIsResident)
            {
                // Resident data lives inside the MFT record itself, already
                // captured in memory by MftRecord.Parse — no disk offset needed.
                yield return new RecoverableFile(
                    Extension: ext,
                    StartOffsetBytes: 0,
                    LengthBytes: record.LogicalFileSize,
                    Source: RecoverySource.NtfsMft,
                    OriginalName: record.FileName,
                    IsDeleted: true,
                    ResidentData: record.ResidentData);
                continue;
            }

            if (record.DataRuns.Count == 0) continue;

            var extents = BuildExtents(record.DataRuns, boot.BytesPerCluster, record.LogicalFileSize);
            if (extents.Count == 0) continue;

            yield return new RecoverableFile(
                Extension: ext,
                StartOffsetBytes: extents[0].OffsetBytes,
                LengthBytes: record.LogicalFileSize,
                Source: RecoverySource.NtfsMft,
                OriginalName: record.FileName,
                IsDeleted: true,
                Extents: extents);
        }
    }

    private static List<FileExtent> BuildExtents(List<DataRun> runs, int bytesPerCluster, long logicalFileSize)
    {
        var extents = new List<FileExtent>(runs.Count);
        long remaining = logicalFileSize;

        foreach (var run in runs)
        {
            if (remaining <= 0) break;

            long runBytes = run.ClusterCount * bytesPerCluster;
            long take = Math.Min(runBytes, remaining);

            extents.Add(new FileExtent(run.StartCluster * bytesPerCluster, take));
            remaining -= take;
        }

        return extents;
    }

    private static string GetExtension(string fileName)
    {
        int dot = fileName.LastIndexOf('.');
        return dot >= 0 && dot < fileName.Length - 1 ? fileName[(dot + 1)..] : "bin";
    }
}
