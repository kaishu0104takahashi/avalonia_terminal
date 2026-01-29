using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace avalonia_terminal.Converters;

public class CaseConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // value(IsUpperCase) が true なら大文字、false なら小文字にして返す
        if (value is bool isUpper && parameter is string text)
        {
            return isUpper ? text.ToUpper() : text.ToLower();
        }
        return parameter;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
