using AwesomeAssertions;
using MusicBeePlugin.Settings;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Settings
{
    public sealed class PanelTextTests
    {
        private const long MiB = 1024 * 1024;

        [Fact]
        public void ArtistPictures_IsHidden_WhenDownloadsAreOffAndNothingIsStored()
        {
            PanelText.ArtistPictures(new CoreCacheStatus()).Should().BeNull();
            PanelText.ArtistPictures(null).Should().BeNull();
        }

        [Fact]
        public void ArtistPictures_CountsWhatIsStoredMissedAndWaiting()
        {
            var status = new CoreCacheStatus
            {
                artist_images_enabled = true,
                artist_images = 312,
                artist_image_bytes = 23 * MiB + 1,
                artist_image_budget = 100 * MiB,
                artist_images_not_found = 40,
                artist_images_ambiguous = 12,
                artist_images_waiting = 18,
            };

            PanelText.ArtistPictures(status).Should().Be(
                "312 artist pictures (24 MB of 100 MB) · 40 without a picture · 12 with no sure match · 18 waiting.");
        }

        [Fact]
        public void ArtistPictures_SaysWhyNoMoreWillArrive()
        {
            var full = new CoreCacheStatus
            {
                artist_images_enabled = true,
                artist_images = 2,
                artist_image_bytes = 100 * MiB,
                artist_image_budget = 100 * MiB,
            };
            var off = new CoreCacheStatus { artist_images = 2, artist_image_budget = 100 * MiB };

            PanelText.ArtistPictures(full).Should().EndWith("disk budget full.");
            PanelText.ArtistPictures(off).Should().EndWith("downloads off.");
        }
    }
}
