using System;
using System.Globalization;
using System.Windows.Data;

namespace NurMarketKassa.Converters
{
    public class StringTruncationConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            // Обработка null
            if (value is null) return string.Empty;

            string input = value.ToString() ?? string.Empty; // Защита от null
            int maxLength = 30; // По умолчанию

            // Парсинг параметра (длина обрезки)
            if (parameter is string paramStr && int.TryParse(paramStr, out int paramLength))
                maxLength = paramLength;
            else if (parameter is int intParam)
                maxLength = intParam;

            // Обрезка
            return input.Length > maxLength
                ? input.Substring(0, maxLength) + "..."
                : input;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}