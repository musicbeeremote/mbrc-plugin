using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MusicBeePlugin.Settings
{
    internal enum HostsSelection
    {
        All,
        Range,
        Specific
    }

    internal class SettingsMenuHandler
    {
        private readonly TextBox _listeningPort;
        private readonly ToolTip _listeningPortToolTip;
        private readonly Button _restartButton;
        private readonly NumericUpDown _ipRange;
        private readonly ComboBox _allowedIpAddressedCb;
        private readonly Button _addAddressButton;
        private readonly Button _removeAddressButton;
        private ComboBox _allowedHostsComboBox;
        private static string _portNumber;
        private static HostsSelection _hostsSelection;
        private readonly TextBox _allowedAddressBox;

        public SettingsMenuHandler()
        {
            _listeningPort = new TextBox();
            _listeningPortToolTip = new ToolTip();
            _restartButton = new Button();
            _allowedAddressBox = new TextBox();
            _ipRange = new NumericUpDown();
        }

        public static string PortNumber
        {
            get { return _portNumber; }
        }

        public static HostsSelection HostSelection
        {
            get { return _hostsSelection; }
        }

        public bool ConfigureSettingsPanel(IntPtr panelHandle, int background, int foreground)
        {
            if (panelHandle == IntPtr.Zero)
                return false;

            Panel panel = (Panel) Control.FromHandle(panelHandle);
            // Settings Loaded
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
            _listeningPort.BackColor = Color.FromArgb(background);
            _listeningPort.ForeColor = Color.FromArgb(foreground);

            _listeningPort.BorderStyle = BorderStyle.FixedSingle;
            _listeningPort.HideSelection = false;
            _restartButton.Bounds = new Rectangle(_listeningPort.Right + 5, 0, 60, _listeningPort.Height);
            _restartButton.Text = "Restart Socket";
            _restartButton.Click += HandleRestartButtonClick;

            List<String> allHostList = new List<String> {"All", "Address Range", "Specific Address"};
            _allowedHostsComboBox = new ComboBox {DataSource = allHostList, DropDownStyle = ComboBoxStyle.DropDownList};
            _allowedHostsComboBox.Bounds = new Rectangle(0, _listeningPort.Bottom + 5, 120, _allowedHostsComboBox.Height);
            _allowedHostsComboBox.SelectedValueChanged += HandleSelectedValueChanged;

            _allowedAddressBox.Bounds = new Rectangle(_allowedHostsComboBox.Width + 10, _listeningPort.Bottom + 5, 120,
                                                      _allowedAddressBox.Height);
            _allowedAddressBox.MaxLength = 15;
            _allowedAddressBox.BackColor = Color.FromArgb(background);
            _allowedAddressBox.ForeColor = Color.FromArgb(foreground);
            _allowedAddressBox.TextChanged += HandleAllowedAddressTextChanged;

            _ipRange.Bounds = new Rectangle(_allowedAddressBox.Right + 5, _listeningPort.Bottom+5, 50, _ipRange.Height);
            _ipRange.Minimum = 1;
            _ipRange.Maximum = 254;

            panel.Controls.AddRange(new Control[]{textBoxLabel, _listeningPort, _restartButton,_allowedHostsComboBox,_allowedAddressBox,_ipRange});

            return false;
        }

        private void HandleAllowedAddressTextChanged(object sender, EventArgs e)
        {
            IPAddress address;
            Regex ipValidation = new Regex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b");
            _allowedAddressBox.BackColor = ipValidation.IsMatch(_allowedAddressBox.Text) &&
                                           IPAddress.TryParse(_allowedAddressBox.Text, out address)
                                               ? Color.LightGreen
                                               : Color.Red;
        }

        private void HandleSelectedValueChanged(object sender, EventArgs e)
        {
            switch (_allowedHostsComboBox.Text)
            {
                case "All":
                    _hostsSelection = HostsSelection.All;
                    _allowedAddressBox.Hide();
                    _ipRange.Hide();
                    break;
                case "Address Range":
                    _hostsSelection = HostsSelection.Range;
                    _allowedAddressBox.Show();
                    _ipRange.Show();
                    break;
                case "Specific Address":
                    _hostsSelection = HostsSelection.Specific;
                    _allowedAddressBox.Show();
                    _ipRange.Hide();
                    break;
            }
        }

        private void HandleRestartButtonClick(object sender, EventArgs e)
        {
            SocketServer.Instance.Stop();
            SocketServer.Instance.Start();
        }

        private void HandleListeningPortTextKeyPressed(object sender, KeyPressEventArgs e)
        {
            if (Regex.IsMatch(e.KeyChar.ToString(CultureInfo.InvariantCulture), "\\D") && e.KeyChar != '\b')
                e.Handled = true;
        }

        private void HandleListeningPortTextChanged(object sender, EventArgs e)
        {
            if (_listeningPort.TextLength == 0)
                return;
            if (_listeningPort.TextLength != 4 && _listeningPort.TextLength < 5)
                return;
            int listeningPort = Int32.Parse(_listeningPort.Text);
            if (listeningPort < 1 || listeningPort > 65535)
            {
                _listeningPortToolTip.ToolTipTitle = "Invalid Port Number";
                _listeningPortToolTip.Show("A valid port number is a number from 1 to 65535", _listeningPort, 0, -40,
                                           2500);
                _listeningPort.Clear();
            }
            else
            {
                _portNumber = _listeningPort.Text;
            }
        }
    }
}