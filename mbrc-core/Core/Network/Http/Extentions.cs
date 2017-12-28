using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MusicBeeRemote.Core.Network.Http
{
    public static class Extentions
    {
        public static void WriteError(this HttpListenerContext context, int code, string description = "")
        {
            context.Response.StatusCode = code;
            context.Response.StatusDescription = description;
            context.OutputUtf8(JsonConvert.SerializeObject(new Response
            {
                Code = code,
                Description = description
            }));
        }
    }
}