using System;
using System.Globalization;

namespace MusicBeeRemote.Core.Settings.Dialog.Validations
{
    public static class PortValidationRule
    {
        private const uint MinPort = 1;
        private const uint MaxPort = 65535;

        public static bool Validate(object value)
        {
            var port = 0;

            try
            {
                var input = value as string;

                if (!string.IsNullOrEmpty(input))
                {
                    port = int.Parse(input, CultureInfo.CurrentCulture);
                }
            }
            catch
            {
                return false;
            }

            return port >= MinPort && port <= MaxPort;
        }
    }
}
