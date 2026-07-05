using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using NLog;
using Encoder = System.Drawing.Imaging.Encoder;

namespace MusicBeePlugin.Utilities.Common
{
    public static class Utilities
    {
        private const string CoverCachePath = @"cache\covers";

        // Image processing constants
        private const long DefaultJpegQuality = 80L;
        private const int DefaultCacheSize = 150;
        private const int DefaultResizeSize = 600;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        ///     Gets the cover storage path for a given storage base path
        /// </summary>
        /// <param name="storagePath">Base storage path</param>
        /// <returns>Full path to cover storage directory</returns>
        public static string GetCoverStoragePath(string storagePath)
        {
            return HashingUtilities.GetCoverStoragePath(storagePath);
        }

        /// <summary>
        ///     Given a <paramref name="storagePath" /> and <paramref name="subdirectory" /> it will return the full
        ///     path where the files will be stored. If the directory does not exist
        ///     it will create the directory.
        /// </summary>
        /// <param name="storagePath">Base storage path</param>
        /// <param name="subdirectory">
        ///     The specific subdirectory see
        ///     <see cref="CacheArtist" /> and <see cref="CacheCover" />
        /// </param>
        /// <returns>
        ///     <see cref="string" /> The full path to to the cache
        /// </returns>
        private static string Directory(string storagePath, string subdirectory)
        {
            var directory = Path.Combine(storagePath, subdirectory);

            if (!System.IO.Directory.Exists(directory))
                System.IO.Directory.CreateDirectory(directory);

            return directory;
        }

        /// <summary>
        ///     Given a string it returns the SHA1 hash of the string
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>System.String.</returns>
        public static string Sha1Hash(string value)
        {
            return HashingUtilities.Sha1Hash(value);
        }

        public static string Sha1Hash(byte[] data)
        {
            return HashingUtilities.Sha1Hash(data);
        }

        public static string Sha1HashFile(string filePath)
        {
            return HashingUtilities.Sha1HashFile(filePath);
        }

        /// <summary>
        ///     Opens a <see cref="Stream" /> and calculates the SHA1 hash for the stream.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <returns>System.String. The SHA1 hash value of calculated from the stream contents.</returns>
        private static string Sha1Hash(Stream stream)
        {
            return HashingUtilities.Sha1Hash(stream);
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            var codecs = ImageCodecInfo.GetImageDecoders();
            return codecs.FirstOrDefault(codec => codec.FormatID == format.Guid);
        }

        /// <summary>
        ///     Gets the JPEG codec with quality encoder parameters.
        ///     Falls back to default ImageFormat.Jpeg save if codec not found.
        /// </summary>
        private static (ImageCodecInfo Codec, EncoderParameters Params) GetJpegCodecWithQuality()
        {
            var codecInfo = GetEncoder(ImageFormat.Jpeg);
            if (codecInfo == null)
            {
                // JPEG codec should always be available on Windows, but handle gracefully
                Logger.Warn("JPEG codec not found, will use default image format");
                return (null, null);
            }

            var encoderParams = new EncoderParameters(1)
            {
                Param = { [0] = new EncoderParameter(Encoder.Quality, DefaultJpegQuality) }
            };
            return (codecInfo, encoderParams);
        }

        /// <summary>
        ///     Given a base64 encoded image it resizes the image and returns the resized image
        ///     in a base64 encoded JPEG.
        /// </summary>
        /// <param name="base64">The base64.</param>
        /// <param name="width">The width.</param>
        /// <param name="height">The height.</param>
        /// <returns>System.String.</returns>
        public static string ImageResize(string base64, int width = DefaultResizeSize, int height = DefaultResizeSize)
        {
            var cover = string.Empty;
            try
            {
                if (string.IsNullOrEmpty(base64))
                    return cover;

                using (var ms = new MemoryStream(Convert.FromBase64String(base64)))
                {
                    cover = ResizeStream(width, height, ms);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }

            return cover;
        }

        public static string ImageResize(byte[] data, int width = DefaultResizeSize, int height = DefaultResizeSize)
        {
            string cover;
            using (var ms = new MemoryStream(data))
            {
                cover = ResizeStream(width, height, ms);
            }

            return cover;
        }

        public static string ImageResizeFile(string file, int width = DefaultResizeSize, int height = DefaultResizeSize)
        {
            string cover;
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read))
            {
                cover = ResizeStream(width, height, fs);
            }

            return cover;
        }

        private static string ResizeStream(int width, int height, Stream stream)
        {
            using (var sourceImage = Image.FromStream(stream, true))
            {
                var targetSize = CalculateScaledSize(sourceImage, width, height);
                using (var resizedBitmap = CreateResizedBitmap(sourceImage, targetSize))
                {
                    return EncodeToJpegBase64(resizedBitmap);
                }
            }
        }

        /// <summary>
        ///     Calculates the scaled dimensions while preserving aspect ratio.
        /// </summary>
        private static Size CalculateScaledSize(Image source, int maxWidth, int maxHeight)
        {
            var scaleX = source.Width < maxWidth ? 1f : maxWidth / (float)source.Width;
            var scaleY = source.Height < maxHeight ? 1f : maxHeight / (float)source.Height;
            var scale = Math.Min(scaleX, scaleY);

            return new Size((int)(source.Width * scale), (int)(source.Height * scale));
        }

        /// <summary>
        ///     Creates a resized bitmap from the source image.
        /// </summary>
        private static Bitmap CreateResizedBitmap(Image source, Size targetSize)
        {
            var bitmap = new Bitmap(targetSize.Width, targetSize.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(source, 0, 0, targetSize.Width, targetSize.Height);
            }
            return bitmap;
        }

        /// <summary>
        ///     Encodes a bitmap to a base64 JPEG string.
        /// </summary>
        private static string EncodeToJpegBase64(Bitmap bitmap)
        {
            using (var stream = new MemoryStream())
            {
                var (codec, encoderParams) = GetJpegCodecWithQuality();
                if (codec != null)
                {
                    bitmap.Save(stream, codec, encoderParams);
                }
                else
                {
                    // Fallback to default JPEG save without quality parameter
                    bitmap.Save(stream, ImageFormat.Jpeg);
                }

                return Convert.ToBase64String(stream.ToArray());
            }
        }

        public static string FileToBase64(string filepath)
        {
            if (string.IsNullOrEmpty(filepath) || !File.Exists(filepath))
                return string.Empty;

            try
            {
                var bytes = File.ReadAllBytes(filepath);
                return Convert.ToBase64String(bytes);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to convert file to base64: {filepath}");
                return string.Empty;
            }
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
            var targetSize = new Size(width, height);
            using (var bitmap = CreateResizedBitmap(image, targetSize))
            {
                SaveAsJpeg(bitmap, filepath);
            }
        }

        /// <summary>
        ///     Saves a bitmap to a file as JPEG.
        /// </summary>
        private static void SaveAsJpeg(Bitmap bitmap, string filepath)
        {
            var (codec, encoderParams) = GetJpegCodecWithQuality();
            if (codec != null)
            {
                bitmap.Save(filepath, codec, encoderParams);
            }
            else
            {
                // Fallback to default JPEG save without quality parameter
                bitmap.Save(filepath, ImageFormat.Jpeg);
            }
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
        private static bool StoreResizedStream(Stream stream, string filepath, int width = DefaultCacheSize, int height = DefaultCacheSize)
        {
            try
            {
                using (var sourceImage = Image.FromStream(stream, false, true))
                {
                    var size = CalculateScaledSize(sourceImage, width, height);
                    StoreImage(filepath, size.Width, size.Height, sourceImage);
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Debug($"{filepath} failed due to {ex.Message}");
                return false;
            }
        }

        /// <summary>
        ///     Resizes an image to specific dimensions and stores it to the plugin Cover cache.
        /// </summary>
        /// <param name="storagePath">Base storage path for cache</param>
        /// <param name="imageData"></param>
        /// <param name="width">The max width of the new image</param>
        /// <param name="height">The max height of the new image</param>
        /// <returns>String SHA1 hash of the image.</returns>
        public static string StoreCoverToCache(string storagePath, byte[] imageData, int width = DefaultCacheSize, int height = DefaultCacheSize)
        {
            var hash = string.Empty;
            if (imageData == null)
                return hash;

            var directory = Directory(storagePath, CoverCachePath);

            using (var ms = new MemoryStream(imageData))
            {
                hash = Sha1Hash(ms);
                var filepath = Path.Combine(directory, hash);
                if (File.Exists(filepath))
                    return hash;

                StoreResizedStream(ms, filepath, width, height);
            }

            return hash;
        }

        /// <summary>
        ///     Resizes the cover and stores it to cache and returns the hash code
        ///     for the image.
        /// </summary>
        /// <param name="storagePath">Base storage path for cache</param>
        /// <param name="url">
        ///     The path where the original cover is stored.
        /// </param>
        /// <param name="width">The width of the cached image.</param>
        /// <param name="height">The height of the cached image.</param>
        /// <returns>
        ///     System.String. The SHA1 hash representing the image
        /// </returns>
        public static string StoreCoverToCache(string storagePath, string url, int width = DefaultCacheSize, int height = DefaultCacheSize)
        {
            var hash = string.Empty;
            if (string.IsNullOrEmpty(url))
                return hash;

            try
            {
                var directory = Directory(storagePath, CoverCachePath);

                using (var fs = new FileStream(url, FileMode.Open, FileAccess.Read))
                {
                    hash = Sha1Hash(fs);
                    var filepath = Path.Combine(directory, hash);
                    if (File.Exists(filepath))
                        return hash;

                    if (!StoreResizedStream(fs, filepath, width, height))
                        hash = HashingUtilities.EmptyHash;
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
        /// <param name="storagePath">Base storage path for cache</param>
        /// <param name="hash">SHA1 hash used as an identifier for the image</param>
        /// <returns></returns>
        public static string GetCoverFromCache(string storagePath, string hash)
        {
            try
            {
                var directory = GetCoverStoragePath(storagePath);
                var filepath = Path.Combine(directory, hash);
                return !File.Exists(filepath) ? string.Empty : FileToBase64(filepath);
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Failed to retrieve cover ${hash}");
                return string.Empty;
            }
        }

        public static string CoverIdentifier(string artist, string album)
        {
            return HashingUtilities.CoverIdentifier(artist, album);
        }
    }
}
