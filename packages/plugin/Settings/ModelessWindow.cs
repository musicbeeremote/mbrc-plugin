using System;
using System.Windows.Forms;
using MusicBeePlugin.Host;

namespace MusicBeePlugin.Settings
{
    /// <summary>
    ///     One modeless window of a kind, owned by MusicBee's window.
    /// </summary>
    /// <remarks>
    ///     Modeless because a modal dialog freezes MusicBee behind it, and these are
    ///     windows people leave open. That means tracking the instance, so a second
    ///     open raises the window already on screen rather than stacking another
    ///     with its own poll timer.
    /// </remarks>
    internal sealed class ModelessWindow<T> where T : Form
    {
        private T _current;

        /// <summary>Show a new window from <paramref name="create" />, or bring the open one to the front.</summary>
        public void Open(PluginHost host, Func<T> create)
        {
            if (host == null)
                return;

            if (_current != null && !_current.IsDisposed)
            {
                // Restore it if it went down with a minimized MusicBee, then focus it.
                if (_current.WindowState == FormWindowState.Minimized)
                    _current.WindowState = FormWindowState.Normal;
                _current.Activate();
                return;
            }

            var window = create();
            // Modeless forms dispose themselves on close, so the field is released
            // here or the next Open would find a dead window and show nothing.
            window.FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_current, s))
                    _current = null;
            };
            _current = window;

            // Owned by MusicBee's window so it stays above it and minimizes with it.
            var owner = host.MusicBeeWindow;
            if (owner != IntPtr.Zero)
            {
                window.Show(new HostWindow(owner));
            }
            else
            {
                // Nothing to sit above, so a taskbar button keeps it reachable.
                window.ShowInTaskbar = true;
                window.Show();
            }
        }

        /// <summary>
        ///     Close the window if it is open. Called as the plugin shuts down: the
        ///     window polls the core on a timer and would otherwise outlive the host
        ///     it reads through.
        /// </summary>
        public void CloseIfOpen()
        {
            var window = _current;
            _current = null;
            if (window == null || window.IsDisposed)
                return;
            try
            {
                window.Close();
            }
            catch (Exception)
            {
                // Shutting down anyway; a window that will not close is not worth
                // taking MusicBee's exit path down with it.
            }
        }

        /// <summary>
        ///     Wraps MusicBee's raw window handle as an owner for
        ///     <see cref="Form.Show(IWin32Window)" />. Not <c>Control.FromHandle</c>,
        ///     which resolves only this AppDomain's own controls.
        /// </summary>
        private sealed class HostWindow : IWin32Window
        {
            public HostWindow(IntPtr handle)
            {
                Handle = handle;
            }

            public IntPtr Handle { get; }
        }
    }
}
