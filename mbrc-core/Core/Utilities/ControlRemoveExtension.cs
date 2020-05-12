using System;
using System.Linq;

namespace MusicBeeRemote.Core.Utilities
{
    public static class ControlRemoveExtension
    {
        /// <summary>
        /// Returns the input string without any control characters included.
        /// If the string is null it will return an empty string.
        /// </summary>
        /// <param name="input">The original string.</param>
        /// <returns>The string after the control characters have been removed. Or an empty string if input string was null.</returns>
        public static string Cleanup(this string input)
        {
            return input == null ? string.Empty : new string(input.Trim().Where(c => !char.IsControl(c)).ToArray());
        }
    }
}
