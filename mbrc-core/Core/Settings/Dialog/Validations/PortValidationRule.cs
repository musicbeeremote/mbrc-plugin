using System;
using System.Globalization;
using System.Windows.Controls;

namespace MusicBeeRemote.Core.Settings.Dialog.Validations
{
    public class PortValidationRule : ValidationRule
    {
        public const uint MinPort = 1;
        public const uint MaxPort = 65535;

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
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
            catch (Exception exception)
            {
                return new ValidationResult(false, $"Invalid characters or {exception.Message}");
            }

            if (port < MinPort || port > MaxPort)
            {
                return new ValidationResult(false, $"Please enter port in the range [${MinPort}, ${MaxPort}]");
            }

            return new ValidationResult(true, null);
        }
    }
}