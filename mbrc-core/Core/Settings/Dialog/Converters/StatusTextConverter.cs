using System;
using System.Globalization;
using System.Windows.Data;

namespace MusicBeeRemote.Core.Settings.Dialog.Converters
{
    public class StatusTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var active = value as bool? ?? false;
            return active ? "Running" : "Stopped";           
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}