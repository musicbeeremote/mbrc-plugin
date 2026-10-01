using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using MusicBeePlugin.Ffi;
using MusicBeePlugin.Host;

namespace MusicBeePlugin.Settings
{
    /// <summary>
    ///     The Party Mode window: the switch, the devices and their roles, pairing
    ///     with a chosen role, and what was refused.
    /// </summary>
    /// <remarks>
    ///     Its own window rather than a group in Settings because it is used during
    ///     the party, and everything in it applies at once, where Settings applies
    ///     on Save. The core owns all of it; this renders the core's state and
    ///     re-reads it on a timer, faster while a pairing code counts down.
    /// </remarks>
    internal sealed class PartyModeDialog : Form
    {
        // Display label + the role name the core round-trips, in order of trust.
        internal static readonly (string Label, string Value)[] Roles =
        {
            ("Host", "host"),
            ("DJ", "dj"),
            ("Guest", "guest"),
            ("Listener", "listener"),
        };

        private const int IdleRefreshMs = 3000;
        private const int CountdownRefreshMs = 1000;

        private readonly PluginHost _host;
        private readonly Timer _timer;
        private CheckBox _enabled;
        private ListView _devices;
        private TextBox _search;

        /// <summary>The devices the core last reported, before the search narrows them.</summary>
        private List<PartyDevice> _allDevices = new List<PartyDevice>();

        /// <summary>What the list shows, so a refresh that changes nothing leaves it alone.</summary>
        private string _shown;
        private Label _trustNote;
        private ComboBox _role;
        private Button _setRole;
        private ComboBox _codeRole;
        private Label _codeStatus;
        private Button _copyCode;
        private ListView _refusals;

        /// <summary>The code on offer, or null when none is outstanding.</summary>
        private string _code;

        /// <summary>True while the controls are filled from the core.</summary>
        private bool _loading;

        public PartyModeDialog(PluginHost host)
        {
            _host = host;

            Text = "MusicBee Remote - Party Mode";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(560, 600);
            MinimumSize = new Size(460, 480);

            BuildLayout();

            _timer = new Timer { Interval = IdleRefreshMs };
            _timer.Tick += (s, e) => Reload();
            _timer.Start();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Reload();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            base.OnFormClosed(e);
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                Padding = new Padding(12),
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // switch
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 60)); // devices
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // pairing
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 40)); // refusals
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // close

            _enabled = new CheckBox
            {
                Text = "Party Mode: guests may only browse and add a song",
                AutoSize = true,
                Margin = new Padding(3, 0, 3, 8),
            };
            _enabled.CheckedChanged += (s, e) => Switch();

            root.Controls.Add(_enabled);
            root.Controls.Add(BuildDevicesGroup());
            root.Controls.Add(BuildPairingGroup());
            root.Controls.Add(BuildRefusalsGroup());
            root.Controls.Add(BuildCloseRow());
            Controls.Add(root);
        }

        private Control BuildDevicesGroup()
        {
            _devices = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                ShowItemToolTips = true,
                Dock = DockStyle.Fill,
            };
            _devices.Columns.Add("Device", 150);
            _devices.Columns.Add("Kind", 65);
            _devices.Columns.Add("Role", 65);
            _devices.Columns.Add("Address", 110);
            _devices.Columns.Add("Last seen", 95);
            _devices.SelectedIndexChanged += (s, e) => FollowSelection();

            _search = new TextBox { Width = 240 };
            _search.TextChanged += (s, e) => ShowDevices();
            var refresh = new Button { Text = "Refresh", AutoSize = true };
            refresh.Click += (s, e) => Reload();
            var searchRow = Flow(Caption("Search:"), _search, refresh);
            searchRow.Margin = new Padding(0, 0, 0, 6);

            _trustNote = new Label
            {
                Text = "Android devices are known by an unverified id: anyone who reads it off the network can claim its role.",
                AutoSize = true,
                MaximumSize = new Size(500, 0),
                ForeColor = SystemColors.GrayText,
                Visible = false,
                Margin = new Padding(0, 4, 0, 0),
            };

            _role = RoleCombo();
            _setRole = new Button { Text = "Set role", AutoSize = true, Enabled = false };
            _setRole.Click += (s, e) => SetSelectedRole();
            var roleRow = Flow(Caption("Selected device:"), _role, _setRole);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(searchRow);
            layout.Controls.Add(_devices);
            layout.Controls.Add(_trustNote);
            layout.Controls.Add(roleRow);
            return Group("Devices", layout, fill: true);
        }

        private Control BuildPairingGroup()
        {
            _codeRole = RoleCombo();
            _codeRole.SelectedIndex = 0;
            var show = new Button { Text = "Show pairing code", AutoSize = true };
            show.Click += (s, e) =>
            {
                _host.GeneratePairingCode(Roles[_codeRole.SelectedIndex].Value);
                Reload();
            };
            _copyCode = new Button { Text = "Copy code", AutoSize = true, Visible = false };
            _copyCode.Click += (s, e) => CopyCode();

            _codeStatus = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(500, 0),
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 6, 0, 0),
            };

            var layout = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true };
            layout.Controls.Add(Flow(Caption("Grants:"), _codeRole, show, _copyCode));
            layout.Controls.Add(_codeStatus);
            return Group("Pairing", layout, fill: false);
        }

        private Control BuildRefusalsGroup()
        {
            _refusals = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                ShowItemToolTips = true,
                Dock = DockStyle.Fill,
            };
            _refusals.Columns.Add("Time", 70);
            _refusals.Columns.Add("Client", 150);
            _refusals.Columns.Add("Request", 150);
            _refusals.Columns.Add("Needs", 100);
            var clear = new Button { Text = "Clear", AutoSize = true, Anchor = AnchorStyles.Right };
            clear.Click += (s, e) =>
            {
                _host.ClearPartyRefusals();
                Reload();
            };

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(_refusals);
            layout.Controls.Add(clear);
            return Group("Refused requests", layout, fill: true);
        }

        private Control BuildCloseRow()
        {
            var close = new Button { Text = "Close", AutoSize = true, Anchor = AnchorStyles.Right };
            close.Click += (s, e) => Close();
            CancelButton = close;
            return close;
        }

        private static GroupBox Group(string title, Control content, bool fill)
        {
            var box = new GroupBox
            {
                Text = title,
                Dock = fill ? DockStyle.Fill : DockStyle.Top,
                AutoSize = !fill,
                Padding = new Padding(8, 4, 8, 8),
                Margin = new Padding(0, 0, 0, 10),
            };
            box.Controls.Add(content);
            return box;
        }

        /// <summary>
        ///     A row of controls centred on its tallest one: a control anchored Left
        ///     only is centred vertically in a flow row, which a top padding would undo.
        /// </summary>
        private static FlowLayoutPanel Flow(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 0), WrapContents = false };
            foreach (var control in controls)
                control.Anchor = AnchorStyles.Left;
            row.Controls.AddRange(controls);
            return row;
        }

        private static Label Caption(string text) => new Label { Text = text, AutoSize = true };

        private static ComboBox RoleCombo()
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
            combo.Items.AddRange(Roles.Select(x => (object)x.Label).ToArray());
            return combo;
        }

        private void Switch()
        {
            if (_loading) return;
            if (!_host.SetPartyMode(_enabled.Checked))
                MessageBox.Show(this, "Party Mode could not be saved, so it may revert when MusicBee restarts.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Reload();
        }

        /// <summary>Render the core's Party Mode state, keeping the device selection.</summary>
        private void Reload()
        {
            var status = _host.ReadPartyModeStatus();
            _loading = true;
            try
            {
                _enabled.Enabled = status != null;
                _enabled.Checked = status != null && status.enabled;
                _allDevices = status?.devices ?? new List<PartyDevice>();
                ShowDevices();
                ShowCode(status);
                ShowRefusals(status?.refusals);
            }
            finally
            {
                _loading = false;
            }

            _timer.Interval = string.IsNullOrEmpty(_code) ? IdleRefreshMs : CountdownRefreshMs;
        }

        /// <summary>
        ///     Fills the list with the devices the search matches, keeping the
        ///     selection and the scroll position.
        /// </summary>
        /// <remarks>
        ///     Skipped when the rows would come out the same: the list refreshes on a
        ///     timer, and rebuilding it each time threw away the place being read.
        /// </remarks>
        private void ShowDevices()
        {
            var rows = _allDevices
                .Where(Matches)
                .Select(device => new
                {
                    Device = device,
                    Cells = new[]
                    {
                        device.label ?? string.Empty,
                        KindLabel(device.kind),
                        RoleLabel(device.role),
                        device.address ?? string.Empty,
                        device.last_seen > 0 ? PanelText.Ago(device.last_seen) : "not yet",
                    },
                })
                .ToList();
            var shown = string.Join("\n", rows.Select(r => r.Device.key + "\t" + string.Join("\t", r.Cells)));
            _trustNote.Visible = _allDevices.Any(d => d.weaker_trust);
            if (shown == _shown) return;
            _shown = shown;

            var selected = SelectedDevice()?.key;
            var top = (_devices.TopItem?.Tag as PartyDevice)?.key;

            _devices.BeginUpdate();
            _devices.Items.Clear();
            foreach (var row in rows)
            {
                var item = new ListViewItem(row.Cells) { Tag = row.Device };
                item.ToolTipText = row.Device.weaker_trust
                    ? row.Device.label + " - known by an unverified id (weaker trust)"
                    : row.Device.label;
                if (row.Device.key == selected) item.Selected = true;
                _devices.Items.Add(item);
            }

            _devices.EndUpdate();
            var keep = _devices.Items.Cast<ListViewItem>().FirstOrDefault(i => ((PartyDevice)i.Tag).key == top);
            if (keep != null) _devices.TopItem = keep;
            _setRole.Enabled = _devices.SelectedItems.Count > 0;
        }

        /// <summary>Whether a device matches the search, by name, kind, role or address.</summary>
        private bool Matches(PartyDevice device)
        {
            var term = _search.Text.Trim();
            if (term.Length == 0) return true;
            return new[] { device.label, KindLabel(device.kind), RoleLabel(device.role), device.address }
                .Any(text => (text ?? string.Empty).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>The role picker follows the chosen device, so Set role starts from its current role.</summary>
        private void FollowSelection()
        {
            var device = SelectedDevice();
            _setRole.Enabled = device != null;
            if (device == null) return;
            var index = Array.FindIndex(Roles, r => r.Value == device.role);
            if (index >= 0) _role.SelectedIndex = index;
        }

        private void SetSelectedRole()
        {
            var device = SelectedDevice();
            if (device == null || _role.SelectedIndex < 0) return;
            if (!_host.SetPartyRole(device.key, Roles[_role.SelectedIndex].Value))
                MessageBox.Show(this, "That role could not be set.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Reload();
        }

        private PartyDevice SelectedDevice()
        {
            return _devices.SelectedItems.Count > 0 ? (PartyDevice)_devices.SelectedItems[0].Tag : null;
        }

        private void ShowCode(PartyModeStatus status)
        {
            var code = status?.pairing_code;
            if (code != _code)
            {
                _code = string.IsNullOrEmpty(code) ? null : code;
                _copyCode.Text = "Copy code";
            }

            _copyCode.Visible = _code != null;
            if (status == null)
                _codeStatus.Text = "Not running";
            else if (_code != null)
                _codeStatus.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    "Enter {0} on the device - grants {1} - {2} left",
                    _code,
                    RoleLabel(status.pairing_code_role),
                    PanelText.Countdown(status.pairing_code_expires_in));
            else
                _codeStatus.Text = status.pairing_code_voided
                    ? "That code was voided after too many wrong attempts - show another."
                    : "A code pairs one browser or app, and gives it the role picked here.";
        }

        private void CopyCode()
        {
            if (_code == null) return;
            Clipboard.SetText(_code);
            _copyCode.Text = "Copied";
        }

        private void ShowRefusals(List<PartyRefusal> refusals)
        {
            _refusals.BeginUpdate();
            _refusals.Items.Clear();
            foreach (var refusal in refusals ?? new List<PartyRefusal>())
            {
                var when = DateTimeOffset.FromUnixTimeMilliseconds(refusal.unix_ms).ToLocalTime();
                var row = new ListViewItem(when.ToString("HH:mm:ss", CultureInfo.CurrentCulture));
                row.SubItems.Add(refusal.client ?? string.Empty);
                row.SubItems.Add(refusal.op ?? string.Empty);
                row.SubItems.Add(string.IsNullOrEmpty(refusal.capability) ? "-" : refusal.capability);
                row.ToolTipText = refusal.message;
                _refusals.Items.Add(row);
            }

            _refusals.EndUpdate();
        }

        internal static string RoleLabel(string role)
        {
            var index = Array.FindIndex(Roles, r => r.Value == role);
            return index >= 0 ? Roles[index].Label : role ?? string.Empty;
        }

        private static string KindLabel(string kind)
        {
            switch (kind)
            {
                case "app": return "App";
                case "browser": return "Browser";
                case "android": return "Android";
                default: return kind ?? string.Empty;
            }
        }
    }
}
