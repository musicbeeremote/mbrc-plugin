using System.Text;
using System.Xml;
using System.Xml.Linq;
using MusicBeePlugin.Ffi.Generated;

namespace MusicBeePlugin.Utilities
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
        /// <param name="source">The library category to search in</param>
        /// <returns>XML filter string</returns>
        public static string CreateFilter(string[] tags, string query, bool isStrict, LibrarySource source)
        {
            return Build(tags, query, isStrict, source);
        }

        /// <summary>
        ///     Every file of one library category, as MusicBee's own lists show it.
        /// </summary>
        public static string AllOf(LibrarySource source)
        {
            return new XElement("Source",
                new XAttribute("Type", (int)source),
                new XElement("Conditions", new XAttribute("CombineMethod", "All"))).ToString();
        }

        /// <summary>
        ///     A filter naming no library category, for the now playing list, whose
        ///     queries MusicBee does not split by one.
        /// </summary>
        public static string CreateFilter(string[] tags, string query, bool isStrict)
        {
            return Build(tags, query, isStrict, null);
        }

        private static string Build(string[] tags, string query, bool isStrict, LibrarySource? source)
        {
            var filter = new XElement("Source");
            if (source.HasValue)
                filter.Add(new XAttribute("Type", (int)source.Value));

            var conditions = new XElement("Conditions",
                new XAttribute("CombineMethod", "Any"));

            foreach (var tag in tags)
            {
                var condition = new XElement("Condition",
                    new XAttribute("Field", tag),
                    new XAttribute("Comparison", isStrict ? "Is" : "Contains"),
                    new XAttribute("Value", XmlCarriable(query)));
                conditions.Add(condition);
            }

            filter.Add(conditions);
            return filter.ToString();
        }

        /// <summary>
        ///     The query without characters XML 1.0 cannot represent, such as control
        ///     characters, which would make <c>ToString</c> throw. No tag can match
        ///     them through a filter anyway.
        /// </summary>
        private static string XmlCarriable(string query)
        {
            if (string.IsNullOrEmpty(query))
                return string.Empty;

            var kept = new StringBuilder(query.Length);
            for (var i = 0; i < query.Length; i++)
            {
                var c = query[i];
                if (XmlConvert.IsXmlChar(c))
                {
                    kept.Append(c);
                }
                else if (i + 1 < query.Length && XmlConvert.IsXmlSurrogatePair(query[i + 1], c))
                {
                    kept.Append(c).Append(query[i + 1]);
                    i++;
                }
            }

            return kept.ToString();
        }
    }
}
