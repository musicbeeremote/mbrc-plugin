using System;
using ServiceStack.Text;


namespace MusicBeePlugin.AndroidRemote.Entities
{
    public class SocketMessage
    {
        public SocketMessage(string context, string type, Object data)
        {
            this.context = context;
            this.data = data;
            this.type = type;
        }

        public SocketMessage(JsonObject jObj)
        {
            this.context = jObj.Get("context");

            var data = jObj.Object("data");

            if (data == null)
            {
                this.data = "";
            }
            else
            {
                if (data.Count <= 1)
                {
                    this.data = jObj.Get("data");
                }
                else
                {
                    this.data = data;
                }
            }


            this.type = jObj.Get("type");
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
