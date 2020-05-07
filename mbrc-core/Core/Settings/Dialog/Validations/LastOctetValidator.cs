using System;
using System.Globalization;
using System.Linq;

namespace MusicBeeRemote.Core.Settings.Dialog.Validations
{
    public static class LastOctetValidator
    {
        public static bool Validate(string address, string lastOctetValue)
        {
            if (address == null)
            {
                return false;
            }

            var lastOctet = !string.IsNullOrEmpty(lastOctetValue) ? int.Parse(lastOctetValue, CultureInfo.CurrentCulture) : 0;

            if (lastOctet > 254 || lastOctet < 1)
            {
                return false;
            }

            if (!AddressValidationRule.Validate(address))
            {
                return false;
            }

            var octets = address.Split(".".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            var baseLastOctet = int.Parse(octets.Last(), CultureInfo.CurrentCulture);

            return lastOctet >= baseLastOctet;
        }
    }
}
