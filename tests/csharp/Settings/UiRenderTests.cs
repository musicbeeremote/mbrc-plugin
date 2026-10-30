using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using AwesomeAssertions;
using MusicBeePlugin;
using MusicBeePlugin.Ffi;
using MusicBeePlugin.Host;
using MusicBeePlugin.Settings;
using Xunit;

namespace MusicBeeRemote.Core.Tests.Settings
{
    /// <summary>
    ///     Renders every settings page and setup step to PNG, so the UI can be looked
    ///     at without MusicBee, and fails when a page outgrows its window.
    /// </summary>
    /// <remarks>
    ///     Explicit, so an ordinary run skips them; <c>tools/render-ui.ps1</c> runs
    ///     them. The host's core never starts, so live values read as unavailable.
    ///     <c>MBRC_RENDER_DIR</c> picks the folder and <c>MBRC_RENDER_SCALE</c> the
    ///     font scale, which drives the same autoscaling a high-DPI display does.
    /// </remarks>
    public class UiRenderTests
    {
        [Fact(Explicit = true)]
        public void SettingsPages_RenderWithoutScrolling()
        {
            OnStaThread(host =>
            {
                using (var dialog = Shown(new SettingsDialog(host, "2.0.0")))
                {
                    foreach (var page in SettingsPages.All.Select(p => p.Page))
                    {
                        dialog.ShowPage(page);
                        Settle();
                        Save(dialog, "settings-" + Slug(page.ToString()));
                        AssertNoPageScrolls(dialog);
                    }

                    dialog.ShowPage(SettingsPage.Access);
                    var filter = Find<ComboBox>(dialog, c => c.Items.Contains("Specific addresses"));
                    foreach (var mode in new[] { "Address range", "Specific addresses" })
                    {
                        filter.SelectedItem = mode;
                        Settle();
                        Save(dialog, "settings-access-" + Slug(mode));
                        AssertNoPageScrolls(dialog);
                    }
                }
            });
        }

        [Fact(Explicit = true)]
        public void SetupSteps_RenderWithoutScrolling()
        {
            OnStaThread(host =>
            {
                var status = new SetupStatus { needed = true, upgrade = true };
                using (var wizard = Shown(new SetupWizard(host, status)))
                {
                    for (var step = 0; step < wizard.StepCount; step++)
                    {
                        wizard.ShowStep(step);
                        Settle();
                        WaitForFirewallCheck(wizard);
                        Save(wizard, "setup-" + step.ToString(CultureInfo.InvariantCulture));
                        AssertNoPageScrolls(wizard);
                    }

                    wizard.ShowStep(1);
                    Settle();
                    wizard.ShowFirewall(new FirewallStatus { rule = "missing", firewall = "on", public_network = "on" });
                    Settle();
                    Save(wizard, "setup-1-public-network");
                    var next = Find<Button>(wizard, b => b.Text == "Next");
                    next.Enabled.Should().BeFalse("a Public network has to be confirmed first");
                }
            });
        }

        private static string OutputDir
        {
            get
            {
                var dir = Environment.GetEnvironmentVariable("MBRC_RENDER_DIR");
                if (string.IsNullOrEmpty(dir))
                    dir = Path.Combine(AppContext.BaseDirectory, "ui-renders");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static float Scale =>
            float.TryParse(Environment.GetEnvironmentVariable("MBRC_RENDER_SCALE"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var scale) && scale > 0
                ? scale
                : 1f;

        /// <summary>WinForms needs an STA thread; the test runner's are MTA.</summary>
        private static void OnStaThread(Action<PluginHost> render)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    // MusicBee draws with visual styles; without them controls render in the flat classic look.
                    Application.EnableVisualStyles();
                    var storage = Path.Combine(Path.GetTempPath(), "mbrc-render-storage");
                    Directory.CreateDirectory(storage);
                    using (var host = new PluginHost(new Plugin.MusicBeeApiInterface(), storage, "2.0.0"))
                    {
                        render(host);
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null)
                throw new InvalidOperationException("Rendering failed", failure);
        }

        /// <summary>Show <paramref name="form" /> off-screen at the requested scale.</summary>
        private static T Shown<T>(T form) where T : Form
        {
            if (Math.Abs(Scale - 1f) > 0.01f)
                form.Font = new Font(form.Font.FontFamily, form.Font.Size * Scale);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-10000, 0);
            form.Show();
            Settle();
            return form;
        }

        /// <summary>
        ///     Wait out the firewall step's background check, so its reply cannot land
        ///     after a status the test sets by hand.
        /// </summary>
        private static void WaitForFirewallCheck(Form wizard)
        {
            var recheck = Find<Button>(wizard, b => b.Text == "Check again");
            for (var i = 0; i < 300 && !recheck.Enabled; i++)
                Settle();
        }

        /// <summary>Let layout and background replies land: the firewall check answers through BeginInvoke.</summary>
        private static void Settle()
        {
            for (var i = 0; i < 20; i++)
            {
                Application.DoEvents();
                Thread.Sleep(25);
            }
        }

        private static void Save(Form form, string name)
        {
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                var suffix = Math.Abs(Scale - 1f) > 0.01f
                    ? "-x" + Scale.ToString("0.##", CultureInfo.InvariantCulture)
                    : string.Empty;
                bitmap.Save(Path.Combine(OutputDir, name + suffix + ".png"), ImageFormat.Png);
            }
        }

        /// <summary>The visible page must fit: a scrollbar means it outgrew the window.</summary>
        private static void AssertNoPageScrolls(Form form)
        {
            var page = Descendants(form).OfType<ScrollableControl>()
                .FirstOrDefault(c => c.AutoScroll && c.Visible);
            if (page == null)
                return;
            page.VerticalScroll.Visible.Should().BeFalse("no page should scroll at scale {0}", Scale);
            page.HorizontalScroll.Visible.Should().BeFalse("no page should scroll sideways at scale {0}", Scale);
        }

        private static T Find<T>(Control root, Func<T, bool> match) where T : Control =>
            Descendants(root).OfType<T>().First(match);

        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var grandchild in Descendants(child))
                    yield return grandchild;
            }
        }

        private static string Slug(string text) =>
            new string(text.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray()).Trim('-');
    }
}
