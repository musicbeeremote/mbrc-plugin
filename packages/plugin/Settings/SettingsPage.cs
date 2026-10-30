namespace MusicBeePlugin.Settings
{
    /// <summary>A page of the settings window. The value is its position in the navigation list.</summary>
    internal enum SettingsPage
    {
        Connection,
        Access,
        WebRemote,
        Library,
        Updates,
        Diagnostics,
    }

    internal static class SettingsPages
    {
        /// <summary>Every page with its title, in navigation order: entry <c>i</c> is page <c>i</c>.</summary>
        public static readonly (SettingsPage Page, string Title)[] All =
        {
            (SettingsPage.Connection, "Connection"),
            (SettingsPage.Access, "Access"),
            (SettingsPage.WebRemote, "Web remote"),
            (SettingsPage.Library, "Library"),
            (SettingsPage.Updates, "Updates"),
            (SettingsPage.Diagnostics, "Diagnostics"),
        };

        public static string Title(SettingsPage page) => All[(int)page].Title;
    }
}
