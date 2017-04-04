using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Settings.Dialog;
using StructureMap;

namespace MusicBeeRemote.Core.Windows
{
    class WindowManager : IWindowManager
    {
        private readonly IInvokeHandler _invokeHandler;
        private readonly IContainer _container;
        private InfoWindow _window;

        public WindowManager(IInvokeHandler invokeHandler, IContainer container)
        {
            _invokeHandler = invokeHandler;
            _container = container;
        }

        public void DisplayInfoWindow()
        {
            _invokeHandler.Invoke(DisplayWindow);
        }

        private void DisplayWindow()
        {
            if (_window == null || !_window.Visible)
            {
                _window = _container.GetInstance<InfoWindow>();            
            }

            _window.Show();
        }
    }
}