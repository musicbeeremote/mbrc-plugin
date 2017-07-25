using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Settings.Dialog;
using MusicBeeRemote.Core.Settings.Dialog.BasePanel;
using MusicBeeRemote.Core.Settings.Dialog.PartyModePanel;
using StructureMap;

namespace MusicBeeRemote.Core.Windows
{
    class WindowManager : IWindowManager
    {
        private readonly IInvokeHandler _invokeHandler;
        private readonly IContainer _container;
        private ConfigurationPanel _window;
        private PartyModePanel _panel;

        public WindowManager(IInvokeHandler invokeHandler, IContainer container)
        {
            _invokeHandler = invokeHandler;
            _container = container;
        }

        public void DisplayInfoWindow()
        {
            _invokeHandler.Invoke(DisplayWindow);
        }

        public void DisplayPartyModeWindow()
        {
            if (_panel == null || !_panel.Visible)
            {
                _panel = _container.GetInstance<PartyModePanel>();
            }
            _panel.Show();
            _panel.Closed += (sender, args) => _panel = null;
        }

        private void DisplayWindow()
        {
            if (_window == null || !_window.Visible)
            {
                _window = _container.GetInstance<ConfigurationPanel>();                
            }

            _window.Show();
            _window.Closed += (sender, args) => _window = null;
        }
    }
}