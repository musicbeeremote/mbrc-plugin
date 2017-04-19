using System.Windows.Controls;

namespace MusicBeeRemote.Core.Settings.Dialog
{
    /// <summary>
    /// Interaction logic for AddressWhitelist.xaml
    /// </summary>
    public partial class AddressWhitelist : UserControl
    {
        public AddressWhitelist(AddressWhitelistViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
