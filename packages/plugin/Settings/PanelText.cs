using System;
using System.Globalization;

namespace MusicBeePlugin.Settings
{
    /// <summary>Short time phrases the settings windows share.</summary>
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
