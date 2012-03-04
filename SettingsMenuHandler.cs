using System;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    class SettingsMenuHandler
    {
        private readonly TextBox _listeningPort;
        private readonly ToolTip _listeningPortToolTip;
        private readonly Button _restartButton;
        private static string _portNumber;

        public SettingsMenuHandler()
        {
            _listeningPort = new TextBox();
            _listeningPortToolTip = new ToolTip();
            _restartButton = new Button();
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
            _listeningPort.Text = UserSettings.ListeningPort.ToString(CultureInfo.InvariantCulture);
            
            Label textBoxLabel = new Label
            {
                Text = "Listening port:",
                Height = _listeningPort.Height
            };
            _listeningPort.TextChanged += HandleListeningPortTextChanged;
            _listeningPort.KeyPress += HandleListeningPortTextKeyPressed;
            _listeningPort.MaxLength = 5;
            _listeningPort.Bounds = new Rectangle(textBoxLabel.Width + 5, 0, 50, _listeningPort.Height);
            _listeningPort.BackColor =
                Color.FromArgb(background);

            _listeningPort.ForeColor =
                Color.FromArgb(forground);

            _listeningPort.BorderStyle = BorderStyle.FixedSingle;
            _listeningPort.HideSelection = false;
            _restartButton.Bounds = new Rectangle(_listeningPort.Right + 5,0,60,_listeningPort.Height);
            _restartButton.Text = "Restart Socket";
            _restartButton.Click += HandleRestartButtonClick;

            panel.Controls.Add(textBoxLabel);
            panel.Controls.Add(_listeningPort);
            panel.Controls.Add(_restartButton);

            return false;
        }

        private void HandleRestartButtonClick(object sender, EventArgs e)
        {
            SocketServer.Instance.Stop();
            SocketServer.Instance.Start();
        }

        private void HandleListeningPortTextKeyPressed(object sender, KeyPressEventArgs e)
        {
            if (Regex.IsMatch(e.KeyChar.ToString(CultureInfo.InvariantCulture), "\\D")&&e.KeyChar!='\b')
                e.Handled = true;
        }

        private void HandleListeningPortTextChanged(object sender, EventArgs e)
        {
            if(_listeningPort.TextLength==0)
                return;  
            if(_listeningPort.TextLength!=4&&_listeningPort.TextLength<5)
                return;
            int listeningPort = Int32.Parse(_listeningPort.Text);
            if(listeningPort<1||listeningPort>65535)
            {
                _listeningPortToolTip.ToolTipTitle = "Invalid Port Number";
                _listeningPortToolTip.Show("A valid port number is a number from 1 to 65535", _listeningPort, 0, -40, 2500);
                _listeningPort.Clear();
            }
            else
            {
                _portNumber = _listeningPort.Text;
            }

            
        }
    }
}
