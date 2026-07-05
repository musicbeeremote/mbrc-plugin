using FluentAssertions;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Models.Commands;
using MusicBeePlugin.Models.Entities;
using Newtonsoft.Json;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Serialization
{
    /// <summary>
    /// Tests to verify JSON serialization format matches v1.4.1 for backward compatibility.
    /// These tests ensure that protocol messages are serialized with the expected field names
    /// and casing to maintain compatibility with existing Android app versions.
    /// </summary>
    public class EntitySerializationTests
    {
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() },
            NullValueHandling = NullValueHandling.Ignore
        };

        private static string Serialize(object obj) =>
            JsonConvert.SerializeObject(obj, SerializerSettings);

        #region Player State Enums

        [Theory]
        [InlineData(PlayState.Playing, "Playing")]
        [InlineData(PlayState.Paused, "Paused")]
        [InlineData(PlayState.Stopped, "Stopped")]
        [InlineData(PlayState.Undefined, "Undefined")]
        public void PlayState_SerializesWithPascalCase(PlayState state, string expected)
        {
            // Act
            var json = Serialize(state);

            // Assert - must match v1.4.1 format (PascalCase)
            json.Should().Be($"\"{expected}\"");
        }

        [Theory]
        [InlineData(RepeatMode.None, "None")]
        [InlineData(RepeatMode.All, "All")]
        [InlineData(RepeatMode.One, "One")]
        [InlineData(RepeatMode.Undefined, "Undefined")]
        public void RepeatMode_SerializesWithPascalCase(RepeatMode mode, string expected)
        {
            // Act
            var json = Serialize(mode);

            // Assert - must match v1.4.1 format (PascalCase)
            json.Should().Be($"\"{expected}\"");
        }

        [Theory]
        [InlineData(ShuffleState.Off, "off")]
        [InlineData(ShuffleState.Shuffle, "shuffle")]
        [InlineData(ShuffleState.AutoDj, "autodj")]
        public void ShuffleState_SerializesWithLowercase(ShuffleState state, string expected)
        {
            // Act
            var json = Serialize(state);

            // Assert - shuffle has always been lowercase
            json.Should().Be($"\"{expected}\"");
        }

        #endregion

        #region LastfmStatus

        [Theory]
        [InlineData(LastfmStatus.Normal, "Normal")]
        [InlineData(LastfmStatus.Love, "Love")]
        [InlineData(LastfmStatus.Ban, "Ban")]
        public void LastfmStatus_SerializesWithPascalCase(LastfmStatus status, string expected)
        {
            // Act
            var json = Serialize(status);

            // Assert - v1.4.1 nowplayinglfmrating used PascalCase
            // Verified from actual v1.4.1 JSON: {"context":"nowplayinglfmrating","data":"Normal"}
            // TODO: Consider normalizing to lowercase in protocol v5
            json.Should().Be($"\"{expected}\"");
        }

        #endregion

        #region NowPlayingListTrack

        [Fact]
        public void NowPlayingListTrack_SerializesWithLowercaseFieldNames()
        {
            // Arrange
            var track = new NowPlayingListTrack("Test Artist", "Test Title", 1)
            {
                Path = @"C:\Music\test.mp3"
            };

            // Act
            var json = Serialize(track);

            // Assert - v1.4.1 nowplayinglist context used lowercase field names
            // Verified from actual v1.4.1 JSON: {"context":"nowplayinglist","data":{...,"data":[{"artist":"...","title":"...","path":"...","position":1}]}}
            json.Should().Contain("\"artist\":\"Test Artist\"");
            json.Should().Contain("\"title\":\"Test Title\"");
            json.Should().Contain("\"position\":1");
            json.Should().Contain("\"path\":");
        }

        #endregion

        #region NowPlayingTrack

        [Fact]
        public void NowPlayingTrack_SerializesWithLowercaseFieldNames()
        {
            // Arrange
            var track = new NowPlayingTrack
            {
                Artist = "Test Artist",
                Title = "Test Title",
                Album = "Test Album",
                Year = "2024"
            };

            // Act
            var json = Serialize(track);

            // Assert - v1.4.1 nowplayingtrack context used lowercase field names
            // Verified from actual v1.4.1 JSON: {"context":"nowplayingtrack","data":{"artist":"...","title":"...","album":"...","year":"..."}}
            json.Should().Contain("\"artist\":\"Test Artist\"");
            json.Should().Contain("\"title\":\"Test Title\"");
            json.Should().Contain("\"album\":\"Test Album\"");
            json.Should().Contain("\"year\":\"2024\"");
        }

        #endregion

        #region NowPlayingTrackV2

        [Fact]
        public void NowPlayingTrackV2_SerializesWithLowercaseFieldNames()
        {
            // Arrange
            var track = new NowPlayingTrackV2
            {
                Artist = "Test Artist",
                Title = "Test Title",
                Album = "Test Album",
                Year = "2024",
                Path = @"C:\Music\test.mp3"
            };

            // Act
            var json = Serialize(track);

            // Assert - v1.4.1 NowPlayingTrackV2 had DataMember with lowercase
            json.Should().Contain("\"artist\":\"Test Artist\"");
            json.Should().Contain("\"title\":\"Test Title\"");
            json.Should().Contain("\"album\":\"Test Album\"");
            json.Should().Contain("\"year\":\"2024\"");
            json.Should().Contain("\"path\":");
        }

        #endregion

        #region ArtistData

        [Fact]
        public void ArtistData_SerializesWithLowercaseFieldNames()
        {
            // Arrange
            var artist = new ArtistData("Test Artist", 10);

            // Act
            var json = Serialize(artist);

            // Assert - v1.4.1 browseartists context used lowercase field names
            // Verified from actual v1.4.1 JSON: {"context":"browseartists","data":{...,"data":[{"artist":"...","count":1}]}}
            json.Should().Contain("\"artist\":\"Test Artist\"");
            json.Should().Contain("\"count\":10");
        }

        #endregion

        #region AlbumData

        [Fact]
        public void AlbumData_SerializesWithLowercaseFieldNames()
        {
            // Arrange
            var album = new AlbumData("Test Artist", "Test Album");

            // Act
            var json = Serialize(album);

            // Assert - v1.4.1 AlbumData had DataMember with lowercase
            json.Should().Contain("\"album\":\"Test Album\"");
            json.Should().Contain("\"artist\":\"Test Artist\"");
            json.Should().Contain("\"count\":1");
        }

        #endregion

        #region GenreData

        [Fact]
        public void GenreData_SerializesWithLowercaseFieldNames()
        {
            // Arrange
            var genre = new GenreData("Rock", 50);

            // Act
            var json = Serialize(genre);

            // Assert - v1.4.1 GenreData had DataMember with lowercase
            json.Should().Contain("\"genre\":\"Rock\"");
            json.Should().Contain("\"count\":50");
        }

        #endregion

        #region Playlist

        [Fact]
        public void Playlist_SerializesWithLowercaseFieldNames()
        {
            // Arrange
            var playlist = new Playlist { Name = "My Playlist", Url = "playlist://123" };

            // Act
            var json = Serialize(playlist);

            // Assert - v1.4.1 Playlist had DataMember with lowercase
            json.Should().Contain("\"name\":\"My Playlist\"");
            json.Should().Contain("\"url\":\"playlist://123\"");
        }

        #endregion

        #region RadioStation

        [Fact]
        public void RadioStation_SerializesWithLowercaseFieldNames()
        {
            // Arrange
            var station = new RadioStation
            {
                Name = "ROCK ANTENNE Heavy Metal",
                Url = "http://example.com/stream.pls"
            };

            // Act
            var json = Serialize(station);

            // Assert - v1.4.1 radiostations context used lowercase field names
            // Verified from actual v1.4.1 JSON: {"context":"radiostations","data":{...,"data":[{"name":"...","url":"..."}]}}
            json.Should().Contain("\"name\":\"ROCK ANTENNE Heavy Metal\"");
            json.Should().Contain("\"url\":\"http://example.com/stream.pls\"");
        }

        #endregion

        #region Track

        [Fact]
        public void Track_SerializesWithLowercaseFieldNames()
        {
            // Arrange
            var track = new Track("Artist", "Title", 1, "src://path")
            {
                Album = "Album",
                AlbumArtist = "Album Artist",
                Genre = "Rock",
                Disc = 1
            };

            // Act
            var json = Serialize(track);

            // Assert - v1.4.1 Track had DataMember with lowercase/snake_case
            json.Should().Contain("\"src\":\"src://path\"");
            json.Should().Contain("\"artist\":\"Artist\"");
            json.Should().Contain("\"title\":\"Title\"");
            json.Should().Contain("\"trackno\":1");
            json.Should().Contain("\"album\":\"Album\"");
            json.Should().Contain("\"album_artist\":\"Album Artist\"");
            json.Should().Contain("\"genre\":\"Rock\"");
            json.Should().Contain("\"disc\":1");
        }

        #endregion

        #region NowPlaying

        [Fact]
        public void NowPlaying_SerializesWithLowercaseFieldNames()
        {
            // Arrange
            var nowPlaying = new NowPlaying
            {
                Artist = "Artist",
                Album = "Album",
                AlbumArtist = "Album Artist",
                Title = "Title",
                Path = "path://file",
                Position = 5
            };

            // Act
            var json = Serialize(nowPlaying);

            // Assert - v1.4.1 NowPlaying had DataMember with lowercase/snake_case
            json.Should().Contain("\"artist\":\"Artist\"");
            json.Should().Contain("\"album\":\"Album\"");
            json.Should().Contain("\"album_artist\":\"Album Artist\"");
            json.Should().Contain("\"title\":\"Title\"");
            json.Should().Contain("\"path\":\"path://file\"");
            json.Should().Contain("\"position\":5");
        }

        #endregion

        #region NowPlayingDetails - camelCase (existing inconsistency)

        [Fact]
        public void NowPlayingDetails_SerializesWithCamelCaseFieldNames()
        {
            // Arrange
            var details = new NowPlayingDetails
            {
                AlbumArtist = "Album Artist",
                Genre = "Rock",
                TrackNo = "1",
                TrackCount = "12",
                DiscNo = "1",
                DiscCount = "2"
            };

            // Act
            var json = Serialize(details);

            // Assert - v1.4.1 NowPlayingDetails used camelCase (inconsistent with rest of API)
            // This is a known inconsistency that should be addressed in protocol v5
            json.Should().Contain("\"albumArtist\":\"Album Artist\"");
            json.Should().Contain("\"genre\":\"Rock\"");
            json.Should().Contain("\"trackNo\":\"1\"");
            json.Should().Contain("\"trackCount\":\"12\"");
            json.Should().Contain("\"discNo\":\"1\"");
            json.Should().Contain("\"discCount\":\"2\"");
        }

        #endregion

        #region PlayerStatus

        [Fact]
        public void PlayerStatus_SerializesWithLowercaseFieldNames()
        {
            // Arrange
            var status = new PlayerStatus
            {
                Repeat = "None",
                Mute = false,
                Shuffle = "off",
                Scrobble = false,
                State = "Stopped",
                Volume = "22"
            };

            // Act
            var json = Serialize(status);

            // Assert - v1.4.1 playerstatus context used lowercase field names
            // Verified from actual v1.4.1 JSON: {"context":"playerstatus","data":{"playerrepeat":"None","playermute":false,"playershuffle":"off","scrobbler":false,"playerstate":"Stopped","playervolume":"22"}}
            json.Should().Contain("\"playerrepeat\":\"None\"");
            json.Should().Contain("\"playermute\":false");
            json.Should().Contain("\"playershuffle\":\"off\"");
            json.Should().Contain("\"scrobbler\":false");
            json.Should().Contain("\"playerstate\":\"Stopped\"");
            json.Should().Contain("\"playervolume\":\"22\"");
        }

        #endregion

        #region CoverPayload

        [Fact]
        public void CoverPayload_NotFound_SerializesWithoutCoverField()
        {
            // Arrange - cover not found scenario
            var coverNotFound = new CoverPayload(null, false);

            // Act
            var json = Serialize(coverNotFound);

            // Assert - v1.4.1 nowplayingcover context: no cover field when not found
            // Verified from actual v1.4.1 JSON: {"context":"nowplayingcover","data":{"status":404}}
            json.Should().Contain("\"status\":404");
            json.Should().NotContain("\"cover\"");
        }

        [Fact]
        public void CoverPayload_WithCoverReady_SerializesWithoutCoverField()
        {
            // Arrange - cover ready but not included scenario
            var coverReady = new CoverPayload("base64data", false);

            // Act
            var json = Serialize(coverReady);

            // Assert - status 1 means cover is ready, but cover field should not be included
            // Verified from actual v1.4.1 JSON: {"context":"nowplayingcover","data":{"status":1}}
            json.Should().Contain("\"status\":1");
            json.Should().NotContain("\"cover\"");
        }

        [Fact]
        public void CoverPayload_WithCoverIncluded_SerializesWithCoverField()
        {
            // Arrange - cover available and included
            var coverIncluded = new CoverPayload("base64data", true);

            // Act
            var json = Serialize(coverIncluded);

            // Assert - status 200 means cover is available and included
            json.Should().Contain("\"status\":200");
            json.Should().Contain("\"cover\":\"base64data\"");
        }

        #endregion

        #region LyricsPayload

        [Fact]
        public void LyricsPayload_SerializesWithLowercaseFieldNames()
        {
            // Arrange - no lyrics scenario
            var noLyrics = new LyricsPayload("");

            // Act
            var json = Serialize(noLyrics);

            // Assert - v1.4.1 nowplayinglyrics context used lowercase field names
            // Verified from actual v1.4.1 JSON: {"context":"nowplayinglyrics","data":{"status":404,"lyrics":""}}
            json.Should().Contain("\"status\":404");
            json.Should().Contain("\"lyrics\":\"\"");
        }

        [Fact]
        public void LyricsPayload_WithLyrics_SerializesCorrectly()
        {
            // Arrange - lyrics found scenario
            var withLyrics = new LyricsPayload("Some lyrics text");

            // Act
            var json = Serialize(withLyrics);

            // Assert - status 200 means lyrics found
            json.Should().Contain("\"status\":200");
            json.Should().Contain("\"lyrics\":\"Some lyrics text\"");
        }

        #endregion

        #region PlaybackPosition

        [Fact]
        public void PlaybackPosition_SerializesWithLowercaseFieldNames()
        {
            // Arrange
            var position = new PlaybackPosition(120000, 300000);

            // Act
            var json = Serialize(position);

            // Assert - v1.4.1 nowplayingposition context used lowercase field names
            // Verified from actual v1.4.1 JSON: {"context":"nowplayingposition","data":{"current":0,"total":271360}}
            json.Should().Contain("\"current\":120000");
            json.Should().Contain("\"total\":300000");
        }

        #endregion
    }
}
