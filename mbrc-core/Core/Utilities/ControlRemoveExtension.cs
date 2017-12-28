using System.Linq;

namespace MusicBeeRemote.Core.Utilities
{
    public static class ControlRemoveExtension
    {
        /// <summary>
        /// Returns the input string without any control characters included.
        /// </summary>
        /// <param name="input">The original string</param>
        /// <returns>The string after the control characters have been removed.</returns>
        public static string Cleanup(this string input)
        {
            return new string(input.Trim().Where(c => !char.IsControl(c)).ToArray());
        }
    }
}