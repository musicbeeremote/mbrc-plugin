using System;
using MusicBeePlugin.Entities;

namespace MusicBeePlugin.Events
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
        private readonly Plugin.RepeatMode _repeatMode;
        private readonly Plugin.PlayState _playState;
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
        public DataEventArgs(EventDataType type, Plugin.PlayState playState)
        {
            _type = type;
            _playState = playState;
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        /// <param name="repeatMode"></param>
        public DataEventArgs(EventDataType type, Plugin.RepeatMode repeatMode)
        {
            _type = type;
            _repeatMode = repeatMode;
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
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        public DataEventArgs(EventDataType type)
        {
            _type = type;
            _stringData = null;
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

        public Plugin.RepeatMode RepeatMode
        {
            get { return _repeatMode; }
        }

        public Plugin.PlayState PlayState
        {
            get { return _playState; }
        }

        public int ClientId
        {
            get { return _clientId; }
        }
    }
}
