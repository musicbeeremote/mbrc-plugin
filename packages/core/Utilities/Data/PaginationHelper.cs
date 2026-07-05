using Newtonsoft.Json.Linq;

namespace MusicBeePlugin.Utilities.Data
{
    /// <summary>
    ///     Utility helper for parsing pagination parameters from JToken data
    /// </summary>
    public static class PaginationHelper
    {
        /// <summary>
        ///     Parses offset and limit parameters from JToken data
        /// </summary>
        /// <param name="data">The JToken data</param>
        /// <param name="defaultLimit">Default limit value if not specified</param>
        /// <returns>Tuple containing (offset, limit)</returns>
        public static (int offset, int limit) ParsePagination(this JToken data, int defaultLimit = 4000)
        {
            var offset = 0;
            var limit = defaultLimit;

            // Only JObject has child properties - JValue, JArray etc. don't support indexer access
            if (data is JObject jsonObject)
            {
                var offsetToken = jsonObject["offset"];
                if (offsetToken != null)
                    offset = offsetToken.Value<int>();

                var limitToken = jsonObject["limit"];
                if (limitToken != null)
                    limit = limitToken.Value<int>();
            }

            return (offset, limit);
        }

        /// <summary>
        ///     Parses offset and limit parameters from event data as JToken
        /// </summary>
        /// <param name="eventData">The event data object (should be JToken)</param>
        /// <param name="defaultLimit">Default limit value if not specified</param>
        /// <returns>Tuple containing (offset, limit)</returns>
        public static (int offset, int limit) ParsePagination(object eventData, int defaultLimit = 4000)
        {
            if (eventData is JToken jsonData)
                return jsonData.ParsePagination(defaultLimit);
            return (0, defaultLimit);
        }
    }
}
