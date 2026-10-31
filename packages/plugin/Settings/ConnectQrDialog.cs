using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MusicBeePlugin.Ffi;
using MusicBeePlugin.Host;

namespace MusicBeePlugin.Settings
{
    /// <summary>
    ///     The connect QR code: a phone's camera opens the link in it, and the
    ///     Android app takes the addresses from there.
    /// </summary>
    /// <remarks>
    ///     Opened on request from the setup wizard and the Connection page, never
    ///     shown by default: typing an address stays the main instruction. A window
    ///     of its own because a scannable code does not fit those fixed-height pages.
    /// </remarks>
    internal sealed class ConnectQrDialog : Form
    {
        internal const string AppVersionNote =
            "Scan with your phone's camera. Needs MusicBee Remote 1.7.1 or later for Android.";

        private readonly ConnectQr _qr;
        private Bitmap _image;

        /// <param name="qr">The core's answer, or null when the core is not running.</param>
        public ConnectQrDialog(ConnectQr qr)
        {
            _qr = qr;

            Text = "Connect with a QR code";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            AutoScaleMode = AutoScaleMode.Font;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            BuildLayout();
        }

        /// <summary>Read the code from the core and show it modally over <paramref name="owner" />.</summary>
        public static void ShowFor(PluginHost host, IWin32Window owner)
        {
            using (var dialog = new ConnectQrDialog(host.ReadConnectQr()))
            {
                dialog.ShowDialog(owner);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _image?.Dispose();
            base.Dispose(disposing);
        }

        private bool HasCode => _qr?.png != null && _qr.png.Count > 0 && !string.IsNullOrEmpty(_qr.link);

        private void BuildLayout()
        {
            _image = HasCode ? LoadImage(_qr.png.ToArray()) : null;
            var textWidth = Math.Max(_image?.Width ?? 0, 320);

            var layout = new TableLayoutPanel
            {
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12),
            };

            if (_image != null)
            {
                layout.Controls.Add(new PictureBox
                {
                    Image = _image,
                    SizeMode = PictureBoxSizeMode.AutoSize,
                    Anchor = AnchorStyles.None,
                    Margin = new Padding(0, 0, 0, 8),
                });
            }

            layout.Controls.Add(Wrapped(_image != null ? AppVersionNote : UnavailableText(), textWidth));

            var addresses = PanelText.Addresses(_qr?.addresses, _qr?.port ?? 0);
            if (addresses.Count > 0)
            {
                layout.Controls.Add(Wrapped(string.Join(Environment.NewLine, addresses), textWidth));
            }

            layout.Controls.Add(BuildButtons());
            Controls.Add(layout);
        }

        private string UnavailableText()
        {
            if (_qr == null)
                return "The QR code is unavailable because the plugin's server is not running.";
            return _qr.addresses == null || _qr.addresses.Count == 0
                ? "No network address a phone could reach was found, so there is no QR code to show."
                : "The QR code could not be drawn. Enter one of these addresses in the app instead.";
        }

        private FlowLayoutPanel BuildButtons()
        {
            var close = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(84, 0), DialogResult = DialogResult.OK };
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0, 8, 0, 0),
            };
            buttons.Controls.Add(close);

            if (HasCode)
            {
                var copy = new Button { Text = "Copy link", AutoSize = true, MinimumSize = new Size(84, 0) };
                copy.Click += (s, e) => Clipboard.SetText(_qr.link);
                buttons.Controls.Add(copy);
            }

            AcceptButton = close;
            CancelButton = close;
            return buttons;
        }

        private static Label Wrapped(string text, int width) =>
            new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(width, 0),
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 0, 6),
            };

        /// <summary>A copy detached from the stream, which GDI+ otherwise needs kept open.</summary>
        private static Bitmap LoadImage(byte[] png)
        {
            try
            {
                using (var stream = new MemoryStream(png))
                using (var decoded = Image.FromStream(stream))
                {
                    return new Bitmap(decoded);
                }
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
