using System;
using System.Windows.Forms;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Services.Core;
using MusicBeePlugin.Services.Media;
using MusicBeePlugin.UI.Windows;

namespace MusicBeePlugin.Services.UI
{
    public class WindowManager : IWindowManager, InfoWindow.IOnInvalidateCacheListener,
        InfoWindow.IOnDebugSelectionChanged
    {
        private readonly ICoverService _coverService;
        private readonly Func<IInfoWindow> _infoWindowFactory;
        private readonly IPluginCore _pluginCore;
        private readonly ISystemOperations _systemAdapter;
        private IInfoWindow _mWindow;

        public WindowManager(
            ISystemOperations systemAdapter,
            ICoverService coverService,
            Func<IInfoWindow> infoWindowFactory,
            IPluginCore pluginCore)
        {
            _systemAdapter = systemAdapter;
            _coverService = coverService;
            _infoWindowFactory = infoWindowFactory;
            _pluginCore = pluginCore;
        }

        public void SelectionChanged(bool enabled)
        {
            _pluginCore.SetLogging(enabled);
        }

        public void InvalidateCache()
        {
            _coverService.InvalidateCache();
        }

        public void UpdateWindowStatus(bool status)
        {
            if (_mWindow != null && _mWindow.Visible)
                _mWindow.UpdateSocketStatus(status);
        }


        public void OpenInfoWindow()
        {
            var hwnd = _systemAdapter.GetWindowHandle();
            var mb = (Form)Control.FromHandle(hwnd);
            mb.Invoke(new MethodInvoker(DisplayInfoWindow));
        }

        private void DisplayInfoWindow()
        {
            if (_mWindow == null || !_mWindow.Visible)
            {
                _mWindow = _infoWindowFactory();

                // Set up listeners if InfoWindow implements the interfaces
                if (_mWindow is InfoWindow concreteWindow)
                {
                    concreteWindow.SetOnDebugSelectionListener(this);
                    concreteWindow.SetOnInvalidateCacheListener(this);
                }
            }

            _mWindow.Show();
        }
    }
}
