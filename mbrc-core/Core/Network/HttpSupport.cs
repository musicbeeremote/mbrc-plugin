using System;
using System.Collections.Generic;
using MusicBeeRemote.Core.Podcasts;
using MusicBeeRemote.Core.Settings;

namespace MusicBeeRemote.Core.Network
{
    public class HttpSupport
    {
        private readonly PersistanceManager _persistanceManager;
        private WebServer _webServer;
        private readonly Dictionary<string, RouteAction> _routes = new Dictionary<string, RouteAction>();

        public HttpSupport(PersistanceManager persistanceManager, PodcastHttpApi podcastHttpApi)
        {
            _persistanceManager = persistanceManager;
            podcastHttpApi.RegisterRoutes(this);
        }

        public void Start()
        {
            var port = _persistanceManager.UserSettingsModel.ListeningPort + 1;
            Stop();
            _webServer = new WebServer($"http://localhost:{port}/");
            foreach (var route in _routes)
            {
                _webServer.AddHandler(route.Key, route.Value);
            }
            _webServer.Start(true);
        }

        public void Stop()
        {
            try
            {
                _webServer?.Stop();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);                
            }            
        }

        public void AddRoute(string path, RouteAction action)
        {
            _routes.Add(path, action);            
        }
    }
}