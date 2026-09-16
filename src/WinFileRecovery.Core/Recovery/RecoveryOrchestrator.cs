using System.Text;
using WinFileRecovery.Core.Native;
using WinFileRecovery.Core.Verification;

namespace WinFileRecovery.Core.Recovery;

/// <summary>
/// Writes a previously found <see cref="RecoverableFile"/> back out to a
/// normal file on a (different, healthy) destination volume, reading its
/// bytes straight from the raw device by offset. After each write, computes
/// a SHA-256 of the recovered bytes and checks for an Authenticode
/// signature, so the caller can tell a clean recovery from a truncated or
/// partially overwritten one.
/// </summary>
public sealed class RecoveryOrchestrator
{
    public RecoveredFileResult RecoverTo(RawDisk disk, RecoverableFile file, string destinationPath, bool verifyIntegrity = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using (var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write))
        {
            if (file.ResidentData is { } resident)
            {
                output.Write(resident, 0, resident.Length);
            }
            else
            {
                var extents = file.Extents ?? new[] { new FileExtent(file.StartOffsetBytes, file.LengthBytes) };
                foreach (var extent in extents)
                    CopyExtent(disk, extent, output);
            }
        }

        string sha256 = verifyIntegrity ? Sha256Hasher.ComputeHex(destinationPath) : "";
        var signatureStatus = verifyIntegrity ? AuthenticodeVerifier.Verify(destinationPath) : AuthenticodeStatus.Unknown;
        long actualSize = new FileInfo(destinationPath).Length;

        return new RecoveredFileResult(file.DisplayName, destinationPath, actualSize, sha256, signatureStatus);
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

    public List<RecoveredFileResult> RecoverMany(
        RawDisk disk,
        IEnumerable<RecoverableFile> files,
        string destinationDirectory,
        IProgress<(int done, int total)>? progress = null,
        CancellationToken cancellationToken = default,
        bool verifyIntegrity = true,
        bool writeReport = true)
    {
        var list = files as IList<RecoverableFile> ?? files.ToList();
        var results = new List<RecoveredFileResult>(list.Count);
        int done = 0;

        foreach (var file in list)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string safeName = MakeSafeFileName(file.DisplayName);
            string path = Path.Combine(destinationDirectory, safeName);
            results.Add(RecoverTo(disk, file, path, verifyIntegrity));
            done++;
            progress?.Report((done, list.Count));
        }

        if (writeReport)
            WriteReport(destinationDirectory, results);

        return results;
    }

    private static void WriteReport(string destinationDirectory, List<RecoveredFileResult> results)
    {
        string reportPath = Path.Combine(destinationDirectory, "recovery_report.csv");
        var sb = new StringBuilder();
        sb.AppendLine("OriginalName,DestinationFile,SizeBytes,SHA-256,AuthenticodeStatus");

        foreach (var r in results)
        {
            sb.AppendLine(string.Join(',',
                CsvEscape(r.OriginalDisplayName),
                CsvEscape(Path.GetFileName(r.DestinationPath)),
                r.SizeBytes,
                r.Sha256,
                r.SignatureStatus));
        }

        File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
    }

    private static string CsvEscape(string value) =>
        value.Contains(',') || value.Contains('"')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;

    private static string MakeSafeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
