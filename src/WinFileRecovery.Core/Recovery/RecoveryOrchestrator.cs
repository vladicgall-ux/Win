using WinFileRecovery.Core.Native;

namespace WinFileRecovery.Core.Recovery;

/// <summary>
/// Writes a previously found <see cref="RecoverableFile"/> back out to a
/// normal file on a (different, healthy) destination volume, reading its
/// bytes straight from the raw device by offset.
/// </summary>
public sealed class RecoveryOrchestrator
{
    public void RecoverTo(RawDisk disk, RecoverableFile file, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);

        if (file.ResidentData is { } resident)
        {
            output.Write(resident, 0, resident.Length);
            return;
        }

        var extents = file.Extents ?? new[] { new FileExtent(file.StartOffsetBytes, file.LengthBytes) };

        foreach (var extent in extents)
            CopyExtent(disk, extent, output);
    }

    private static void CopyExtent(RawDisk disk, FileExtent extent, FileStream output)
    {
        long remaining = extent.LengthBytes;
        long sector = extent.OffsetBytes / disk.SectorSize;
        int leadingSkip = (int)(extent.OffsetBytes % disk.SectorSize);

        const int readSectorsPerChunk = 2048; // 1 MiB chunks
        bool first = true;

        while (remaining > 0)
        {
            byte[] data = disk.ReadSectors(sector, readSectorsPerChunk);
            if (data.Length == 0) break;

            int offsetInChunk = first ? leadingSkip : 0;
            int available = data.Length - offsetInChunk;
            int toWrite = (int)Math.Min(available, remaining);
            if (toWrite <= 0) break;

            output.Write(data, offsetInChunk, toWrite);

            remaining -= toWrite;
            sector += readSectorsPerChunk;
            first = false;
        }
    }

    public void RecoverMany(
        RawDisk disk,
        IEnumerable<RecoverableFile> files,
        string destinationDirectory,
        IProgress<(int done, int total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var list = files as IList<RecoverableFile> ?? files.ToList();
        int done = 0;

        foreach (var file in list)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string safeName = MakeSafeFileName(file.DisplayName);
            string path = Path.Combine(destinationDirectory, safeName);
            RecoverTo(disk, file, path);
            done++;
            progress?.Report((done, list.Count));
        }
    }

    private static string MakeSafeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
