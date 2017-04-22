using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MusicBeeRemote.Core.Settings.Dialog.Converters
{
    public class StatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var active = value as bool? ?? false;
            return new SolidColorBrush(active ? Colors.Green : Colors.Red);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}