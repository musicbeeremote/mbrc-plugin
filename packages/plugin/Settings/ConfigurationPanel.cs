using System.Drawing;
using System.Windows.Forms;
using MusicBeePlugin.Host;

namespace MusicBeePlugin.Settings
{
    /// <summary>
    ///     The compact panel MusicBee embeds in its Preferences &gt; Plugins page.
    ///     MusicBee already renders the plugin name and description in the row, so
    ///     this panel is just a Configure button (positioned like every other
    ///     plugin's) that opens the separate <see cref="SettingsDialog"/> - also
    ///     reachable from the Tools menu. Keeping the panel to a single button
    ///     avoids MusicBee's Save/Apply owning our form.
    /// </summary>
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
            configure.Click += (s, e) => OpenDialog(parent);
            parent.Controls.Add(configure);
        }

        /// <summary>Open the settings dialog modally, parented to the panel's window.</summary>
        private void OpenDialog(Control parent)
        {
            using (var dialog = new SettingsDialog(_host, _version))
            {
                var owner = parent.FindForm();
                if (owner != null)
                    dialog.ShowDialog(owner);
                else
                    dialog.ShowDialog();
            }
        }
    }
}
