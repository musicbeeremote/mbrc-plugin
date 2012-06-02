using System;
using System.Drawing;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using MusicBeePlugin.Controller;
using MusicBeePlugin.Networking;

namespace MusicBeePlugin.Settings
{
    internal enum FilteringSelection
    {
        All,
        Range,
        Specific
    }

    internal class SettingsMenuHandler:IDisposable
    {
        private readonly TextBox _listeningPort;
        private readonly ToolTip _listeningPortToolTip;
        private readonly Button _restartButton;
        private readonly NumericUpDown _lastOctetMax;
        private readonly ComboBox _allowedIpAddressedCb;
        private readonly Button _addAddressButton;
        private readonly Button _removeAddressButton;
        private ComboBox _filterSelectionComboBox;
        private readonly TextBox _addressInputTextBox;

        private static ApplicationSettings _applicationSettings;

        public static ApplicationSettings Settings
        {
            get { return _applicationSettings; }
        }

        public SettingsMenuHandler()
        {
            _listeningPort = new TextBox();
            _listeningPortToolTip = new ToolTip();
            _restartButton = new Button();
            _addressInputTextBox = new TextBox();
            _lastOctetMax = new NumericUpDown();
            _allowedIpAddressedCb = new ComboBox();
            _addAddressButton = new Button();
            _removeAddressButton = new Button();
        }

        public bool ConfigureSettingsPanel(IntPtr panelHandle, int background, int foreground)
        {
            if (panelHandle == IntPtr.Zero)
                return false;

            Panel panel = (Panel) Control.FromHandle(panelHandle);
            // Settings Loaded
            _applicationSettings = UserSettings.Settings;

            
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

            _filterSelectionComboBox = new ComboBox {DropDownStyle = ComboBoxStyle.DropDownList};
            _filterSelectionComboBox.Bounds = new Rectangle(0, _listeningPort.Bottom + 5, 120, _filterSelectionComboBox.Height);
            _filterSelectionComboBox.Items.AddRange(new object[] {"All","Range", "Specific"});
         
            _filterSelectionComboBox.SelectedValueChanged += HandleSelectedValueChanged;

            switch (_applicationSettings.FilterSelection)
            {
                case FilteringSelection.All:
                    _filterSelectionComboBox.SelectedIndex = _filterSelectionComboBox.Items.IndexOf("All");
                    break;
                case FilteringSelection.Range:
                    _filterSelectionComboBox.SelectedIndex = _filterSelectionComboBox.Items.IndexOf("Range");
                    break;
                case FilteringSelection.Specific:
                    _filterSelectionComboBox.SelectedIndex = _filterSelectionComboBox.Items.IndexOf("Specific");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(string.Format("{0}Undefined Filtering option", "ARG0"));
            }

            _addressInputTextBox.Bounds = new Rectangle(_filterSelectionComboBox.Width + 10, _listeningPort.Bottom + 5, 100,
                                                        _addressInputTextBox.Height);
            _addressInputTextBox.MaxLength = 15;
            _addressInputTextBox.BorderStyle = BorderStyle.FixedSingle;
            _addressInputTextBox.BackColor = Color.FromArgb(background);
            _addressInputTextBox.ForeColor = Color.FromArgb(foreground);
            _addressInputTextBox.TextChanged += HandleAddressInputTextTextChanged;

            _addAddressButton.Bounds = new Rectangle(_addressInputTextBox.Right + 5, _listeningPort.Bottom + 5, 20, 20);
            _addAddressButton.Text = Properties.Resources.plus;
            _addAddressButton.Enabled = false;
            _addAddressButton.Click += HandleAddAddressButtonClick;

            _removeAddressButton.Bounds = new Rectangle(_addAddressButton.Right + 5, _listeningPort.Bottom + 5, 20, 20);
            _removeAddressButton.Text = Properties.Resources.minus;
            _removeAddressButton.Click += HandleRemoveAddressButtonClick;

            _allowedIpAddressedCb.Bounds = new Rectangle(_removeAddressButton.Right + 5, _listeningPort.Bottom + 5, 120,
                                                         _addressInputTextBox.Height);
            _allowedIpAddressedCb.DropDownStyle = ComboBoxStyle.DropDownList;
            _allowedIpAddressedCb.DataSource = _applicationSettings.IpAddressList;

            _lastOctetMax.Bounds = new Rectangle(_addressInputTextBox.Right + 5, _listeningPort.Bottom + 5, 50,
                                            _lastOctetMax.Height);
            _lastOctetMax.Minimum = 1;
            _lastOctetMax.Maximum = 254;
            _lastOctetMax.ValueChanged += HandleLastOctetMaxValueChanged;

            // Getting settings out of the ApplicationSettings item.
            _listeningPort.Text = _applicationSettings.ListeningPort.ToString(CultureInfo.InvariantCulture);
            _lastOctetMax.Value = _applicationSettings.LastOctetMax;
            BindListToCombo();
            if (_applicationSettings.FilterSelection == FilteringSelection.Range)
            {
                _addressInputTextBox.Text = _applicationSettings.BaseIp;
            }

            panel.Controls.AddRange(new Control[]
                                        {
                                            textBoxLabel, _listeningPort, _restartButton, _filterSelectionComboBox,
                                            _addressInputTextBox, _lastOctetMax, _addAddressButton, _removeAddressButton,
                                            _allowedIpAddressedCb
                                        });

            return false;
        }

        private void BindListToCombo()
        {
            _allowedIpAddressedCb.DataSource = null;
            _allowedIpAddressedCb.DataSource = _applicationSettings.IpAddressList;
        }

        private void HandleLastOctetMaxValueChanged(object sender, EventArgs e)
        {
            _applicationSettings.LastOctetMax = Convert.ToInt32(_lastOctetMax.Value);
        }

        private void HandleRemoveAddressButtonClick(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(_allowedIpAddressedCb.Text)) return;
            _applicationSettings.IpAddressList.Remove(_allowedIpAddressedCb.Text);
            BindListToCombo();
        }

        private void HandleAddAddressButtonClick(object sender, EventArgs e)
        {
            if (_applicationSettings.IpAddressList.Contains(_addressInputTextBox.Text)) return;
            _applicationSettings.IpAddressList.Add(_addressInputTextBox.Text);
            BindListToCombo();
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
            if(isValid&&_applicationSettings.FilterSelection==FilteringSelection.Range)
            {
                _applicationSettings.BaseIp = _addressInputTextBox.Text;
            }
        }

        private void HandleSelectedValueChanged(object sender, EventArgs e)
        {
            switch (_filterSelectionComboBox.Text)
            {
                case "All":
                    _applicationSettings.FilterSelection = FilteringSelection.All;
                    _addressInputTextBox.Hide();
                    _lastOctetMax.Hide();
                    _addAddressButton.Hide();
                    _removeAddressButton.Hide();
                    _allowedIpAddressedCb.Hide();
                    break;
                case "Range":
                    _applicationSettings.FilterSelection = FilteringSelection.Range;
                    _addressInputTextBox.Show();
                    _lastOctetMax.Show();
                    _addAddressButton.Hide();
                    _removeAddressButton.Hide();
                    _allowedIpAddressedCb.Hide();
                    break;
                case "Specific":
                    _applicationSettings.FilterSelection = FilteringSelection.Specific;
                    _addressInputTextBox.Show();
                    _lastOctetMax.Hide();
                    _addAddressButton.Show();
                    _removeAddressButton.Show();
                    _allowedIpAddressedCb.Show();
                    break;
            }
        }

        private void HandleRestartButtonClick(object sender, EventArgs e)
        {
            RemoteController.Instance.StopSocket();
            RemoteController.Instance.StartSocket();
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
                _listeningPortToolTip.ToolTipTitle = Properties.Resources.InvalidPort;
                _listeningPortToolTip.Show(Properties.Resources.ValidRange, _listeningPort, 0, -40,
                                           2500);
                _listeningPort.Clear();
            }
            else
            {
                _applicationSettings.ListeningPort = int.Parse(_listeningPort.Text);
            }
        }

        public void Dispose()
        {
          _listeningPort.Dispose();
          _listeningPortToolTip.Dispose();
          _restartButton.Dispose();
          _lastOctetMax.Dispose();
          _allowedIpAddressedCb.Dispose();
          _addAddressButton.Dispose();
          _removeAddressButton.Dispose();
          _filterSelectionComboBox.Dispose();
          _addressInputTextBox.Dispose();
        }
    }
}