using System.Globalization;
using System.Windows.Data;

namespace WinFileRecovery.App.Converters;

public sealed class NullableUtcToLocalStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTime utc ? utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", culture) : "неизвестно";

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
