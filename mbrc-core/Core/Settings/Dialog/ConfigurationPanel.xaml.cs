using System.Windows;

namespace MusicBeeRemote.Core.Settings.Dialog
{
    /// <summary>
    /// Interaction logic for ConfigurationPanel.xaml
    /// </summary>
    public partial class ConfigurationPanel : Window
    {
        public ConfigurationPanel(ConfigurationPanelViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}