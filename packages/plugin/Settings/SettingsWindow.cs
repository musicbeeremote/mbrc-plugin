using System;
using System.Windows.Forms;
using MusicBeePlugin.Host;

namespace MusicBeePlugin.Settings
{
    /// <summary>
    ///     Owns the one settings window, opened from both the Configure button and
    ///     the Tools menu entry.
    ///
    /// </summary>
    /// <remarks>
    ///     Shown modeless: a modal dialog freezes MusicBee behind it, and this is
    ///     a window people leave open while they reproduce a problem, watch the
    ///     cache rebuild, or wait on an update.
    ///     <para>
    ///         That means tracking the instance, so a second Configure click
    ///         raises the window already on screen rather than stacking another
    ///         with its own subscription and poll timer.
    ///     </para>
    /// </remarks>
    internal static class SettingsWindow
    {
        private static SettingsDialog _current;

        /// <summary>
        ///     Show the settings window, or bring the open one to the front.
        ///     No-op if the host failed to start.
        /// </summary>
        public static void Open(PluginHost host, string version)
        {
            if (host == null)
                return;

            if (_current != null && !_current.IsDisposed)
            {
                // Already open: restore it if it went down with a minimized
                // MusicBee, then focus it.
                if (_current.WindowState == FormWindowState.Minimized)
                    _current.WindowState = FormWindowState.Normal;
                _current.Activate();
                return;
            }

            var dialog = new SettingsDialog(host, version);
            // Modeless forms dispose themselves on close, so the field has to be
            // released here or the next Open would find a dead window and never
            // show anything.
            dialog.FormClosed += (s, e) =>
            {
                if (ReferenceEquals(_current, s))
                    _current = null;
            };
            _current = dialog;

            // Owned by MusicBee's window so it stays above it and minimizes with
            // it, instead of drifting behind as an unrelated top-level window.
            var owner = host.MusicBeeWindow;
            if (owner != IntPtr.Zero)
            {
                dialog.Show(new HostWindow(owner));
            }
            else
            {
                // No owner to sit above, so give it a taskbar button - otherwise a
                // window hidden behind MusicBee would be unreachable.
                dialog.ShowInTaskbar = true;
                dialog.Show();
            }
        }

        /// <summary>
        ///     Close the window if it is open. Called as the plugin shuts down: the
        ///     window polls the core on a timer and would otherwise outlive the
        ///     host it reads through.
        /// </summary>
        public static void CloseIfOpen()
        {
            var dialog = _current;
            _current = null;
            if (dialog == null || dialog.IsDisposed)
                return;
            try
            {
                dialog.Close();
            }
            catch (Exception)
            {
                // Shutting down anyway; a window that will not close is not worth
                // taking MusicBee's exit path down with it.
            }
        }

        /// <summary>
        ///     Wraps MusicBee's raw window handle as an owner for
        ///     <see cref="Form.Show(IWin32Window)" />. Deliberately not
        ///     <c>Control.FromHandle</c>: that only resolves handles belonging to
        ///     this AppDomain's WinForms controls, and returns null otherwise.
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
