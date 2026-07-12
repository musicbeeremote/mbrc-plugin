using MessagePack;
using MessagePack.Resolvers;

namespace MusicBeePlugin.Ffi
{
    /// <summary>
    ///     Shared MessagePack configuration for the FFI DTOs. The contractless
    ///     resolver uses property names as keys, matching the Rust core's
    ///     <c>rmp_serde::to_vec_named</c> (name-keyed maps, not positional arrays).
    /// </summary>
    internal static class Msgpack
    {
        private static readonly MessagePackSerializerOptions Options =
            MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

        public static byte[] Serialize<T>(T value) => MessagePackSerializer.Serialize(value, Options);

        public static T Deserialize<T>(byte[] bytes) => MessagePackSerializer.Deserialize<T>(bytes, Options);
    }
}
