using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Services.Configuration;

namespace MusicBeePlugin.Utilities.Data
{
    /// <summary>
    ///     Helper class for determining the search source based on user settings
    /// </summary>
    public static class SearchSourceHelper
    {
        /// <summary>
        ///     Gets the search source based on user settings, defaulting to Library if not set
        /// </summary>
        /// <param name="userSettings">The user settings to check</param>
        /// <returns>The search source to use</returns>
        public static SearchSource GetSearchSource(IUserSettings userSettings)
        {
            return userSettings.Source != SearchSource.None
                ? userSettings.Source
                : SearchSource.Library;
        }
    }
}
