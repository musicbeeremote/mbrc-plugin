using System;
using System.Windows.Forms;
using AwesomeAssertions;
using MusicBeePlugin.Settings;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Settings
{
    public sealed class PageEditsTests : IDisposable
    {
        private readonly CheckBox _mdns = new CheckBox();
        private readonly NumericUpDown _lastOctet = new NumericUpDown { Maximum = 255 };
        private readonly TextBox _allowed = new TextBox();
        private readonly ComboBox _logLevel = new ComboBox();
        private readonly PageEdits _edits = new PageEdits();

        public PageEditsTests()
        {
            var connection = new Panel();
            connection.Controls.Add(_mdns);

            // Nested, as the range fields sit in their own row panel.
            var range = new FlowLayoutPanel();
            range.Controls.Add(_lastOctet);
            var access = new Panel();
            access.Controls.Add(range);
            access.Controls.Add(_allowed);

            _logLevel.Items.AddRange(new object[] { "Normal", "Debug" });
            _logLevel.SelectedIndex = 0;
            var diagnostics = new Panel();
            diagnostics.Controls.Add(_logLevel);

            _edits.Track(SettingsPage.Connection, connection);
            _edits.Track(SettingsPage.Access, access);
            _edits.Track(SettingsPage.Diagnostics, diagnostics);
            _edits.MarkSaved();
        }

        public void Dispose()
        {
            _mdns.Dispose();
            _lastOctet.Dispose();
            _allowed.Dispose();
            _logLevel.Dispose();
        }

        [Fact]
        public void NothingEdited_ReportsNoPages()
        {
            _edits.EditedPages().Should().BeEmpty();
            PageEdits.Describe(_edits.EditedPages()).Should().BeEmpty();
        }

        [Fact]
        public void AnEdit_FlagsOnlyItsOwnPage()
        {
            _lastOctet.Value = 200;

            _edits.EditedPages().Should().Equal(SettingsPage.Access);
        }

        [Fact]
        public void AnEditRevertedByHand_StopsCounting()
        {
            _mdns.Checked = true;
            _mdns.Checked = false;

            _edits.EditedPages().Should().BeEmpty();
        }

        [Fact]
        public void WhitespaceSaveWouldDiscard_IsNotAnEdit()
        {
            _allowed.Text = "192.168.1.50";
            _edits.MarkSaved();

            _allowed.Text = "  192.168.1.50 \r\n\r\n";

            _edits.EditedPages().Should().BeEmpty();
        }

        [Fact]
        public void Save_ClearsEveryPage()
        {
            _mdns.Checked = true;
            _allowed.Text = "192.168.1.50";

            _edits.MarkSaved();

            _edits.EditedPages().Should().BeEmpty();
        }

        [Fact]
        public void Pages_AreListedInNavigationOrder_NotEditOrder()
        {
            _logLevel.SelectedIndex = 1;
            _mdns.Checked = true;

            _edits.EditedPages().Should().Equal(SettingsPage.Connection, SettingsPage.Diagnostics);
            PageEdits.Describe(_edits.EditedPages())
                .Should().Be("Unsaved changes on Connection, Diagnostics - press Save to apply them.");
        }

        [Fact]
        public void EveryInputChange_RaisesInputChanged()
        {
            var raised = 0;
            _edits.InputChanged += (s, e) => raised++;

            _mdns.Checked = true;
            _lastOctet.Value = 10;
            _allowed.Text = "x";
            _logLevel.SelectedIndex = 1;

            raised.Should().Be(4);
        }
    }
}
