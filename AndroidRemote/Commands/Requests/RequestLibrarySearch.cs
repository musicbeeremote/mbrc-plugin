using System.Collections.Generic;
using System.Xml.Linq;
using MusicBeePlugin.AndroidRemote.Enumerations;
using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    class RequestLibrarySearch :ICommand
    {
        public void Dispose()
        {

        }

        public void Execute(IEvent eEvent)
        {

            Dictionary<string,MetaTag> map = new Dictionary<string, MetaTag>
                {
                    {"artist", MetaTag.artist},
                    {"genre", MetaTag.genre},
                    {"album", MetaTag.album},
                    {"title", MetaTag.title},
                    {"none", MetaTag.none}
                };

            
            XElement node = XElement.Parse("<libsearch>"+eEvent.Data+"</libsearch>");

            MetaTag tag;
            
            string query = (string) node.Element("query"); 

            map.TryGetValue((string)node.Element("tag"), out tag);


            //Plugin.Instance.SearchMusicBeeLibrary(eEvent.ClientId, tag, (bool)node.Element("filter"), query);
        }
    }
}
