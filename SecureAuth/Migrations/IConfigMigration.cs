namespace Jellyfin.Plugin.SecureAuth.Migrations;

/// <summary>
///     migration tool for the legacy config
/// </summary>
public interface IConfigMigration
{
    /// <summary>
    ///     Migrate config to a newer version
    /// </summary>
    void Migrate();
}