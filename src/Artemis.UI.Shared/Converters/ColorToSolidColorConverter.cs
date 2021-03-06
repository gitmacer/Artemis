﻿using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Artemis.UI.Shared
{
    /// <inheritdoc />
    /// <summary>
    ///     Converts <see cref="T:System.Windows.Media.Color" /> into a <see cref="T:System.Windows.Media.Color" /> with full
    ///     opacity.
    /// </summary>
    [ValueConversion(typeof(Color), typeof(string))]
    public class ColorToSolidColorConverter : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var color = (Color) value;
            return Color.FromRgb(color.R, color.G, color.B);
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}