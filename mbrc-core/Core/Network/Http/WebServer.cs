using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;

namespace MusicBeeRemote.Core.Network.Http
{
    // Poor Man's Web Server with Regex Routing in 177 LOC of C#
    //
    // This is a simple standalone http server that handles routing with regular expressions
    // matching. For each request the router passes capture groups to handlers as a data dictionary.
    //
    // Router implementation constructs a single composite regex to match request path, based on
    // https://nikic.github.io/2014/02/18/Fast-request-routing-using-regular-expressions.html
    //
    // One can use `WebServer` and `Router` classes alone, just has to register all custom
    // entry-point functions by calling `AddHandler` and start processing request/response through
    // methods/props of the `HttpListenerContext` param and entries in data `Dictionary` param.
    //
    // There is an extension class included that provides some utility functions, most notably
    // `BrowseDirectory` can be used for simple directory browsing under a top-level folder.
    //
    // Take a look at the provided `Main` function for sample server registration and usage.
    //
    // Note: Check out this gist history for even simpler implementation based on `Dictionary`
    // for routing (no regex handling)
    public delegate void RouteAction(HttpListenerContext ctx, Dictionary<string, string> data);

    public class WebServer : IDisposable
    {
        private HttpListener _httpListener;
        private Router _router;
        private bool _isDisposed;

        public WebServer(string urls)
        {
            if (urls == null)
            {
                throw new ArgumentNullException(nameof(urls));
            }

            _httpListener = new HttpListener();
            urls.Split('|').ToList().ForEach(_httpListener.Prefixes.Add);
            _router = new Router();
        }

        public void AddHandler(string path, RouteAction fn)
        {
            _router.Add(path, fn);
        }

        public void Start(bool async = false)
        {
            try
            {
                RealStart(async);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        public void Stop()
        {
            _httpListener.Stop();
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
                _httpListener?.Close();
                _httpListener = null;
                (_router as IDisposable)?.Dispose();
                _router = null;
            }

            _isDisposed = true;
        }

        private void RealStart(bool async)
        {
            void WorkerFunction()
            {
                var clientIsListening = _httpListener != null && _httpListener.IsListening;
                while (clientIsListening)
                {
                    ThreadPool.QueueUserWorkItem(QueueItem, _httpListener.GetContext());
                }
            }

            _httpListener.Start();

            if (async)
            {
                ThreadPool.QueueUserWorkItem(_ => WorkerFunction());
            }
            else
            {
                WorkerFunction();
            }
        }

        private void QueueItem(object state)
        {
            if (!(state is HttpListenerContext ctx))
            {
                return;
            }

            try
            {
                if (_router.TryGetValue(ctx.Request.Url.LocalPath, out var fnRoute, out var data)
                    || _router.TryGetValue("*", out fnRoute, out data))
                {
                    fnRoute(ctx, data);
                }
                else
                {
                    ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
                }
            }
            catch
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }

            try
            {
                ctx.Response.Close();
            }
            catch
            {
            }
        }
    }
}
