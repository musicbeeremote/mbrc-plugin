using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using MusicBeePlugin.Ffi.Generated;
using MusicBeePlugin.Host;

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
        // Label + SearchSource flag value. Single-select in the UI (the common
        // case); the value stored is the flag int the core round-trips verbatim.
        private static readonly (string Label, int Value)[] Sources =
        {
            ("Library", 1),
            ("Inbox", 2),
            ("Podcasts", 4),
            ("Audiobooks", 32),
            ("Videos", 64),
        };

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

        private readonly PluginHost _host;
        private readonly string _version;

        private NumericUpDown _port;
        private Label _connStatus;
        private Button _testConn;
        private ComboBox _filterMode;
        private TextBox _baseIp;
        private NumericUpDown _lastOctet;
        private TextBox _allowedAddresses;
        private Button _blockedBtn;
        private ComboBox _searchSource;
        private ComboBox _logLevel;
        private CheckBox _firewall;
        private Label _status;
        private Label _cacheStatus;
        private System.Windows.Forms.Timer _blockedTimer;
        private Button _rebuildMeta;
        private Button _rebuildCovers;

        public SettingsDialog(PluginHost host, string version)
        {
            _host = host;
            _version = version;

            Text = "MusicBee Remote";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(560, 660);

            BuildLayout();
            LoadFromCore();
            LoadCacheStatus();
            LoadBlockedCount();
            RunConnectionTest();

            // The core pushes a CacheStatusChanged event when a rebuild starts or
            // finishes; refresh the line live while this dialog is open.
            _host.CoreEvent += OnCoreEvent;

            // The blocked-connections log has no push event (it is polled), so keep
            // the count button current while this panel is open: a device blocked
            // during troubleshooting bumps the number without reopening.
            _blockedTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            _blockedTimer.Tick += (s, e) => LoadBlockedCount();
            _blockedTimer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _host.CoreEvent -= OnCoreEvent;
            _blockedTimer?.Stop();
            _blockedTimer?.Dispose();
            base.OnFormClosed(e);
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                Padding = new Padding(12),
                AutoSize = false,
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // connection
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // access
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // library
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // advanced
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // cache
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // spacer

            // No title label: the window title bar already reads "MusicBee Remote";
            // the version sits bottom-left in the button row instead.
            root.Controls.Add(BuildConnectionGroup());
            root.Controls.Add(BuildAccessGroup());
            root.Controls.Add(BuildLibraryGroup());
            root.Controls.Add(BuildAdvancedGroup());
            root.Controls.Add(BuildCacheGroup());
            root.Controls.Add(new Panel { Dock = DockStyle.Fill }); // spacer

            // The button row is docked to the bottom of the form so it (and the
            // version/Help footer inside it) always stays on-screen even if the
            // grouped settings above overflow the fixed dialog height. Add the
            // bottom-docked row first, then the fill panel, so the fill claims the
            // remaining space above it.
            var footer = BuildButtonRow();
            footer.Dock = DockStyle.Bottom;
            footer.Padding = new Padding(12, 6, 12, 10);
            Controls.Add(footer);
            Controls.Add(root);
        }

        private Control BuildConnectionGroup()
        {
            _port = new NumericUpDown { Minimum = 1, Maximum = 65535, Width = 100, Anchor = AnchorStyles.Left };

            _testConn = new Button
            {
                Text = "Test connection",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 8, 0),
            };
            _testConn.Click += (s, e) => RunConnectionTest();
            _connStatus = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, 4, 0, 0),
                ForeColor = SystemColors.GrayText,
            };

            var statusRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0),
            };
            statusRow.Controls.Add(_testConn);
            statusRow.Controls.Add(_connStatus);

            var layout = GroupLayout();
            AddRow(layout, "Listening port", _port);
            AddRow(layout, "Status", statusRow);
            return WrapGroup("Connection", layout);
        }

        private Control BuildAccessGroup()
        {
            _filterMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, Anchor = AnchorStyles.Left };
            _filterMode.Items.AddRange(new object[] { "All clients", "Address range", "Specific addresses" });
            _filterMode.SelectedIndexChanged += (s, e) => UpdateFilterEnabled();

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

            // A count-only button that opens the blocked-connections view; kept
            // out of the main layout so the panel stays uncluttered when nothing
            // is being blocked. Disabled (greyed) while the count is zero.
            _blockedBtn = new Button
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0),
                Enabled = false,
                Text = "Blocked connections (0)",
            };
            _blockedBtn.Click += (s, e) => ShowBlockedConnections();

            var layout = GroupLayout();
            AddRow(layout, "Allowed clients", _filterMode);
            AddRow(layout, "Range (base IPv4)", rangePanel);
            AddRow(layout, "Allowed addresses", _allowedAddresses);
            AddRow(layout, string.Empty, new Label
            {
                Text = "One per line: an IP (192.168.1.50) or a subnet (192.168.1.0/24).",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Anchor = AnchorStyles.Left,
            });
            AddRow(layout, "Blocked", _blockedBtn);
            return WrapGroup("Access control", layout);
        }

        private Control BuildLibraryGroup()
        {
            _searchSource = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, Anchor = AnchorStyles.Left };
            _searchSource.Items.AddRange(Sources.Select(x => (object)x.Label).ToArray());
            var layout = GroupLayout();
            AddRow(layout, "Search source", _searchSource);
            return WrapGroup("Library", layout);
        }

        private Control BuildAdvancedGroup()
        {
            _logLevel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
            _logLevel.Items.AddRange(LogLevels.Select(x => (object)x.Label).ToArray());

            var openLogs = new Button
            {
                Text = "Open log folder",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(8, 1, 0, 0),
            };
            openLogs.Click += (s, e) => OpenLogFolder();

            // Log level + the open-folder button on one row.
            var logRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0),
            };
            logRow.Controls.Add(_logLevel);
            logRow.Controls.Add(openLogs);

            _firewall = new CheckBox { Text = "Add a Windows firewall rule on save", AutoSize = true, Anchor = AnchorStyles.Left };
            var layout = GroupLayout();
            AddRow(layout, "Log level", logRow);
            AddRow(layout, string.Empty, _firewall);
            return WrapGroup("Advanced", layout);
        }

        private Control BuildCacheGroup()
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
                Margin = new Padding(0, 0, 8, 0),
            };
            _rebuildMeta.Click += (s, e) => RebuildMetadata();
            _rebuildCovers = new Button
            {
                Text = "Rebuild covers",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0),
            };
            _rebuildCovers.Click += (s, e) => RebuildCovers();

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0),
            };
            buttons.Controls.Add(_rebuildMeta);
            buttons.Controls.Add(_rebuildCovers);

            var layout = GroupLayout();
            AddRow(layout, "Status", _cacheStatus);
            AddRow(layout, string.Empty, buttons);
            return WrapGroup("Cache", layout);
        }

        private Control BuildButtonRow()
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

            var close = new Button { Text = "Close", Width = 90, Margin = new Padding(6, 0, 0, 0), DialogResult = DialogResult.Cancel };
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

        private static TableLayoutPanel GroupLayout()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                AutoSize = true,
                Padding = new Padding(8, 4, 8, 8),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return layout;
        }

        private static Control WrapGroup(string title, Control content)
        {
            var box = new GroupBox
            {
                Text = title,
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 10),
                Padding = new Padding(4, 4, 4, 4),
            };
            box.Controls.Add(content);
            return box;
        }

        private static void AddRow(TableLayoutPanel layout, string label, Control control)
        {
            var row = layout.RowCount;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, 6, 0, 0),
            }, 0, row);
            layout.Controls.Add(control, 1, row);
            layout.RowCount = row + 1;
        }

        /// <summary>Open the log folder (mbrc-core.log + bootstrap) in Explorer.</summary>
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
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SetStatus($"Couldn't open log folder: {ex.Message}", false);
            }
        }

        /// <summary>Open the online plugin documentation in the default browser.</summary>
        private void OpenHelp()
        {
            try
            {
                Process.Start(new ProcessStartInfo(HelpUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SetStatus($"Couldn't open help: {ex.Message}", false);
            }
        }

        /// <summary>Persist the edited settings to the core.</summary>
        private void Apply()
        {
            var ok = _host.ApplySettings(Collect());
            SetStatus(
                ok ? "Settings saved." : "Settings were rejected - check the port and range values.",
                ok);
        }

        private void LoadFromCore()
        {
            var s = _host.ReadSettings() ?? new CoreSettings();
            _port.Value = Clamp(s.port, 1, 65535);
            _filterMode.SelectedIndex = FilterModeToIndex(s.filter_mode);
            _baseIp.Text = s.base_ip ?? string.Empty;
            _lastOctet.Value = Clamp(s.last_octet_max, 0, 255);
            _allowedAddresses.Lines = (s.allowed_addresses ?? new List<string>()).ToArray();
            _searchSource.SelectedIndex = SourceToIndex(s.search_source);
            _logLevel.SelectedIndex = LogLevelToIndex(s.log_level);
            _firewall.Checked = s.update_firewall;
            UpdateFilterEnabled();
            SetStatus(string.Empty, true);
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

        // Self-test: connect to the running server on loopback and round-trip a
        // `verifyconnection` frame (answered before the handshake), the same probe
        // the old plugin used. Proves the plugin is listening and the socket path
        // works locally - NOT that a remote client can reach it through a firewall.
        // Runs off the UI thread (a connect can block); the result marshals back.
        private void RunConnectionTest()
        {
            // The live/running port (not the possibly-unsaved edit in the field).
            var port = _host.ReadSettings()?.port ?? (int)_port.Value;
            _testConn.Enabled = false;
            _connStatus.Text = "Testing...";
            _connStatus.ForeColor = SystemColors.GrayText;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var ok = ProbeVerifyConnection(port, out var detail);
                if (IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        var p = port.ToString(CultureInfo.InvariantCulture);
                        _connStatus.Text = ok
                            ? "Listening on port " + p + " (local)."
                            : "No response on port " + p + (string.IsNullOrEmpty(detail) ? "." : " (" + detail + ").");
                        _connStatus.ForeColor = ok ? Color.Green : Color.Firebrick;
                        _testConn.Enabled = true;
                    }));
                }
                catch (Exception)
                {
                    // The dialog is closing; nothing to update.
                }
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
        // thread and refresh the cache line when a rebuild starts/finishes.
        private void OnCoreEvent(HostEventType e)
        {
            if (e != HostEventType.CacheStatusChanged) return;
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)LoadCacheStatus);
            }
            catch (Exception)
            {
                // The form is closing/handle gone; nothing to refresh.
            }
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
                search_source = IndexToSource(_searchSource.SelectedIndex),
                update_firewall = _firewall.Checked,
                log_level = IndexToLogLevel(_logLevel.SelectedIndex),
            };
        }

        // Enable only the inputs the selected filter mode uses: base IP + last
        // octet for Range, the address list for Specific, neither for All.
        private void UpdateFilterEnabled()
        {
            var isRange = _filterMode.SelectedIndex == 1;
            var isSpecific = _filterMode.SelectedIndex == 2;
            _baseIp.Enabled = isRange;
            _lastOctet.Enabled = isRange;
            _allowedAddresses.Enabled = isSpecific;
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

        private static int SourceToIndex(int flags)
        {
            for (var i = 0; i < Sources.Length; i++)
                if (Sources[i].Value == flags)
                    return i;
            return 0; // default to Library
        }

        private static int IndexToSource(int index)
        {
            return index >= 0 && index < Sources.Length ? Sources[index].Value : 1;
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
