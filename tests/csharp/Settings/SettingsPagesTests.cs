using System;
using System.Linq;
using AwesomeAssertions;
using MusicBeePlugin.Settings;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Settings
{
    public class SettingsPagesTests
    {
        [Fact]
        public void EveryPage_IsListed_AtItsOwnPosition()
        {
            var pages = (SettingsPage[])Enum.GetValues(typeof(SettingsPage));

            SettingsPages.All.Select(p => p.Page).Should().Equal(pages);
            SettingsPages.All.Select((p, i) => (int)p.Page == i).Should().AllBeEquivalentTo(true);
        }

        [Fact]
        public void EveryPage_HasATitle()
        {
            SettingsPages.All.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.Title));
            SettingsPages.Title(SettingsPage.WebRemote).Should().Be("Web remote");
        }

        [Fact]
        public void OpeningOnAPage_MakesItThePageShown_EvenBeforeTheHostStarts()
        {
            var before = SettingsDialog.LastPage;
            try
            {
                SettingsWindow.Open(null, "2.0.0", SettingsPage.Diagnostics);

                SettingsDialog.LastPage.Should().Be(SettingsPage.Diagnostics);
            }
            finally
            {
                SettingsDialog.LastPage = before;
            }
        }

        [Fact]
        public void OpeningWithoutAPage_KeepsTheLastOne()
        {
            var before = SettingsDialog.LastPage;
            try
            {
                SettingsDialog.LastPage = SettingsPage.Updates;

                SettingsWindow.Open(null, "2.0.0");

                SettingsDialog.LastPage.Should().Be(SettingsPage.Updates);
            }
            finally
            {
                SettingsDialog.LastPage = before;
            }
        }
    }
}
