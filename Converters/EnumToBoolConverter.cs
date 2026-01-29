using System;
using System.Globalization;
using Avalonia.Data.Converters;
using avalonia_terminal.ViewModels; 

namespace avalonia_terminal.Converters;

public class EnumToBoolConverter : IValueConverter
{
    public static EnumToBoolConverter IsList { get; } = new(GalleryViewMode.List);
    public static EnumToBoolConverter IsDetail { get; } = new(GalleryViewMode.Detail);

    private readonly GalleryViewMode _targetMode;

    private EnumToBoolConverter(GalleryViewMode targetMode)
    {
        _targetMode = targetMode;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is GalleryViewMode currentMode)
        {
            return currentMode == _targetMode;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
