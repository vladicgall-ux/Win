using WinFileRecovery.Core.Native;
using WinFileRecovery.Core.Recovery;

namespace WinFileRecovery.Core.FileSystems.Fat32;

/// <summary>
/// Walks FAT32 directory structures looking for deleted entries. The FAT
/// chain for a deleted file is normally already cleared, so — like every
/// mainstream FAT undelete tool — recovery assumes the file's clusters were
/// contiguous starting at StartCluster and reads exactly enough clusters to
/// cover FileSize. This works for the common case (files that weren't
/// fragmented) and is the best available signal once the chain is gone.
/// </summary>
public sealed class Fat32RecoveryEngine
{
    public IEnumerable<RecoverableFile> FindDeletedFiles(
        RawDisk volume,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var boot = Fat32BootSector.Read(volume);

        var directoriesToVisit = new Queue<uint>();
        directoriesToVisit.Enqueue(boot.RootCluster);
        var visited = new HashSet<uint>();

        while (directoriesToVisit.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            uint dirCluster = directoriesToVisit.Dequeue();
            if (!visited.Add(dirCluster)) continue;

            foreach (var (entry, subDirCluster) in ReadDirectory(volume, boot, dirCluster))
            {
                if (subDirCluster is { } sub && sub >= 2)
                {
                    directoriesToVisit.Enqueue(sub);
                    continue;
                }

                if (!entry.IsDeleted || entry.IsDirectory || entry.IsVolumeLabel) continue;
                if (entry.FileSize == 0 || entry.StartCluster < 2) continue;

                long offset = boot.ClusterToSector(entry.StartCluster) * boot.BytesPerSector;

                yield return new RecoverableFile(
                    Extension: GetExtension(entry.ShortName),
                    StartOffsetBytes: offset,
                    LengthBytes: entry.FileSize,
                    Source: RecoverySource.Fat32DirectoryEntry,
                    OriginalName: entry.ShortName,
                    IsDeleted: true,
                    EstimatedDeletionUtc: entry.LastWriteTime);
            }
        }

        progress?.Report(1.0);
    }

    private IEnumerable<(Fat32DirectoryEntry Entry, uint? SubDirCluster)> ReadDirectory(
        RawDisk volume, Fat32BootSector boot, uint startCluster)
    {
        long sector = boot.ClusterToSector(startCluster);
        byte[] clusterData = volume.ReadSectors(sector, boot.SectorsPerCluster);

        for (int offset = 0; offset + 32 <= clusterData.Length; offset += 32)
        {
            var entry = Fat32DirectoryEntry.Parse(clusterData, offset);
            if (entry is null) yield break; // 0x00 marker: no more entries in this cluster

            uint? subDir = (!entry.IsDeleted && entry.IsDirectory && entry.StartCluster >= 2)
                ? entry.StartCluster
                : null;

            yield return (entry, subDir);
        }
    }

    private static string GetExtension(string shortName)
    {
        int dot = shortName.LastIndexOf('.');
        return dot >= 0 && dot < shortName.Length - 1 ? shortName[(dot + 1)..].ToLowerInvariant() : "bin";
    }
}
