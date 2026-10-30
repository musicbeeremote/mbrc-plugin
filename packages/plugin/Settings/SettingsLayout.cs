using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace MusicBeePlugin.Settings
{
    /// <summary>
    ///     The sizes and building blocks the settings window and the setup wizard share.
    /// </summary>
    /// <remarks>
    ///     Sizes are logical, written for 96 DPI. A window built from them sets
    ///     <see cref="BaseScale" /> as its <c>AutoScaleDimensions</c> with
    ///     <c>AutoScaleMode.Font</c>, so every size, margin and column width scales
    ///     with the font.
    /// </remarks>
    internal static class SettingsLayout
    {
        public const int DialogWidth = 720;

        /// <summary>A floor: <see cref="FitTallestPage" /> grows the window to its tallest page.</summary>
        public const int DialogHeight = 440;

        public const int NavWidth = 150;
        public const int PagePadding = 12;

        /// <summary>Space after each block on a page: a status with its actions, or a group of settings.</summary>
        public const int BlockGap = 12;

        /// <summary>The column every row lays its caption in; the value column takes the rest.</summary>
        public const int LabelColumnWidth = 130;

        /// <summary>
        ///     What is left for a value: the page less its padding, the table's own
        ///     padding (4 + 4), the label column and the value's margins (3 + 3).
        /// </summary>
        public const int ValueColumnWidth = DialogWidth - NavWidth - 2 * PagePadding - 8 - LabelColumnWidth - 6;

        /// <summary>The font metrics the sizes above were written against.</summary>
        public static readonly SizeF BaseScale = new SizeF(6F, 13F);

        /// <summary>A two-column table for <see cref="SettingsRows" />: caption, then value.</summary>
        public static TableLayoutPanel Table()
        {
            var layout = new TableLayoutPanel
            {
                ColumnCount = 2,
                Padding = new Padding(4, 0, 4, 0),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumnWidth));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return layout;
        }

        /// <summary>
        ///     A row of controls that act at once, under the status they change.
        ///
        ///     Its left edge lines up with the status text above; buttons are
        ///     spaced by the row, whatever margins they were built with.
        /// </summary>
        public static FlowLayoutPanel ActionRow(params Control[] controls)
        {
            foreach (var control in controls)
                control.Margin = new Padding(0, 0, 6, 0);

            var row = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(3, 2, 3, 0),
            };
            row.Controls.AddRange(controls);
            return row;
        }

        /// <summary>A page: <paramref name="heading" /> over <paramref name="body" />, hidden until shown, scrolling only if it must.</summary>
        public static TableLayoutPanel PageFrame(string heading, Control body, Font headingFont)
        {
            // Top and auto-sized: a filled table hands its spare height to the last row.
            body.Dock = DockStyle.Top;
            body.AutoSize = true;

            var frame = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                Padding = new Padding(PagePadding, PagePadding, PagePadding, 0),
                AutoScroll = true,
                Visible = false,
            };
            frame.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            frame.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            frame.Controls.Add(new Label
            {
                Text = heading,
                AutoSize = true,
                Font = headingFont,
                Margin = new Padding(7, 0, 0, 10),
            }, 0, 0);
            frame.Controls.Add(body, 0, 1);
            return frame;
        }

        /// <summary>
        ///     Run <paramref name="work" /> on the thread pool, then <paramref name="done" /> on
        ///     <paramref name="owner" />'s thread, unless the window closed meanwhile.
        /// </summary>
        public static void OffThread<T>(Control owner, Func<T> work, Action<T> done)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var result = work();
                if (owner.IsDisposed || !owner.IsHandleCreated) return;
                try
                {
                    owner.BeginInvoke(new Action(() => done(result)));
                }
                catch (InvalidOperationException)
                {
                    // Closed between the check and the call.
                }
            });
        }

        /// <summary>Open <paramref name="url" /> in the default browser; false with the reason when that fails.</summary>
        public static bool TryOpenUrl(string url, out string error)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        ///     Grow <paramref name="window" /> until the tallest of <paramref name="pages" /> fits
        ///     <paramref name="content" />, within the screen's work area.
        ///
        ///     Pages are expected to scroll, so one that still does not fit is
        ///     reachable rather than clipped.
        /// </summary>
        public static void FitTallestPage(Form window, Control content, IEnumerable<Control> pages)
        {
            window.PerformLayout();
            var tallest = pages.Max(p => p.GetPreferredSize(new Size(content.Width, 0)).Height);
            var extra = tallest - content.ClientSize.Height;
            if (extra <= 0) return;

            var chrome = window.Height - window.ClientSize.Height;
            var room = Screen.FromControl(window).WorkingArea.Height - chrome;
            window.ClientSize = new Size(window.ClientSize.Width, Math.Min(window.ClientSize.Height + extra, room));
        }
    }
}
