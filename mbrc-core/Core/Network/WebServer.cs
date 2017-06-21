using System;
using System.Net;
using System.Text;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace MusicBeeRemote.Core.Network
{
    //
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
    //


    public delegate void RouteAction(HttpListenerContext ctx, Dictionary<string, string> data);

    public class WebServer : IDisposable
    {
        private HttpListener _httpListener;
        private Router _router;

        public WebServer(string urls)
        {
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

        private void RealStart(bool async)
        {
            Action workerFunction = () =>
            {
                var clientIsListening = _httpListener != null && _httpListener.IsListening;
                while (clientIsListening)
                {
                    ThreadPool.QueueUserWorkItem(QueueItem, _httpListener.GetContext());
                }
            };

            _httpListener.Start();

            if (async)
            {
                ThreadPool.QueueUserWorkItem(_ => workerFunction());
            }                
            else {
                workerFunction();
            }
        }

        private void QueueItem(object state)
        {
            var ctx = state as HttpListenerContext;
            if (ctx == null)
            {
                return;
            }
            
            try
            {
                RouteAction fnRoute;
                Dictionary<string, string> data;
                if (_router.TryGetValue(ctx.Request.Url.LocalPath, out fnRoute, out data)
                    || _router.TryGetValue("*", out fnRoute, out data))
                    fnRoute(ctx, data);
                else
                    ctx.Response.StatusCode = (int) HttpStatusCode.NotFound;
            }
            catch
            {
                ctx.Response.StatusCode = (int) HttpStatusCode.InternalServerError;
            }
            try
            {
                ctx.Response.Close();
            }
            catch
            {
            }
        }

        public void Stop()
        {
            _httpListener.Stop();
        }

        public void Dispose()
        {
            _httpListener?.Close();
            _httpListener = null;
            (_router as IDisposable)?.Dispose();
            _router = null;
        }
    }

    // Each new route is assigned a key from permutations of `KeyBase` ("123456") and is stored in 
    // `_routes` dictionary. Router implementation builds a composite regex from all routes 
    // patterns that looks like
    //    route_pattern1 | route_pattern2 | route_pattern3 | route_pattern4 | ...
    // where `route_patternN` is prefixed with it's key pattern that looks like
    //    ^(?<__c1__>1)(?<__c5__>2)(?<__c3__>3)(?<__c2__>4)(?<__c4__>5)(?<__c6__>6)
    // These key patterns always match `KeyBase` ("123456") but in different named captures, so in the 
    // sample key pattern above when matched against "123456/local/path" the `__c1__` to `__c6__` 
    // named captures will concatenate to "143526" for currently matched route key. The corresponding 
    // entry in `_routes` has `GroupStart` to `GroupEnd` that are used to extract handler data 
    // dictionary from the composite regex anonymous captures.
    internal class Router : IDisposable
    {
        private static readonly string KeyBase = "123456";

        private static readonly Regex RoutePattern = new Regex(
            @"(/(({(?<data>[^}/:]+)(:(?<type>[^}/]+))?}?)|(?<static>[^/]+))|\*)",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private class RouteEntry
        {
            public string Pattern { get; set; }
            public int GroupStart { get; set; }
            public int GroupEnd { get; set; }
            public RouteAction Handler { get; set; }
        }

        private Dictionary<string, RouteEntry> _routes = new Dictionary<string, RouteEntry>();

        private IEnumerator<IEnumerable<char>> _permEnum =
            GetPermutations(KeyBase.ToCharArray(), KeyBase.Length).GetEnumerator();

        private string[] _groupNames = new string[32];
        private Regex _pathParser;

        public void Add(string route, RouteAction handler)
        {
            // for each "{key:type}" check regex pattern in `type` and raise `ArgumentException` on failure
            RoutePattern.Replace(route, m =>
            {
                if (string.IsNullOrEmpty(m.Groups["static"].Value) && !string.IsNullOrEmpty(m.Groups["data"].Value)
                    && !string.IsNullOrEmpty(m.Groups["type"].Value))
                    Regex.Match("", m.Groups["type"].Value);
                return null;
            });
            _permEnum.MoveNext();
            _routes.Add(string.Join(null, _permEnum.Current), new RouteEntry {Pattern = route, Handler = handler});
            _pathParser = null;
        }

        public bool TryGetValue(string localPath, out RouteAction handler, out Dictionary<string, string> data)
        {
            handler = null;
            data = null;
            if (_pathParser == null)
                _pathParser = RebuildParser();
            var match = _pathParser.Match(KeyBase + localPath);
            if (match.Success)
            {
                string routeKey = null;
                for (int idx = 1; idx <= KeyBase.Length; idx++)
                    routeKey += match.Groups[$"__c{idx}__"].Value;
                var entry = _routes[routeKey];
                handler = entry.Handler;
                if (entry.GroupStart < entry.GroupEnd)
                    data = new Dictionary<string, string>();
                for (var groupIdx = entry.GroupStart; groupIdx < entry.GroupEnd; groupIdx++)
                    data[_groupNames[groupIdx]] = match.Groups[groupIdx].Value;
            }
            return match.Success;
        }

        private Regex RebuildParser()
        {
            string[] rev = new string[KeyBase.Length];
            var sb = new StringBuilder();
            int groupIdx = 1;

            foreach (string key in _routes.Keys)
            {
                var entry = _routes[key];
                entry.GroupStart = groupIdx;
                int el = 1;
                foreach (char c in key.ToCharArray())
                    rev[c - '1'] = $"(?<__c{el++}__>{c})";
                sb.AppendLine((sb.Length > 0 ? "|" : null) + "^" + string.Join(null, rev) +
                              RoutePattern.Replace(entry.Pattern, m =>
                              {
                                  string str = m.Groups["static"].Value;
                                  if (!string.IsNullOrEmpty(str))
                                      return "/" + Regex.Escape(str);
                                  str = m.Groups["data"].Value;
                                  if (!string.IsNullOrEmpty(str))
                                  {
                                      if (groupIdx >= _groupNames.Length)
                                          Array.Resize(ref _groupNames, _groupNames.Length * 2);
                                      _groupNames[groupIdx++] = str;
                                      str = m.Groups["type"].Value;
                                      return $"/({(string.IsNullOrEmpty(str) ? "[^/]*" : str)})";
                                  }
                                  return Regex.Escape(m.Groups[0].Value);
                              }));
                entry.GroupEnd = groupIdx;
            }
            return new Regex(sb.ToString(),
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);
        }

        public void Dispose()
        {
            _permEnum?.Dispose();
            _permEnum = null;
        }

        private static IEnumerable<IEnumerable<T>> GetPermutations<T>(IEnumerable<T> list, int length)
        {
            if (length == 1) return list.Select(t => new T[] {t});
            return GetPermutations(list, length - 1).SelectMany(t => list
                .Where(o => !t.Contains(o)), (t1, t2) => t1.Concat(new T[] {t2}));
        }
    }

    internal static class ContextExtensions
    {
        public static void OutputUtf8(this HttpListenerContext ctx, string html, string contentType = "text/html",
            Encoding encoding = null)
        {
            encoding = encoding ?? Encoding.UTF8;
            OutputBinary(ctx, encoding.GetBytes(html), $"{contentType}; charset={encoding.WebName}");
        }

        public static void OutputText(this HttpListenerContext ctx, string text, string contentType = "text/plain",
            Encoding encoding = null)
        {
            encoding = encoding ?? Encoding.Default;
            OutputBinary(ctx, encoding.GetBytes(text), $"{contentType}; charset={encoding.WebName}");
        }

        public static void OutputBinary(this HttpListenerContext ctx, byte[] content,
            string contentType = "application/octet-stream")
        {
            ctx.Response.ContentType = contentType;
            ctx.Response.ContentLength64 = content.Length;
            ctx.Response.OutputStream.Write(content, 0, content.Length);
        }

        public static void AddFromMembers(this WebServer server, object callback)
        {
            var methods = callback.GetType()
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.InvokeMethod);
            foreach (MethodInfo method in methods)
            {
                var parms = method.GetParameters();
                if (parms.Length == 1 && parms[0].GetType().Name == "RouteAction")
                    server.AddHandler("/" + method.Name, (ctx, data) => method.Invoke(callback, new object[] {ctx}));
            }
        }

        public static void BrowseDirectory(this HttpListenerContext ctx, string rootFolder)
        {
            if (!string.IsNullOrEmpty(rootFolder) && !rootFolder.EndsWith(@"\"))
                rootFolder += @"\";
            else
                rootFolder = rootFolder ?? string.Empty;
            var path = Path.Combine(rootFolder, ctx.Request.Url.LocalPath.Substring(1).Replace('/', '\\'));
            if (File.Exists(path))
            {
                var mimeType =
                    Registry.GetValue($"HKEY_CLASSES_ROOT\\{Path.GetExtension(path)}", "Content Type",
                        null) as string ?? "application/octet-stream";
                if (string.Equals(mimeType.Substring(0, 5), "text/", StringComparison.OrdinalIgnoreCase))
                    ctx.OutputUtf8(File.ReadAllText(path), mimeType);
                else
                    ctx.OutputBinary(File.ReadAllBytes(path), mimeType);
            }
            else if (Directory.Exists(path))
            {
                var html = new StringBuilder($"<html>\n<body>\n<h1>Listing of {path}</h1>\n" +
                                             $"<table style=\"font-family: courier; padding: 10px;\">\n" +
                                             $"<th style=\"min-width: 300px;\">Name</th><th>Last modified</th><th style=\"min-width: 90px;\">Size</th>\n");
                var dirInfo = new DirectoryInfo(path);
                var url = dirInfo.Parent?.FullName;
                if (!string.IsNullOrEmpty(url) && url.Length >= rootFolder.Length - 1)
                {
                    if (!url.EndsWith(@"\"))
                        url += @"\";
                    url = "/" + url.Substring(rootFolder.Length).Replace('\\', '/');
                    html.AppendLine(
                        $"<tr><td><a href=\"{url}\">Parent Directory</a></td><td>&nbsp;</td><td align=\"right\">&lt;DIR&gt;</td></tr>");
                }
                foreach (var dir in dirInfo.EnumerateDirectories())
                {
                    url = "/" + dir.FullName.Substring(rootFolder.Length).Replace('\\', '/');
                    html.AppendLine($"<tr><td><a href=\"{url}\">{WebUtility.HtmlEncode(dir.Name)}</a></td>" +
                                    $"<td>{dir.LastWriteTime:yyyy-MMM-dd hh:mm:ss}</td><td align=\"right\">&lt;DIR&gt;</td></tr>\n");
                }
                foreach (var file in dirInfo.EnumerateFiles())
                {
                    url = "/" + file.FullName.Substring(rootFolder.Length).Replace('\\', '/');
                    html.AppendLine($"<tr><td><a href=\"{url}\">{WebUtility.HtmlEncode(file.Name)}</a></td>" +
                                    $"<td>{file.LastWriteTime:yyyy-MMM-dd hh:mm:ss}</td><td align=\"right\">{file.Length:#,#}</td></tr>");
                }
                html.AppendLine("</table>\n</body>\n</html>");
                ctx.OutputUtf8(html.ToString());
            }
            else
                ctx.Response.StatusCode = (int) HttpStatusCode.NotFound;
        }
    }
}