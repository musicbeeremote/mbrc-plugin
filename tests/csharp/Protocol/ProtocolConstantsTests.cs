using FluentAssertions;
using MusicBeePlugin.Protocol.Messages;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Protocol
{
    public class ProtocolConstantsTests
    {
        [Fact]
        public void MessageTerminator_IsCrLf()
        {
            // Assert
            ProtocolConstants.MessageTerminator.Should().Be("\r\n");
        }

        [Fact]
        public void ProtocolVersion_IsV4()
        {
            // Assert
            ProtocolConstants.ProtocolVersion.Should().Be(4);
        }

        [Fact]
        public void PlayerName_IsMusicBee()
        {
            // Assert
            ProtocolConstants.PlayerName.Should().Be("MusicBee");
        }

        [Theory]
        [InlineData(ProtocolConstants.V2, 2)]
        [InlineData(ProtocolConstants.V3, 3)]
        [InlineData(ProtocolConstants.V4, 4)]
        public void VersionConstants_HaveCorrectValues(int constant, int expected)
        {
            // Assert
            constant.Should().Be(expected);
        }

        [Fact]
        public void Player_ContextString_IsCorrect()
        {
            // Assert
            ProtocolConstants.Player.Should().Be("player");
        }

        [Fact]
        public void Protocol_ContextString_IsCorrect()
        {
            // Assert
            ProtocolConstants.Protocol.Should().Be("protocol");
        }

        [Fact]
        public void VerifyConnection_ContextString_IsCorrect()
        {
            // Assert
            ProtocolConstants.VerifyConnection.Should().Be("verifyconnection");
        }

        [Theory]
        [InlineData("playerplay")]
        [InlineData("playerpause")]
        [InlineData("playerplaypause")]
        [InlineData("playerstop")]
        [InlineData("playernext")]
        [InlineData("playerprevious")]
        [InlineData("playervolume")]
        [InlineData("playermute")]
        [InlineData("playershuffle")]
        [InlineData("playerrepeat")]
        [InlineData("playerautodj")]
        [InlineData("playerstatus")]
        public void PlayerCommands_AreDefinedCorrectly(string expectedCommand)
        {
            // These commands should exist and be non-empty
            var commands = new[]
            {
                ProtocolConstants.PlayerPlay,
                ProtocolConstants.PlayerPause,
                ProtocolConstants.PlayerPlayPause,
                ProtocolConstants.PlayerStop,
                ProtocolConstants.PlayerNext,
                ProtocolConstants.PlayerPrevious,
                ProtocolConstants.PlayerVolume,
                ProtocolConstants.PlayerMute,
                ProtocolConstants.PlayerShuffle,
                ProtocolConstants.PlayerRepeat,
                ProtocolConstants.PlayerAutoDj,
                ProtocolConstants.PlayerStatus
            };

            commands.Should().Contain(expectedCommand);
        }

        [Theory]
        [InlineData("nowplayingtrack")]
        [InlineData("nowplayingcover")]
        [InlineData("nowplayingposition")]
        [InlineData("nowplayinglyrics")]
        [InlineData("nowplayingrating")]
        [InlineData("nowplayinglist")]
        public void NowPlayingCommands_AreDefinedCorrectly(string expectedCommand)
        {
            var commands = new[]
            {
                ProtocolConstants.NowPlayingTrack,
                ProtocolConstants.NowPlayingCover,
                ProtocolConstants.NowPlayingPosition,
                ProtocolConstants.NowPlayingLyrics,
                ProtocolConstants.NowPlayingRating,
                ProtocolConstants.NowPlayingList
            };

            commands.Should().Contain(expectedCommand);
        }

        [Theory]
        [InlineData("librarysearchartist")]
        [InlineData("librarysearchalbum")]
        [InlineData("librarysearchgenre")]
        [InlineData("librarysearchtitle")]
        public void LibrarySearchCommands_AreDefinedCorrectly(string expectedCommand)
        {
            var commands = new[]
            {
                ProtocolConstants.LibrarySearchArtist,
                ProtocolConstants.LibrarySearchAlbum,
                ProtocolConstants.LibrarySearchGenre,
                ProtocolConstants.LibrarySearchTitle
            };

            commands.Should().Contain(expectedCommand);
        }

        [Fact]
        public void Ping_IsDefinedCorrectly()
        {
            ProtocolConstants.Ping.Should().Be("ping");
        }

        [Fact]
        public void Pong_IsDefinedCorrectly()
        {
            ProtocolConstants.Pong.Should().Be("pong");
        }

        [Fact]
        public void Init_IsDefinedCorrectly()
        {
            ProtocolConstants.Init.Should().Be("init");
        }

        [Fact]
        public void Error_IsDefinedCorrectly()
        {
            ProtocolConstants.Error.Should().Be("error");
        }

        [Fact]
        public void NotAllowed_IsDefinedCorrectly()
        {
            ProtocolConstants.NotAllowed.Should().Be("notallowed");
        }

        [Fact]
        public void PlaylistList_IsDefinedCorrectly()
        {
            ProtocolConstants.PlaylistList.Should().Be("playlistlist");
        }

        [Fact]
        public void PlaylistPlay_IsDefinedCorrectly()
        {
            ProtocolConstants.PlaylistPlay.Should().Be("playlistplay");
        }

        [Fact]
        public void RadioStations_IsDefinedCorrectly()
        {
            ProtocolConstants.RadioStations.Should().Be("radiostations");
        }

        [Fact]
        public void PlayerOutput_IsDefinedCorrectly()
        {
            ProtocolConstants.PlayerOutput.Should().Be("playeroutput");
        }

        [Fact]
        public void PlayerOutputSwitch_IsDefinedCorrectly()
        {
            ProtocolConstants.PlayerOutputSwitch.Should().Be("playeroutputswitch");
        }

        [Fact]
        public void NoBroadcast_IsDefinedCorrectly()
        {
            ProtocolConstants.NoBroadcast.Should().Be("nobroadcast");
        }

        [Theory]
        [InlineData("browsegenres")]
        [InlineData("browseartists")]
        [InlineData("browsealbums")]
        [InlineData("browsetracks")]
        public void BrowseCommands_AreDefinedCorrectly(string expectedCommand)
        {
            var commands = new[]
            {
                ProtocolConstants.LibraryBrowseGenres,
                ProtocolConstants.LibraryBrowseArtists,
                ProtocolConstants.LibraryBrowseAlbums,
                ProtocolConstants.LibraryBrowseTracks
            };

            commands.Should().Contain(expectedCommand);
        }
    }
}
