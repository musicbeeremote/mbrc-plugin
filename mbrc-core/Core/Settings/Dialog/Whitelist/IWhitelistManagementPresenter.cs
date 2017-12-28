namespace MusicBeeRemote.Core.Settings.Dialog.Whitelist
{
    public interface IWhitelistManagementPresenter
    {
        void Load();
        void Attach(IWhitelistManagementView view);
        void AddAddress(string ipAddress);
        void RemoveAddress(string ipAddress);
    }
}