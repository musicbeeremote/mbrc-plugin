using System;

namespace MusicBeePlugin.Events.Args
{
    class DataEventArgs:EventArgs
    {
    

        private DataType _type;
        private string _value;

        public DataEventArgs(DataType type, string value)
        {
            _type = type;
            _value = value;
        }

        public DataEventArgs(DataType type)
        {
            _type = type;
            _value = null;
        }

        public DataType Type
        {
            get { return _type; }
        }

        public string Value
        {
            get { return _value; }
        }
    }
}
