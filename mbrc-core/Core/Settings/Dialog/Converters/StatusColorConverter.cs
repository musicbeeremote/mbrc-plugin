using System;
using System.Globalization;

namespace MusicBeeRemote.Core.Settings.Dialog.Converters
{
    public class StatusColorConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var active = value as bool? ?? false;
            //return new SolidColorBrush(active ? Colors.Green : Colors.Red);
            return null;
        }
      
    }
}