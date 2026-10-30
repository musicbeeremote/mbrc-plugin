using MusicBeePlugin.Ffi;
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

        /// <summary>
        ///     Show the settings window, or bring the open one to the front. No-op if the host failed to start.
        ///
        ///     Opens on <paramref name="page" /> when given, otherwise on the page shown last.
        /// </summary>
        public static void Open(PluginHost host, string version, SettingsPage? page = null)
        {
            if (page.HasValue) SettingsDialog.LastPage = page.Value;
            Window.Open(host, () => new SettingsDialog(host, version))?.ShowPage(SettingsDialog.LastPage);
        }

        /// <inheritdoc cref="ModelessWindow{T}.CloseIfOpen" />
        public static void CloseIfOpen() => Window.CloseIfOpen();
    }

    /// <summary>
    ///     Owns the one setup wizard, opened at startup while setup is due and
    ///     from the Configure panel's "Run setup again".
    /// </summary>
    internal static class SetupWindow
    {
        private static readonly ModelessWindow<SetupWizard> Window = new ModelessWindow<SetupWizard>();

        /// <summary>Show the wizard, or bring the open one to the front. No-op if the host failed to start.</summary>
        public static void Open(PluginHost host) => Open(host, host?.ReadSetupStatus());

        /// <summary>Open the wizard if the core says this install has not done the current setup.</summary>
        public static void OpenIfDue(PluginHost host)
        {
            var status = host?.ReadSetupStatus();
            if (status?.needed == true) Open(host, status);
        }

        private static void Open(PluginHost host, SetupStatus status) =>
            Window.Open(host, () => new SetupWizard(host, status));

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
