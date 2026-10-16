using System;
using System.IO;
using System.Xml.Linq;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SecureAuth.Migrations;

/// <summary>
///     Migration runner
/// </summary>
public static class Migrations
{
    /// <summary>
    ///     Runs the migration chain for a config
    /// </summary>
    /// <param name="config">The old config</param>
    /// <param name="version">The version of the old config</param>
    public static PluginConfiguration Run(object config, int version)
    {
        while (version < PluginConfiguration.CurrentVersion)
        {
            config = version switch
            {
                0 => Migrate0To1.ToV1((PluginConfiguration0)config),
                _ => throw new InvalidOperationException($"No migration registered from config version {version}."),
            };
            version++;
        }

        var result = (PluginConfiguration)config;
        result.ConfigVersion = PluginConfiguration.CurrentVersion;
        return result;
    }

    /// <summary>Read config ver from the xml</summary>
    public static int PeekVersion(string path)
    {
        using FileStream stream = File.OpenRead(path);
        XDocument document = XDocument.Load(stream);
        return (int?)document.Root?.Element("ConfigVersion") ?? 0;
    }

    /// <summary>Deserializes a file into the snapshot type matching its version.</summary>
    public static object DeserializeAs(IXmlSerializer xmlSerializer, int version, string path)
    {
        return version switch
        {
            0 => xmlSerializer.DeserializeFromFile(typeof(PluginConfiguration0), path),
            _ => throw new InvalidOperationException($"No snapshot type registered for config version {version}."),
        };
    }
}