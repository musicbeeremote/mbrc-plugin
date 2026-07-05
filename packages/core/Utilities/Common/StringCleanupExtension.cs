using System.Linq;

namespace MusicBeePlugin.Utilities.Common
{
    /// <summary>
    ///     Extension methods for cleaning up string content.
    /// </summary>
    public static class StringCleanupExtension
    {
        /// <summary>
        ///     Removes control characters from the string and trims whitespace.
        /// </summary>
        /// <param name="input">The original string</param>
        /// <returns>The string with control characters removed and whitespace trimmed.</returns>
        public static string Cleanup(this string input)
        {
            return new string(input.Trim().Where(c => !char.IsControl(c)).ToArray());
        }
    }
}
