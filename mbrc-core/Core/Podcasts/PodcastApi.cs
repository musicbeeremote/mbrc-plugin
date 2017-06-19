using System.Linq;
using System.Text;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Commands.Requests;
using MusicBeeRemote.Core.Network;
using Newtonsoft.Json;

namespace MusicBeeRemote.Core.Podcasts
{
    public class PodcastApi
    {
        private readonly ILibraryApiAdapter _adapter;

        public PodcastApi(ILibraryApiAdapter adapter)
        {
            _adapter = adapter;           
        }

        public void RegisterRoutes(HttpSupport httpSupport)
        {
            httpSupport.AddRoute(@"/podcasts", (ctx, data) =>
            {
                var page = PaginatedData.CreatePage(0, 10, _adapter.GetPodcastSubscriptions().ToList());
                ctx.OutputUtf8(JsonConvert.SerializeObject(page), "application/json", Encoding.UTF8);       
            });
            
            httpSupport.AddRoute(@"/podcasts/episode", (ctx, data) =>
            {
                
            });
            
            httpSupport.AddRoute(@"/podcasts/artwork", (ctx, data) =>
            {
                
            });
        }
    }
}