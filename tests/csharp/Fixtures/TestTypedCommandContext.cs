using MusicBeePlugin.Commands.Contracts;

namespace MusicBeeRemote.Core.Tests.Fixtures
{
    /// <summary>
    ///     Test implementation of ITypedCommandContext for unit tests.
    /// </summary>
    public class TestTypedCommandContext<TRequest> : ITypedCommandContext<TRequest>
    {
        private readonly ICommandContext _innerContext;
        private readonly TRequest _typedData;
        private readonly bool _deserializationSucceeded;

        public TestTypedCommandContext(string commandType, TRequest typedData, string connectionId = "test-connection")
        {
            CommandType = commandType;
            _typedData = typedData;
            ConnectionId = connectionId;
            Data = typedData;
            _deserializationSucceeded = typedData != null;
        }

        public TestTypedCommandContext(ICommandContext innerContext)
        {
            _innerContext = innerContext;
            CommandType = innerContext.CommandType;
            Data = innerContext.Data;
            ConnectionId = innerContext.ConnectionId;
            _deserializationSucceeded = innerContext.TryGetData<TRequest>(out var data);
            _typedData = data;
        }

        public string CommandType { get; }
        public object Data { get; }
        public string ConnectionId { get; }
        public string ShortId => _innerContext?.ShortId ?? (ConnectionId?.Length > 6 ? ConnectionId.Substring(0, 6) : ConnectionId ?? "?");
        public TRequest TypedData => _typedData;

        public bool IsValid
        {
            get
            {
                if (!_deserializationSucceeded || _typedData == null)
                    return false;

                if (_typedData is IValidatable validatable)
                    return validatable.IsValid;

                return true;
            }
        }

        public bool TryGetData<T>(out T value)
        {
            if (_innerContext != null)
                return _innerContext.TryGetData(out value);

            value = default;
            if (Data is T typedData)
            {
                value = typedData;
                return true;
            }

            return false;
        }

        public T GetDataOrDefault<T>(T defaultValue = default)
        {
            if (_innerContext != null)
                return _innerContext.GetDataOrDefault(defaultValue);

            return TryGetData<T>(out var value) ? value : defaultValue;
        }
    }
}
