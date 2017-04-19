using System;
using System.Windows;
using System.Windows.Controls;

namespace MusicBeeRemote.Core.Settings.Dialog
{
    /// <summary>
    /// Interaction logic for ConfigurationPanel.xaml
    /// </summary>
    public partial class ConfigurationPanel : Window
    {
        private readonly AddressWhitelist _whitelistControl;
        private readonly RangeFilter _rangeFilterControl;

        public ConfigurationPanel(ConfigurationPanelViewModel viewModel, AddressWhitelist whitelistControl, RangeFilter rangeFilterControl)
        {
            _whitelistControl = whitelistControl;
            _rangeFilterControl = rangeFilterControl;
            InitializeComponent();
            DataContext = viewModel;
        }

        private void SelectionFilteringComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FilteringControl.Children.Clear();
            var addedItems = e.AddedItems;

            if (addedItems.Count <= 0)
            {
                return;
            }

            var selection = addedItems[0] as FilteringSelection?;
            switch (selection)
            {                    
                case FilteringSelection.Range:
                    _rangeFilterControl.SetValue(Grid.ColumnProperty, 2);
                    FilteringControl.Children.Add(_rangeFilterControl);
                    break;
                case FilteringSelection.Specific:
                    _whitelistControl.SetValue(Grid.ColumnProperty, 2);
                    FilteringControl.Children.Add(_whitelistControl);
                    break;                    
                default:
                    // Do nothing on default, there is no user control to display and anything that existed
                    // is already removed from the grid children.
                    break;
            }
        }
    }
}