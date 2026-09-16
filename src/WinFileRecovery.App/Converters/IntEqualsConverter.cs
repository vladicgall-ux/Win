using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WinFileRecovery.App.Converters;

/// <summary>Binds a RadioButton's IsChecked to one value of an int "selected index" property.</summary>
public sealed class IntEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int current = value is int i ? i : 0;
        int target = System.Convert.ToInt32(parameter, CultureInfo.InvariantCulture);
        return current == target;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isChecked = value is bool b && b;
        if (!isChecked) return Binding.DoNothing;
        return System.Convert.ToInt32(parameter, CultureInfo.InvariantCulture);
    }
}
