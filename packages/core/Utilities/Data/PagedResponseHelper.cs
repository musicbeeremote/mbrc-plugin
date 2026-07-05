using System.Collections.Generic;
using System.Linq;
using MusicBeePlugin.Models.Entities;
using MusicBeePlugin.Models.Responses;

namespace MusicBeePlugin.Utilities.Data
{
    /// <summary>
    ///     Utility helper for creating paged responses with SocketMessage
    /// </summary>
    public static class PagedResponseHelper
    {
        /// <summary>
        ///     Creates a paged SocketMessage
        /// </summary>
        /// <typeparam name="T">Type of data in the page</typeparam>
        /// <param name="context">The message context constant</param>
        /// <param name="data">The complete dataset</param>
        /// <param name="offset">Starting position (zero-based)</param>
        /// <param name="limit">Maximum number of items to return</param>
        /// <returns>The created SocketMessage</returns>
        public static SocketMessage CreatePagedMessage<T>(string context, List<T> data, int offset, int limit)
        {
            var total = data.Count;
            var realLimit = offset + limit > total ? total - offset : limit;

            return new SocketMessage
            {
                Context = context,
                Data = new Page<T>
                {
                    Data = offset > total ? new List<T>() : data.GetRange(offset, realLimit),
                    Offset = offset,
                    Limit = limit,
                    Total = total
                }
            };
        }

        /// <summary>
        ///     Creates a Page&lt;T&gt; object with proper bounds checking
        /// </summary>
        /// <typeparam name="T">Type of data in the page</typeparam>
        /// <param name="data">The complete dataset</param>
        /// <param name="offset">Starting position (zero-based)</param>
        /// <param name="limit">Maximum number of items to return</param>
        /// <returns>The created Page object</returns>
        public static Page<T> CreatePage<T>(List<T> data, int offset, int limit)
        {
            var total = data.Count;
            var realLimit = offset + limit > total ? total - offset : limit;

            return new Page<T>
            {
                Data = offset > total ? new List<T>() : data.GetRange(offset, realLimit),
                Offset = offset,
                Limit = limit,
                Total = total
            };
        }

        /// <summary>
        ///     Creates a Page&lt;T&gt; object from an IEnumerable with proper bounds checking
        /// </summary>
        /// <typeparam name="T">Type of data in the page</typeparam>
        /// <param name="data">The complete dataset as IEnumerable</param>
        /// <param name="offset">Starting position (zero-based)</param>
        /// <param name="limit">Maximum number of items to return</param>
        /// <returns>The created Page object</returns>
        public static Page<T> CreatePage<T>(IEnumerable<T> data, int offset, int limit)
        {
            var dataList = data.ToList();
            return CreatePage(dataList, offset, limit);
        }
    }
}
