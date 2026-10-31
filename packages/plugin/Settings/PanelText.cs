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
