namespace MusicBeeRemote.Core.Settings.Dialog.Range
{
    public interface IRangeManagementView
    {
        void Update(string baseIp, uint lastOctet);
    }

    public interface IRangeManagementPresenter
    {
        void Attach(IRangeManagementView view);
        void Load();
    }

    class RangeManagementPresenter : IRangeManagementPresenter
    {
        private readonly RangeManagementViewModel _viewModel;
        private IRangeManagementView _view;

        public RangeManagementPresenter(RangeManagementViewModel viewModel)
        {
            _viewModel = viewModel;
        }

        public void Attach(IRangeManagementView view)
        {
            _view = view;
        }

        public void Load()
        {
            _view.Update(_viewModel.BaseIp, _viewModel.LastOctetMax);
        }
    }
}