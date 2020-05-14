using System.Collections.Generic;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Model.Entities;

namespace MbrcTester.ApiAdapters
{
    public class NowPlayingApiAdapter : INowPlayingApiAdapter
    {
        private readonly MockNowPlaying _mockNowPlaying;
        private readonly MockPlayer _mockPlayer;

        public NowPlayingApiAdapter(MockNowPlaying mockNowPlaying, MockPlayer mockPlayer)
        {
            _mockNowPlaying = mockNowPlaying;
            _mockPlayer = mockPlayer;
        }

        public bool MoveTrack(int startPosition, int endPosition)
        {
            throw new System.NotImplementedException();
        }

        public bool PlayIndex(int index)
        {
            _mockPlayer.PlayIndex(index);
            return true;
        }

        bool INowPlayingApiAdapter.PlayPath(string path)
        {
            return _mockPlayer.PlayPath(path);
        }

        public bool RemoveIndex(int index)
        {
            _mockNowPlaying.NowPlayingList.RemoveAt(index);
            return true;
        }

        public IEnumerable<NowPlayingTrackInfo> GetTracks(int offset = 0, int limit = 5000)
        {
            return _mockNowPlaying.GetNowPlaying();
        }

        public IEnumerable<NowPlayingListTrack> GetTracksLegacy(int offset = 0, int limit = 5000)
        {
            return _mockNowPlaying.GetNowPlayingLegacy();
        }
    }
}
