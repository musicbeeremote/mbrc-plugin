using System;
using System.Collections;

namespace MusicBeePlugin.Ffi
{
    /// <summary>
    ///     A query's result, built while the call is in MusicBee and packed after
    ///     it, so the serial lock is not held while MessagePack encodes it.
    /// </summary>
    public sealed class QueryReply
    {
        private readonly Func<byte[]> _pack;

        private QueryReply(Func<byte[]> pack)
        {
            _pack = pack;
        }

        /// <summary>
        ///     Wraps a complete result.
        /// </summary>
        /// <remarks>
        ///     A lazy sequence is refused: enumerating it during the pack would walk
        ///     a query cursor after the serial lock was released.
        /// </remarks>
        public static QueryReply Of<T>(T value)
        {
            if (value is IEnumerable && !(value is ICollection) && !(value is string))
                throw new InvalidOperationException(
                    "A query result must be complete before the serial lock is released, not a lazy " + value.GetType());
            return new QueryReply(() => Msgpack.Serialize(value));
        }

        /// <summary>The MessagePack reply.</summary>
        public byte[] Pack() => _pack();
    }
}
