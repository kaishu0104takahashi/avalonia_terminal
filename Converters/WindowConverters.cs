using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace avalonia_terminal.Converters;

// bool(IsFullScreen) -> WindowState
public class BoolToWindowStateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value is bool b && b) ? WindowState.FullScreen : WindowState.Normal;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value is WindowState state && state == WindowState.FullScreen);
    }
}

// bool(IsFullScreen) -> SystemDecorations
public class BoolToSystemDecorationsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value is bool b && b) ? SystemDecorations.None : SystemDecorations.Full;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

// bool(IsFullScreen) -> Grid.ColumnSpan (全画面なら2列ぶち抜き、通常なら1列)
public class FullScreenGridSpanConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value is bool b && b) ? 2 : 1;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

// bool(IsFullScreen) -> Grid.Column (全画面なら左端(0)から、通常なら右側(1)に配置)
public class FullScreenGridColumnConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value is bool b && b) ? 0 : 1;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

// bool反転 (IsVisible用)
public class FullScreenInvertBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value is bool b) ? !b : false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value is bool b) ? !b : false;
    }
}
