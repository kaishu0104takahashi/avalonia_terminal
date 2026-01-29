using System;
using System.Globalization;
using Avalonia.Data.Converters;
namespace avalonia_terminal.Converters;
public class BooleanToGridColumnConverter : IValueConverter {
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is bool isGalleryActive) return isGalleryActive ? 0 : 1;
        return 1;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Avalonia.Data.BindingOperations.DoNothing;
}
