using System;
using MusicBeePlugin.AndroidRemote.Entities;
using MusicBeePlugin.AndroidRemote.Enumerations;

namespace MusicBeePlugin.AndroidRemote.Events
{
    /// <summary>
    /// 
    /// </summary>
    public class DataEventArgs : EventArgs
    {
        private readonly EventDataType _type;
        private readonly string _stringData;
        private readonly int _intData;
        private readonly float _floatData;
        private readonly bool _boolData;
        private readonly TrackInfo _trackData;
        private readonly Repeat _repeatMode;
        private readonly PlayerState _playState;
        private readonly int _clientId;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        /// <param name="stringData"></param>
        /// <param name="clientId"> </param>
        public DataEventArgs(EventDataType type, string stringData, int clientId)
        {
            _type = type;
            _stringData = stringData;
            _clientId = clientId;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        /// <param name="playState"></param>
        public DataEventArgs(EventDataType type, PlayerState playState)
        {
            _type = type;
            _playState = playState;
            _clientId = -1;
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        /// <param name="repeatMode"></param>
        public DataEventArgs(EventDataType type, Repeat repeatMode)
        {
            _type = type;
            _repeatMode = repeatMode;
            _clientId = -1;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        /// <param name="track"></param>
        public DataEventArgs(EventDataType type, TrackInfo track)
        {
            _type = type;
            _trackData = track;
            _clientId = -1;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        /// <param name="stringData"></param>
        public DataEventArgs(EventDataType type, string stringData)
        {
            _type = type;
            _stringData = stringData;
            _clientId = -1;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        /// <param name="intData"></param>
        public DataEventArgs(EventDataType type, int intData)
        {
            _type = type;
            _intData = intData;
            _clientId = -1;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        /// <param name="floatData"></param>
        public DataEventArgs(EventDataType type, float floatData)
        {
            _type = type;
            _floatData = floatData;
            _clientId = -1;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        /// <param name="boolData"></param>
        public DataEventArgs(EventDataType type, bool boolData)
        {
            _type = type;
            _boolData = boolData;
            _clientId = -1;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        public DataEventArgs(EventDataType type)
        {
            _type = type;
            _stringData = null;
            _clientId = -1;
        }


        /// <summary>
        /// 
        /// </summary>
        public EventDataType Type
        {
            get { return _type; }
        }

        /// <summary>
        /// 
        /// </summary>
        public string StringData
        {
            get { return _stringData; }
        }

        /// <summary>
        /// 
        /// </summary>
        public int IntData
        {
            get { return _intData; }
        }

        /// <summary>
        /// 
        /// </summary>
        public float FloatData
        {
            get { return _floatData; }
        }

        /// <summary>
        /// 
        /// </summary>
        public bool BoolData
        {
            get { return _boolData; }
        }

        /// <summary>
        /// 
        /// </summary>
        public TrackInfo TrackData
        {
            get { return _trackData; }
        }

        /// <summary>
        /// 
        /// </summary>
        public Repeat RepeatMode
        {
            get { return _repeatMode; }
        }

        /// <summary>
        /// 
        /// </summary>
        public PlayerState PlayState
        {
            get { return _playState; }
        }

        /// <summary>
        /// 
        /// </summary>
        public int ClientId
        {
            get { return _clientId; }
        }
    }
}
