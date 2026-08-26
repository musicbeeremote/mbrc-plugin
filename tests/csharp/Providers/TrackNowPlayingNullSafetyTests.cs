using AwesomeAssertions;
using MusicBeePlugin;
using MusicBeePlugin.Models;
using MusicBeePlugin.Providers;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Providers
{
    /// <summary>
    /// With nothing loaded, MusicBee's now-playing reads return null rather than an
    /// empty string. Both queries used to dereference that and throw, which showed up
    /// as a pair of NullReferenceExceptions on every startup with no track (#187).
    /// </summary>
    public class TrackNowPlayingNullSafetyTests
    {
        /// <summary>
        /// A MusicBee that has nothing playing: every now-playing read hands back null,
        /// and the bulk tag read reports success with a null-filled array, which is what
        /// it does for a loaded track with unset tags.
        /// </summary>
        private static Plugin.MusicBeeApiInterface NothingPlaying()
        {
            return new Plugin.MusicBeeApiInterface
            {
                NowPlaying_GetFileUrl = () => null,
                NowPlaying_GetFileProperty = _ => null,
                NowPlaying_GetFileTag = _ => null,
                NowPlaying_GetFileTags = (Plugin.MetaDataType[] fields, out string[] results) =>
                {
                    results = new string[fields.Length];
                    return true;
                },
            };
        }

        [Fact]
        public void GetNowPlayingTrackDetails_NothingPlaying_ReturnsEmptyStrings()
        {
            var details = new TrackDataProvider(NothingPlaying()).GetNowPlayingTrackDetails();

            details.albumArtist.Should().BeEmpty();
            details.genre.Should().BeEmpty();
            details.encoder.Should().BeEmpty();
            details.kind.Should().BeEmpty();
            details.duration.Should().BeEmpty();
            details.dateModified.Should().BeEmpty();
        }

        [Fact]
        public void GetNowPlayingTrackDetails_BulkReadReportsSuccessWithNoArray_ReturnsEmptyStrings()
        {
            var api = NothingPlaying();
            api.NowPlaying_GetFileTags = (Plugin.MetaDataType[] fields, out string[] results) =>
            {
                results = null;
                return true;
            };

            var details = new TrackDataProvider(api).GetNowPlayingTrackDetails();

            details.albumArtist.Should().BeEmpty();
            details.comment.Should().BeEmpty();
        }

        [Fact]
        public void GetNowPlayingLastfmStatus_NothingPlaying_IsNormal()
        {
            var status = new TrackDataProvider(NothingPlaying()).GetNowPlayingLastfmStatus();

            status.Should().Be(LastfmStatus.Normal);
        }

        [Theory]
        [InlineData("L", LastfmStatus.Love)]
        [InlineData("lfm", LastfmStatus.Love)]
        [InlineData("Llfm", LastfmStatus.Love)]
        [InlineData("B", LastfmStatus.Ban)]
        [InlineData("Blfm", LastfmStatus.Ban)]
        [InlineData("", LastfmStatus.Normal)]
        [InlineData("something else", LastfmStatus.Normal)]
        public void GetNowPlayingLastfmStatus_MapsTheTagValues(string tag, LastfmStatus expected)
        {
            var api = NothingPlaying();
            api.NowPlaying_GetFileTag = _ => tag;

            new TrackDataProvider(api).GetNowPlayingLastfmStatus().Should().Be(expected);
        }
    }
}
