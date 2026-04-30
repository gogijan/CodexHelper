using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CodexHelper.Infrastructure;

public sealed class MinimumThumbViewportConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 4 ||
            !TryGetFiniteDouble(values[0], out var viewportSize) ||
            !TryGetFiniteDouble(values[1], out var minimum) ||
            !TryGetFiniteDouble(values[2], out var maximum) ||
            !TryGetFiniteDouble(values[3], out var trackLength) ||
            !TryGetMinimumThumbLength(parameter, culture, out var minimumThumbLength))
        {
            return Binding.DoNothing;
        }

        var range = maximum - minimum;
        if (viewportSize <= 0 || range <= 0 || trackLength <= 0)
        {
            return viewportSize;
        }

        if (trackLength <= minimumThumbLength)
        {
            return range * 1000;
        }

        var naturalThumbLength = trackLength * viewportSize / (range + viewportSize);
        if (naturalThumbLength >= minimumThumbLength)
        {
            return viewportSize;
        }

        var adjustedViewportSize = minimumThumbLength * range / (trackLength - minimumThumbLength);
        return Math.Max(viewportSize, adjustedViewportSize);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static bool TryGetFiniteDouble(object? value, out double result)
    {
        result = 0;

        if (value is null || ReferenceEquals(value, DependencyProperty.UnsetValue))
        {
            return false;
        }

        try
        {
            result = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return !double.IsNaN(result) && !double.IsInfinity(result);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
    }

    private static bool TryGetMinimumThumbLength(object? parameter, CultureInfo culture, out double result)
    {
        result = 0;

        if (parameter is double doubleValue)
        {
            result = doubleValue;
            return result > 0;
        }

        if (parameter is string text &&
            double.TryParse(text, NumberStyles.Float, culture, out var parsed))
        {
            result = parsed;
            return result > 0;
        }

        return false;
    }
}
