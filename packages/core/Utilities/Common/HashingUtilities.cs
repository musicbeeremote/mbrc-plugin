using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MusicBeePlugin.Utilities.Common
{
    /// <summary>
    /// Provides hashing utilities for cache identification and file hashing.
    /// These methods are separated from image processing to enable cross-platform testing.
    /// </summary>
    public static class HashingUtilities
    {
        private const int Sha1HashLength = 40;
        private const string CoverCachePath = @"cache\covers";

        private static readonly string EmptySha1Hash = new string('0', Sha1HashLength);

        /// <summary>
        /// Gets the cover storage path for a given storage base path.
        /// </summary>
        /// <param name="storagePath">Base storage path</param>
        /// <returns>Full path to cover storage directory</returns>
        public static string GetCoverStoragePath(string storagePath)
        {
            return Path.Combine(storagePath, CoverCachePath);
        }

        /// <summary>
        /// Given a string it returns the SHA1 hash of the string.
        /// </summary>
        /// <param name="value">The value to hash.</param>
        /// <returns>The SHA1 hash as a lowercase hexadecimal string.</returns>
        public static string Sha1Hash(string value)
        {
            if (string.IsNullOrEmpty(value))
                return EmptySha1Hash;

            return Sha1Hash(Encoding.UTF8.GetBytes(value));
        }

        /// <summary>
        /// Computes the SHA1 hash of a byte array.
        /// </summary>
        /// <param name="data">The data to hash.</param>
        /// <returns>The SHA1 hash as a lowercase hexadecimal string.</returns>
        public static string Sha1Hash(byte[] data)
        {
            if (data == null || data.Length == 0)
                return EmptySha1Hash;

#pragma warning disable CA5350 // SHA1 used for cache identifiers, not cryptography
            using (var sha1 = SHA1.Create())
#pragma warning restore CA5350
            {
                var hash = sha1.ComputeHash(data);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// Computes the SHA1 hash of a file.
        /// </summary>
        /// <param name="filePath">Path to the file.</param>
        /// <returns>The SHA1 hash as a lowercase hexadecimal string.</returns>
        public static string Sha1HashFile(string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                return Sha1Hash(fs);
            }
        }

        /// <summary>
        /// Opens a <see cref="Stream"/> and calculates the SHA1 hash for the stream.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <returns>The SHA1 hash value calculated from the stream contents.</returns>
        public static string Sha1Hash(Stream stream)
        {
            if (stream == null)
                return EmptySha1Hash;

#pragma warning disable CA5350 // SHA1 used for cache identifiers, not cryptography
            using (var sha1 = SHA1.Create())
#pragma warning restore CA5350
            {
                var hash = sha1.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// Creates a cover identifier from artist and album names.
        /// </summary>
        /// <param name="artist">The artist name.</param>
        /// <param name="album">The album name.</param>
        /// <returns>A SHA1 hash identifier for the cover.</returns>
        public static string CoverIdentifier(string artist, string album)
        {
            return Sha1Hash($"{artist.ToLowerInvariant()} {album.ToLowerInvariant()}");
        }

        /// <summary>
        /// Gets the empty SHA1 hash (40 zeros).
        /// </summary>
        public static string EmptyHash => EmptySha1Hash;
    }
}
