using System;

namespace MusicBeePlugin.Services.Core
{
    /// <summary>
    ///     Late-binding wrapper for <see cref="INativeBridge"/>. The
    ///     bridge is constructed by the Plugin host *after* the DI
    ///     container is built (it depends on data providers the
    ///     container itself supplies), so it can't be auto-resolved.
    ///     This holder is registered up-front; the host populates it
    ///     once construction completes, and consumers (InfoWindow,
    ///     CoreRestartRequested subscriber, …) resolve through it.
    /// </summary>
    public sealed class NativeBridgeHolder : INativeBridge
    {
        private INativeBridge _bridge;

        public void Set(INativeBridge bridge)
        {
            _bridge = bridge;
        }

        public bool IsNetworkingRunning => _bridge?.IsNetworkingRunning ?? false;

        public bool VerifyConnection() => _bridge?.VerifyConnection() ?? false;
    }
}
