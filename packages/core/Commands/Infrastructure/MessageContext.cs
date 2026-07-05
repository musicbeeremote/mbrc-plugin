using System;
using MusicBeePlugin.Commands.Contracts;
using Newtonsoft.Json.Linq;

namespace MusicBeePlugin.Commands.Infrastructure
{
    /// <summary>
    ///     Represents the context for command execution, containing command type, data, and client information
    /// </summary>
    internal sealed class MessageContext : ICommandContext
    {
        public MessageContext(string commandType, object data, string connectionId, string shortId = null)
        {
            CommandType = commandType;
            Data = data;
            ConnectionId = connectionId;
            ShortId = shortId ?? (connectionId?.Length > 6 ? connectionId.Substring(0, 6) : connectionId ?? "?");
        }

        /// <summary>
        ///     The type of command being executed
        /// </summary>
        public string CommandType { get; }

        /// <summary>
        ///     The command data payload
        /// </summary>
        public object Data { get; }

        /// <summary>
        ///     The connection identifier that sent the command
        /// </summary>
        public string ConnectionId { get; }

        /// <summary>
        ///     A short identifier for logging purposes (first 6 chars of connectionId:clientId)
        /// </summary>
        public string ShortId { get; }

        /// <summary>
        ///     Attempts to get the data as the specified type, handling JSON conversions
        /// </summary>
        public bool TryGetData<T>(out T value)
        {
            value = default;

            try
            {
                // Direct type match
                if (Data is T typedData)
                {
                    value = typedData;
                    return true;
                }

                // Handle JToken conversions
                if (Data is JToken jToken)
                {
                    value = jToken.ToObject<T>();
                    return true;
                }

                // Handle string parsing for primitive types
                if (Data is string stringValue)
                {
                    var targetType = typeof(T);

                    if (targetType == typeof(int))
                    {
                        if (int.TryParse(stringValue, out var intValue))
                        {
                            value = (T)(object)intValue;
                            return true;
                        }
                    }
                    else if (targetType == typeof(bool))
                    {
                        if (bool.TryParse(stringValue, out var boolValue))
                        {
                            value = (T)(object)boolValue;
                            return true;
                        }
                    }
                    else if (targetType.IsEnum)
                    {
                        try
                        {
                            value = (T)Enum.Parse(targetType, stringValue, true);
                            return true;
                        }
                        catch (ArgumentException)
                        {
                            // Invalid enum value - expected in TryParse pattern
                            return false;
                        }
                    }
                }

                return false;
            }
            catch (Exception)
            {
                // TryParse pattern: return false on any conversion failure
                return false;
            }
        }

        /// <summary>
        ///     Gets the data as the specified type, handling JSON conversions
        /// </summary>
        public T GetDataOrDefault<T>(T defaultValue = default)
        {
            return TryGetData<T>(out var value) ? value : defaultValue;
        }

        /// <summary>
        ///     Returns a string representation of the MessageContext with CommandType and ConnectionId
        /// </summary>
        public override string ToString()
        {
            return $"Context: {CommandType}, Connection: {ConnectionId}";
        }

        /// <summary>
        ///     Helper method to safely extract a value from a JToken property
        /// </summary>
        public static T SafeGetValue<T>(JToken token, T defaultValue = default)
        {
            if (token == null)
                return defaultValue;

            try
            {
                // Try direct conversion first
                var value = token.ToObject<T>();
                if (value != null)
                    return value;

                // Handle string conversions for primitive types
                var stringValue = token.ToString();
                if (string.IsNullOrEmpty(stringValue))
                    return defaultValue;

                var targetType = typeof(T);

                if (targetType == typeof(int))
                {
                    if (int.TryParse(stringValue, out var intValue))
                        return (T)(object)intValue;
                }
                else if (targetType == typeof(bool))
                {
                    if (bool.TryParse(stringValue, out var boolValue))
                        return (T)(object)boolValue;
                }
                else if (targetType == typeof(string))
                {
                    return (T)(object)stringValue;
                }
                else if (targetType.IsEnum)
                {
                    try
                    {
                        return (T)Enum.Parse(targetType, stringValue, true);
                    }
                    catch (ArgumentException)
                    {
                        // Invalid enum value - return default
                        return defaultValue;
                    }
                }

                return defaultValue;
            }
            catch (Exception)
            {
                // Safe extraction pattern: return default on any conversion failure
                return defaultValue;
            }
        }
    }
}
