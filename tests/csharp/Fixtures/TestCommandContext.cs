using System;
using MusicBeePlugin.Commands.Contracts;
using Newtonsoft.Json.Linq;

namespace MusicBeeRemote.Core.Tests.Fixtures
{
    public class TestCommandContext : ICommandContext
    {
        public TestCommandContext(string commandType, object data = null, string connectionId = "test-connection")
        {
            CommandType = commandType;
            Data = data;
            ConnectionId = connectionId;
        }

        public string CommandType { get; }
        public object Data { get; }
        public string ConnectionId { get; }
        public string ShortId => ConnectionId?.Length > 6 ? ConnectionId.Substring(0, 6) : ConnectionId ?? "?";

        public bool TryGetData<T>(out T value)
        {
            value = default;

            try
            {
                if (Data is T typedData)
                {
                    value = typedData;
                    return true;
                }

                if (Data is JToken jToken)
                {
                    value = jToken.ToObject<T>();
                    return true;
                }

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
                            return false;
                        }
                    }
                }

                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public T GetDataOrDefault<T>(T defaultValue = default)
        {
            return TryGetData<T>(out var value) ? value : defaultValue;
        }
    }
}
