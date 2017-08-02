namespace MusicBeeRemote.Core.Settings
{
    public interface IJsonSettingsFileManager
    {
        void Save(UserSettingsModel model);

        UserSettingsModel Load();

        void SaveLimitedModeSettingsModel(LimitedModeSettingsModel model);

        LimitedModeSettingsModel LoadLimitedModeSettingsModel();
    }
}