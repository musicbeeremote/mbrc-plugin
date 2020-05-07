using System.Windows.Forms;
using MusicBeeRemote.Core.ApiAdapters;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin.ApiAdapters
{
    /// <inheritdoc/>
    public class InvokeHandler : IInvokeHandler
    {
        private readonly MusicBeeApiInterface _api;

        /// <summary>
        /// Initializes a new instance of the <see cref="InvokeHandler"/> class.
        /// </summary>
        /// <param name="api">The MusicBee API.</param>
        public InvokeHandler(MusicBeeApiInterface api)
        {
            _api = api;
        }

        /// <inheritdoc/>
        public void Invoke(MethodInvoker invoker)
        {
            var hwnd = _api.MB_GetWindowHandle();
            var mb = (Form)Control.FromHandle(hwnd);
            mb.Invoke(invoker);
        }
    }
}
