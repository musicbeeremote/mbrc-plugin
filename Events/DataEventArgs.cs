using System;

namespace MusicBeePlugin.Events
{
    /// <summary>
    /// 
    /// </summary>
    public class DataEventArgs:EventArgs
    {
        private readonly DataType _type;
        private readonly string _value;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        /// <param name="value"></param>
        public DataEventArgs(DataType type, string value)
        {
            _type = type;
            _value = value;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        public DataEventArgs(DataType type)
        {
            _type = type;
            _value = null;
        }

        /// <summary>
        /// 
        /// </summary>
        public DataType Type
        {
            get { return _type; }
        }

        /// <summary>
        /// 
        /// </summary>
        public string Value
        {
            get { return _value; }
        }
    }
}
