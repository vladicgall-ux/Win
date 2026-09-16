namespace WinFileRecovery.Core.Carving;

public static class SignatureCatalog
{
    public static readonly IReadOnlyList<FileSignature> Default = new List<FileSignature>
    {
        FileSignature.WithFooter("jpg", 20 * 1024 * 1024,
            header: new byte[] { 0xFF, 0xD8, 0xFF },
            footer: new byte[] { 0xFF, 0xD9 }),

        FileSignature.WithFooter("png", 30 * 1024 * 1024,
            header: new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
            footer: new byte[] { 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 }),

        FileSignature.WithFooter("gif", 15 * 1024 * 1024,
            header: new byte[] { 0x47, 0x49, 0x46, 0x38 },
            footer: new byte[] { 0x00, 0x3B }),

        FileSignature.WithFooter("pdf", 100 * 1024 * 1024,
            header: new byte[] { 0x25, 0x50, 0x44, 0x46 },
            footer: new byte[] { 0x25, 0x25, 0x45, 0x4F, 0x46 }),

        FileSignature.Simple("zip", 200 * 1024 * 1024,
            0x50, 0x4B, 0x03, 0x04),

        // docx/xlsx/pptx are ZIP containers; same header, disambiguated
        // post-extraction by inspecting the archive's internal content types.
        FileSignature.Simple("docx", 50 * 1024 * 1024,
            0x50, 0x4B, 0x03, 0x04),

        FileSignature.WithFooter("mp4", 500 * 1024 * 1024,
            header: new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 },
            footer: Array.Empty<byte>()),

        FileSignature.Simple("wav", 500 * 1024 * 1024,
            0x52, 0x49, 0x46, 0x46),

        FileSignature.Simple("mp3", 50 * 1024 * 1024,
            0x49, 0x44, 0x33),

        FileSignature.Simple("rar", 200 * 1024 * 1024,
            0x52, 0x61, 0x72, 0x21, 0x1A, 0x07),

        FileSignature.Simple("exe", 200 * 1024 * 1024,
            0x4D, 0x5A),
    };
}
