using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using MusicBeePlugin.Ffi;
using MusicBeePlugin.Host;

namespace MusicBeePlugin.Settings
{
    /// <summary>
    ///     Shows the core's recent rejected connection attempts (address filter /
    ///     connection caps), so a user can see why a device can't connect when IP
    ///     filtering is enabled. The list is Rust-owned and in-memory (last 50,
    ///     newest first); this dialog only renders it and offers a Clear action.
    ///     Auto-refreshes while open so a device blocked during troubleshooting
    ///     appears without reopening.
    /// </summary>
    internal sealed class BlockedConnectionsDialog : Form
    {
        private const int RefreshMs = 3000;

        private readonly PluginHost _host;
        private ListView _list;
        private Label _empty;
        private Button _clear;
        private Timer _timer;

        public BlockedConnectionsDialog(PluginHost host)
        {
            _host = host;

            Text = "Blocked connections";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            ShowIcon = false; // no generic form icon in the title bar
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(540, 360);
            MinimumSize = new Size(420, 260);

            BuildLayout();

            _timer = new Timer { Interval = RefreshMs };
            _timer.Tick += (s, e) => LoadEntries();
            _timer.Start();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // Populate here, not in the constructor: the window handle only exists
            // once shown, and LoadEntries no-ops without it - doing it in the ctor
            // left the list empty until the first timer tick.
            LoadEntries();
            SizeReasonColumn();
        }

        /// <summary>Stretch the last ("Reason") column to fill the list width.</summary>
        private void SizeReasonColumn()
        {
            if (_list.Columns.Count < 4) return;
            var used = _list.Columns[0].Width + _list.Columns[1].Width + _list.Columns[2].Width;
            var fill = _list.ClientSize.Width - used - 4;
            if (fill > 140) _list.Columns[3].Width = fill;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer?.Stop();
            _timer?.Dispose();
            base.OnFormClosed(e);
        }

        private void BuildLayout()
        {
            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                MultiSelect = false,
            };
            _list.Columns.Add("Time", 150);
            _list.Columns.Add("IP address", 120);
            _list.Columns.Add("Port", 55);
            // "Reason" fills the remaining width (its text is the widest column),
            // and re-fills when the window is resized.
            _list.Columns.Add("Reason", 200);
            _list.Resize += (s, e) => SizeReasonColumn();

            // Shown in place of the list when there is nothing to display.
            _empty = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = SystemColors.GrayText,
                Text = "No blocked connections recorded.",
                Visible = false,
            };

            var listHost = new Panel { Dock = DockStyle.Fill };
            listHost.Controls.Add(_list);
            listHost.Controls.Add(_empty);

            // Equal explicit size + margin so the two buttons line up (AutoSize
            // gave them different widths/heights and an uneven gap).
            var buttonSize = new Size(84, 27);
            _clear = new Button
            {
                Text = "Clear",
                Size = buttonSize,
                Margin = new Padding(8, 0, 0, 0),
            };
            _clear.Click += (s, e) => ClearLog();

            var close = new Button
            {
                Text = "Close",
                Size = buttonSize,
                Margin = new Padding(8, 0, 0, 0),
                DialogResult = DialogResult.OK,
            };
            close.Click += (s, e) => Close();

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12, 8, 12, 10),
            };
            // RightToLeft flow: first added sits rightmost.
            buttons.Controls.Add(close);
            buttons.Controls.Add(_clear);

            Controls.Add(listHost);
            Controls.Add(buttons);
            AcceptButton = close;
        }

        /// <summary>Read the log and repaint the list, preserving scroll where possible.</summary>
        private void LoadEntries()
        {
            if (IsDisposed || !IsHandleCreated) return;

            List<BlockedConnection> entries;
            try
            {
                entries = _host.ReadBlockedConnections();
            }
            catch (Exception)
            {
                // Core unavailable (e.g. reloading); leave the current view.
                return;
            }

            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (var e in entries)
                {
                    var when = DateTimeOffset.FromUnixTimeMilliseconds(e.unix_ms)
                        .LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
                    var item = new ListViewItem(when);
                    item.SubItems.Add(e.ip ?? string.Empty);
                    item.SubItems.Add(e.port.ToString(CultureInfo.InvariantCulture));
                    item.SubItems.Add(e.reason ?? string.Empty);
                    _list.Items.Add(item);
                }
            }
            finally
            {
                _list.EndUpdate();
            }

            var any = entries.Count > 0;
            _list.Visible = any;
            _empty.Visible = !any;
            _clear.Enabled = any;
        }

        private void ClearLog()
        {
            _host.ClearBlockedConnections();
            LoadEntries();
        }
    }
}
