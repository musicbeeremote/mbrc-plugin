using System;
using System.Collections.Generic;
using System.Windows.Forms;
using MusicBeePlugin.AndroidRemote.Events;
using MusicBeePlugin.AndroidRemote.Model;
using MusicBeePlugin.AndroidRemote.Settings;
using MusicBeePlugin.Tools;

namespace MusicBeePlugin
{
    internal class SettingsController : IDisposable
    {
        
        public static SettingsModel SettingsModel { get; private set; }
        private SettingsPanel _sPanel;

        public bool ConfigureSettingsPanel(IntPtr panelHandle, int background, int foreground)
        {
            if (panelHandle == IntPtr.Zero)
                return false;

            Panel panel = (Panel) Control.FromHandle(panelHandle);
            // SettingsModel Loaded
            SettingsModel = UserSettings.SettingsModel;

            List<string> list = NetworkTools.GetPrivateAddressList();
        
            _sPanel = new SettingsPanel();
           // _sPanel.SelectionChanged += HandleSettingsPanelSelectionChanged;
           // _sPanel.RangeChanged += HandleSettingsPanelRangeChanged;
           // _sPanel.AddressAdded += HandleSettingsPanelAddressAdded;
           // _sPanel.AddressRemoved += HandleSettingsPanelAddressRemoved;
           // _sPanel.PortChanged += HandleSettingsPanelPortChanged;

            _sPanel.UpdateFilteringSelection(SettingsModel.FilterSelection);
            _sPanel.UpdatePortNumber(SettingsModel.ListeningPort);
            _sPanel.UpdateValues(SettingsModel.GetValues());    

            panel.Controls.Add(_sPanel);
            return false;
        }

        //private void HandleSettingsPanelPortChanged(object sender, MessageEventArgs e)
        //{
        //    SettingsModel.ListeningPort = int.Parse(e.Message);
        //}

        //private void HandleSettingsPanelAddressRemoved(object sender, MessageEventArgs e)
        //{
        //    SettingsModel.IpAddressList.Remove(e.Message);
        //    _sPanel.UpdateValues(SettingsModel.GetValues());
        //}

        //private void HandleSettingsPanelAddressAdded(object sender, MessageEventArgs e)
        //{
        //    SettingsModel.IpAddressList.Add(e.Message);
        //    _sPanel.UpdateValues(SettingsModel.GetValues());
        //}

        //private void HandleSettingsPanelRangeChanged(object sender, MessageEventArgs e)
        //{
        //    SettingsModel.UnFlattenAllowedRange(e.Message);
        //}

        //private void HandleSettingsPanelSelectionChanged(object sender, MessageEventArgs e)
        //{
        //    switch (e.Message)
        //    {
        //        case "0":
        //            SettingsModel.FilterSelection = FilteringSelection.All;
        //            break;
        //        case "1":
        //            SettingsModel.FilterSelection = FilteringSelection.Range;
        //            break;
        //        case "2":
        //            SettingsModel.FilterSelection = FilteringSelection.Specific;
        //            break;
        //    }
        //}

        public void Dispose()
        {
        }
    }
}