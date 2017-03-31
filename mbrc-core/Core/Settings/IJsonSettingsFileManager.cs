namespace MusicBeeRemoteCore.Core.Settings
{
    public interface IJsonSettingsFileManager
    {
        void Save(UserSettingsModel model);

        UserSettingsModel Load();
    }
}