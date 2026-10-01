using MusicBeePlugin.Host;

namespace MusicBeePlugin.Settings
{
    /// <summary>
    ///     Owns the one settings window, opened from both the Configure button and
    ///     the Tools menu entry.
    /// </summary>
    internal static class SettingsWindow
    {
        private static readonly ModelessWindow<SettingsDialog> Window = new ModelessWindow<SettingsDialog>();

        /// <summary>Show the settings window, or bring the open one to the front. No-op if the host failed to start.</summary>
        public static void Open(PluginHost host, string version) =>
            Window.Open(host, () => new SettingsDialog(host, version));

        /// <inheritdoc cref="ModelessWindow{T}.CloseIfOpen" />
        public static void CloseIfOpen() => Window.CloseIfOpen();
    }

    /// <summary>
    ///     Owns the one Party Mode window, opened from its Tools menu entry and from
    ///     the settings window.
    /// </summary>
    internal static class PartyModeWindow
    {
        private static readonly ModelessWindow<PartyModeDialog> Window = new ModelessWindow<PartyModeDialog>();

        /// <summary>Show the Party Mode window, or bring the open one to the front. No-op if the host failed to start.</summary>
        public static void Open(PluginHost host) => Window.Open(host, () => new PartyModeDialog(host));

        /// <inheritdoc cref="ModelessWindow{T}.CloseIfOpen" />
        public static void CloseIfOpen() => Window.CloseIfOpen();
    }
}
