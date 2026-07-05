using MusicBeePlugin.Commands.Contracts;

namespace MusicBeePlugin.Commands.Infrastructure
{
    /// <summary>
    ///     Wraps an ICommandContext and provides strongly-typed access to request data.
    ///     Handles deserialization and validation automatically.
    /// </summary>
    /// <typeparam name="TRequest">The expected request type</typeparam>
    internal sealed class TypedCommandContext<TRequest> : ITypedCommandContext<TRequest>
    {
        private readonly ICommandContext _innerContext;
        private readonly TRequest _typedData;
        private readonly bool _deserializationSucceeded;

        public TypedCommandContext(ICommandContext innerContext)
        {
            _innerContext = innerContext;

            // Attempt to deserialize the request data
            _deserializationSucceeded = innerContext.TryGetData<TRequest>(out var data);
            _typedData = data;
        }

        /// <inheritdoc />
        public string CommandType => _innerContext.CommandType;

        /// <inheritdoc />
        public object Data => _innerContext.Data;

        /// <inheritdoc />
        public string ConnectionId => _innerContext.ConnectionId;

        /// <inheritdoc />
        public string ShortId => _innerContext.ShortId;

        /// <inheritdoc />
        public TRequest TypedData => _typedData;

        /// <inheritdoc />
        public bool IsValid
        {
            get
            {
                // If deserialization failed, not valid
                if (!_deserializationSucceeded || _typedData == null)
                    return false;

                // If the request implements IValidatable, use its validation
                if (_typedData is IValidatable validatable)
                    return validatable.IsValid;

                // Otherwise, consider valid if we successfully deserialized non-null data
                return true;
            }
        }

        /// <inheritdoc />
        public bool TryGetData<T>(out T value)
        {
            return _innerContext.TryGetData(out value);
        }

        /// <inheritdoc />
        public T GetDataOrDefault<T>(T defaultValue = default)
        {
            return _innerContext.GetDataOrDefault(defaultValue);
        }
    }
}
