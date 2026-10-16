using System;
using System.IO;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SecureAuth.Migrations;

/// <inheritdoc />
public class ConfigMigration(
    IApplicationPaths applicationPaths,
    IXmlSerializer xmlSerializer,
    ILogger<ConfigMigration> logger) : IConfigMigration
{
    private const string LegacyFileName = "openid-connect.xml";

    /// <inheritdoc />
    public void Migrate()
    {
        MigrateLegacyFile();
        MigrateOwnFile();
    }

    private void MigrateLegacyFile()
    {
        string legacyPath = Path.Combine(applicationPaths.PluginConfigurationsPath, LegacyFileName);
        if (!File.Exists(legacyPath))
        {
            return;
        }

        try
        {
            if (xmlSerializer.DeserializeFromFile(typeof(PluginConfiguration0), legacyPath)
                is not PluginConfiguration0 legacy)
            {
                return;
            }

            PluginConfiguration current = Migrations.Run(legacy, 0);
            SecureAuth.Instance.UpdateConfiguration(current);

            File.Move(legacyPath, legacyPath + ".migrated", true);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to migrate legacy config from {File}", LegacyFileName);
        }
    }

    private void MigrateOwnFile()
    {
        string ownPath = SecureAuth.Instance.ConfigurationFilePath;
        if (!File.Exists(ownPath))
        {
            return;
        }

        try
        {
            int version = Migrations.PeekVersion(ownPath);

            switch (version)
            {
                case > PluginConfiguration.CurrentVersion:
                    logger.LogWarning(
                        "Config written by a newer plugin version: {FileVersion} > {CurrentVersion}.",
                        version,
                        PluginConfiguration.CurrentVersion);
                    return;
                case PluginConfiguration.CurrentVersion:
                    return;
            }

            object snapshot = Migrations.DeserializeAs(xmlSerializer, version, ownPath);
            PluginConfiguration current = Migrations.Run(snapshot, version);

            SecureAuth.Instance.UpdateConfiguration(current);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to migrate config from {File}", ownPath);
        }
    }
}