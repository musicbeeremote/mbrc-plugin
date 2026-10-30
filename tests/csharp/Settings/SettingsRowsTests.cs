using System;
using System.Windows.Forms;
using AwesomeAssertions;
using MusicBeePlugin.Settings;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Settings
{
    public sealed class SettingsRowsTests : IDisposable
    {
        private const int ValueWidth = 400;
        private const int BlockGap = 12;

        private readonly TableLayoutPanel _layout = new TableLayoutPanel { ColumnCount = 2 };
        private readonly SettingsRows _rows = new SettingsRows(ValueWidth, BlockGap);

        public void Dispose() => _layout.Dispose();

        [Fact]
        public void HidingARow_HidesItsCaptionToo()
        {
            var addresses = new TextBox { Multiline = true };
            _rows.Add(_layout, "Allowed addresses", addresses);

            _rows.SetVisible(addresses, false);

            _rows.CaptionOf(addresses).Visible.Should().BeFalse();
            addresses.Visible.Should().BeFalse();
        }

        [Fact]
        public void AHiddenRow_CanBeShownAgain()
        {
            var range = new FlowLayoutPanel();
            _rows.Add(_layout, "Range", range);
            _rows.SetVisible(range, false);

            _rows.SetVisible(range, true);

            _rows.CaptionOf(range).Visible.Should().BeTrue();
            range.Visible.Should().BeTrue();
        }

        [Fact]
        public void ALabelValue_WrapsAtTheValueColumn()
        {
            var status = new Label();

            _rows.Add(_layout, "Status", status);

            status.MaximumSize.Width.Should().Be(ValueWidth);
        }

        [Fact]
        public void ACaptionBesideSeveralLines_SitsLevelWithTheFirst()
        {
            var paired = new ListView();
            var port = new NumericUpDown();

            _rows.Add(_layout, "Paired", paired);
            _rows.Add(_layout, "Listening port", port);

            _rows.CaptionOf(paired).Anchor.Should().Be(AnchorStyles.Left | AnchorStyles.Top);
            _rows.CaptionOf(port).Anchor.Should().Be(AnchorStyles.Left);
        }

        [Fact]
        public void ARowThatEndsABlock_LeavesTheGapBelowIt()
        {
            var plain = new Button();
            var last = new Button();

            _rows.Add(_layout, "Plain", plain);
            _rows.Add(_layout, "Last", last, endsBlock: true);

            plain.Margin.Bottom.Should().NotBe(BlockGap);
            last.Margin.Bottom.Should().Be(BlockGap);
        }
    }
}
