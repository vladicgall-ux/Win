using WinFileRecovery.Core.Native;

namespace WinFileRecovery.Core.Carving.FormatValidators;

/// <summary>
/// Determines a carved file's real length by walking its internal
/// structure (markers, chunks, directory records), instead of trusting the
/// first occurrence of a short footer byte pattern — which for formats
/// like JPEG can appear inside compressed scan data long before the file's
/// actual end, producing a truncated recovery.
/// </summary>
internal interface IFormatValidator
{
    string Extension { get; }

    /// <summary>
    /// Returns the file's true length in bytes starting at
    /// <paramref name="headerOffset"/>, or null if the structure could not
    /// be walked to a confident end (caller falls back to the header/footer
    /// heuristic in that case).
    /// </summary>
    long? DetermineLength(RawDisk disk, long headerOffset, long maxSizeBytes);
}
