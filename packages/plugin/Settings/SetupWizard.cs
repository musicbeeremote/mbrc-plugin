using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using MusicBeePlugin.Ffi;
using MusicBeePlugin.Host;
using static MusicBeePlugin.Settings.SettingsLayout;

namespace MusicBeePlugin.Settings
{
    /// <summary>
    ///     The first-run setup wizard: firewall, web remote, update checks, and a
    ///     live test that a phone can connect.
    /// </summary>
    /// <remarks>
    ///     Opens once per <c>setup_version</c>. Finish, Skip and closing the window
    ///     all record that setup is done, so it never asks twice. Finish saves the
    ///     choices in the same write; the firewall rule is the one change made at
    ///     once, because it needs the user's permission there and then.
    /// </remarks>
    internal sealed class SetupWizard : Form
    {
        private const string AndroidAppUrl = "https://play.google.com/store/apps/details?id=com.kelsos.mbrc";

        // The page less its padding, a paragraph's own margins (7 + 3) and 4 px of slack.
        private const int ParagraphWidth = DialogWidth - NavWidth - 2 * PagePadding - 14;

        private readonly PluginHost _host;
        private readonly CoreSettings _saved;
        private readonly SetupStatus _status;
        private readonly IList<string> _addresses;
        private readonly SettingsRows _rows = new SettingsRows(ValueColumnWidth, BlockGap);
        private readonly List<Control> _pages = new List<Control>();
        private readonly List<Label> _stepLabels = new List<Label>();
        private readonly Font _headingFont;
        private readonly Font _boldFont;
        private readonly Timer _clientPoll;

        private FlowLayoutPanel _steps;
        private Panel _content;
        private Button _back;
        private Button _next;
        private Label _footerStatus;
        private int _step = -1;
        private bool _done;

        private Control _firewallPage;
        private Label _firewallStatus;
        private Button _fixFirewall;
        private Button _recheckFirewall;
        private Label _publicWarning;
        private CheckBox _publicConfirm;
        private bool _firewallBusy;
        private bool _firewallChecked;
        private FirewallStatus _firewall;

        private CheckBox _webEnabled;
        private CheckBox _webAuth;
        private CheckBox _autoCheck;
        private RadioButton _testing;
        private Label _clientStatus;

        public SetupWizard(PluginHost host, SetupStatus status)
        {
            _host = host;
            _status = status ?? new SetupStatus();
            _saved = host.ReadSettings() ?? new CoreSettings();
            _addresses = PanelText.Addresses(host.ReadListeningAddresses());

            Text = "MusicBee Remote setup";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            SuspendLayout();
            AutoScaleDimensions = BaseScale;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(DialogWidth, DialogHeight);
            _headingFont = new Font(Font.FontFamily, Font.Size + 3, FontStyle.Bold);
            _boldFont = new Font(Font, FontStyle.Bold);
            BuildLayout();
            ResumeLayout(false);

            _clientPoll = new Timer { Interval = 1000 };
            _clientPoll.Tick += (s, e) => ShowClients();

            ShowStep(0);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            FitTallestPage(this, _content, _pages);
        }

        /// <summary>Closing the window counts as Skip: the wizard never asks twice.</summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (!_done) _host.CompleteSetup(null);
            _clientPoll.Stop();
            _clientPoll.Dispose();
            _headingFont.Dispose();
            _boldFont.Dispose();
            base.OnFormClosed(e);
        }

        internal int StepCount => _pages.Count;

        private bool OnLastStep => _step == _pages.Count - 1;

        private void BuildLayout()
        {
            _steps = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = SystemColors.Window,
                Padding = new Padding(12, 12, 0, 0),
                Margin = new Padding(0),
            };
            _content = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };

            AddPage("Welcome", "Welcome to MusicBee Remote 2.0", BuildWelcome());
            _firewallPage = AddPage("Firewall", "Firewall", BuildFirewall());
            AddPage("Web remote", "Web remote", BuildWebRemote());
            AddPage("Updates", "Updates", BuildUpdates());
            AddPage("Connect a device", "Connect a device", BuildConnect());

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, NavWidth));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(_steps, 0, 0);
            root.Controls.Add(_content, 1, 0);
            var footer = BuildFooter();
            root.Controls.Add(footer, 0, 1);
            root.SetColumnSpan(footer, 2);
            Controls.Add(root);
        }

        private TableLayoutPanel BuildFooter()
        {
            var skip = new Button { Text = "Skip", AutoSize = true, MinimumSize = new Size(90, 0), Anchor = AnchorStyles.Left };
            skip.Click += (s, e) => Close();
            _footerStatus = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.Firebrick };

            _back = new Button { Text = "Back", Width = 90 };
            _back.Click += (s, e) => ShowStep(_step - 1);
            _next = new Button { Text = "Next", Width = 90 };
            _next.Click += (s, e) => Advance();
            AcceptButton = _next;
            CancelButton = skip;

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0),
            };
            buttons.Controls.Add(_next);
            buttons.Controls.Add(_back);

            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, Padding = new Padding(12, 6, 12, 10) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.Controls.Add(skip, 0, 0);
            row.Controls.Add(_footerStatus, 1, 0);
            row.Controls.Add(buttons, 2, 0);
            return row;
        }

        /// <summary>Add a step: its name in the step list and its page, hidden until reached.</summary>
        private TableLayoutPanel AddPage(string stepName, string heading, TableLayoutPanel body)
        {
            var label = new Label { Text = stepName, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
            _stepLabels.Add(label);
            _steps.Controls.Add(label);

            var frame = PageFrame(heading, body, _headingFont);
            _pages.Add(frame);
            _content.Controls.Add(frame);
            return frame;
        }

        /// <summary>A page body: a wrapping paragraph, then captioned rows.</summary>
        private static TableLayoutPanel Body(string intro, out TableLayoutPanel rows)
        {
            var body = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Margin = new Padding(0) };
            body.Controls.Add(Paragraph(intro));
            rows = Table();
            rows.Dock = DockStyle.Top;
            rows.AutoSize = true;
            body.Controls.Add(rows);
            return body;
        }

        private static Label Paragraph(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(ParagraphWidth, 0),
            Margin = new Padding(7, 0, 3, BlockGap),
        };

        private static Label Note(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = SystemColors.GrayText,
        };

        private Label Subheading(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            Font = _boldFont,
            Margin = new Padding(7, 0, 3, 6),
        };

        /// <summary>A bulleted list whose wrapped lines stay indented under their text, not under the bullet.</summary>
        private static TableLayoutPanel Bullets(params string[] items)
        {
            const int bulletWidth = 16;
            var list = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                Margin = new Padding(7, 0, 3, BlockGap),
            };
            list.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, bulletWidth));
            list.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            foreach (var item in items)
            {
                var row = list.RowCount;
                list.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                list.Controls.Add(new Label { Text = "•", AutoSize = true, Margin = new Padding(0, 0, 0, 6) }, 0, row);
                list.Controls.Add(new Label
                {
                    Text = item,
                    AutoSize = true,
                    MaximumSize = new Size(ParagraphWidth - bulletWidth, 0),
                    Margin = new Padding(0, 0, 0, 6),
                }, 1, row);
                list.RowCount = row + 1;
            }

            return list;
        }

        /// <summary>
        ///     A link that opens <paramref name="url" />, showing <paramref name="text" /> or the URL itself.
        ///     The top padding levels its first line with the row's caption.
        /// </summary>
        private static LinkLabel Link(string url, string text = null, int top = 6)
        {
            var link = new LinkLabel
            {
                Text = text ?? url,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, top, 0, 0),
                Margin = new Padding(3, 0, 3, 2),
            };
            link.LinkClicked += (s, e) => TryOpenUrl(url, out _);
            return link;
        }

        /// <summary>One link per URL, stacked.</summary>
        private static FlowLayoutPanel Links(IEnumerable<string> urls)
        {
            var list = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0),
            };
            foreach (var url in urls) list.Controls.Add(Link(url, top: list.Controls.Count == 0 ? 6 : 0));
            return list;
        }

        private TableLayoutPanel BuildWelcome()
        {
            var body = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Margin = new Padding(0) };
            body.Controls.Add(Paragraph(
                "A few quick questions get MusicBee Remote ready for your phone and your network."));
            body.Controls.Add(Subheading("What's new in 2.0"));
            body.Controls.Add(Bullets(
                "Web remote: control MusicBee from a browser on any device on your network.",
                "Party Mode: guests add songs to the queue, with roles you choose (Tools > MusicBee Remote).",
                "A new protocol for upcoming versions of the Android app. The current version keeps working."));
            if (_status.upgrade)
                body.Controls.Add(Paragraph(
                    "Your settings from the previous version carried over. Nothing changes until you press Finish."));
            body.Controls.Add(Paragraph(
                "Skip keeps your current settings. You can run setup again from Preferences > Plugins > "
                + "MusicBee Remote."));
            return body;
        }

        private TableLayoutPanel BuildFirewall()
        {
            var port = _saved.port.ToString(CultureInfo.CurrentCulture);
            var body = Body(
                "Windows Firewall has to let phones reach port " + port + ". This checks only the rule "
                + "MusicBee Remote adds.", out var rows);

            _firewallStatus = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) };
            _fixFirewall = new Button { Text = "Add the rule", AutoSize = true, Visible = false };
            _fixFirewall.Click += (s, e) => FixFirewall();
            _recheckFirewall = new Button { Text = "Check again", AutoSize = true };
            _recheckFirewall.Click += (s, e) => CheckFirewall();

            _publicWarning = new Label
            {
                Text = "Windows treats this network as Public. With port " + port
                       + " open, anyone on this network can reach MusicBee Remote.",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, 6, 0, 0),
            };
            _publicConfirm = new CheckBox
            {
                Text = "I understand, continue on this network",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
            };
            _publicConfirm.CheckedChanged += (s, e) =>
            {
                // On a Public network, pairing starts on; the next step can still turn it off.
                if (_publicConfirm.Checked) _webAuth.Checked = true;
                UpdateButtons();
            };

            _rows.Add(rows, "Status", _firewallStatus);
            _rows.Add(rows, string.Empty, ActionRow(_fixFirewall, _recheckFirewall), endsBlock: true);
            _rows.Add(rows, "Public network", _publicWarning);
            _rows.Add(rows, string.Empty, _publicConfirm);
            _rows.SetVisible(_publicWarning, false);
            _rows.SetVisible(_publicConfirm, false);
            return body;
        }

        private TableLayoutPanel BuildWebRemote()
        {
            var body = Body(
                "MusicBee Remote can also serve a remote you open in a browser, on port "
                + _saved.port.ToString(CultureInfo.CurrentCulture) + ".", out var rows);

            _webEnabled = new CheckBox
            {
                Text = "Serve the web remote",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Checked = _saved.web_enabled,
            };
            _webAuth = new CheckBox
            {
                Text = "Require browsers to pair with a code",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Checked = _saved.web_auth_required,
                Enabled = _saved.web_enabled,
            };
            _webEnabled.CheckedChanged += (s, e) => _webAuth.Enabled = _webEnabled.Checked;

            _rows.Add(rows, "Web remote", _webEnabled);
            _rows.Add(rows, "Pairing", _webAuth);
            _rows.Add(rows, string.Empty,
                Note("Pairing asks each browser for a code shown in MusicBee before it can control anything."),
                endsBlock: true);
            _rows.Add(rows, "Open", _addresses.Count > 0
                ? (Control)Links(_addresses.Select(a => "http://" + a + "/"))
                : Note("No network address found."));
            return body;
        }

        private TableLayoutPanel BuildUpdates()
        {
            var body = Body(
                "MusicBee Remote can check for a new release once a day. Each check is a request to github.com.",
                out var rows);

            _autoCheck = new CheckBox
            {
                Text = "Check for updates automatically",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Checked = _saved.update_check_enabled,
            };
            var testing = string.Equals(_saved.update_channel, "testing", StringComparison.OrdinalIgnoreCase);
            var stable = new RadioButton { Text = "Stable releases", AutoSize = true, Checked = !testing };
            _testing = new RadioButton { Text = "Testing: also offer pre-releases", AutoSize = true, Checked = testing };
            var channel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0),
            };
            channel.Controls.Add(stable);
            channel.Controls.Add(_testing);

            _rows.Add(rows, "Automatic", _autoCheck, endsBlock: true);
            _rows.Add(rows, "Channel", channel);
            _rows.Add(rows, string.Empty, Note("The settings window has Check now either way."));
            return body;
        }

        private TableLayoutPanel BuildConnect()
        {
            var body = Body(
                "Install MusicBee Remote on your phone, join the same network as this PC, and enter one of these "
                + "addresses in the app.", out var rows);

            var addressList = new Label
            {
                Text = _addresses.Count > 0 ? string.Join(Environment.NewLine, _addresses) : "No network address found.",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, 6, 0, 0),
            };
            _clientStatus = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) };

            _rows.Add(rows, "Address", addressList, endsBlock: true);
            _rows.Add(rows, "Android app", Link(AndroidAppUrl, "MusicBee Remote on Google Play"), endsBlock: true);
            _rows.Add(rows, "Status", _clientStatus);
            return body;
        }

        private void Advance()
        {
            if (OnLastStep)
                Finish();
            else
                ShowStep(_step + 1);
        }

        internal void ShowStep(int step)
        {
            if (step < 0 || step >= _pages.Count) return;
            _step = step;
            for (var i = 0; i < _pages.Count; i++)
            {
                _pages[i].Visible = i == step;
                _stepLabels[i].Font = i == step ? _boldFont : Font;
                _stepLabels[i].ForeColor = i > step ? SystemColors.GrayText : SystemColors.ControlText;
            }

            if (_pages[step] == _firewallPage && !_firewallChecked) CheckFirewall();
            if (OnLastStep)
            {
                ShowClients();
                _clientPoll.Start();
            }
            else
            {
                _clientPoll.Stop();
            }

            UpdateButtons();
        }

        private void UpdateButtons()
        {
            _back.Enabled = _step > 0;
            _next.Text = OnLastStep ? "Finish" : "Next";
            var unconfirmedPublic = _firewall?.public_network == "on" && !_publicConfirm.Checked;
            _next.Enabled = _pages[_step] != _firewallPage || (!_firewallBusy && !unconfirmedPublic);
            _fixFirewall.Enabled = !_firewallBusy;
            _recheckFirewall.Enabled = !_firewallBusy;
        }

        private void CheckFirewall()
        {
            _firewallChecked = true;
            _firewallBusy = true;
            SetFirewallStatus("Checking...", SystemColors.GrayText);
            UpdateButtons();
            var port = _saved.port;
            OffThread(this, () => _host.ReadFirewallStatus(port), ShowFirewall);
        }

        internal void ShowFirewall(FirewallStatus status)
        {
            _firewallBusy = false;
            _firewall = status;
            var port = _saved.port.ToString(CultureInfo.CurrentCulture);
            var blocked = false;

            if (status == null)
                SetFirewallStatus(
                    "Could not read the firewall's state. You can continue; if your phone cannot connect, "
                    + "check the firewall.", SystemColors.GrayText);
            else if (status.firewall == "off")
                SetFirewallStatus("Windows Firewall is off, so nothing blocks port " + port + ".", Color.Green);
            else if (status.rule == "open")
                SetFirewallStatus("Port " + port + " is open to MusicBee Remote.", Color.Green);
            else
            {
                blocked = true;
                SetFirewallStatus(DescribeBlocked(status.rule, port), Color.Firebrick);
                _fixFirewall.Text = status.rule == "missing" ? "Add the rule" : "Fix the rule";
            }

            _fixFirewall.Visible = blocked;
            var isPublic = status?.public_network == "on";
            _rows.SetVisible(_publicWarning, isPublic);
            _rows.SetVisible(_publicConfirm, isPublic);
            UpdateButtons();
        }

        private static string DescribeBlocked(string rule, string port)
        {
            switch (rule)
            {
                case "missing": return "No firewall rule lets phones reach port " + port + " yet.";
                case "disabled": return "The MusicBee Remote firewall rule is turned off.";
                default: return "The MusicBee Remote firewall rule does not include port " + port + ".";
            }
        }

        private void FixFirewall()
        {
            _firewallBusy = true;
            SetFirewallStatus("Waiting for Windows to allow the change...", SystemColors.GrayText);
            UpdateButtons();
            var port = _saved.port;
            OffThread(this, () => _host.FixFirewall(port), outcome =>
            {
                switch (outcome)
                {
                    case "done":
                    case "pending":
                        CheckFirewall();
                        return;
                    case "declined":
                        SetFirewallStatus("The permission prompt was declined, so nothing changed.", Color.Firebrick);
                        break;
                    case "not_elevated":
                        SetFirewallStatus("Adding the rule needs administrator rights.", Color.Firebrick);
                        break;
                    default:
                        SetFirewallStatus("The rule could not be added. The log has the details.", Color.Firebrick);
                        break;
                }

                _firewallBusy = false;
                UpdateButtons();
            });
        }

        private void SetFirewallStatus(string text, Color color)
        {
            _firewallStatus.Text = text;
            _firewallStatus.ForeColor = color;
        }

        /// <summary>Show whether a phone has connected; once one has, stop asking.</summary>
        private void ShowClients()
        {
            var connected = _host.ReadSetupStatus()?.connected_devices ?? 0;
            if (connected == 0)
            {
                _clientStatus.Text = "Waiting for a device to connect...";
                _clientStatus.ForeColor = SystemColors.GrayText;
                return;
            }

            _clientPoll.Stop();
            _clientStatus.Text = connected == 1
                ? "A device is connected."
                : connected.ToString(CultureInfo.CurrentCulture) + " devices are connected.";
            _clientStatus.ForeColor = Color.Green;
        }

        /// <summary>Save the choices with the setup version and close; on a refusal, say so and stay.</summary>
        private void Finish()
        {
            var choices = new SetupChoices
            {
                web_enabled = _webEnabled.Checked,
                web_auth_required = _webAuth.Checked,
                update_check_enabled = _autoCheck.Checked,
                update_channel = _testing.Checked ? "testing" : "stable",
            };
            if (!_host.CompleteSetup(choices))
            {
                _footerStatus.Text = "Your choices could not be saved. The log has the details.";
                return;
            }

            _done = true;
            Close();
        }
    }
}
