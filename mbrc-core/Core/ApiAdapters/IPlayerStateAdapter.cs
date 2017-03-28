using MusicBeeRemoteCore.Remote.Enumerations;

namespace MusicBeeRemoteCore.ApiAdapters
{
    public interface IPlayerStateAdapter
    {
        ShuffleState GetShuffleState();
        Repeat GetRepeatMode();
        bool ScrobblingEnabled();
    }
}