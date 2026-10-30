using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace MusicBeePlugin.Settings
{
    /// <summary>
    ///     Which settings pages hold edits that Save has not applied yet.
    /// </summary>
    /// <remarks>
    ///     Every input is compared against its value at the last load or save, so
    ///     an input added to a page later is covered without being listed here, and
    ///     an edit reverted by hand stops counting.
    /// </remarks>
    internal sealed class PageEdits
    {
        private readonly List<KeyValuePair<SettingsPage, Control>> _inputs =
            new List<KeyValuePair<SettingsPage, Control>>();

        private readonly Dictionary<Control, object> _saved = new Dictionary<Control, object>();

        /// <summary>Raised when any tracked input changes value.</summary>
        public event EventHandler InputChanged;

        /// <summary>Track every input under <paramref name="root" /> as belonging to <paramref name="page" />.</summary>
        public void Track(SettingsPage page, Control root)
        {
            foreach (Control child in root.Controls)
            {
                // Not into an input's own children: a NumericUpDown hosts a TextBox.
                if (ValueOf(child) != null)
                {
                    _inputs.Add(new KeyValuePair<SettingsPage, Control>(page, child));
                    Watch(child);
                }
                else
                {
                    Track(page, child);
                }
            }
        }

        /// <summary>Take every input's current value as the saved one.</summary>
        public void MarkSaved()
        {
            foreach (var input in _inputs)
                _saved[input.Value] = ValueOf(input.Value);
        }

        /// <summary>The pages with at least one input off its saved value, in navigation order.</summary>
        public IList<SettingsPage> EditedPages()
        {
            return _inputs
                .Where(i => !_saved.TryGetValue(i.Value, out var saved) || !Equals(saved, ValueOf(i.Value)))
                .Select(i => i.Key)
                .Distinct()
                .OrderBy(page => page)
                .ToList();
        }

        /// <summary>The footer line for <paramref name="pages" />, or empty when there is nothing unsaved.</summary>
        public static string Describe(IList<SettingsPage> pages)
        {
            if (pages.Count == 0) return string.Empty;
            return "Unsaved changes on " + string.Join(", ", pages.Select(SettingsPages.Title))
                   + " - press Save to apply them.";
        }

        /// <summary>
        ///     The value an input contributes to the settings.
        ///
        ///     Text is compared as the settings read it: lines trimmed, blank ones
        ///     dropped, so whitespace that Save would discard is not an edit.
        /// </summary>
        private static object ValueOf(Control control)
        {
            if (control is CheckBox check) return check.Checked;
            if (control is NumericUpDown number) return number.Value;
            if (control is ComboBox combo) return combo.SelectedIndex;
            if (control is TextBox text)
                return string.Join("\n", text.Lines.Select(l => l.Trim()).Where(l => l.Length > 0));
            return null;
        }

        private void Watch(Control input)
        {
            EventHandler changed = (s, e) => InputChanged?.Invoke(this, EventArgs.Empty);
            if (input is CheckBox check) check.CheckedChanged += changed;
            else if (input is NumericUpDown number) number.ValueChanged += changed;
            else if (input is ComboBox combo) combo.SelectedIndexChanged += changed;
            else if (input is TextBox text) text.TextChanged += changed;
        }
    }
}
