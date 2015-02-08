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

            var data = jObj.Get("data");
            if (data == null)
            {
                this.data = "";
            }
            else
            {
                if (data.Contains("{")&&data.Contains("}"))
                {
                    this.data = jObj.Object("data");
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
