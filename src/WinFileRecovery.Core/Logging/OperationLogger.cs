using System.Globalization;

namespace WinFileRecovery.Core.Logging;

/// <summary>
/// Append-only, best-effort log of high-level operations (scan/recover):
/// what ran, how long it took, and how many items were found/failed.
///
/// Deliberately never logs: recovered file contents, file names/paths,
/// passwords, or raw disk data — only counts and durations, so the log is
/// safe to attach to a support request without leaking what was on the disk.
/// </summary>
public static class OperationLogger
{
    private static readonly object Lock = new();
    private static string? _logFilePath;

    public static void Configure(string logFilePath) => _logFilePath = logFilePath;

    public static void LogOperation(string operationType, string result, TimeSpan duration, int itemsFound = 0, int errors = 0)
    {
        string line = string.Join('\t',
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            operationType,
            result,
            duration.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + "ms",
            $"items={itemsFound}",
            $"errors={errors}");

        Append(line);
    }

    public static void LogError(string operationType, string message)
    {
        // message is expected to be a short diagnostic (exception message),
        // never file contents or full paths from the scanned disk.
        string line = string.Join('\t',
            DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            operationType,
            "ERROR",
            message.Replace('\t', ' ').Replace('\n', ' '));

        Append(line);
    }

    private static void Append(string line)
    {
        string? path = _logFilePath;
        if (path is null) return;

        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never itself break a scan/recovery — swallow.
        }
    }
}
