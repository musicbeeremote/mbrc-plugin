using System.Security.Cryptography;
using System.Text;

namespace MusicBeePlugin.Utilities.Common
{
    internal static class IdGenerator
    {
        private const int KeySize = 8;
        private const string AllowedCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";

        public static string GetUniqueKey()
        {
            var chars = AllowedCharacters.ToCharArray();
            var data = new byte[KeySize];

            using (var crypto = new RNGCryptoServiceProvider())
            {
                crypto.GetNonZeroBytes(data);
            }

            var result = new StringBuilder(KeySize);
            foreach (var b in data)
                result.Append(chars[b % (chars.Length - 1)]);

            return result.ToString();
        }
    }
}
