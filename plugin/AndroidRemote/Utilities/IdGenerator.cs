using System.Security.Cryptography;
using System.Text;

namespace MusicBeePlugin.AndroidRemote.Utilities
{
    internal static class IdGenerator
    {
        public static string GetUniqueKey()
        {
            const int maxSize = 8;
            const string a = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
            var chars = a.ToCharArray();
            var size = maxSize;
            var data = new byte[1];
            var crypto = new RNGCryptoServiceProvider();
            crypto.GetNonZeroBytes(data);
            size = maxSize;
            data = new byte[size];
            crypto.GetNonZeroBytes(data);
            var result = new StringBuilder(size);
            foreach (var b in data) result.Append(chars[b % (chars.Length - 1)]);
            return result.ToString();
        }
    }
}