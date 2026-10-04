using System.Runtime.InteropServices;
using AwesomeAssertions;
using MusicBeePlugin;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Host
{
    /// <summary>
    ///     The API struct is copied out of MusicBee by size, so a field added to
    ///     it must come with the size each older MusicBee can supply; reading past
    ///     what a version has would hand the plugin garbage function pointers.
    /// </summary>
    public class MusicBeeApiInterfaceTests
    {
        [Fact]
        public void TheStructIsTheSizeOfApiRevision58()
        {
            Marshal.SizeOf(typeof(Plugin.MusicBeeApiInterface)).Should().Be(704);
        }

        [Theory]
        [InlineData(53, Plugin.MusicBeeVersion.v3_1)]
        [InlineData(54, Plugin.MusicBeeVersion.v3_4)]
        [InlineData(55, Plugin.MusicBeeVersion.v3_4_1)]
        [InlineData(56, Plugin.MusicBeeVersion.v3_4_1)]
        [InlineData(57, Plugin.MusicBeeVersion.v3_5)]
        [InlineData(58, Plugin.MusicBeeVersion.v3_6)]
        public void ApiRevisionMapsToItsMusicBeeVersion(short revision, Plugin.MusicBeeVersion expected)
        {
            var api = new Plugin.MusicBeeApiInterface { ApiRevision = revision };

            api.MusicBeeVersion.Should().Be(expected);
        }
    }
}
