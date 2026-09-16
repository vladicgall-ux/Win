using System.Globalization;
using System.Windows.Data;

namespace WinFileRecovery.App.Converters;

/// <summary>Formats a raw byte count as "465,3 ГБ" / "12,4 МБ" instead of a long digit string, for a friendlier UI.</summary>
public sealed class BytesToReadableConverter : IValueConverter
{
    private static readonly string[] Units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        long bytes = value switch
        {
            long l => l,
            int i => i,
            _ => 0,
        };

        double size = bytes;
        int unitIndex = 0;
        while (size >= 1024 && unitIndex < Units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{size:0} {Units[unitIndex]}"
            : $"{size:0.#} {Units[unitIndex]}";
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
