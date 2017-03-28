using System.Xml.Linq;

namespace MusicBeeRemoteCore.Core.Support
{
    public class FilterHelper
    {
        public static string XmlFilter(string[] tags,
            string query,
            bool isStrict,
            SearchSource source = SearchSource.Library)
        {
            short src;
            if (source != SearchSource.None)
            {
                src = (short) source;
            }
            else
            {
                src = (short) SearchSource.Library;
            }

            var filter = new XElement("Source", new XAttribute("Type", src));
            var conditions = new XElement("Conditions", new XAttribute("CombineMethod", "Any"));

            foreach (var tag in tags)
            {
                var condition = new XElement("Condition",
                    new XAttribute("Field", tag),
                    new XAttribute("Comparison", isStrict ? "Is" : "Contains"),
                    new XAttribute("Value", query));
                conditions.Add(condition);
            }
            filter.Add(conditions);

            return filter.ToString();
        }
    }
}