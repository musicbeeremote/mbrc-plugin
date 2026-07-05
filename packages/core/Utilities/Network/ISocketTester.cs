namespace MusicBeePlugin.Utilities.Network
{
    /// <summary>
    ///     Interface for receiving connection test results.
    /// </summary>
    public interface IConnectionListener
    {
        /// <summary>
        ///     Called when connection test completes.
        /// </summary>
        /// <param name="isConnected">True if connection was successful</param>
        void OnConnectionResult(bool isConnected);
    }

    /// <summary>
    ///     Interface for testing socket connections.
    /// </summary>
    public interface ISocketTester
    {
        /// <summary>
        ///     Sets the connection listener for test results.
        /// </summary>
        IConnectionListener ConnectionListener { get; set; }

        /// <summary>
        ///     Starts verification of the socket connection.
        /// </summary>
        void VerifyConnection();
    }
}
