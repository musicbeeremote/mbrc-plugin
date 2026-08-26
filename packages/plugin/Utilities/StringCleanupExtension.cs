using System.Linq;

namespace MusicBeePlugin.Utilities
{
    /// <summary>
    ///     Extension methods for cleaning up string content.
    /// </summary>
    public static class StringCleanupExtension
    {
        /// <summary>
        ///     Removes control characters from the string and trims whitespace.
        /// </summary>
        /// <remarks>
        ///     Null in, empty out. This is called directly on MusicBee API returns all over the
        ///     providers, and the now-playing ones return null whenever nothing is loaded - so a
        ///     null here is ordinary state, not a bug worth throwing over.
        /// </remarks>
        /// <param name="input">The original string, possibly null</param>
        /// <returns>The string with control characters removed and whitespace trimmed.</returns>
        public static string Cleanup(this string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            return new string(input.Trim().Where(c => !char.IsControl(c)).ToArray());
        }
    }
}
