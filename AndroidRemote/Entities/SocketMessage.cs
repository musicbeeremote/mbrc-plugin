using System;
using ServiceStack.Text;


namespace MusicBeePlugin.AndroidRemote.Entities
{
    class SocketMessage
    {
        public SocketMessage(string context, string type, Object data)
        {
            this.context = context;
            this.data = data;
            this.type = type;
        }

        public string context { get; set; }

        public string type { get; set; }

        public object data { get; set; }

        public string toJsonString()
        {
            return JsonSerializer.SerializeToString(this);
        }
    }
}
