using System.Collections.Generic;
using System.Linq;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Model.Entities;

namespace MbrcTester.ApiAdapters
{
    public class NowPlayingApiAdapter : INowPlayingApiAdapter
    {
        private readonly MockNowPlaying _mockNowPlaying;


        public NowPlayingApiAdapter(MockNowPlaying mockNowPlaying)
        {
            _mockNowPlaying = mockNowPlaying;
        }

        public bool MoveTrack(int from, int to)
        {
            int[] aFrom = {from};
            int dIn;
            if (from > to)
            {
                dIn = to - 1;
            }
            else
            {
                dIn = to;
            }

            throw new System.NotImplementedException();
        }

        public bool PlayIndex(int index)
        {
            throw new System.NotImplementedException();
        }

        public bool RemoveIndex(int index)
        {
            _mockNowPlaying.NowPlayingList.RemoveAt(index);
            return true;
        }

        public IEnumerable<NowPlaying> GetTracks(int offset = 0, int limit = 4000)
        {
            throw new System.NotImplementedException();
        }

        public IEnumerable<NowPlayingListTrack> GetTracksLegacy(int offset = 0, int limit = 5000)
        {
            throw new System.NotImplementedException();
        }
    }
}