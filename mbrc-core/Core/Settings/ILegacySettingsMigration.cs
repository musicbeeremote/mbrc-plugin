namespace MusicBeeRemote.Core.Settings
{
    public interface ILegacySettingsMigration
    {
        bool MigrateLegacySettings(UserSettingsModel model);
    }
}