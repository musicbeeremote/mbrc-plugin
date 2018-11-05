using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Commands.Requests;
using MusicBeeRemote.Core.Model;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Network.Http;
using Newtonsoft.Json;

namespace MusicBeeRemote.Core.Podcasts
{
    public class PodcastHttpApi
    {
        private readonly ILibraryApiAdapter _adapter;

        public PodcastHttpApi(ILibraryApiAdapter adapter)
        {
            _adapter = adapter;           
        }

        public void RegisterRoutes(HttpSupport httpSupport)
        {
            httpSupport.AddRoute(@"/podcasts", (ctx, data) =>
            {
                var request = ctx.Request;
                if (CheckIfNotPost(request, ctx)) return;
                if (CheckIfNotJson(request, ctx)) return;

                var requestPayload = ReadPayload(request);

                if (CheckIfNotEmpty(requestPayload, ctx)) return;

                var requestData = JsonConvert.DeserializeObject<PaginatedRequest>(requestPayload);
                var subscriptions = _adapter.GetPodcastSubscriptions().ToList();
                var page = PaginatedData.CreatePage(requestData.Offset, requestData.Limit, subscriptions);
                ctx.OutputUtf8(JsonConvert.SerializeObject(page), "application/json", Encoding.UTF8);       
            });
            
            httpSupport.AddRoute(@"/episodes", (ctx, data) =>
            {
                var request = ctx.Request;
                if (CheckIfNotPost(request, ctx)) return;
                if (CheckIfNotJson(request, ctx)) return;

                var requestPayload = ReadPayload(request);

                if (CheckIfNotEmpty(requestPayload, ctx)) return;

                var requestData = JsonConvert.DeserializeObject<IdentifiablePaginatedRequest>(requestPayload);

                if (string.IsNullOrWhiteSpace(requestData.Id))
                {                    
                    ctx.WriteError((int) HttpStatusCode.BadRequest, "invalid id");
                    return;
                }

                var episodes = _adapter.GetEpisodes(requestData.Id).ToList();                
                var page = PaginatedData.CreatePage(requestData.Offset, requestData.Limit, episodes);
                ctx.OutputUtf8(JsonConvert.SerializeObject(page), "application/json", Encoding.UTF8);       
            });
            
            httpSupport.AddRoute(@"/subscription-artwork", (ctx, data) =>
            {
                var request = ctx.Request;
                if (CheckIfNotPost(request, ctx)) return;
                if (CheckIfNotJson(request, ctx)) return;

                var requestPayload = ReadPayload(request);

                if (CheckIfNotEmpty(requestPayload, ctx)) return;
                var requestData = JsonConvert.DeserializeObject<IdentifiableRequest>(requestPayload);

                if (string.IsNullOrWhiteSpace(requestData.Id))
                {
                    ctx.Response.StatusCode = (int) HttpStatusCode.BadRequest;
                    return;
                }
                ctx.OutputBinary(_adapter.GetPodcastSubscriptionArtwork(requestData.Id), "image/jpeg");
            });
        }

        private static string ReadPayload(HttpListenerRequest request)
        {
            string requestPayload;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                requestPayload = reader.ReadToEnd();
            }
            return requestPayload;
        }

        private static bool CheckIfNotEmpty(string requestPayload, HttpListenerContext ctx)
        {
            if (string.IsNullOrEmpty(requestPayload))
            {
                ctx.WriteError((int) HttpStatusCode.BadRequest, "payload was empty");
                return true;
            }
            return false;
        }

        private static bool CheckIfNotJson(HttpListenerRequest request, HttpListenerContext ctx)
        {
            if (request.ContentType == null || !request.ContentType.Contains("application/json"))
            {
                ctx.WriteError((int) HttpStatusCode.BadRequest, "application/json");               
                return true;
            }
            return false;
        }

        private static bool CheckIfNotPost(HttpListenerRequest request, HttpListenerContext ctx)
        {
            if (request.HttpMethod != "POST")
            {
                ctx.WriteError((int) HttpStatusCode.BadRequest, "Was expecting POST");                
                return true;
            }
            return false;
        }

    
    }
}