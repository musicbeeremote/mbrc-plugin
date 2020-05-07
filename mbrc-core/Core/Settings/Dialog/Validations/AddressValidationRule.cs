using System.Text.RegularExpressions;

namespace MusicBeeRemote.Core.Settings.Dialog.Validations
{
    public static class AddressValidationRule
    {
        private const string IpAddressRegex =
            @"\b(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b";

        public static bool Validate(object value)
        {
            return value != null && Regex.IsMatch(value.ToString(), IpAddressRegex);
        }
    }
}
