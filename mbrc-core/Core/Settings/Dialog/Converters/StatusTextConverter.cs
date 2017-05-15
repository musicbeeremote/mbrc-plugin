using System;
using System.Globalization;
using System.Windows.Data;

namespace MusicBeeRemote.Core.Settings.Dialog.Converters
{
    public class StatusTextConverter 
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var active = value as bool? ?? false;
            return active ? "Running" : "Stopped";           
        }        
    }
}