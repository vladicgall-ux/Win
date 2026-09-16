namespace WinFileRecovery.Core.Carving;

/// <summary>Groups extensions into user-facing categories for a "what to look for" pre-scan filter.</summary>
public sealed record FileCategory(string Name, IReadOnlyList<string> Extensions)
{
    public static readonly IReadOnlyList<FileCategory> All = new List<FileCategory>
    {
        new("Документы", new[] { "docx", "pdf" }),
        new("Фото", new[] { "jpg", "png", "gif" }),
        new("Видео", new[] { "mp4" }),
        new("Аудио", new[] { "wav", "mp3" }),
        new("Архивы", new[] { "zip", "rar" }),
        new("Программы", new[] { "exe" }),
    };
}
