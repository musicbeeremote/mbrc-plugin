using System.Windows.Forms;

namespace MusicBeeRemote.Core.Settings.Dialog
{
    public partial class ConfigurationPanel : Form
    {
        private readonly ConfigurationPanelViewModel _viewModel;

        public ConfigurationPanel(ConfigurationPanelViewModel viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();
        }
    }
}
