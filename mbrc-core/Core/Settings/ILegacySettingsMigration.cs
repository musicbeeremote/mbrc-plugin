namespace MusicBeeRemoteCore.Core.Settings
{
    public interface ILegacySettingsMigration
    {
        bool MigrateLegacySettings(UserSettingsModel model);
    }
}