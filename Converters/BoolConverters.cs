using System;
using System.Globalization;
using Avalonia.Data.Converters;
namespace avalonia_terminal.Converters;
public class InvertBoolConverter : IValueConverter {
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b ? !b : false;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b ? !b : false;
}
