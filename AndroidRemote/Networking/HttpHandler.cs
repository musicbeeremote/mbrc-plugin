
namespace MusicBeePlugin.AndroidRemote.Networking
{
    using System.Text;
    internal class HttpHandler
    {
        public bool IsHttpRequest(string message)
        {
            return message.Contains("GET / HTTP/1.1");
        }

        public string GetHttpReply()
        {
            StringBuilder content = new StringBuilder();
            content.Append("<!doctype html>\r\n");
            content.Append("<html lang=\"en\">\r\n");
            content.Append("<head>\r\n");
            content.Append("<meta charset=\"utf-8\">\r\n");
            content.Append("<title>MusicBee Remote</title>\r\n");
            content.Append("<meta name=\"description\" content=\"MusicBee Remote\">\r\n");
            content.Append("<meta name=\"author\" content=\"Kelsos\">\r\n");
            content.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\r\n");
            content.Append("<!--[if lt IE 9]>\r\n");
            content.Append(
                "<script src=\"http://html5shiv.googlecode.com/svn/trunk/html5.js\"></script>\r\n");
            content.Append("<![endif]-->\r\n");
            content.Append("</head>\r\n");
            content.Append("<body>\r\n");
            content.Append("<h1>It is accessible</h1>");
            content.Append("</body>\r\n");
            content.Append("</html>\r\n");

            int length = Encoding.UTF8.GetByteCount(content.ToString());

            StringBuilder str = new StringBuilder();
            str.Append("HTTP/1.1 200 OK\r\n");
            str.Append("Date: Tue, 17 Aug 2011 11:40:00 EST\r\n");
            str.Append("Server: MusicBee Remote\r\n");
            str.Append("Content-Type: text/html; charset=utf-8\r\n");
            str.Append("Content Length:" + length + "\r\n");
            str.Append("\r\n\r\n");
            str.Append(content);

            return str.ToString();
        }
    }
}