using System;

namespace MusicBeeRemoteCore
{
    [Flags]
    public enum SearchSource : short
    {
        None = 0,
        Library = 1,
        Inbox = 2,
        Podcasts = 4,
        Audiobooks = 32,
        Videos = 64
    }
}