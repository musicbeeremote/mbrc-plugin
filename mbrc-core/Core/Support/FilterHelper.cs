using System.Xml.Linq;

namespace MusicBeeRemoteCore.Core.Support
{
    public class FilterHelper
    {
        public static string XmlFilter(string[] tags, string query, bool isStrict)
        {
            var filter = new XElement("Source", new XAttribute("Type", 1));
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