using System.Collections.Generic;
using System.Linq;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Jellyfin.Plugin.SecureAuth.Migrations;

public static class Migrate1To2
{
    public static PluginConfiguration ToV2(PluginConfiguration1 old)
    {
        var config = new PluginConfiguration
        {
            ConfigVersion = 2,
            HasMigratedFrom9P4 = old.HasMigrated,
            OpenIdConnect = new OpenIdConnect
            {
                Enable = true,
                AllowPrivateAvatarIp = old.AllowPrivateAvatarIp,
                DisableAvatarSsrfCheck = old.DisableAvatarSsrfCheck,
            },
        };

        foreach ((string name, Config1 oldProvider) in old.Configs)
        {
            config.OpenIdConnect.Providers[name] = ToV2(oldProvider);
        }

        return config;
    }

    private static OidcProvider ToV2(Config1 old)
    {
        return new OidcProvider
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
            FolderRoleMapping = ToV2(old.FolderRoleMapping),
            EnableLiveTv = old.EnableLiveTv,
            EnableLiveTvManagement = old.EnableLiveTvManagement,
            EnableLiveTvRoles = old.EnableLiveTvRoles,
            LiveTvRoles = old.LiveTvRoles ?? [],
            LiveTvManagementRoles = old.LiveTvManagementRoles ?? [],
            Scopes = old.Scopes ?? [],
            DefaultAuthProvider = old.DefaultAuthProvider,
            DisableHttps = old.DisableHttps,
            DisablePushedAuthorization = old.DisablePushedAuthorization,
            DoNotValidateEndpoints = old.DoNotValidateEndpoints,
            DoNotValidateIssuerName = old.DoNotValidateIssuerName,
            UseHttp = old.UseHTTP,
            DoNotLoadProfile = old.DoNotLoadProfile,
            PortOverride = old.PortOverride,
            Links = old.Links,
        };
    }

    private static List<OidcFolderRoleMap> ToV2(List<FolderRoleMap1> old)
    {
        return old?.Select(static m => new OidcFolderRoleMap
        {
            Role = m.Role,
            Folders = m.Folders ?? [],
        }).ToList() ?? [];
    }
}