using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NLog;
using Encoder = System.Drawing.Imaging.Encoder;

namespace MusicBeePlugin.AndroidRemote.Utilities
{
    internal class Utilities
    {
        private static readonly SHA1Managed Sha1 = new SHA1Managed();
        private static byte[] _hash = new byte[20];

        /// <summary>
        /// Given a string it returns the SHA1 hash of the string
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>System.String.</returns>
        public static string Sha1Hash(string value)
        {
            var mHash = new string('0', 40);
            if (string.IsNullOrEmpty(value)) return mHash;
            _hash = Sha1.ComputeHash(Encoding.UTF8.GetBytes(value));
            var sb = new StringBuilder();
            foreach (var hex in _hash.Select(b => b.ToString("x2")))
            {
                sb.Append(hex);
            }
            mHash = sb.ToString();

            return mHash;
        }

        /// <summary>
        /// Given a filestream it returns the SHA1 hash of the string
        /// </summary>
        /// <param name="fs">The fs.</param>
        /// <returns>System.String.</returns>
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

        /// <summary>
        /// Given a base64 encoded image it resizes the image and returns the resized image
        /// in a base64 encoded JPEG.
        /// </summary>
        /// <param name="base64">The base64.</param>
        /// <param name="width">The width.</param>
        /// <param name="height">The height.</param>
        /// <returns>System.String.</returns>
        public static string ImageResize(string base64, int width = 600, int height = 600)
        {
            var cover = string.Empty;
            try
            {
                if (string.IsNullOrEmpty(base64))
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
                var logger = LogManager.GetCurrentClassLogger();
                logger.Error(ex);
            }
            return cover;
        }
    }
}
