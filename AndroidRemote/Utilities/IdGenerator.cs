namespace MusicBeePlugin.AndroidRemote.Utilities
{
    using System.Security.Cryptography;
    using System.Text;

    internal class IdGenerator
    {
        public static string GetUniqueKey()
        {
            const int maxSize = 8;
            const string a = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
            char[] chars = a.ToCharArray();
            int size = maxSize;
            byte[] data = new byte[1];
            RNGCryptoServiceProvider crypto = new RNGCryptoServiceProvider();
            crypto.GetNonZeroBytes(data);
            size = maxSize;
            data = new byte[size];
            crypto.GetNonZeroBytes(data);
            StringBuilder result = new StringBuilder(size);
            foreach (byte b in data)
            {
                result.Append(chars[b%(chars.Length - 1)]);
            }
            return result.ToString();
        }
    }
}