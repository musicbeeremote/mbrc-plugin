using System.Globalization;
using Newtonsoft.Json.Linq;

namespace MusicBeePlugin.Utilities.Network
{
    /// <summary>
    /// Maps protocol version values from various formats to supported integer versions.
    /// Handles legacy float versions (2.1, 2.2) by mapping them to their integer equivalents.
    /// </summary>
    public static class ProtocolVersionMapper
    {
        /// <summary>
        /// Minimum supported protocol version.
        /// </summary>
        public const int MinVersion = 2;

        /// <summary>
        /// Maximum supported protocol version.
        /// </summary>
        public const int MaxVersion = 4;

        /// <summary>
        /// Default protocol version when parsing fails.
        /// </summary>
        public const int DefaultVersion = 2;

        /// <summary>
        /// Tries to parse a protocol version from various formats and maps legacy versions.
        /// </summary>
        /// <param name="value">The value to parse (int, string, or float).</param>
        /// <param name="version">The parsed and mapped version (integer).</param>
        /// <param name="rawVersion">The raw version value (for capability determination).</param>
        /// <returns>True if parsing succeeded, false otherwise.</returns>
        public static bool TryParse(object value, out int version, out double rawVersion)
        {
            version = DefaultVersion;
            rawVersion = DefaultVersion;

            if (value == null)
                return false;

            // Direct int
            if (value is int intValue)
            {
                version = MapVersion(intValue);
                rawVersion = intValue;
                return true;
            }

            // Direct long (JSON integers can be long)
            if (value is long longValue)
            {
                version = MapVersion((int)longValue);
                rawVersion = longValue;
                return true;
            }

            // Direct double/float
            if (value is double doubleValue)
            {
                version = MapLegacyFloat(doubleValue);
                rawVersion = doubleValue;
                return true;
            }

            if (value is float floatValue)
            {
                version = MapLegacyFloat(floatValue);
                rawVersion = floatValue;
                return true;
            }

            // String parsing
            if (value is string stringValue)
            {
                return TryParseString(stringValue, out version, out rawVersion);
            }

            // Handle JToken types (from Newtonsoft.Json)
            if (value is JValue jValue)
            {
                return TryParseJValue(jValue, out version, out rawVersion);
            }

            return false;
        }

        /// <summary>
        /// Tries to parse a protocol version from various formats and maps legacy versions.
        /// Overload that discards the raw version.
        /// </summary>
        /// <param name="value">The value to parse (int, string, or float).</param>
        /// <param name="version">The parsed and mapped version.</param>
        /// <returns>True if parsing succeeded, false otherwise.</returns>
        public static bool TryParse(object value, out int version)
        {
            return TryParse(value, out version, out _);
        }

        /// <summary>
        /// Parses a protocol version from a JValue.
        /// </summary>
        private static bool TryParseJValue(JValue jValue, out int version, out double rawVersion)
        {
            version = DefaultVersion;
            rawVersion = DefaultVersion;

            if (jValue == null || jValue.Type == JTokenType.Null)
                return false;

            switch (jValue.Type)
            {
                case JTokenType.Integer:
                    var intVal = jValue.Value<int>();
                    version = MapVersion(intVal);
                    rawVersion = intVal;
                    return true;

                case JTokenType.Float:
                    var floatVal = jValue.Value<double>();
                    version = MapLegacyFloat(floatVal);
                    rawVersion = floatVal;
                    return true;

                case JTokenType.String:
                    return TryParseString(jValue.Value<string>(), out version, out rawVersion);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Parses a protocol version string, handling both integer and float formats.
        /// </summary>
        /// <param name="value">The string value to parse.</param>
        /// <param name="version">The parsed and mapped version.</param>
        /// <param name="rawVersion">The raw version value (for capability determination).</param>
        /// <returns>True if parsing succeeded, false otherwise.</returns>
        public static bool TryParseString(string value, out int version, out double rawVersion)
        {
            version = DefaultVersion;
            rawVersion = DefaultVersion;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            // Try integer first (most common case)
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intVersion))
            {
                version = MapVersion(intVersion);
                rawVersion = intVersion;
                return true;
            }

            // Try float for legacy versions like "2.1", "2.2"
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatVersion))
            {
                version = MapLegacyFloat(floatVersion);
                rawVersion = floatVersion;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Parses a protocol version string, handling both integer and float formats.
        /// Overload that discards the raw version.
        /// </summary>
        public static bool TryParseString(string value, out int version)
        {
            return TryParseString(value, out version, out _);
        }

        /// <summary>
        /// Maps a legacy float protocol version to its integer equivalent.
        /// Float versions are truncated to their integer part (2.1 → 2, 2.2 → 2).
        /// Capabilities for legacy versions are handled separately in the command context.
        /// </summary>
        /// <param name="floatVersion">The float version value.</param>
        /// <returns>The mapped integer version (truncated and clamped).</returns>
        public static int MapLegacyFloat(double floatVersion)
        {
            // Truncate to integer part and map to supported range
            // Legacy float versions (2.1, 2.2) map to their integer base (2)
            // Capabilities are handled separately in the context
            return MapVersion((int)floatVersion);
        }

        /// <summary>
        /// Maps an integer version to the supported range.
        /// </summary>
        /// <param name="version">The raw version number.</param>
        /// <returns>The version clamped to the supported range.</returns>
        public static int MapVersion(int version)
        {
            if (version < MinVersion)
                return MinVersion;
            if (version > MaxVersion)
                return MaxVersion;
            return version;
        }
    }
}
