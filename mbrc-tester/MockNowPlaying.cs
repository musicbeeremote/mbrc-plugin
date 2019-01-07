using System.Collections.Generic;
using System.Linq;
using MusicBeeRemote.Core.Model.Entities;

namespace MbrcTester
{
    public class MockNowPlaying
    {
        public List<MockTrackMetadata> NowPlayingList { get; } = new List<MockTrackMetadata>();

        public IEnumerable<NowPlaying> GetNowPlaying()
        {
            return NowPlayingList.Select((metadata, index) => new NowPlaying()
            {
                Artist = metadata.Artist,
                Path = metadata._id,
                Position = index,
                Title = metadata.Title
            });
        }

        public IEnumerable<NowPlayingListTrack> GetNowPlayingLegacy()
        {
            return NowPlayingList.Select((metadata, index) => new NowPlayingListTrack()
            {
                Artist = metadata.Artist,
                Path = metadata.Album,
                Position = index,
                Title = metadata.Title
            });
        }
    }
}