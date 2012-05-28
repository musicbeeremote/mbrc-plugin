namespace MusicBeePlugin
{
    class PlayerStateModel
    {
        private string _artist;
        private string _title;
        private string _album;
        private string _year;
        private string _cover;

        private string _lyrics;

        private Plugin.PlayState _playState;
        private int _volume;

        private bool _shuffleState;
        private Plugin.RepeatMode _repeatMode;
        private bool _muteState;
        private bool _scrobblerState;
        private int _trackRating;


    }
}
