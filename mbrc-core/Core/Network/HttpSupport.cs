using System;
using System.Collections.Generic;
using MusicBeeRemote.Core.Network.Http;
using MusicBeeRemote.Core.Podcasts;
using MusicBeeRemote.Core.Settings;
using NLog;

namespace MusicBeeRemote.Core.Network
{
    public class HttpSupport : IDisposable
    {
        private readonly PersistenceManager _persistenceManager;
        private readonly Dictionary<string, RouteAction> _routes = new Dictionary<string, RouteAction>();
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private WebServer _webServer;
        private bool _isDisposed;

        public HttpSupport(PersistenceManager persistenceManager, PodcastHttpApi podcastHttpApi)
        {
            if (podcastHttpApi == null)
            {
                throw new ArgumentNullException(nameof(podcastHttpApi));
            }

            _persistenceManager = persistenceManager;
            podcastHttpApi.RegisterRoutes(this);
        }

        public void Start()
        {
            var port = _persistenceManager.UserSettingsModel.ListeningPort + 1;
            Terminate();
            _webServer = new WebServer($"http://localhost:{port}/");
            foreach (var route in _routes)
            {
                _webServer.AddHandler(route.Key, route.Value);
            }

            _webServer.Start(true);
        }

        public void Terminate()
        {
            try
            {
                _webServer?.Stop();
            }
            catch (Exception e)
            {
                _logger.Error(e);
            }
        }

        public void AddRoute(string path, RouteAction action)
        {
            _routes.Add(path, action);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
            {
                return;
            }

            if (disposing)
            {
                _webServer.Dispose();
            }

            _isDisposed = true;
        }
    }
}
