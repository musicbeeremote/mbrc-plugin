using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MusicBeePlugin.Ffi;

namespace MusicBeePlugin.Settings
{
    /// <summary>Short phrases the settings windows share.</summary>
    internal static class PanelText
    {
        /// <summary>What the artist picture setting sends where, shown beside it in both windows.</summary>
        public const string ArtistPicturesHint =
            "Uses MusicBee's artist pictures and cached backdrops; for artists it has none of, sends the name to MusicBrainz and Wikimedia.";

        /// <summary>How long ago a moment was, for a column that has to be short.</summary>
        public static string Ago(long unixSeconds)
        {
            if (unixSeconds <= 0) return "never";
            var when = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime();
            var elapsed = DateTimeOffset.Now - when;
            if (elapsed.TotalSeconds < 60) return "just now";
            if (elapsed.TotalMinutes < 60) return (int)elapsed.TotalMinutes + " min ago";
            if (elapsed.TotalHours < 24) return (int)elapsed.TotalHours + "h ago";
            return when.ToString("d MMM HH:mm", CultureInfo.CurrentCulture);
        }

        /// <summary>The <c>address:port</c> pairs a client can reach; empty when there are none.</summary>
        public static IList<string> Addresses(ListeningInfo info) => Addresses(info?.addresses, info?.port ?? 0);

        /// <inheritdoc cref="Addresses(ListeningInfo)" />
        public static IList<string> Addresses(IList<string> addresses, ushort port)
        {
            if (addresses == null) return new List<string>();
            var text = port.ToString(CultureInfo.InvariantCulture);
            return addresses.Select(a => a + ":" + text).ToList();
        }

        /// <summary>
        ///     The artist pictures line under the cache line, or null when downloads
        ///     are off and nothing is stored.
        /// </summary>
        public static string ArtistPictures(CoreCacheStatus status)
        {
            if (status == null || (!status.artist_images_enabled && status.artist_images == 0)) return null;
            var parts = new List<string>
            {
                string.Format(CultureInfo.CurrentCulture, "{0:N0} artist pictures ({1} of {2})",
                    status.artist_images, Megabytes(status.artist_image_bytes),
                    Megabytes(status.artist_image_budget)),
            };
            AddCount(parts, status.artist_images_not_found, "without a picture");
            AddCount(parts, status.artist_images_ambiguous, "with no sure match");
            AddCount(parts, status.artist_images_failed, "to retry");
            AddCount(parts, status.artist_images_waiting, "waiting");
            if (!status.artist_images_enabled)
                parts.Add("downloads off");
            else if (status.artist_image_bytes >= status.artist_image_budget)
                parts.Add("disk budget full; raise it or clear the pictures to fetch more");
            return string.Join(" · ", parts) + ".";
        }

        private static void AddCount(List<string> parts, int count, string what)
        {
            if (count > 0) parts.Add(string.Format(CultureInfo.CurrentCulture, "{0:N0} {1}", count, what));
        }

        private static string Megabytes(long bytes) =>
            string.Format(CultureInfo.CurrentCulture, "{0:N0} MB", Math.Ceiling(bytes / (1024.0 * 1024.0)));

        /// <summary>Seconds left on a pairing code, as m:ss or Ns.</summary>
        public static string Countdown(int seconds)
        {
            if (seconds <= 0) return "no time";
            return seconds >= 60
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", seconds / 60, seconds % 60)
                : seconds + "s";
        }
    }
}
