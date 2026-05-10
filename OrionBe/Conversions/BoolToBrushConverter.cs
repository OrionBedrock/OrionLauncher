using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace OrionBe.Conversions;

public sealed class BoolToBrushConverter : IValueConverter
{
    public IBrush TrueBrush { get; set; } = Brushes.Transparent;

    public IBrush FalseBrush { get; set; } = Brushes.Transparent;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TrueBrush : FalseBrush;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
