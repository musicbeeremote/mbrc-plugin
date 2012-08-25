using System;
using System.Windows.Forms;
using MusicBeePlugin.AndroidRemote.Model;
using MusicBeePlugin.AndroidRemote.Settings;

namespace MusicBeePlugin
{
    internal class SettingsMenuHandler : IDisposable
    {
        public static SettingsModel SettingsModel { get; private set; }

        public bool ConfigureSettingsPanel(IntPtr panelHandle, int background, int foreground)
        {
            if (panelHandle == IntPtr.Zero)
                return false;

            Panel panel = (Panel) Control.FromHandle(panelHandle);
            // SettingsModel Loaded
            SettingsModel = UserSettings.SettingsModel;

            panel.Controls.Add(new SettingsPanel());
            return false;
        }


        public void Dispose()
        {

        }
    }
}