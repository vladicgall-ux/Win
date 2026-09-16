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
    /// <summary>A single carved/parsed file is never trusted to be this big — protects against a corrupt LengthBytes turning into a runaway allocation or write.</summary>
    public const long MaxSingleFileSizeBytes = 200L * 1024 * 1024 * 1024; // 200 GiB

    public RecoveredFileResult RecoverTo(RawDisk disk, RecoverableFile file, string destinationPath, bool verifyIntegrity = true)
    {
        ValidateFile(disk, file);

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

    /// <summary>
    /// Rejects a file whose metadata (size, offsets) is nonsensical or
    /// implausibly large before any read/allocation happens — this is
    /// distinct from RawDisk's own per-read bounds checks, which only catch
    /// a bad *range*, not a technically-in-range but absurd overall size.
    /// </summary>
    private static void ValidateFile(RawDisk disk, RecoverableFile file)
    {
        if (file.ResidentData is not null) return; // small, already-in-memory — no offset/length to validate

        if (file.LengthBytes <= 0)
            throw new InvalidDataException($"Некорректный размер файла: {file.LengthBytes} байт.");
        if (file.LengthBytes > MaxSingleFileSizeBytes)
            throw new InvalidDataException($"Заявленный размер файла ({file.LengthBytes:N0} байт) превышает допустимый предел.");

        var extents = file.Extents ?? new[] { new FileExtent(file.StartOffsetBytes, file.LengthBytes) };
        long totalExtentBytes = 0;

        foreach (var extent in extents)
        {
            if (extent.OffsetBytes < 0 || extent.LengthBytes <= 0)
                throw new InvalidDataException("Повреждённые метаданные восстанавливаемого файла (отрицательное смещение или длина).");
            if (disk.LengthBytes >= 0 && extent.OffsetBytes + extent.LengthBytes > disk.LengthBytes)
                throw new InvalidDataException("Восстанавливаемый файл выходит за пределы диска — метаданные повреждены.");

            totalExtentBytes += extent.LengthBytes;
            if (totalExtentBytes > MaxSingleFileSizeBytes)
                throw new InvalidDataException("Заявленный размер файла превышает допустимый предел.");
        }
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
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string normalizedDestination = NormalizeDirectory(destinationDirectory);
        int done = 0;

        foreach (var file in list)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string path;
            try
            {
                string safeName = MakeUniqueSafeFileName(file.DisplayName, usedNames);
                path = ResolveWithinDestination(destinationDirectory, normalizedDestination, safeName);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A malformed name/path shouldn't be possible after
                // MakeSafeFileName, but treat it the same as any other
                // per-file failure rather than trust it blindly.
                results.Add(new RecoveredFileResult(file.DisplayName, "", 0, "", AuthenticodeStatus.Unknown, ex.Message));
                done++;
                progress?.Report((done, list.Count));
                continue;
            }

            try
            {
                // One corrupt file's bad metadata (invalid offset, absurd
                // size, out-of-range extent) must not abort recovery of
                // everything else the user selected.
                results.Add(RecoverTo(disk, file, path, verifyIntegrity));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new RecoveredFileResult(file.DisplayName, path, 0, "", AuthenticodeStatus.Unknown, ex.Message));
            }

            done++;
            progress?.Report((done, list.Count));
        }

        if (writeReport)
            WriteReport(destinationDirectory, results);

        return results;
    }

    private static string NormalizeDirectory(string directory) =>
        Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        + Path.DirectorySeparatorChar;

    /// <summary>
    /// Combines destinationDirectory with fileName and verifies the result
    /// actually lands inside that directory. MakeSafeFileName already
    /// strips path separators and "." / "..", but this is the real
    /// guarantee against path traversal — it checks the fact, not just the
    /// input that was supposed to prevent it.
    /// </summary>
    private static string ResolveWithinDestination(string destinationDirectory, string normalizedDestination, string fileName)
    {
        string candidate = Path.GetFullPath(Path.Combine(destinationDirectory, fileName));
        if (!candidate.StartsWith(normalizedDestination, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Недопустимое имя файла (попытка выхода за пределы папки назначения).");

        return candidate;
    }

    private static void WriteReport(string destinationDirectory, List<RecoveredFileResult> results)
    {
        string reportPath = Path.Combine(destinationDirectory, "recovery_report.csv");
        var sb = new StringBuilder();
        sb.AppendLine("OriginalName,DestinationFile,SizeBytes,SHA-256,AuthenticodeStatus,Error");

        foreach (var r in results)
        {
            sb.AppendLine(string.Join(',',
                CsvEscape(r.OriginalDisplayName),
                CsvEscape(string.IsNullOrEmpty(r.DestinationPath) ? "" : Path.GetFileName(r.DestinationPath)),
                r.SizeBytes,
                r.Sha256,
                r.SignatureStatus,
                CsvEscape(r.Error ?? "")));
        }

        File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
    }

    private static string CsvEscape(string value) =>
        value.Contains(',') || value.Contains('"')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;

    private const int MaxFileNameLength = 200;

    /// <summary>
    /// Sanitizes a name that ultimately comes from untrusted on-disk
    /// metadata (an NTFS $FILE_NAME, a FAT short name, or a synthesized
    /// carving name) before it is ever used to build a filesystem path.
    /// </summary>
    private static string MakeSafeFileName(string name)
    {
        // Path separators and invalid chars first — a name containing "/"
        // or "\" must not be allowed to reintroduce path structure.
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            bool isPathSeparator = c is '/' or '\\';
            bool isControlChar = char.IsControl(c);
            bool isInvalid = isPathSeparator || isControlChar || Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0;
            sb.Append(isInvalid ? '_' : c);
        }

        string sanitized = sb.ToString().Trim();

        // "." / ".." (and anything that sanitizes down to just dots/spaces)
        // are directory-traversal or no-op names on Windows — never valid
        // as a real recovered file's name.
        if (sanitized.Trim('.', ' ').Length == 0)
            sanitized = "";

        if (sanitized.Length == 0)
            sanitized = "recovered_file";

        if (sanitized.Length > MaxFileNameLength)
        {
            string ext = Path.GetExtension(sanitized);
            if (ext.Length > 32) ext = ""; // a "huge extension" is itself a sign the name is garbage
            sanitized = sanitized[..(MaxFileNameLength - ext.Length)] + ext;
        }

        return sanitized;
    }

    /// <summary>Appends " (1)", " (2)", ... on a name collision, matching Windows Explorer's own convention.</summary>
    private static string MakeUniqueSafeFileName(string displayName, HashSet<string> usedNames)
    {
        string safeName = MakeSafeFileName(displayName);
        if (usedNames.Add(safeName)) return safeName;

        string ext = Path.GetExtension(safeName);
        string stem = safeName[..^ext.Length];

        for (int i = 1; ; i++)
        {
            string candidate = $"{stem} ({i}){ext}";
            if (usedNames.Add(candidate)) return candidate;
        }
    }
}
