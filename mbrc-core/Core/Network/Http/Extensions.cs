using System;
using System.Net;
using Newtonsoft.Json;

namespace MusicBeeRemote.Core.Network.Http
{
    public static class Extensions
    {
        public static void WriteError(this HttpListenerContext context, int code, string description = "")
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            context.Response.StatusCode = code;
            context.Response.StatusDescription = description;
            context.OutputUtf8(JsonConvert.SerializeObject(new Response { Code = code, Description = description }));
        }
    }
}
