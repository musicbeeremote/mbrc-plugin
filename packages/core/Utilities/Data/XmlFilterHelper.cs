using System.Xml.Linq;
using MusicBeePlugin.Enumerations;

namespace MusicBeePlugin.Utilities.Data
{
    /// <summary>
    ///     Helper class for creating XML filters for MusicBee library queries
    /// </summary>
    public static class XmlFilterHelper
    {
        /// <summary>
        ///     Creates an XML filter string for library queries
        /// </summary>
        /// <param name="tags">The metadata fields to search in</param>
        /// <param name="query">The search query</param>
        /// <param name="isStrict">Whether to use exact match (Is) or partial match (Contains)</param>
        /// <param name="source">The source to search in</param>
        /// <returns>XML filter string</returns>
        public static string CreateFilter(string[] tags, string query, bool isStrict, SearchSource source)
        {
            var filter = new XElement("Source",
                new XAttribute("Type", (short)source));

            var conditions = new XElement("Conditions",
                new XAttribute("CombineMethod", "Any"));

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
