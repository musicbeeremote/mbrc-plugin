using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace MusicBeeRemote.Core.Network.Http
{
    internal static class ContextExtensions
    {
        public static void OutputUtf8(
            this HttpListenerContext ctx,
            string html,
            string contentType = "text/html",
            Encoding encoding = null)
        {
            encoding = encoding ?? Encoding.UTF8;
            OutputBinary(ctx, encoding.GetBytes(html), $"{contentType}; charset={encoding.WebName}");
        }

        public static void OutputText(
            this HttpListenerContext ctx,
            string text,
            string contentType = "text/plain",
            Encoding encoding = null)
        {
            encoding = encoding ?? Encoding.Default;
            OutputBinary(ctx, encoding.GetBytes(text), $"{contentType}; charset={encoding.WebName}");
        }

        public static void OutputBinary(
            this HttpListenerContext ctx,
            byte[] content,
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

            foreach (var memberInfo in methods)
            {
                var method = (MethodInfo)memberInfo;
                var parms = method.GetParameters();
                if (parms.Length == 1 && parms[0].GetType().Name == "RouteAction")
                {
                    server.AddHandler($"/{method.Name}", (ctx, data) => method.Invoke(callback, new object[] { ctx }));
                }
            }
        }

        public static void BrowseDirectory(this HttpListenerContext ctx, string rootFolder)
        {
            if (!string.IsNullOrEmpty(rootFolder) &&
                !rootFolder.EndsWith(@"\", StringComparison.InvariantCultureIgnoreCase))
            {
                rootFolder += @"\";
            }
            else
            {
                rootFolder = rootFolder ?? string.Empty;
            }

            var path = Path.Combine(rootFolder, ctx.Request.Url.LocalPath.Substring(1).Replace('/', '\\'));
            if (File.Exists(path))
            {
                WriteFile(ctx, path);
            }
            else if (Directory.Exists(path))
            {
                IndexDirectory(ctx, rootFolder, path);
            }
            else
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
            }
        }

        private static void IndexDirectory(HttpListenerContext ctx, string rootFolder, string path)
        {
            var html = new StringBuilder($"<html>\n<body>\n<h1>Listing of {path}</h1>\n" +
                                         "<table style=\"font-family: courier; padding: 10px;\">\n" +
                                         "<th style=\"min-width: 300px;\">Name</th><th>Last modified</th><th style=\"min-width: 90px;\">Size</th>\n");
            var dirInfo = new DirectoryInfo(path);
            var url = dirInfo.Parent?.FullName;
            if (!string.IsNullOrEmpty(url) && url.Length >= rootFolder.Length - 1)
            {
                if (!url.EndsWith(@"\", StringComparison.InvariantCultureIgnoreCase))
                {
                    url += @"\";
                }

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

        private static void WriteFile(HttpListenerContext ctx, string path)
        {
            var mimeType =
                Registry.GetValue(
                    $"HKEY_CLASSES_ROOT\\{Path.GetExtension(path)}",
                    "Content Type",
                    null) as string ?? "application/octet-stream";
            if (string.Equals(mimeType.Substring(0, 5), "text/", StringComparison.OrdinalIgnoreCase))
            {
                ctx.OutputUtf8(File.ReadAllText(path), mimeType);
            }
            else
            {
                ctx.OutputBinary(File.ReadAllBytes(path), mimeType);
            }
        }
    }
}
