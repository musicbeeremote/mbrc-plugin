using System.Drawing;
using System.Windows.Forms;
using MusicBeePlugin.Host;

namespace MusicBeePlugin.Settings
{
    /// <summary>
    ///     The compact panel MusicBee embeds in its Preferences &gt; Plugins page.
    /// </summary>
    /// <remarks>
    ///     MusicBee already renders the name and description, so this is just a
    ///     Configure button opening <see cref="SettingsDialog"/>, which keeps
    ///     MusicBee's Save/Apply from owning our form. The window is opened
    ///     through <see cref="SettingsWindow"/>, which owns the single modeless
    ///     instance shared with the Tools menu entry.
    /// </remarks>
    internal sealed class ConfigurationPanel
    {
        private readonly PluginHost _host;
        private readonly string _version;

        public ConfigurationPanel(PluginHost host, string version)
        {
            _host = host;
            _version = version;
        }

        /// <summary>Place the Configure button at the panel's top-left.</summary>
        public void AttachTo(Control parent)
        {
            parent.Controls.Clear();

            var configure = new Button
            {
                Text = "Configure...",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Location = new Point(0, 2),
                Margin = new Padding(0),
            };
            configure.Click += (s, e) => OpenDialog();
            parent.Controls.Add(configure);
        }

        /// <summary>
        ///     Open the settings window, or raise the one already open.
        ///
        ///     Modeless, through the same single-instance owner the Tools menu
        ///     entry uses: opening settings must not freeze MusicBee behind it,
        ///     and clicking Configure twice must not stack two windows.
        /// </summary>
        private void OpenDialog()
        {
            SettingsWindow.Open(_host, _version);
        }
    }
}
