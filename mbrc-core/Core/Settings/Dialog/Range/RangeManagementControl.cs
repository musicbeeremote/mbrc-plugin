using System.Windows.Forms;
using MusicBeeRemote.Core.Settings.Dialog.BasePanel;

namespace MusicBeeRemote.Core.Settings.Dialog.Range
{
    public partial class RangeManagementControl : UserControl
    {
        private readonly ConfigurationPanelViewModel _viewModel;

        public RangeManagementControl(ConfigurationPanelViewModel viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();
        }
    }
}
