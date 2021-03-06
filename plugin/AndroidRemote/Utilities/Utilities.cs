using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MusicBeePlugin.AndroidRemote.Model.Entities;
using NLog;
using Encoder = System.Drawing.Imaging.Encoder;

namespace MusicBeePlugin.AndroidRemote.Utilities
{
    internal class Utilities
    {
        private static readonly SHA1Managed Sha1 = new SHA1Managed();
        private static byte[] _hash = new byte[20];
        
        /// <summary>
        /// Base path where the files of the plugin are stored.
        /// </summary>
        public static string StoragePath { get; set; }
        private const string CoverCachePath = @"\cache\covers\";

        public static string CoverStorage => StoragePath + CoverCachePath;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        
        /// <summary>
        ///     Given a <paramref name="subdirectory" /> it will return the full
        ///     path where the files will be stored. If the directory does not exist
        ///     it will create the directory.
        /// </summary>
        /// <param name="subdirectory">
        ///     The specific subdirectory see
        ///     <see cref="CacheArtist" /> and <see cref="CacheCover" />
        /// </param>
        /// <returns>
        ///     <see cref="string" /> The full path to to the cache
        /// </returns>
        private static string Directory(string subdirectory)
        {
            var directory = StoragePath + subdirectory;

            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            return directory;
        }

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
        private static string Sha1Hash(FileStream fs)
        {
            _hash = Sha1.ComputeHash(fs);
            var sb = new StringBuilder();
            foreach (var hex in _hash.Select(b => b.ToString("x2")))
            {
                sb.Append(hex);
            }

            return sb.ToString();
        }
        
        /// <summary>
        ///     Opens a <see cref="Stream" /> and calculates the SHA1 hash for the stream.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <returns>System.String. The SHA1 hash value of calculated from the stream contents.</returns>
        private static string Sha1Hash(Stream stream)
        {
            _hash = Sha1.ComputeHash(stream);
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
                Logger.Error(ex);
            }
            return cover;
        }
        
        /// <summary>
        ///     Given an <paramref name="filepath" />, an <paramref name="image" />
        ///     and the dimensions it will store the <see cref="Image" /> to the
        ///     specified path in the filesystem.
        /// </summary>
        /// <param name="filepath"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="image"></param>
        private static void StoreImage(string filepath, int width, int height, Image image)
        {
            using (var bmp = new Bitmap(width, height))
            {
                var graph = Graphics.FromImage(bmp);
                graph.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graph.DrawImage(image, 0, 0, width, height);
                graph.Dispose();

                var info = GetEncoder(ImageFormat.Jpeg);
                var encoder = Encoder.Quality;
                var @params = new EncoderParameters(1);
                var param = new EncoderParameter(encoder, 80L);
                @params.Param[0] = param;
                bmp.Save(filepath, info, @params);
            }
        }
        
        /// <summary>
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="albumCover"></param>
        /// <returns></returns>
        private static Size CalculateNewSize(int width, int height, Image albumCover)
        {
            var sourceWidth = albumCover.Width;
            var sourceHeight = albumCover.Height;
            var newWidth = sourceWidth;
            var newHeight = sourceHeight;

            if (sourceWidth <= width && sourceHeight <= height)
            {
                return new Size(newWidth, newHeight);
            }

            var nPercentW = width / (float)sourceWidth;
            var nPercentH = height / (float)sourceHeight;

            var nPercent = nPercentH < nPercentW ? nPercentH : nPercentW;
            newWidth = (int)(sourceWidth * nPercent);
            newHeight = (int)(sourceHeight * nPercent);
            return new Size(newWidth, newHeight);
        }
        
        /// <summary>
        ///     Given a Stream that is supposedly an image that function will try to
        ///     resize the image to the supplied <paramref name="width" /> and
        ///     <paramref name="height" /> . If the image is not square the the
        ///     dimensions represent the largest size.
        /// </summary>
        /// <param name="stream">A stream containing an image.</param>
        /// <param name="filepath">The path where the file will be saved</param>
        /// <param name="width">The max width of the file saved</param>
        /// <param name="height">The max height of the file saved</param>
        private static bool StoreResizedStream(Stream stream, string filepath, int width = 150, int height = 150)
        {
            var success = true;
            try
            {
                var albumCover = Image.FromStream(stream, false, true);
                var size = CalculateNewSize(width, height, albumCover);
                StoreImage(filepath, size.Width, size.Height, albumCover);
            }
            catch (Exception ex)
            {
                Logger.Debug($"{filepath} failed due to {ex.Message}");
                success = false;
            }

            return success;
        }
        
        /// <summary>
        /// Resizes an image to specific dimensions and stores it to the plugin Cover cache.
        /// </summary>
        /// <param name="imageData"></param>
        /// <param name="width">The max width of the new image</param>
        /// <param name="height">The max height of the new image</param>
        /// <returns>String SHA1 hash of the image.</returns>
        public static string StoreCoverToCache(byte[] imageData, int width = 150, int height = 150)
        {
            var hash = string.Empty;
            if (imageData == null)
            {
                return hash;
            }

            var directory = Directory(CoverCachePath);

            using (var ms = new MemoryStream(imageData))
            {
                hash = Sha1Hash(ms);
                var filepath = directory + hash;
                if (File.Exists(filepath))
                {
                    return hash;
                }

                StoreResizedStream(ms, filepath, width, height);
            }

            return hash;
        }
        
        /// <summary>
        ///     Resizes the cover and stores it to cache and returns the hash code
        ///     for the image.
        /// </summary>
        /// <param name="url">
        ///     The path where the original cover is stored.
        /// </param>
        /// <param name="width">The width of the cached image.</param>
        /// <param name="height">The height of the cached image.</param>
        /// <returns>
        ///     System.String. The SHA1 hash representing the image
        /// </returns>
        public static string StoreCoverToCache(string url, int width = 150, int height = 150)
        {
            var hash = string.Empty;
            if (string.IsNullOrEmpty(url))
            {
                return hash;
            }

            try
            {
                var directory = Directory(CoverCachePath);

                using (var fs = new FileStream(url, FileMode.Open, FileAccess.Read))
                {
                    hash = Sha1Hash(fs);
                    var filepath = directory + hash;
                    if (File.Exists(filepath))
                    {
                        return hash;
                    }

                    if (!StoreResizedStream(fs, filepath, width, height))
                    {
                        hash = new string('0', 40);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex);
            }

            return hash;
        }
        
        /// <summary>
        ///     Reads an image file from the filesystem and returns a base64 string.
        /// </summary>
        /// <param name="hash">SHA1 hash used as an identifier for the image</param>
        /// <returns></returns>
        public static string GetCoverFromCache(string hash)
        {
            var directory = StoragePath + CoverCachePath;
            var filepath = directory + hash;
            if (!File.Exists(filepath))
            {
                return string.Empty;
            }
            
            using (var fs = new FileStream(filepath, FileMode.Open, FileAccess.Read))
            {
                using (var ms = new MemoryStream())
                {
                    fs.CopyTo(ms);
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        public static string CoverIdentifier(string artist, string album)
        {
            return Sha1Hash($"{artist.ToLowerInvariant()} {album.ToLowerInvariant()}");
        }
    }
}
