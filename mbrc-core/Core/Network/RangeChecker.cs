using System;
using System.Globalization;
using MusicBeeRemote.Core.Settings.Dialog.Validations;

namespace MusicBeeRemote.Core.Network
{
    public static class RangeChecker
    {
        public static bool AddressInRange(string address, string firstRangeAddress, uint lastOctet)
        {
            if (address == null)
            {
                throw new ArgumentNullException(nameof(address));
            }

            if (firstRangeAddress == null)
            {
                throw new ArgumentNullException(nameof(firstRangeAddress));
            }

            var addressIsValid = AddressValidationRule.Validate(address);
            var rangeStartIsValid = AddressValidationRule.Validate(firstRangeAddress);
            var lastOctetValid = lastOctet > 0 && lastOctet < 255;
            if (!addressIsValid || !rangeStartIsValid || !lastOctetValid)
            {
                return false;
            }

            var addressOctets = address.Split(".".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            var startOctets = firstRangeAddress.Split(".".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

            var firstOctetMatch = addressOctets[0] == startOctets[0];
            var secondOctetMatch = addressOctets[1] == startOctets[1];
            var thirdOctetMatch = addressOctets[2] == startOctets[2];
            var finalOctet = uint.Parse(addressOctets[3], CultureInfo.CurrentCulture);
            var finalStartRangeOcted = uint.Parse(startOctets[3], CultureInfo.CurrentCulture);
            var lastOctedInRange = finalOctet >= finalStartRangeOcted && finalOctet <= lastOctet;

            return firstOctetMatch && secondOctetMatch && thirdOctetMatch && lastOctedInRange;
        }
    }
}
