using System;
using System.Drawing;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    class SettingsMenuHandler
    {
        private readonly MaskedTextBox _listeningPort;
        private readonly ToolTip _listeningPortToolTip;
        private static string _portNumber;

        public SettingsMenuHandler()
        {
            _listeningPort = new MaskedTextBox("00000");
            _listeningPortToolTip = new ToolTip();
        }

        public static string PortNumber
        {
            get { return _portNumber; }
        }

        public bool ConfigureSettingsPanel(IntPtr panelHandle, int background, int forground)
        {
            if (panelHandle == IntPtr.Zero)
                return false;

            Panel panel = (Panel)Control.FromHandle(panelHandle);
            _listeningPort.Text = UserSettings.ListeningPort;
            
            Label textBoxLabel = new Label
            {
                Text = "Listening port:",
                Height = _listeningPort.Height
            };
            _listeningPort.MaskInputRejected +=HandleListeningPortMaskInputRejected;
            _listeningPort.TextChanged += HandleListeningPortTextChanged;
            _listeningPort.Bounds = new Rectangle(textBoxLabel.Width + 5, 0, 50, _listeningPort.Height);
            _listeningPort.BackColor =
                Color.FromArgb(background);

            _listeningPort.ForeColor =
                Color.FromArgb(forground);

            _listeningPort.BorderStyle = BorderStyle.FixedSingle;
            _listeningPort.HideSelection = false;

            panel.Controls.Add(textBoxLabel);
            panel.Controls.Add(_listeningPort);

            return false;
        }

        private void HandleListeningPortTextChanged(object sender, EventArgs e)
        {
            if(!_listeningPort.MaskCompleted)
                return;
            int listeningPort = Int32.Parse(_listeningPort.Text);
            if(listeningPort<1||listeningPort>65535)
            {
                _listeningPortToolTip.ToolTipTitle = "Invalid Port Number";
                _listeningPortToolTip.Show("A valid port number is a number from 1 to 65535", _listeningPort, 0, -20, 5000);
                _listeningPort.Clear();
            }
            else
            {
                _portNumber = _listeningPort.Text;
            }

            
        }

        private void HandleListeningPortMaskInputRejected(object sender, MaskInputRejectedEventArgs e)
        {
            
            if (_listeningPort.MaskFull)
            {
                _listeningPortToolTip.ToolTipTitle = "Input Rejected";
                _listeningPortToolTip.Show("You cannot enter any more data into the date field. Delete some characters in order to insert more data.", _listeningPort, 0, -20, 5000);
            }
            else if (e.Position == _listeningPort.Mask.Length)
            {
                _listeningPortToolTip.ToolTipTitle = "Input Rejected - End of Field";
                _listeningPortToolTip.Show("You cannot add extra characters to the end of this date field.", _listeningPort, 0, -20, 5000);
            }
            else
            {
                _listeningPortToolTip.ToolTipTitle = "Input Rejected";
                _listeningPortToolTip.Show("You can only add numeric characters (0-9) into this date field.", _listeningPort, 0, -20, 5000);
            }
        }
    }
}
