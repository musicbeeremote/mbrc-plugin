using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        private static int _maxIp;
        private static string _startingIp;
        private static BindingList<string> _addressBindingList;
        private readonly TextBox _addressInputTextBox;

        public SettingsMenuHandler()
        {
            _listeningPort = new TextBox();
            _listeningPortToolTip = new ToolTip();
            _restartButton = new Button();
            _addressInputTextBox = new TextBox();
            _ipRange = new NumericUpDown();
            _allowedIpAddressedCb = new ComboBox();
            _addAddressButton = new Button();
            _removeAddressButton = new Button();
            _addressBindingList = new BindingList<string>();
        }

        public static string PortNumber
        {
            get { return _portNumber; }
        }

        public static HostsSelection HostSelection
        {
            get { return _hostsSelection; }
        }

        public static BindingList<string> AllowedIpAddresses
        {
            get { return _addressBindingList; }
        }

        public static int MaxIp
        {
            get { return _maxIp; }
        }

        public static string StartingIp
        {
            get { return _startingIp; }
        }

        public bool ConfigureSettingsPanel(IntPtr panelHandle, int background, int foreground)
        {
            if (panelHandle == IntPtr.Zero)
                return false;

            Panel panel = (Panel) Control.FromHandle(panelHandle);
            // Settings Loaded
            _listeningPort.Text = _portNumber = UserSettings.ListeningPort.ToString(CultureInfo.InvariantCulture);
            _hostsSelection = UserSettings.HostSelection;
            if(_hostsSelection==HostsSelection.Range)
            {
                _addressInputTextBox.Text = _startingIp = UserSettings.StartingIp;
                _ipRange.Value = _maxIp = UserSettings.MaxIp;
            }
            _addressBindingList = UserSettings.IpAddressList;
            
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
            switch (_hostsSelection)
            {
                case HostsSelection.All:
                    _allowedHostsComboBox.SelectedText = "All";
                    break;
                case HostsSelection.Range:
                    _allowedHostsComboBox.SelectedText = "Address Range";
                    break;
                case HostsSelection.Specific:
                    _allowedHostsComboBox.SelectedText = "Specific Address";
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            _addressInputTextBox.Bounds = new Rectangle(_allowedHostsComboBox.Width + 10, _listeningPort.Bottom + 5, 100,
                                                        _addressInputTextBox.Height);
            _addressInputTextBox.MaxLength = 15;
            _addressInputTextBox.BorderStyle = BorderStyle.FixedSingle;
            _addressInputTextBox.BackColor = Color.FromArgb(background);
            _addressInputTextBox.ForeColor = Color.FromArgb(foreground);
            _addressInputTextBox.TextChanged += HandleAddressInputTextTextChanged;

            _addAddressButton.Bounds = new Rectangle(_addressInputTextBox.Right + 5, _listeningPort.Bottom + 5, 20, 20);
            _addAddressButton.Text = "+";
            _addAddressButton.Enabled = false;
            _addAddressButton.Click += HandleAddAddressButtonClick;

            _removeAddressButton.Bounds = new Rectangle(_addAddressButton.Right + 5, _listeningPort.Bottom + 5, 20, 20);
            _removeAddressButton.Text = "-";
            _removeAddressButton.Click += HandleRemoveAddressButtonClick;

            _allowedIpAddressedCb.Bounds = new Rectangle(_removeAddressButton.Right + 5, _listeningPort.Bottom + 5, 120,
                                                         _addressInputTextBox.Height);
            _allowedIpAddressedCb.DropDownStyle = ComboBoxStyle.DropDownList;
            _allowedIpAddressedCb.DataSource = _addressBindingList;

            _ipRange.Bounds = new Rectangle(_addressInputTextBox.Right + 5, _listeningPort.Bottom + 5, 50,
                                            _ipRange.Height);
            _ipRange.Minimum = 1;
            _ipRange.Maximum = 254;
            _ipRange.ValueChanged += HandleIpRangeValueChanged;


            panel.Controls.AddRange(new Control[]
                                        {
                                            textBoxLabel, _listeningPort, _restartButton, _allowedHostsComboBox,
                                            _addressInputTextBox, _ipRange, _addAddressButton, _removeAddressButton,
                                            _allowedIpAddressedCb
                                        });

            return false;
        }

        private void HandleIpRangeValueChanged(object sender, EventArgs e)
        {
            _maxIp = Convert.ToInt32(_ipRange.Value);
        }

        private void HandleRemoveAddressButtonClick(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(_allowedIpAddressedCb.Text)) return;
            _addressBindingList.Remove(_allowedIpAddressedCb.Text);
        }

        private void HandleAddAddressButtonClick(object sender, EventArgs e)
        {
            if (_addressBindingList.Contains(_addressInputTextBox.Text)) return;
            _addressBindingList.Add(_addressInputTextBox.Text);
        }

        private void HandleAddressInputTextTextChanged(object sender, EventArgs e)
        {
            IPAddress address;
            Regex ipValidation = new Regex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b");
            bool isValid = ipValidation.IsMatch(_addressInputTextBox.Text) &&
                           IPAddress.TryParse(_addressInputTextBox.Text, out address);
            _addressInputTextBox.BackColor = isValid
                                                 ? Color.LightGreen
                                                 : Color.Red;
            _addAddressButton.Enabled = isValid;
            if(isValid&&_hostsSelection==HostsSelection.Range)
            {
                _startingIp = _addressInputTextBox.Text;
            }
        }

        private void HandleSelectedValueChanged(object sender, EventArgs e)
        {
            switch (_allowedHostsComboBox.Text)
            {
                case "All":
                    _hostsSelection = HostsSelection.All;
                    _addressInputTextBox.Hide();
                    _ipRange.Hide();
                    _addAddressButton.Hide();
                    _removeAddressButton.Hide();
                    _allowedIpAddressedCb.Hide();
                    break;
                case "Address Range":
                    _hostsSelection = HostsSelection.Range;
                    _addressInputTextBox.Show();
                    _ipRange.Show();
                    _addAddressButton.Hide();
                    _removeAddressButton.Hide();
                    _allowedIpAddressedCb.Hide();
                    break;
                case "Specific Address":
                    _hostsSelection = HostsSelection.Specific;
                    _addressInputTextBox.Show();
                    _ipRange.Hide();
                    _addAddressButton.Show();
                    _removeAddressButton.Show();
                    _allowedIpAddressedCb.Show();
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