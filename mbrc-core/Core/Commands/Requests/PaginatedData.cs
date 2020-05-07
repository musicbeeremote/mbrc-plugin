using System;
using System.Collections.Generic;
using MusicBeeRemote.Core.Model.Entities;

namespace MusicBeeRemote.Core.Commands.Requests
{
    public static class PaginatedData
    {
        public static SocketMessage CreateMessage<T>(int offset, int limit, List<T> data, string context)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            var total = data.Count;
            var realLimit = offset + limit > total ? total - offset : limit;
            var message = new SocketMessage
            {
                Context = context,
                Data = new Page<T>
                {
                    Data = offset > total ? new List<T>() : data.GetRange(offset, realLimit),
                    Offset = offset,
                    Limit = limit,
                    Total = total,
                },
                NewLineTerminated = true,
            };
            return message;
        }

        public static Page<T> CreatePage<T>(int offset, int limit, List<T> data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            var total = data.Count;
            var realLimit = offset + limit > total ? total - offset : limit;
            return new Page<T>
            {
                Data = offset > total ? new List<T>() : data.GetRange(offset, realLimit),
                Offset = offset,
                Limit = limit,
                Total = total,
            };
        }
    }
}
