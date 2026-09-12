using System;
using System.Globalization;

namespace MusicBeePlugin.Utilities
{
    /// <summary>
    ///     Conversions for the dates MusicBee hands back as display strings.
    /// </summary>
    public static class DateExtensions
    {
        /// <summary>
        ///     A MusicBee date display string (in the running culture) as ISO-8601
        ///     UTC. Empty when unparseable, so the core surfaces a null date rather
        ///     than a value nobody can read (#114).
        /// </summary>
        public static string ToIso8601Utc(this string raw)
        {
            return DateTime.TryParse(raw, out var dt)
                ? dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
                : string.Empty;
        }
    }
}
