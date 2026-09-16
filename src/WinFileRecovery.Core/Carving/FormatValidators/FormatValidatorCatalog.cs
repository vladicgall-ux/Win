namespace WinFileRecovery.Core.Carving.FormatValidators;

internal static class FormatValidatorCatalog
{
    private static readonly IReadOnlyDictionary<string, IFormatValidator> ByExtension =
        BuildMap();

    private static Dictionary<string, IFormatValidator> BuildMap()
    {
        var jpeg = new JpegFormatValidator();
        var png = new PngFormatValidator();
        var zip = new ZipFormatValidator();
        return new Dictionary<string, IFormatValidator>(StringComparer.OrdinalIgnoreCase)
        {
            [jpeg.Extension] = jpeg,
            [png.Extension] = png,
            [zip.Extension] = zip,
            ["docx"] = zip, // docx/xlsx/pptx are ZIP containers under a different carved extension
        };
    }

    public static IFormatValidator? TryGet(string extension) =>
        ByExtension.TryGetValue(extension, out var validator) ? validator : null;
}
