using System;

namespace MusicBeeRemote.Core.Settings.Dialog.Validations
{
    public class PortValidationRule
    {
        public const uint MinPort = 1;
        public const uint MaxPort = 65535;

        public bool Validate(object value)
        {
            var port = 0;

            try
            {
                var input = value as string;

                if (!string.IsNullOrEmpty(input))
                {
                    port = int.Parse(input);
                }
            }
            catch (Exception)
            {
                return false;
            }

            return port >= MinPort && port <= MaxPort;
        }
    }
}