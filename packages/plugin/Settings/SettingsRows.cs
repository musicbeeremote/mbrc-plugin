using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MusicBeePlugin.Settings
{
    /// <summary>
    ///     Builds the captioned rows of the settings pages, and shows or hides a
    ///     row with its caption.
    /// </summary>
    internal sealed class SettingsRows
    {
        private readonly int _valueWidth;
        private readonly int _blockGap;
        private readonly Dictionary<Control, Label> _captions = new Dictionary<Control, Label>();

        /// <param name="valueWidth">Width a label value wraps at.</param>
        /// <param name="blockGap">Space left after a row that ends a block.</param>
        public SettingsRows(int valueWidth, int blockGap)
        {
            _valueWidth = valueWidth;
            _blockGap = blockGap;
        }

        /// <summary>
        ///     Add a captioned row; <paramref name="endsBlock" /> leaves the gap that separates blocks on a page.
        ///
        ///     A caption beside a value that can run to several lines sits level
        ///     with its first line; any other is centred on the value. A label
        ///     value wraps at the value column rather than running off the page.
        /// </summary>
        public void Add(TableLayoutPanel layout, string label, Control value, bool endsBlock = false)
        {
            if (value is Label text) text.MaximumSize = new Size(_valueWidth, 0);
            if (endsBlock)
                value.Margin = new Padding(value.Margin.Left, value.Margin.Top, value.Margin.Right, _blockGap);

            var row = layout.RowCount;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var caption = new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = SpansLines(value) ? AnchorStyles.Left | AnchorStyles.Top : AnchorStyles.Left,
                Padding = new Padding(0, 6, 0, 0),
            };
            layout.Controls.Add(caption, 0, row);
            layout.Controls.Add(value, 1, row);
            layout.RowCount = row + 1;
            _captions[value] = caption;
        }

        /// <summary>The caption of the row holding <paramref name="value" />.</summary>
        public Label CaptionOf(Control value) => _captions[value];

        /// <summary>Show or hide the row holding <paramref name="value" />, caption included.</summary>
        public void SetVisible(Control value, bool visible)
        {
            _captions[value].Visible = visible;
            value.Visible = visible;
        }

        private static bool SpansLines(Control value) =>
            value is Label || value is ListView || (value is TextBox box && box.Multiline);
    }
}
