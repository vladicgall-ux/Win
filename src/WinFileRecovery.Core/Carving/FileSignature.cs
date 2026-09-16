namespace WinFileRecovery.Core.Carving;

/// <summary>
/// Header (and optional footer) byte pattern used to carve a file type out
/// of raw, unallocated disk space when filesystem metadata is gone.
/// </summary>
public sealed record FileSignature(
    string Extension,
    byte[] Header,
    byte[]? Footer,
    long MaxSizeBytes)
{
    public static FileSignature Simple(string ext, long maxSize, params byte[] header) =>
        new(ext, header, null, maxSize);

    public static FileSignature WithFooter(string ext, long maxSize, byte[] header, byte[] footer) =>
        new(ext, header, footer, maxSize);
}
