using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using MusicBeePlugin.Ffi;
using MusicBeePlugin.Host;

namespace MusicBeePlugin.Settings
{
    /// <summary>
    ///     The warning before a remote timer closes MusicBee, sleeps or shuts down the PC (#108),
    ///     with the one button that stops it.
    /// </summary>
    internal sealed class AutoShutdownDialog : Form
    {
        private readonly PluginHost _host;
        private readonly Label _message;
        private readonly Timer _tick;

        public AutoShutdownDialog(PluginHost host)
        {
            _host = host;
            Text = "MusicBee Remote";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);

            _message = new Label { AutoSize = true, MaximumSize = new Size(360, 0), Margin = new Padding(0, 0, 0, 12) };
            var cancel = new Button { Text = "Cancel", AutoSize = true, Anchor = AnchorStyles.Right };
            cancel.Click += (s, e) =>
            {
                _host.CancelAutoShutdown();
                Close();
            };
            CancelButton = cancel;

            var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
            layout.Controls.Add(_message);
            layout.Controls.Add(cancel);
            Controls.Add(layout);

            _tick = new Timer { Interval = 500 };
            _tick.Tick += (s, e) =>
            {
                if (!ShowStatus(_host.ReadPowerStatus())) Close();
            };
            _tick.Start();
            ShowStatus(_host.ReadPowerStatus());
        }

        /// <summary>Shows the warning while the final countdown runs, and closes it otherwise.</summary>
        public static void Sync(PluginHost host)
        {
            var status = host?.ReadPowerStatus();
            if (status != null && status.timer_active && status.final_countdown)
                AutoShutdownWindow.Open(host);
            else
                AutoShutdownWindow.CloseIfOpen();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tick.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>Writes the countdown; false once there is none to show.</summary>
        private bool ShowStatus(PowerPanelStatus status)
        {
            if (status == null || !status.timer_active || !status.final_countdown) return false;

            var seconds = (int)Math.Ceiling(status.remaining_ms / 1000.0);
            _message.Text = string.Format(
                CultureInfo.CurrentCulture,
                "A remote timer will {0} in {1} seconds.",
                Describe(status.action),
                seconds);
            return true;
        }

        private static string Describe(string action)
        {
            switch (action)
            {
                case "close": return "close MusicBee";
                case "sleep": return "put this PC to sleep";
                case "shutdown": return "shut down this PC";
                default: return action;
            }
        }
    }

    /// <summary>The one auto-shutdown warning on screen.</summary>
    internal static class AutoShutdownWindow
    {
        private static readonly ModelessWindow<AutoShutdownDialog> Window = new ModelessWindow<AutoShutdownDialog>();

        public static void Open(PluginHost host) => Window.Open(host, () => new AutoShutdownDialog(host));

        public static void CloseIfOpen() => Window.CloseIfOpen();
    }
}
