namespace MusicBeePlugin.AndroidRemote.Entities
{
    using Networking;
    using Utilities;

    internal class PlayerStatus
    {
        public string RepeatState { get; set; }
        public string MuteState { get; set; }
        public string ShuffleState { get; set; }
        public string ScrobblerState { get; set; }
        public string PlayState { get; set; }
        public string Volume { get; set; }

        public string ToXmlString()
        {
            string pStatus = XmlCreator.Create(Constants.Repeat, RepeatState, false, false);
            pStatus += XmlCreator.Create(Constants.Mute, MuteState, false, false);
            pStatus += XmlCreator.Create(Constants.Shuffle, ShuffleState, false, false);
            pStatus += XmlCreator.Create(Constants.Scrobble, ScrobblerState, false, false);
            pStatus += XmlCreator.Create(Constants.PlayState, PlayState, false, false);
            pStatus += XmlCreator.Create(Constants.Volume, Volume, false, false);
            return pStatus;
        }
    }
}