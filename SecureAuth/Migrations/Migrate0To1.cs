using System.Collections.Generic;
using System.Linq;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Jellyfin.Plugin.SecureAuth.Migrations;

public static class Migrate0To1
{
    public static PluginConfiguration ToV1(PluginConfiguration0 old)
    {
        var config = new PluginConfiguration
        {
            AllowPrivateAvatarIp = old.AllowPrivateAvatarIp,
            ConfigVersion = 1,
            DisableAvatarSsrfCheck = old.DisableAvatarSsrfCheck,
            HasMigrated = old.HasMigrated,
        };

        foreach ((string name, Config0 oldProvider) in old.Configs)
        {
            config.Configs[name] = ToV1(oldProvider);
        }

        return config;
    }

    private static Config ToV1(Config0 old)
    {
        return new Config
        {
            Endpoint = old.Endpoint,
            ClientId = old.ClientId,
            Secret = old.Secret,
            Enabled = old.Enabled,
            RoleClaim = old.RoleClaim,
            DefaultUsernameClaim = old.DefaultUsernameClaim,
            AvatarClaim = old.AvatarClaim,
            EnableUserProvisioning = old.EnableUserProvisioning,
            UpdateUsersOnLogin = old.UpdateUsersOnLogin,
            Roles = old.Roles ?? [],
            AdminRoles = old.AdminRoles ?? [],
            AutoLinkingAllowList = old.AutoLinkingAllowList ?? [],
            EnableAllFolders = old.EnableAllFolders,
            EnabledFolders = old.EnabledFolders ?? [],
            EnableFolderRoles = old.EnableFolderRoles,
            FolderRoleMapping = ToV1(old.FolderRoleMapping),
            EnableLiveTv = old.EnableLiveTv,
            EnableLiveTvManagement = old.EnableLiveTvManagement,
            EnableLiveTvRoles = old.EnableLiveTvRoles,
            LiveTvRoles = old.LiveTvRoles ?? [],
            LiveTvManagementRoles = old.LiveTvManagementRoles ?? [],
            Scopes = old.Scopes ?? [],
            DefaultAuthProvider = old.DefaultAuthProvider?.Replace(
                "Jellyfin.Plugin.OpenIDConnect.AuthProvider",
                "Jellyfin.Plugin.SecureAuth.Auth.OIDCProvider"),
            DisableHttps = old.DisableHttps,
            DisablePushedAuthorization = old.DisablePushedAuthorization,
            DoNotValidateEndpoints = old.DoNotValidateEndpoints,
            DoNotValidateIssuerName = old.DoNotValidateIssuerName,
            UseHTTP = old.UseHTTP,
            DoNotLoadProfile = old.DoNotLoadProfile,
            PortOverride = old.PortOverride,
            Links = old.Links,
        };
    }

    private static List<FolderRoleMap> ToV1(List<FolderRoleMap0> old)
    {
        return old?.Select(static m => new FolderRoleMap
        {
            Role = m.Role,
            Folders = m.Folders ?? [],
        }).ToList() ?? [];
    }
}