using System;
using System.Linq;

namespace MusicBeeRemote.Core.Settings.Dialog.Validations
{
    public class LastOctetValidator
    {
        private readonly AddressValidationRule _addressValidationRule;

        public LastOctetValidator()
        {
            _addressValidationRule = new AddressValidationRule();
        }

        public bool Validate(string address, string lastOctetValue)
        {           
            var lastOctet = !string.IsNullOrEmpty(lastOctetValue) ? int.Parse(lastOctetValue) : 0;

            if (lastOctet > 254 || lastOctet < 1)
            {
                return false;
            }

            if (!_addressValidationRule.Validate(address))
            {               
                return false;
            }
            
            var octets = address.Split(".".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            var baseLastOctet = int.Parse(octets.Last());

            return lastOctet >= baseLastOctet;
        }
    }
}