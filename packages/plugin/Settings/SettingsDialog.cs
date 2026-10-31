using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using MusicBeePlugin.Ffi;
using MusicBeePlugin.Ffi.Generated;
using MusicBeePlugin.Host;
using MusicBeePlugin.Utilities;
using static MusicBeePlugin.Settings.SettingsLayout;

namespace MusicBeePlugin.Settings
{
    /// <summary>
    ///     The MusicBee Remote settings window. Opened from the Configure button in
    ///     MusicBee's plugin preferences panel and from the Tools menu entry. Renders
    ///     the Rust-owned settings (read from the core) into grouped WinForms controls
    ///     and applies edits back through the core. The core is the single source of
    ///     truth; this dialog keeps no store of its own.
    /// </summary>
    internal sealed class SettingsDialog : Form
    {
        // Display label + the log_level value the core round-trips. Order is the
        // combo-box order (increasing verbosity).
        private static readonly (string Label, string Value)[] LogLevels =
        {
            ("Normal", "info"),
            ("Debug", "debug"),
            ("Trace", "trace"),
        };

        // Plugin help / documentation, opened from the footer link.
        private const string HelpUrl = "https://mbrc.kelsos.net/help/plugin/";

        /// <summary>The page shown last, so reopening the window returns to it for the rest of the session.</summary>
        internal static SettingsPage LastPage = SettingsPage.Connection;

        // Closing MusicBee after an update is handed to the helper: the same
        // message its own title bar sends.
        private const uint WM_CLOSE = 0x0010;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private readonly PluginHost _host;
        private readonly string _version;

        private NumericUpDown _port;
        private Label _connStatus;
        private Label _addresses;
        private Button _testConn;
        private ComboBox _filterMode;
        private TextBox _baseIp;
        private NumericUpDown _lastOctet;
        private TextBox _allowedAddresses;
        private Button _blockedBtn;
        private ComboBox _logLevel;
        private CheckBox _firewall;
        private CheckBox _mdns;
        private CheckBox _webEnabled;
        private CheckBox _webAuth;
        private Label _webStatus;
        private Button _webPairBtn;
        private Button _webRevokeBtn;
        private Button _webUnpairOneBtn;
        private Button _webRenameBtn;
        private Button _webCopyCodeBtn;

        /// <summary>The code on offer, or null when none is outstanding.</summary>
        private string _pairingCode;

        /// <summary>
        ///     True while a name is being typed. The countdown redraws the list
        ///     once a second, and redrawing it closes the editor: a rename would
        ///     have to be finished inside a second to survive one.
        /// </summary>
        private bool _renaming;
        private ListView _webPaired;
        private Label _partyStatus;

        /// <summary>
        ///     Ticks while a pairing code is outstanding, so the panel counts it
        ///     down and notices the moment it is used or lapses. Only the core
        ///     knows which of those happened, so this asks rather than assumes.
        /// </summary>
        private System.Windows.Forms.Timer _pairingTick;

        /// <summary>The paired count when the panel last looked.</summary>
        private int _pairedBefore;
        private Label _status;
        private ListBox _nav;
        private Panel _content;
        private Font _headingFont;
        private readonly Dictionary<SettingsPage, Control> _pages = new Dictionary<SettingsPage, Control>();
        private readonly SettingsRows _rows = new SettingsRows(ValueColumnWidth, BlockGap);
        private readonly PageEdits _edits = new PageEdits();
        private IList<SettingsPage> _editedPages = new List<SettingsPage>();

        // Shown only for the filter mode that uses them.
        private Control _rangePanel;
        private Label _allowedHint;

        /// <summary>True while the controls are being filled from the core.</summary>
        private bool _loading;
        private Label _cacheStatus;
        private System.Windows.Forms.Timer _blockedTimer;
        private Button _rebuildMeta;
        private Button _rebuildCovers;
        private Label _updateStatus;
        private Button _updateAction;
        private Button _updateSkip;
        private LinkLabel _releaseNotes;
        private CheckBox _autoCheck;
        private Label _captureStatus;
        private Button _captureStart;
        private Button _captureStop;
        private Button _captureCancel;
        private string _captureState = CaptureStates.Idle;
        private string _bundlePath = string.Empty;
        private string _updateState = UpdateStates.Unknown;
        private string _notesUrl = string.Empty;

        /// <summary>
        ///     The offered release needs a newer MusicBee than this one, so the
        ///     action button re-checks instead of downloading.
        /// </summary>
        private bool _updateBlocked;

        public SettingsDialog(PluginHost host, string version)
        {
            _host = host;
            _version = version;

            Text = "MusicBee Remote";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            // CenterScreen, not CenterParent: the latter only positions a window
            // shown with ShowDialog, and this one is modeless. CenterParent here
            // would silently degrade to "wherever Windows feels like".
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            // Sizes are written for 96 DPI and scale with the font (see SettingsLayout).
            SuspendLayout();
            AutoScaleDimensions = BaseScale;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(DialogWidth, DialogHeight);
            _headingFont = new Font(Font.FontFamily, Font.Size + 3, FontStyle.Bold);
            BuildLayout();
            ResumeLayout(false);
            _nav.ItemHeight = _nav.Font.Height * 2;

            _edits.InputChanged += (s, e) => ShowEdits();
            LoadFromCore();
            LoadListeningAddresses();
            LoadCacheStatus();
            LoadUpdateStatus();
            ShowPage(LastPage);
            RunConnectionTest();

            // The core pushes CacheStatusChanged when a rebuild starts or
            // finishes, and UpdateStatusChanged when a check or download does;
            // refresh those lines live while this dialog is open.
            _host.CoreEvent += OnCoreEvent;

            // What has no event to wait on is polled, for the visible page only:
            // a page catches up the moment it is shown (see RefreshPage).
            _blockedTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            _blockedTimer.Tick += (s, e) => RefreshPage(LastPage);
            _blockedTimer.Start();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            FitPairedColumns();
            FitTallestPage(this, _content, _pages.Values);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _host.CoreEvent -= OnCoreEvent;
            _blockedTimer?.Stop();
            _blockedTimer?.Dispose();
            _headingFont.Dispose();
            base.OnFormClosed(e);
        }

        /// <summary>Switch to <paramref name="page" />, keeping every page's edits.</summary>
        public void ShowPage(SettingsPage page)
        {
            _nav.SelectedIndex = (int)page;
        }

        /// <summary>
        ///     Re-read the core state a page shows that no event announces: the
        ///     blocked log, Party Mode, web pairing, and a capture's countdown.
        /// </summary>
        private void RefreshPage(SettingsPage page)
        {
            switch (page)
            {
                case SettingsPage.Access:
                    LoadBlockedCount();
                    LoadPartyMode();
                    break;
                case SettingsPage.WebRemote:
                    // A code made in the Party Mode window shows here too, counting down.
                    LoadWebStatus();
                    break;
                case SettingsPage.Diagnostics:
                    LoadCaptureStatus();
                    break;
            }
        }

        /// <summary>Split the paired list's width between its columns, leaving room for the scrollbar.</summary>
        private void FitPairedColumns()
        {
            var width = _webPaired.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
            _webPaired.Columns[0].Width = width / 2;
            _webPaired.Columns[1].Width = width / 4;
            _webPaired.Columns[2].Width = width - width / 2 - width / 4;
        }

        /// <summary>Ctrl+Tab and Ctrl+Shift+Tab step through the pages, as in a tabbed dialog.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.Shift | Keys.Tab))
            {
                var step = (keyData & Keys.Shift) == Keys.Shift ? -1 : 1;
                var count = SettingsPages.All.Length;
                _nav.SelectedIndex = (_nav.SelectedIndex + step + count) % count;
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void BuildLayout()
        {
            _nav = new ListBox
            {
                Dock = DockStyle.Fill,
                DrawMode = DrawMode.OwnerDrawFixed,
                IntegralHeight = false,
                BorderStyle = BorderStyle.None,
                Margin = new Padding(0),
            };
            _nav.Items.AddRange(SettingsPages.All.Select(p => (object)p.Title).ToArray());
            _nav.DrawItem += DrawNavItem;
            _nav.SelectedIndexChanged += (s, e) => OnPageSelected();

            _content = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            AddPage(SettingsPage.Connection, BuildConnectionPage());
            AddPage(SettingsPage.Access, BuildAccessPage());
            AddPage(SettingsPage.WebRemote, BuildWebPage());
            AddPage(SettingsPage.Library, BuildLibraryPage());
            AddPage(SettingsPage.Updates, BuildUpdatesPage());
            AddPage(SettingsPage.Diagnostics, BuildDiagnosticsPage());

            var footer = BuildButtonRow();
            footer.Padding = new Padding(12, 6, 12, 10);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, NavWidth));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(_nav, 0, 0);
            root.Controls.Add(_content, 1, 0);
            root.Controls.Add(footer, 0, 1);
            root.SetColumnSpan(footer, 2);
            Controls.Add(root);
        }

        /// <summary>Add a page: a heading over its rows, hidden until selected.</summary>
        private void AddPage(SettingsPage page, TableLayoutPanel rows)
        {
            var frame = PageFrame(SettingsPages.Title(page), rows, _headingFont);
            _pages[page] = frame;
            _edits.Track(page, rows);
            _content.Controls.Add(frame);
        }

        private void OnPageSelected()
        {
            if (_nav.SelectedIndex < 0) return;
            LastPage = (SettingsPage)_nav.SelectedIndex;
            foreach (var entry in _pages)
                entry.Value.Visible = entry.Key == LastPage;
            RefreshPage(LastPage);
        }

        /// <summary>Draws an item's own text, indented; the text carries the unsaved marker (see ShowEdits).</summary>
        private void DrawNavItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            e.DrawBackground();
            var text = (string)_nav.Items[e.Index];
            var bounds = new Rectangle(e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, e.Font, bounds, e.ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private TableLayoutPanel BuildConnectionPage()
        {
            _port = new NumericUpDown { Minimum = 1, Maximum = 65535, Width = 100, Anchor = AnchorStyles.Left };

            _testConn = new Button
            {
                Text = "Test connection",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };
            _testConn.Click += (s, e) => RunConnectionTest();
            _connStatus = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, 6, 0, 0),
                ForeColor = SystemColors.GrayText,
            };

            // The addresses a phone app can be pointed at: each interface IPv4
            // with the bound port.
            _addresses = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, 6, 0, 0),
                ForeColor = SystemColors.GrayText,
            };

            // Additive to the custom UDP discovery every shipped client uses, so
            // clearing it only hides the plugin from Bonjour/NsdManager browsers.
            _mdns = new CheckBox
            {
                Text = "Also advertise over mDNS (Bonjour)",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
            };
            _firewall = new CheckBox { Text = "Add a Windows firewall rule on save", AutoSize = true, Anchor = AnchorStyles.Left };

            var showQr = new Button { Text = "Show a QR code", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            showQr.Click += (s, e) => ConnectQrDialog.ShowFor(_host, this);

            var layout = Table();
            _rows.Add(layout, "Status", _connStatus);
            _rows.Add(layout, string.Empty, ActionRow(_testConn), endsBlock: true);
            _rows.Add(layout, "Reachable at", _addresses);
            _rows.Add(layout, string.Empty, ActionRow(showQr), endsBlock: true);
            _rows.Add(layout, "Listening port", _port);
            _rows.Add(layout, "Discovery", _mdns);
            _rows.Add(layout, "Firewall", _firewall);
            return layout;
        }

        private TableLayoutPanel BuildAccessPage()
        {
            _filterMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, Anchor = AnchorStyles.Left };
            _filterMode.Items.AddRange(new object[] { "All clients", "Address range", "Specific addresses" });
            _filterMode.SelectedIndexChanged += (s, e) => ShowFilterRows();

            _baseIp = new TextBox { Width = 130 };
            _lastOctet = new NumericUpDown { Minimum = 0, Maximum = 255, Width = 60 };
            var rangePanel = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
            rangePanel.Controls.Add(_baseIp);
            rangePanel.Controls.Add(new Label { Text = "to last octet", AutoSize = true, Padding = new Padding(4, 6, 4, 0) });
            rangePanel.Controls.Add(_lastOctet);

            _allowedAddresses = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Width = 280,
                Height = 70,
                Anchor = AnchorStyles.Left,
            };
            _allowedHint = new Label
            {
                Text = "One per line: an IP (192.168.1.50) or a subnet (192.168.1.0/24).",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Anchor = AnchorStyles.Left,
            };

            // Greyed while nothing has been blocked; opens the detail view otherwise.
            _blockedBtn = new Button
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Left,
                Enabled = false,
                Text = "Blocked connections (0)",
            };
            _blockedBtn.Click += (s, e) => ShowBlockedConnections();

            // Party Mode applies at once in its own window rather than on Save.
            _partyStatus = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, 6, 0, 0),
                ForeColor = SystemColors.GrayText,
            };
            var openParty = new Button { Text = "Open Party Mode...", AutoSize = true };
            openParty.Click += (s, e) => PartyModeWindow.Open(_host);

            var layout = Table();
            _rows.Add(layout, "Allowed clients", _filterMode);
            _rangePanel = rangePanel;
            _rows.Add(layout, "Range (base IPv4)", rangePanel);
            _rows.Add(layout, "Allowed addresses", _allowedAddresses);
            _rows.Add(layout, string.Empty, _allowedHint);
            _rows.Add(layout, "Blocked", _blockedBtn, endsBlock: true);
            _rows.Add(layout, "Party Mode", _partyStatus);
            _rows.Add(layout, string.Empty, ActionRow(openParty), endsBlock: true);
            return layout;
        }

        private TableLayoutPanel BuildLibraryPage()
        {
            _cacheStatus = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, 6, 0, 0),
                ForeColor = SystemColors.GrayText,
            };
            _rebuildMeta = new Button
            {
                Text = "Rebuild metadata",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };
            _rebuildMeta.Click += (s, e) => RebuildMetadata();
            _rebuildCovers = new Button
            {
                Text = "Rebuild covers",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };
            _rebuildCovers.Click += (s, e) => RebuildCovers();

            var layout = Table();
            _rows.Add(layout, "Cache", _cacheStatus);
            _rows.Add(layout, string.Empty, ActionRow(_rebuildMeta, _rebuildCovers), endsBlock: true);
            return layout;
        }

        private TableLayoutPanel BuildUpdatesPage()
        {
            _updateStatus = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, 6, 0, 0),
                ForeColor = SystemColors.GrayText,
                // Wrap instead of running off the fixed-width dialog: this line
                // carries whole sentences (a failure reason, a build
                // requirement), not the one-word status the other groups show.
            };

            // One button whose meaning follows the state (check / download /
            // install), because at any moment there is exactly one sensible next
            // step and three permanently-greyed buttons explain nothing.
            _updateAction = new Button
            {
                Text = "Check now",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };
            _updateAction.Click += (s, e) => RunUpdateAction();

            _updateSkip = new Button
            {
                Text = "Skip this version",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Visible = false,
            };
            _updateSkip.Click += (s, e) => SkipUpdate();

            _releaseNotes = new LinkLabel
            {
                Text = "Release notes",
                AutoSize = true,
                Padding = new Padding(0, 5, 0, 0),
                Visible = false,
            };
            _releaseNotes.LinkClicked += (s, e) => OpenReleaseNotes();

            var buttons = ActionRow(_updateAction, _updateSkip, _releaseNotes);

            _autoCheck = new CheckBox
            {
                Text = "Check for updates automatically",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
            };

            var layout = Table();
            _rows.Add(layout, "Status", _updateStatus);
            _rows.Add(layout, string.Empty, buttons, endsBlock: true);
            _rows.Add(layout, "Schedule", _autoCheck);
            return layout;
        }

        // Start capture -> reproduce the problem -> Stop capture, which leaves
        // one zip on the Desktop to attach to an issue.
        private TableLayoutPanel BuildDiagnosticsPage()
        {
            _logLevel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, Anchor = AnchorStyles.Left };
            _logLevel.Items.AddRange(LogLevels.Select(x => (object)x.Label).ToArray());

            var openLogs = new Button
            {
                Text = "Open log folder",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Left,
            };
            openLogs.Click += (s, e) => OpenLogFolder();

            _captureStatus = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, 6, 0, 0),
                ForeColor = SystemColors.GrayText,
            };
            _captureStart = new Button
            {
                Text = "Start capture",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };
            _captureStart.Click += (s, e) => StartCapture();
            _captureStop = new Button
            {
                Text = "Stop and save",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };
            _captureStop.Click += (s, e) => StopCapture();
            _captureCancel = new Button
            {
                Text = "Cancel",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };
            _captureCancel.Click += (s, e) => CancelCapture();

            var buttons = ActionRow(_captureStart, _captureStop, _captureCancel);

            var layout = Table();
            _rows.Add(layout, "Log level", _logLevel);
            _rows.Add(layout, string.Empty, ActionRow(openLogs), endsBlock: true);
            _rows.Add(layout, "Capture", _captureStatus);
            _rows.Add(layout, string.Empty, buttons, endsBlock: true);
            return layout;
        }

        private TableLayoutPanel BuildButtonRow()
        {
            // Bottom-left: the version (the title bar already names the plugin), a
            // Help link to the online docs, and the save status shown when saving.
            var version = new Label
            {
                Text = string.IsNullOrEmpty(_version) ? "MusicBee Remote" : $"v{_version}",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, 4, 0, 0),
                Margin = new Padding(0, 0, 10, 0),
            };
            var help = new LinkLabel
            {
                Text = "Help",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, 4, 0, 0),
                Margin = new Padding(0, 0, 12, 0),
            };
            help.LinkClicked += (s, e) => OpenHelp();
            _status = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 4, 0, 0), Margin = new Padding(0) };

            var left = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 8, 0, 0),
            };
            left.Controls.Add(version);
            left.Controls.Add(help);
            left.Controls.Add(_status);

            var save = new Button { Text = "Save", Width = 90, Margin = new Padding(6, 0, 0, 0) };
            save.Click += (s, e) => Apply();

            // Closed explicitly rather than through DialogResult: this window is
            // shown modeless (so MusicBee stays usable behind it), and assigning
            // DialogResult only closes a form that was shown with ShowDialog.
            // CancelButton still routes Esc here, so both paths end up in Close().
            var close = new Button { Text = "Close", Width = 90, Margin = new Padding(6, 0, 0, 0) };
            close.Click += (s, e) => Close();
            CancelButton = close;

            // RightToLeft + no wrapping keeps Save/Close side by side (Save leftmost,
            // Close rightmost) instead of stacking when the cell is narrow.
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0, 4, 0, 0),
            };
            buttons.Controls.Add(close);
            buttons.Controls.Add(save);

            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.Controls.Add(left, 0, 0);
            row.Controls.Add(buttons, 1, 0);
            return row;
        }

        /// <summary>
        ///     The Web remote page: the two toggles, plus the pairing controls
        ///     that only matter while pairing is enforced.
        /// </summary>
        private TableLayoutPanel BuildWebPage()
        {
            _webEnabled = new CheckBox
            {
                Text = "Serve the web remote on the listening port",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
            };
            _webAuth = new CheckBox
            {
                Text = "Require browsers to pair with a code",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
            };
            _webAuth.CheckedChanged += (s, e) => UpdateWebEnabled();
            _webEnabled.CheckedChanged += (s, e) => UpdateWebEnabled();

            _webStatus = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, 6, 0, 0),
                ForeColor = SystemColors.GrayText,
            };

            // Codes are made in the Party Mode window, which picks the role a code
            // grants; Host is preselected there, which is what web pairing needs.
            _webPairBtn = new Button { Text = "Show pairing code", AutoSize = true };
            _webPairBtn.Click += (s, e) => PartyModeWindow.Open(_host);
            _webCopyCodeBtn = new Button { Text = "Copy code", AutoSize = true, Visible = false };
            _webCopyCodeBtn.Click += (s, e) => CopyPairingCode();
            _webRevokeBtn = new Button { Text = "Unpair all browsers", AutoSize = true };
            _webRevokeBtn.Click += (s, e) =>
            {
                _host.RevokeWebPairings();
                LoadWebStatus();
            };
            _webUnpairOneBtn = new Button { Text = "Unpair selected", AutoSize = true, Enabled = false };
            _webUnpairOneBtn.Click += (s, e) => UnpairSelected();
            // F2 does this too, and nobody finds F2.
            _webRenameBtn = new Button { Text = "Rename", AutoSize = true, Enabled = false };
            _webRenameBtn.Click += (s, e) =>
            {
                if (_webPaired.SelectedItems.Count > 0) _webPaired.SelectedItems[0].BeginEdit();
            };

            // Columns rather than one line per browser: a name, a date and a
            // time on one row is a line that runs off the end of any box it is
            // given, and the part that fell off was the part being read.
            _webPaired = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Height = 112,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(3, 2, 3, 2)
            };
            _webPaired.Columns.Add("Browser");
            _webPaired.Columns.Add("Paired");
            _webPaired.Columns.Add("Last seen");
            _webPaired.ShowItemToolTips = true;
            // Edited in place, which fills the box with the current name: a
            // rename is nearly always a correction to it rather than a fresh
            // thought, and retyping one is how a good name gets lost.
            _webPaired.LabelEdit = true;
            _webPaired.BeforeLabelEdit += (s, e) => _renaming = true;
            _webPaired.AfterLabelEdit += RenameBrowser;
            _webPaired.SelectedIndexChanged += (s, e) => FollowSelection();

            var layout = Table();
            _rows.Add(layout, "Web remote", _webEnabled);
            _rows.Add(layout, "Pairing", _webAuth);
            _rows.Add(layout, "Status", _webStatus);
            _rows.Add(layout, string.Empty, ActionRow(_webPairBtn, _webCopyCodeBtn), endsBlock: true);
            _rows.Add(layout, "Paired", _webPaired);
            _rows.Add(layout, string.Empty, ActionRow(_webRenameBtn, _webUnpairOneBtn, _webRevokeBtn), endsBlock: true);
            return layout;
        }

        /// <summary>
        ///     Render the core's pairing state: the outstanding code if there is
        ///     one, otherwise how many browsers are paired.
        /// </summary>
        private void LoadWebStatus()
        {
            var status = _host.ReadWebStatus();
            if (status == null)
            {
                _webStatus.Text = "Not running";
                OfferCode(null);
                return;
            }

            var wasOffered = _pairingTick != null && _pairingTick.Enabled;
            var pairedNow = status.paired_count;
            OfferCode(status.pairing_code);

            if (!string.IsNullOrEmpty(status.pairing_code))
            {
                _webStatus.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    "Enter {0} in the browser - {1} left",
                    status.pairing_code,
                    PanelText.Countdown(status.pairing_code_expires_in));
                StartPairingTick();
            }
            else
            {
                // A code that has gone was either spent or lapsed, and only the
                // count tells them apart: without it the panel would report a
                // browser pairing as a code running out.
                if (wasOffered)
                    _webStatus.Text = pairedNow > _pairedBefore
                        ? "Paired. That code is spent."
                        : "That code expired - press Show pairing code for another.";
                else
                    _webStatus.Text = Describe(pairedNow);
                StopPairingTick();
            }

            _pairedBefore = pairedNow;
            ShowPairedBrowsers(status.paired);
        }

        /// <summary>
        ///     Points the copy button at <paramref name="code" />, or hides it
        ///     when there is nothing to copy.
        ///
        ///     The acknowledgement on the button lasts until the code changes
        ///     rather than for a moment, because the countdown redraws this panel
        ///     every second and anything shorter would rarely be seen.
        /// </summary>
        private void OfferCode(string code)
        {
            if (code != _pairingCode)
            {
                _pairingCode = code;
                _webCopyCodeBtn.Text = "Copy code";
            }

            _webCopyCodeBtn.Visible = !string.IsNullOrEmpty(code);
        }

        /// <summary>
        ///     Puts the code on the clipboard on its own.
        ///
        ///     The status line reads as a sentence and the code sits in the
        ///     middle of it, so selecting it by hand costs more than the typing
        ///     it saves, and the countdown rewrites the line under any selection
        ///     a second later.
        /// </summary>
        private void CopyPairingCode()
        {
            if (string.IsNullOrEmpty(_pairingCode)) return;
            Clipboard.SetText(_pairingCode);
            _webCopyCodeBtn.Text = "Copied";
        }

        private static string Describe(int paired)
        {
            if (paired == 0) return "No browsers paired";
            return paired == 1 ? "1 browser paired" : paired + " browsers paired";
        }

        /// <summary>Fills the list, keeping the selection where it still exists.</summary>
        private void ShowPairedBrowsers(List<PairedBrowser> paired)
        {
            // Never while a name is being typed: rebuilding closes the editor.
            if (_renaming) return;

            var selected = SelectedBrowserId();

            _webPaired.BeginUpdate();
            _webPaired.Items.Clear();
            foreach (var browser in paired ?? new List<PairedBrowser>())
            {
                var label = string.IsNullOrEmpty(browser.label) ? "browser" : browser.label;
                var row = new ListViewItem(label) { Tag = browser.id ?? string.Empty };
                row.SubItems.Add(PanelText.Ago(browser.paired_at));
                row.SubItems.Add(PanelText.Ago(browser.last_seen));
                // The whole row on hover, for a name wider than its column.
                row.ToolTipText = string.Format(CultureInfo.CurrentCulture,
                    "{0} - paired {1}, last seen {2}",
                    label, PanelText.Ago(browser.paired_at), PanelText.Ago(browser.last_seen));
                if ((string)row.Tag == selected) row.Selected = true;
                _webPaired.Items.Add(row);
            }

            _webPaired.EndUpdate();
            FollowSelection();
        }

        /// <summary>The per-browser buttons only mean something with a row chosen.</summary>
        private void FollowSelection()
        {
            var chosen = _webPaired.SelectedItems.Count > 0;
            _webUnpairOneBtn.Enabled = chosen;
            _webRenameBtn.Enabled = chosen;
        }

        /// <summary>
        ///     Takes the edited name, or puts the old one back when it was
        ///     cancelled, emptied, or refused by the core.
        /// </summary>
        private void RenameBrowser(object sender, LabelEditEventArgs e)
        {
            _renaming = false;
            var id = (string)_webPaired.Items[e.Item].Tag;
            var label = (e.Label ?? string.Empty).Trim();
            if (label.Length == 0 || !_host.RenameWebPairing(id, label))
            {
                e.CancelEdit = true;
                return;
            }

            // The core trims and bounds what it keeps, so the row is redrawn
            // from what it stored rather than from what was typed.
            e.CancelEdit = true;
            LoadWebStatus();
        }

        private string SelectedBrowserId()
        {
            return _webPaired.SelectedItems.Count > 0
                ? (string)_webPaired.SelectedItems[0].Tag
                : null;
        }

        private void UnpairSelected()
        {
            var id = SelectedBrowserId();
            if (id == null) return;

            // Refused means it was already gone, which the refreshed list shows.
            if (!_host.RevokeWebPairing(id))
                SetStatus("That browser was already unpaired.", true);
            LoadWebStatus();
        }

        private void StartPairingTick()
        {
            if (_pairingTick == null)
            {
                _pairingTick = new System.Windows.Forms.Timer { Interval = 1000 };
                _pairingTick.Tick += (s, e) => LoadWebStatus();
            }
            _pairingTick.Start();
        }

        private void StopPairingTick()
        {
            if (_pairingTick != null) _pairingTick.Stop();
        }

        // The pairing buttons only mean anything while the server serves and
        // demands a token; leaving them live otherwise invites a code nothing
        // will ever ask for.
        private void UpdateWebEnabled()
        {
            _webAuth.Enabled = _webEnabled.Checked;
            var pairing = _webEnabled.Checked && _webAuth.Checked;
            _webPairBtn.Enabled = pairing;
            _webRevokeBtn.Enabled = pairing;
            if (_webPaired != null) _webPaired.Enabled = pairing;
            if (_webUnpairOneBtn != null && _webRenameBtn != null)
            {
                var chosen = pairing && _webPaired.SelectedItems.Count > 0;
                _webUnpairOneBtn.Enabled = chosen;
                _webRenameBtn.Enabled = chosen;
            }
        }

        /// <summary>Summarize Party Mode: off, or on with how many devices hold a role.</summary>
        private void LoadPartyMode()
        {
            var status = _host.ReadPartyModeStatus();
            if (status == null)
            {
                _partyStatus.Text = "Not running";
                return;
            }

            if (!status.enabled)
            {
                _partyStatus.Text = "Off";
                return;
            }

            var promoted = (status.devices ?? new List<PartyDevice>()).Count(d => d.role != "guest");
            _partyStatus.Text = promoted == 1
                ? "On - 1 device has a role"
                : "On - " + promoted + " devices have a role";
        }

        /// <summary>Open the log folder (mbrc-core.log + bootstrap) in Explorer.</summary>
        // Render whatever the core says the capture is doing. The single source
        // of truth is the core's status - this method never infers state from
        // which button was pressed, so a capture started before this dialog was
        // opened (or resumed across a MusicBee restart) renders correctly.
        private void LoadCaptureStatus()
        {
            var status = _host.ReadCaptureStatus();
            var previous = _captureState;
            _captureState = status?.state ?? CaptureStates.Idle;
            _bundlePath = status?.bundle_path ?? string.Empty;

            var capturing = _captureState == CaptureStates.Capturing;
            var writing = _captureState == CaptureStates.Writing;
            _captureStart.Enabled = !capturing && !writing;
            _captureStop.Enabled = capturing;
            _captureCancel.Enabled = capturing;
            _captureStatus.Text = DescribeCapture(status);
            _captureStatus.ForeColor = _captureState == CaptureStates.Error
                ? Color.Firebrick
                : SystemColors.GrayText;

            // Only the Writing -> Done transition, meaning a bundle that finished
            // while this dialog watched. Testing "was not Done" would fire for
            // every dialog opened later: the core stays Done while a fresh
            // dialog's field starts at Idle.
            if (previous == CaptureStates.Writing && _captureState == CaptureStates.Done) RevealBundle();
        }

        private static string DescribeCapture(CaptureStatus status)
        {
            if (status == null)
                return "Diagnostics are unavailable - the core is not running.";

            switch (status.state)
            {
                case CaptureStates.Capturing:
                    var minutes = Math.Max(1, status.seconds_remaining / 60);
                    return "Capturing since " + FormatUnixMs(status.started_unix_ms)
                        + ". Reproduce the problem, then press Stop and save. Stops on its own in "
                        + minutes.ToString(CultureInfo.InvariantCulture) + " min.";
                case CaptureStates.Writing:
                    return "Writing the diagnostics file...";
                case CaptureStates.Done:
                    // Named from the path the core actually wrote, not from where
                    // it was asked to: StopCapture falls back to the log folder
                    // when there is no Desktop to resolve, and pointing the user
                    // at the wrong folder is worse than naming a longer one.
                    return "Saved " + Path.GetFileName(status.bundle_path)
                        + " to " + DescribeFolder(status.bundle_path)
                        + ". Attach it to a bug report.";
                case CaptureStates.Expired:
                case CaptureStates.Error:
                    return status.message;
                default:
                    return "Start a capture, reproduce the problem, then stop it: the plugin saves one "
                        + "file to your Desktop with the detail a bug report needs.";
            }
        }

        // "your Desktop" when that is where it went, otherwise the folder name.
        private static string DescribeFolder(string bundlePath)
        {
            try
            {
                var folder = Path.GetDirectoryName(bundlePath);
                if (string.IsNullOrEmpty(folder)) return "your Desktop";
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                return string.Equals(folder.TrimEnd('\\'), (desktop ?? string.Empty).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase)
                    ? "your Desktop"
                    : folder;
            }
            catch (ArgumentException)
            {
                return "your Desktop";
            }
        }

        // Local time; the core records UTC epoch milliseconds.
        private static string FormatUnixMs(long unixMs)
        {
            if (unixMs <= 0) return "an unknown time";
            try
            {
                return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddMilliseconds(unixMs)
                    .ToLocalTime()
                    .ToString("t", CultureInfo.CurrentCulture);
            }
            catch (ArgumentOutOfRangeException)
            {
                return "an unknown time";
            }
        }

        // The consent point. A capture raises the log level and the file it
        // produces carries the user's folder paths and LAN addresses, so say so
        // before either happens rather than in a doc they will not read.
        private void StartCapture()
        {
            var confirm = MessageBox.Show(
                this,
                "While a capture runs the plugin logs in more detail, then saves one file to your "
                + "Desktop when you stop it.\r\n\r\nThat file contains your settings, your music "
                + "folder paths and this PC's local network addresses - not your password for "
                + "anything. Look inside it before sending it anywhere.\r\n\r\n"
                + "The capture stops on its own after 30 minutes.",
                "MusicBee Remote",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);
            if (confirm != DialogResult.OK) return;

            if (!_host.StartCapture(HostEnvironment()))
            {
                // The command channel only reports pass/fail, and the same false
                // covers a core that is not running at all, so "already running"
                // is read back rather than assumed.
                var running = _host.ReadCaptureStatus()?.state == CaptureStates.Capturing;
                SetStatus(running
                    ? "A capture is already running."
                    : "Could not start a capture - see mbrc-core.log.", false);
                LoadCaptureStatus();
                return;
            }
            LoadCaptureStatus();
        }

        // The bundle lands on the Desktop. Resolved here rather than in the core
        // because a OneDrive-redirected Desktop is something only the CLR's
        // known-folder lookup gets right; %USERPROFILE%\Desktop does not.
        private void StopCapture()
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktop))
            {
                // No Desktop to write to (a locked-down or redirected profile);
                // the log folder is somewhere we know exists.
                desktop = _host.LogDirectory;
            }
            if (!_host.StopCapture(desktop))
            {
                SetStatus("There was no capture to stop.", false);
            }
            LoadCaptureStatus();
        }

        private void CancelCapture()
        {
            _host.CancelCapture();
            LoadCaptureStatus();
        }

        // What the core cannot see for itself: it only ever talks to this shim,
        // so MusicBee's own version, Windows and the CLR have to be handed over.
        private List<CaptureEnvEntry> HostEnvironment()
        {
            var entries = new List<CaptureEnvEntry>();
            Action<string, string> add = (key, value) =>
                entries.Add(new CaptureEnvEntry { key = key, value = value ?? string.Empty });

            add("plugin_version", _version);
            add("musicbee_build", MusicBeeBuild().ToString(CultureInfo.InvariantCulture));
            add("musicbee_api_revision", _host.ApiRevision.ToString(CultureInfo.InvariantCulture));
            try
            {
                add("os", Environment.OSVersion.VersionString);
                add("os_64bit", Environment.Is64BitOperatingSystem.ToString());
                add("clr", Environment.Version.ToString());
            }
            catch (Exception)
            {
                // A report missing a line is still a report worth having.
            }
            return entries;
        }

        // Show the finished bundle rather than just naming it: the user's next
        // move is to attach it to something, and they have to find it first.
        private void RevealBundle()
        {
            if (string.IsNullOrEmpty(_bundlePath) || !File.Exists(_bundlePath)) return;
            try
            {
                // Usually the Desktop, which MSIX does not redirect - but the
                // capture falls back to the log folder when no Desktop resolves,
                // and that one is redirected on the Store build.
                var shown = PackagePaths.ForExternalProcess(_bundlePath);
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + shown + "\"")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception)
            {
                // Explorer refused; the status line still names the file.
            }
        }

        private void OpenLogFolder()
        {
            try
            {
                var dir = _host.LogDirectory;
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    SetStatus("Log folder not found.", false);
                    return;
                }
                // Explorer runs outside the MSIX container, so it resolves the
                // un-redirected %APPDATA% and reports "Location is not available".
                // The check above cannot see that: in here the path does exist.
                Process.Start(new ProcessStartInfo(PackagePaths.ForExternalProcess(dir))
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                SetStatus($"Couldn't open log folder: {ex.Message}", false);
            }
        }

        /// <summary>Open the online plugin documentation in the default browser.</summary>
        private void OpenHelp()
        {
            if (!TryOpenUrl(HelpUrl, out var error))
                SetStatus($"Couldn't open help: {error}", false);
        }

        /// <summary>Persist the edited settings to the core.</summary>
        private void Apply()
        {
            var ok = _host.ApplySettings(Collect(), out var reloaded);
            if (ok) MarkSaved();
            SetStatus(
                ok ? "Settings saved." : "Settings were rejected - check the port and range values.",
                ok);

            // A save that rebound the listener (a new port, a changed filter) makes
            // the two rows describing it stale: both were rendered from the socket
            // that no longer exists. The reload finishes inside ApplySettings, so by
            // here the core reports the new port and is already listening on it.
            if (ok && reloaded)
            {
                LoadListeningAddresses();
                RunConnectionTest();
            }
        }

        private void LoadFromCore()
        {
            _loading = true;
            var s = _host.ReadSettings() ?? new CoreSettings();
            _port.Value = Clamp(s.port, 1, 65535);
            _filterMode.SelectedIndex = FilterModeToIndex(s.filter_mode);
            _baseIp.Text = s.base_ip ?? string.Empty;
            _lastOctet.Value = Clamp(s.last_octet_max, 0, 255);
            _allowedAddresses.Lines = (s.allowed_addresses ?? new List<string>()).ToArray();
            _logLevel.SelectedIndex = LogLevelToIndex(s.log_level);
            _firewall.Checked = s.update_firewall;
            _mdns.Checked = s.mdns_enabled;
            _webEnabled.Checked = s.web_enabled;
            _webAuth.Checked = s.web_auth_required;
            _autoCheck.Checked = s.update_check_enabled;
            ShowFilterRows();
            UpdateWebEnabled();
            LoadWebStatus();
            _loading = false;
            MarkSaved();
        }

        /// <summary>
        ///     Render the addresses a client can reach this server on (each
        ///     interface IPv4 + the bound port), read from the core. Shown so the
        ///     user knows what to enter in the phone app. The list reflects the
        ///     running server, so it is re-read on load and after a save that
        ///     rebinds the listener - nothing else changes it while the dialog is
        ///     open.
        /// </summary>
        private void LoadListeningAddresses()
        {
            var info = _host.ReadListeningAddresses();
            if (info == null)
            {
                _addresses.Text = "Unavailable (core not running).";
                return;
            }

            if (info.addresses == null || info.addresses.Count == 0)
            {
                _addresses.Text = "No reachable network address detected.";
                return;
            }

            _addresses.Text = string.Join(Environment.NewLine, PanelText.Addresses(info));
        }

        /// <summary>Read the cache status from the core and render the line.</summary>
        private void LoadCacheStatus()
        {
            var status = _host.ReadCacheStatus();
            if (status == null)
            {
                _cacheStatus.Text = "Unavailable (core not running).";
                SetRebuildEnabled(false);
                return;
            }

            if (status.building)
            {
                _cacheStatus.Text = "Rebuilding cache...";
                SetRebuildEnabled(false);
            }
            else
            {
                var line = $"{status.tracks_cached:N0} tracks · {status.covers_cached:N0} covers cached";
                _cacheStatus.Text = status.metadata_ready ? line + "." : line + " (metadata not ready).";
                SetRebuildEnabled(true);
            }
        }

        private void SetRebuildEnabled(bool enabled)
        {
            _rebuildMeta.Enabled = enabled;
            _rebuildCovers.Enabled = enabled;
        }

        /// <summary>
        ///     Refresh the "Blocked connections (N)" button from the core's
        ///     in-memory log. The button opens the detail view; it is greyed while
        ///     the log is empty.
        /// </summary>
        private void LoadBlockedCount()
        {
            var count = _host.ReadBlockedConnections().Count;
            _blockedBtn.Text = $"Blocked connections ({count})";
            _blockedBtn.Enabled = count > 0;
        }

        /// <summary>
        ///     Open the modal blocked-connections view, then refresh the count
        ///     button (the view can clear the log).
        /// </summary>
        private void ShowBlockedConnections()
        {
            using (var dialog = new BlockedConnectionsDialog(_host))
            {
                dialog.ShowDialog(this);
            }
            LoadBlockedCount();
        }

        /// <summary>
        ///     Render the core's update status: the line, what the one action
        ///     button currently means, and whether skipping or reading the notes
        ///     is on offer.
        /// </summary>
        private void LoadUpdateStatus()
        {
            var status = _host.ReadUpdateStatus();
            if (status == null)
            {
                _updateState = UpdateStates.Unknown;
                _notesUrl = string.Empty;
                _updateStatus.Text = "Unavailable (core not running).";
                _updateAction.Text = "Check now";
                _updateAction.Enabled = false;
                _updateSkip.Visible = false;
                _releaseNotes.Visible = false;
                return;
            }

            _updateState = status.state ?? UpdateStates.Unknown;
            _notesUrl = status.notes_url ?? string.Empty;

            // An update the panel could download, meaning the states where the
            // button fetches rather than checks or installs. The manifest's floor
            // is enforced here because the core cannot see MusicBee's version.
            var offered = _updateState == UpdateStates.Available
                          || _updateState == UpdateStates.DownloadFailed;
            var build = MusicBeeBuild();
            _updateBlocked = offered
                             && build > 0
                             && status.min_musicbee_build > 0
                             && build < status.min_musicbee_build;

            _updateStatus.Text = DescribeUpdate(status, _updateBlocked, build);
            // A release that needs a newer MusicBee leaves the button offering a
            // re-check rather than greying it out: greyed, the only control in the
            // group would be dead for the rest of the session, with nothing to
            // press after upgrading MusicBee or once a compatible release lands.
            _updateAction.Text = _updateBlocked ? "Check now" : ActionLabel(_updateState);
            _updateAction.Enabled = _updateState != UpdateStates.Checking
                                    && _updateState != UpdateStates.Downloading;
            _updateSkip.Visible = offered;
            _releaseNotes.Visible = _notesUrl.Length > 0
                                    && (offered || _updateState == UpdateStates.Staged);
        }

        private static string ActionLabel(string state)
        {
            switch (state)
            {
                case UpdateStates.Available: return "Download";
                case UpdateStates.DownloadFailed: return "Retry download";
                case UpdateStates.Staged: return "Install and restart";
                case UpdateStates.Checking: return "Checking...";
                case UpdateStates.Downloading: return "Downloading...";
                default: return "Check now";
            }
        }

        private static string DescribeUpdate(Ffi.UpdateStatus status, bool tooOld, int build)
        {
            var version = status.version ?? string.Empty;
            if (tooOld)
                return $"Version {version} needs MusicBee build {status.min_musicbee_build} or newer "
                       + $"(this is build {build}).";

            switch (status.state)
            {
                case UpdateStates.Checking:
                    return "Checking for updates...";
                case UpdateStates.Available:
                    return $"Version {version} is available." + LastChecked(status);
                case UpdateStates.Downloading:
                    return $"Downloading version {version}...";
                case UpdateStates.DownloadFailed:
                    return $"Version {version} could not be downloaded - see the log for details.";
                case UpdateStates.Staged:
                    return $"Version {version} is ready to install.";
                case UpdateStates.Skipped:
                    return $"Version {version} was skipped." + LastChecked(status);
                case UpdateStates.UpToDate:
                    return (version.Length > 0
                               ? $"Up to date (latest release is {version})."
                               : "Up to date.")
                           + LastChecked(status);
                case UpdateStates.Disabled:
                    return "Automatic checks are off. Use Check now.";
                case UpdateStates.Error:
                    // Whatever went wrong - an unreachable host, a release with no
                    // manifest, a signature that did not verify - the user's answer
                    // is the same and the diagnostic is already in mbrc-core.log,
                    // written by the core as the failure happened.
                    return "No update could be found." + LastChecked(status);
                default:
                    return "Not checked yet.";
            }
        }

        /// <summary>
        ///     " Last checked &lt;local time&gt;." for a status that carries one.
        ///     The core stores UTC; the user reads local.
        /// </summary>
        private static string LastChecked(Ffi.UpdateStatus status)
        {
            if (string.IsNullOrEmpty(status.checked_at)) return string.Empty;
            DateTime when;
            if (!DateTime.TryParse(
                    status.checked_at,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out when))
                return string.Empty;
            return " Last checked " + when.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) + ".";
        }

        /// <summary>
        ///     The action the current state calls for. Every path just kicks the
        ///     core and re-reads; the core pushes UpdateStatusChanged as the
        ///     background job progresses.
        /// </summary>
        private void RunUpdateAction()
        {
            // Blocked on the MusicBee build: the button says Check now and means
            // it, whatever state the core is in.
            if (_updateBlocked)
            {
                _updateAction.Enabled = false;
                _updateStatus.Text = "Checking for updates...";
                if (!_host.CheckForUpdate()) LoadUpdateStatus();
                return;
            }

            switch (_updateState)
            {
                case UpdateStates.DownloadFailed:
                case UpdateStates.Available:
                    _updateAction.Enabled = false;
                    _updateStatus.Text = "Starting the download...";
                    if (!_host.DownloadUpdate()) LoadUpdateStatus();
                    break;
                case UpdateStates.Staged:
                    InstallStagedUpdate();
                    break;
                default:
                    _updateAction.Enabled = false;
                    _updateStatus.Text = "Checking for updates...";
                    if (!_host.CheckForUpdate()) LoadUpdateStatus();
                    break;
            }
        }

        private void SkipUpdate()
        {
            _host.SkipUpdate();
            LoadUpdateStatus();
        }

        private void OpenReleaseNotes()
        {
            if (string.IsNullOrEmpty(_notesUrl)) return;
            if (!TryOpenUrl(_notesUrl, out var error))
                SetStatus($"Couldn't open the release notes: {error}", false);
        }

        /// <summary>
        ///     Hand the staged update to the elevated helper, then close MusicBee -
        ///     the helper is already waiting on this process and cannot replace
        ///     files that are still loaded.
        /// </summary>
        private void InstallStagedUpdate()
        {
            var confirm = MessageBox.Show(
                this,
                "MusicBee will close so the update can be installed, then reopen.\n\n"
                + "Windows may ask for permission to update the plugin files.\n"
                + "Any settings changes you have not saved will be lost.",
                "MusicBee Remote",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);
            if (confirm != DialogResult.OK) return;

            switch (_host.ApplyStagedUpdate())
            {
                case Ffi.Generated.UpdateLaunch.Launched:
                    // From here the helper owns the outcome; it waits two minutes
                    // for this process to exit before giving up untouched.
                    Close();
                    CloseMusicBee();
                    break;
                case Ffi.Generated.UpdateLaunch.Cancelled:
                    _updateStatus.Text =
                        "Installation needs permission to change the plugin files. "
                        + "The download is still there - press Install and restart to try again.";
                    break;
                case Ffi.Generated.UpdateLaunch.NotAnUpgrade:
                    // The bundle verified but is not newer. Every release is
                    // public and signed, so this is what a stale one staged by
                    // something other than a check looks like.
                    _updateStatus.Text =
                        "The downloaded update is not newer than the installed version, "
                        + "so it was not applied. Check again for a newer release.";
                    break;
                case Ffi.Generated.UpdateLaunch.VerifyFailed:
                    // The one outcome worth interrupting for: the staged bundle no
                    // longer matches what the release signed.
                    MessageBox.Show(
                        this,
                        "The downloaded update failed its signature check and was not installed.\n\n"
                        + "Download it again, or install it manually from the releases page.",
                        "MusicBee Remote",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    LoadUpdateStatus();
                    break;
                default:
                    _updateStatus.Text = "The update could not be started - see the log for details.";
                    break;
            }
        }

        /// <summary>
        ///     Ask MusicBee's main window to close, the same way the title bar's X
        ///     does. Posted, not sent: this runs on MusicBee's own UI thread. If it
        ///     is configured to minimize instead of exit the process stays up and
        ///     the helper times out having touched nothing, so the update simply
        ///     waits for the next real exit.
        /// </summary>
        private void CloseMusicBee()
        {
            var window = _host.MusicBeeWindow;
            if (window == IntPtr.Zero) return;
            PostMessage(window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>
        ///     MusicBee's build number (the third component of MusicBee.exe's file
        ///     version, which is what its own installer gates on), or 0 when it
        ///     cannot be read - in which case nothing is gated on it.
        /// </summary>
        private static int MusicBeeBuild()
        {
            try
            {
                using (var process = Process.GetCurrentProcess())
                {
                    return process.MainModule.FileVersionInfo.FileBuildPart;
                }
            }
            catch (Exception)
            {
                return 0;
            }
        }

        // Round-trips a `verifyconnection` frame on loopback, which proves the
        // plugin is listening and the socket path works locally - not that a
        // remote client can get through a firewall. Runs off the UI thread.
        private void RunConnectionTest()
        {
            // The live/running port (not the possibly-unsaved edit in the field).
            var port = _host.ReadSettings()?.port ?? (int)_port.Value;
            _testConn.Enabled = false;
            _connStatus.Text = "Testing...";
            _connStatus.ForeColor = SystemColors.GrayText;

            OffThread(this, () =>
            {
                var ok = ProbeVerifyConnection(port, out var detail);
                return (ok, detail);
            }, result =>
            {
                var p = port.ToString(CultureInfo.InvariantCulture);
                _connStatus.Text = result.ok
                    ? "Listening on port " + p + " (local)."
                    : "No response on port " + p + (string.IsNullOrEmpty(result.detail) ? "." : " (" + result.detail + ").");
                _connStatus.ForeColor = result.ok ? Color.Green : Color.Firebrick;
                _testConn.Enabled = true;
            });
        }

        private static bool ProbeVerifyConnection(int port, out string detail)
        {
            detail = string.Empty;
            try
            {
                using (var client = new TcpClient())
                {
                    var connect = client.BeginConnect(IPAddress.Loopback, port, null, null);
                    if (!connect.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2)))
                    {
                        detail = "timed out";
                        return false;
                    }
                    client.EndConnect(connect); // throws if the connect failed (e.g. refused)

                    using (var stream = client.GetStream())
                    {
                        stream.ReadTimeout = 2000;
                        stream.WriteTimeout = 2000;
                        var frame = Encoding.UTF8.GetBytes("{\"context\":\"verifyconnection\",\"data\":\"\"}\r\n");
                        stream.Write(frame, 0, frame.Length);

                        var buf = new byte[512];
                        var read = stream.Read(buf, 0, buf.Length);
                        if (read <= 0)
                        {
                            detail = "no reply";
                            return false;
                        }
                        var reply = Encoding.UTF8.GetString(buf, 0, read);
                        return reply.IndexOf("verifyconnection", StringComparison.Ordinal) >= 0;
                    }
                }
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                return false;
            }
        }

        // Kick a background rebuild; the core's CacheStatusChanged event refreshes
        // the line when it finishes. A rejected call (core down) just re-reads.
        private void RebuildMetadata()
        {
            SetRebuildEnabled(false);
            _cacheStatus.Text = "Rebuilding metadata...";
            if (!_host.RebuildMetadata()) LoadCacheStatus();
        }

        private void RebuildCovers()
        {
            SetRebuildEnabled(false);
            _cacheStatus.Text = "Rebuilding covers...";
            if (!_host.RebuildCovers()) LoadCacheStatus();
        }

        // Core -> host push (raised on a background thread): marshal to the UI
        // thread and refresh the line the event is about - the cache one when a
        // rebuild starts/finishes, the update one when a check or download does.
        private void OnCoreEvent(HostEventType e)
        {
            Action refresh;
            switch (e)
            {
                case HostEventType.CacheStatusChanged:
                    refresh = LoadCacheStatus;
                    break;
                case HostEventType.UpdateStatusChanged:
                    refresh = LoadUpdateStatus;
                    break;
                case HostEventType.CaptureStatusChanged:
                    refresh = LoadCaptureStatus;
                    break;
                default:
                    return;
            }

            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(refresh);
            }
            catch (Exception)
            {
                // The form is closing/handle gone; nothing to refresh.
            }
        }

        /// <summary>
        ///     Flag the pages whose inputs differ from what the core holds, in the list and the footer.
        ///
        ///     The marker is part of each item's text, so a screen reader reads it
        ///     too. Nothing is redrawn while typing keeps the same pages edited.
        /// </summary>
        private void ShowEdits()
        {
            if (_loading) return;

            var edited = _edits.EditedPages();
            if (edited.SequenceEqual(_editedPages)) return;
            _editedPages = edited;

            foreach (var entry in SettingsPages.All)
            {
                var text = edited.Contains(entry.Page) ? entry.Title + " *" : entry.Title;
                if (!text.Equals(_nav.Items[(int)entry.Page])) _nav.Items[(int)entry.Page] = text;
            }

            SetStatus(PageEdits.Describe(edited), true);
        }

        /// <summary>Takes what is on screen as the saved state.</summary>
        private void MarkSaved()
        {
            _edits.MarkSaved();
            ShowEdits();
        }

        private CoreSettings Collect()
        {
            return new CoreSettings
            {
                port = (int)_port.Value,
                filter_mode = IndexToFilterMode(_filterMode.SelectedIndex),
                base_ip = _baseIp.Text.Trim(),
                last_octet_max = (int)_lastOctet.Value,
                allowed_addresses = _allowedAddresses.Lines
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0)
                    .ToList(),
                update_firewall = _firewall.Checked,
                mdns_enabled = _mdns.Checked,
                web_enabled = _webEnabled.Checked,
                web_auth_required = _webAuth.Checked,
                log_level = IndexToLogLevel(_logLevel.SelectedIndex),
                update_check_enabled = _autoCheck.Checked,
            };
        }

        // Show only the inputs the selected filter mode uses: base IP + last
        // octet for Range, the address list for Specific, neither for All.
        private void ShowFilterRows()
        {
            _rows.SetVisible(_rangePanel, _filterMode.SelectedIndex == 1);
            _rows.SetVisible(_allowedAddresses, _filterMode.SelectedIndex == 2);
            _rows.SetVisible(_allowedHint, _filterMode.SelectedIndex == 2);
        }


        private void SetStatus(string text, bool ok)
        {
            if (_status == null) return;
            _status.Text = text;
            _status.ForeColor = ok ? SystemColors.ControlText : Color.Firebrick;
        }

        private static int FilterModeToIndex(string mode)
        {
            switch (mode)
            {
                case "range": return 1;
                case "specific": return 2;
                default: return 0;
            }
        }

        private static string IndexToFilterMode(int index)
        {
            switch (index)
            {
                case 1: return "range";
                case 2: return "specific";
                default: return "all";
            }
        }

        private static int LogLevelToIndex(string level)
        {
            var value = (level ?? "info").Trim().ToLowerInvariant();
            for (var i = 0; i < LogLevels.Length; i++)
                if (LogLevels[i].Value == value)
                    return i;
            return 0; // default to Normal (info)
        }

        private static string IndexToLogLevel(int index)
        {
            return index >= 0 && index < LogLevels.Length ? LogLevels[index].Value : "info";
        }

        private static decimal Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
