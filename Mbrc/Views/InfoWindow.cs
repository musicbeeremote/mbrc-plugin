using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;
using MusicBeePlugin.Tools;

namespace MusicBeePlugin.Mbrc.Views
{
    public partial class InfoWindow : Form
    {
        /// <summary>
        /// 
        /// </summary>
        public InfoWindow()
        {
            InitializeComponent();
        }

        private void HelpButtonClick(object sender, EventArgs e)
        {
            Process.Start("http://kelsos.net/musicbeeremote/#help");
        }

        private void InfoWindowLoad(object sender, EventArgs e)
        {
           Version v = Assembly.GetExecutingAssembly().GetName().Version;
            internalIPList.DataSource = NetworkTools.GetPrivateAddressList();
            versionLabel.Text = v.ToString();
        }
    }
}
