using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using MediaBrowser.Model.Plugins;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Jellyfin.Plugin.SecureAuth.Migrations;

[XmlRoot("PluginConfiguration")]
public class PluginConfiguration0 : BasePluginConfiguration
{
    private const int CurrentVersion = 0;

    [XmlElement("Configs")]
    public SerializableDictionary<string, Config0> Configs { get; set; } =
        new SerializableDictionary<string, Config0>();

    public bool HasMigrated { get; set; }

    public bool AllowPrivateAvatarIp { get; set; }

    public bool DisableAvatarSsrfCheck { get; set; }
}

[XmlRoot("PluginConfiguration")]
public class Config0
{
    public string Endpoint { get; set; }

    public string ClientId { get; set; }

    public string Secret { get; set; }

    public bool Enabled { get; set; }

    public string RoleClaim { get; set; }

    public string DefaultUsernameClaim { get; set; }

    public string AvatarClaim { get; set; }

    public bool EnableUserProvisioning { get; set; }

    public bool UpdateUsersOnLogin { get; set; }

    public string[] Roles { get; set; }

    public string[] AdminRoles { get; set; }

    public string[] AutoLinkingAllowList { get; set; }

    public bool EnableAllFolders { get; set; }

    public string[] EnabledFolders { get; set; }

    public bool EnableFolderRoles { get; set; }

    [XmlArray("FolderRoleMappings")]
    [XmlArrayItem(typeof(FolderRoleMap0), ElementName = "FolderRoleMappings")]
    public List<FolderRoleMap0> FolderRoleMapping { get; set; }

    public bool EnableLiveTv { get; set; }

    public bool EnableLiveTvManagement { get; set; }

    public bool EnableLiveTvRoles { get; set; }

    public string[] LiveTvRoles { get; set; }

    public string[] LiveTvManagementRoles { get; set; }

    public string[] Scopes { get; set; }

    public string DefaultAuthProvider { get; set; }

    public bool DisableHttps { get; set; }

    public bool DisablePushedAuthorization { get; set; }

    public bool DoNotValidateEndpoints { get; set; }

    public bool DoNotValidateIssuerName { get; set; }

    public bool UseHTTP { get; set; }

    public bool DoNotLoadProfile { get; set; }

    public int? PortOverride { get; set; }

    [XmlElement("Links")]
    public SerializableDictionary<string, Guid> Links
    {
        get => field ?? new SerializableDictionary<string, Guid>();
        set;
    }
}

public class FolderRoleMap0
{
    public string Role { get; set; }

    public List<string> Folders { get; set; }
}