using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SecureAuth;

/// <summary>
///     Plugin Configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    ///     The current version of the configuration.
    /// </summary>
    public const int CurrentVersion = 2;

    /// <summary>
    ///     Gets or sets whether this instance has been migrated from 9p4's plugin.
    /// </summary>
    public bool HasMigratedFrom9P4 { get; set; }

    /// <summary>
    ///     The OIDC configuration.
    /// </summary>
    public OpenIdConnect OpenIdConnect { get; set; } = new OpenIdConnect();

    /// <summary>
    ///     The QR configuration.
    /// </summary>
    public QrConfig Qr { get; set; } = new QrConfig();

    /// <summary>
    ///     The version of the configuration.
    /// </summary>
    public int ConfigVersion { get; set; } = CurrentVersion;
}

/// <summary>
///     The QR configuration.
/// </summary>
[XmlRoot("QR")]
public class QrConfig
{
    /// <summary>
    ///     Whether to enable QR code authentication.
    /// </summary>
    public bool Enabled { get; set; }
}

/// <summary>
///     The OIDC configuration.
/// </summary>
[XmlRoot("OpenIdConnect")]
public class OpenIdConnect
{
    /// <summary>
    ///     Whether to enable OIDC.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    ///     Gets or sets the provider configurations available.
    /// </summary>
    [XmlElement("Providers")]
    public SerializableDictionary<string, OidcProvider> Providers { get; set; } =
        new SerializableDictionary<string, OidcProvider>();

    /// <summary>
    ///     Whether to allow private IP addresses for avatars.
    /// </summary>
    public bool AllowPrivateAvatarIp { get; set; }

    /// <summary>
    ///     Whether to disable SSRF protection for avatar IP addresses.
    /// </summary>
    public bool DisableAvatarSsrfCheck { get; set; }
}

/// <summary>
///     The configuration required for a OpenID provider.
/// </summary>
[XmlRoot("OidcProvider")]
public class OidcProvider
{
    /// <summary>
    ///     Gets or sets the OpenID well-known information endpoint.
    /// </summary>
    public string Endpoint { get; set; }

    /// <summary>
    ///     Gets or sets OpenID client ID.
    /// </summary>
    public string ClientId { get; set; }

    /// <summary>
    ///     Gets or sets OpenID shared secret.
    /// </summary>
    public string Secret { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the provider is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    ///     Gets or sets the claim to check roles against. Separated by "."s.
    /// </summary>
    public string RoleClaim { get; set; }

    /// <summary>
    ///     Gets or sets the default username claim when creating new accounts.
    /// </summary>
    public string DefaultUsernameClaim { get; set; }

    /// <summary>
    ///     Gets or sets the claim containing the profile picture for the user.
    /// </summary>
    public string AvatarClaim { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the IdP will autoprovision users.
    /// </summary>
    public bool EnableUserProvisioning { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the IdP will autoupdate users.
    /// </summary>
    public bool UpdateUsersOnLogin { get; set; }

    /// <summary>
    ///     Gets or sets what roles are checked to determine whether the user is allowed to use Jellyfin.
    /// </summary>
    public string[] Roles { get; set; }

    /// <summary>
    ///     Gets or sets the roles that are checked to determine whether the user is an administrator.
    /// </summary>
    public string[] AdminRoles { get; set; }

    /// <summary>
    ///     Gets or sets the list of users that IdP users can be linked to.
    /// </summary>
    public string[] AutoLinkingAllowList { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether all folders are allowed by default.
    /// </summary>
    public bool EnableAllFolders { get; set; }

    /// <summary>
    ///     Gets or sets what folders should users have access to by default.
    /// </summary>
    public string[] EnabledFolders { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether RBAC is used to manage folder access.
    /// </summary>
    public bool EnableFolderRoles { get; set; }

    /// <summary>
    ///     Gets or sets which folders map to what roles in RBAC.
    /// </summary>
    [XmlArray("FolderRoleMappings")]
    [XmlArrayItem(typeof(OidcFolderRoleMap), ElementName = "FolderRoleMappings")]
    public List<OidcFolderRoleMap> FolderRoleMapping { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether Live TV is enabled by default.
    /// </summary>
    public bool EnableLiveTv { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether Live TV is allowed to be managed by default.
    /// </summary>
    public bool EnableLiveTvManagement { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether RBAC is used to manage Live TV access.
    /// </summary>
    public bool EnableLiveTvRoles { get; set; }

    /// <summary>
    ///     Gets or sets the roles that are checked to determine whether the user is allowed to view Live TV.
    /// </summary>
    public string[] LiveTvRoles { get; set; }

    /// <summary>
    ///     Gets or sets the roles that are checked to determine whether the user is allowed to manage Live TV.
    /// </summary>
    public string[] LiveTvManagementRoles { get; set; }

    /// <summary>
    ///     Gets or Sets additional Scopes to request access to in the authorization request.
    /// </summary>
    public string[] Scopes { get; set; }

    /// <summary>
    ///     Gets or Sets which authentication to use when creating new users.
    /// </summary>
    public string DefaultAuthProvider { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether HTTPS in the discovery endpoint is required.
    /// </summary>
    public bool DisableHttps { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether pushed authorization is required.
    /// </summary>
    public bool DisablePushedAuthorization { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the OpenID endpoints are validated.
    /// </summary>
    public bool DoNotValidateEndpoints { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the OpenID issuer name is validated.
    /// </summary>
    public bool DoNotValidateIssuerName { get; set; }

    /// <summary>
    ///     Gets or sets the whether http is used for redirect urls.
    /// </summary>
    public bool UseHttp { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the UserInfo endpoint is used to get profile data.
    /// </summary>
    public bool DoNotLoadProfile { get; set; }

    /// <summary>
    ///     Gets or sets the redirect port override.
    /// </summary>
    public int? PortOverride { get; set; }

    /// <summary>
    ///     Gets or sets a mapping of canonical names from the provider to jellyfin user ids.
    /// </summary>
    [XmlElement("Links")]
    public SerializableDictionary<string, Guid> Links
    {
        get => field ?? new SerializableDictionary<string, Guid>();
        set;
    }
}

/// <summary>
///     The OpenID client ID.
/// </summary>
public class OidcFolderRoleMap
{
    /// <summary>
    ///     Gets or sets the role of the mapping.
    /// </summary>
    public string Role { get; set; }

    /// <summary>
    ///     Gets or sets the folders that are allowed from the given role.
    /// </summary>
    public List<string> Folders { get; set; }
}