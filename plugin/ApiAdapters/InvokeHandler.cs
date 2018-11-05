using System.Windows.Forms;
using MusicBeeRemote.Core.ApiAdapters;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin.ApiAdapters
{
    class InvokeHandler : IInvokeHandler
    {
        private readonly MusicBeeApiInterface _api;

        public InvokeHandler(MusicBeeApiInterface api)
        {
            _api = api;
        }

        public void Invoke(MethodInvoker function)
        {
            var hwnd = _api.MB_GetWindowHandle();
            var mb = (Form) Control.FromHandle(hwnd);
            mb.Invoke(function);
        }
    }
}