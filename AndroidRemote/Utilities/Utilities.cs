using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Encoder = System.Drawing.Imaging.Encoder;
using MusicBeePlugin.AndroidRemote.Error;

namespace MusicBeePlugin.AndroidRemote.Utilities
{
    class Utilities
    {
        private static readonly SHA1Managed Sha1 = new SHA1Managed();
        private static byte[] _hash = new byte[20];

        public static string Sha1Hash(string value)
        {
            var mHash = new String('0', 40);
            if (String.IsNullOrEmpty(value)) return mHash;
            _hash = Sha1.ComputeHash(Encoding.UTF8.GetBytes(value));
            var sb = new StringBuilder();
            foreach (var hex in _hash.Select(b => b.ToString("x2")))
            {
                sb.Append(hex);
            }
            mHash = sb.ToString();

            return mHash;
        }

        public static string Sha1Hash(FileStream fs)
        {
            _hash = Sha1.ComputeHash(fs);
            var sb = new StringBuilder();
            foreach (var hex in _hash.Select(b => b.ToString("x2")))
            {
                sb.Append(hex);
            }

            return sb.ToString();
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            var codecs = ImageCodecInfo.GetImageDecoders();
            return codecs.FirstOrDefault(codec => codec.FormatID == format.Guid);
        }

        public static string ImageResize(string base64, int width = 300, int height = 300)
        {
            var cover = String.Empty;
            try
            {
                if (String.IsNullOrEmpty(base64))
                {
                    return cover;
                }
                using (var ms = new MemoryStream(Convert.FromBase64String(base64)))
                using (var albumCover = Image.FromStream(ms, true))
                {
                    ms.Flush();
                    var sourceWidth = albumCover.Width;
                    var sourceHeight = albumCover.Height;

                    var nPercentW = (width / (float)sourceWidth);
                    var nPercentH = (height / (float)sourceHeight);

                    var nPercent = nPercentH < nPercentW ? nPercentH : nPercentW;
                    var destWidth = (int)(sourceWidth * nPercent);
                    var destHeight = (int)(sourceHeight * nPercent);
                    using (var bmp = new Bitmap(destWidth, destHeight))
                    using (var ms2 = new MemoryStream())
                    {
                        var graph = Graphics.FromImage(bmp);
                        graph.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graph.DrawImage(albumCover, 0, 0, destWidth, destHeight);
                        graph.Dispose();

                        var mInfo = GetEncoder(ImageFormat.Jpeg);
                        var mEncoder = Encoder.Quality;
                        var mParams = new EncoderParameters(1);
                        var mParam = new EncoderParameter(mEncoder, 80L);
                        mParams.Param[0] = mParam;

                        bmp.Save(ms2, mInfo, mParams);
                        cover = Convert.ToBase64String(ms2.ToArray());
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                ErrorHandler.LogError(ex);
#endif
            }
            return cover;
        }
    }
}
