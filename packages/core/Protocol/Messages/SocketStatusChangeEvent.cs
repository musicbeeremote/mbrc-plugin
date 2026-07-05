namespace MusicBeePlugin.Protocol.Messages
{
    /// <summary>
    ///     Event published when the socket server status changes.
    /// </summary>
    public class SocketStatusChangeEvent
    {
        /// <summary>
        ///     Initializes a new instance of the SocketStatusChangeEvent.
        /// </summary>
        /// <param name="isRunning">True if the socket server is running, false otherwise</param>
        public SocketStatusChangeEvent(bool isRunning)
        {
            IsRunning = isRunning;
        }

        /// <summary>
        ///     Gets whether the socket server is currently running.
        /// </summary>
        public bool IsRunning { get; }

        /// <summary>
        ///     Creates a new SocketStatusChangeEvent instance.
        /// </summary>
        /// <param name="isRunning">True if the socket server is running, false otherwise</param>
        /// <returns>A new SocketStatusChangeEvent instance</returns>
        public static SocketStatusChangeEvent Create(bool isRunning)
        {
            return new SocketStatusChangeEvent(isRunning);
        }
    }
}
